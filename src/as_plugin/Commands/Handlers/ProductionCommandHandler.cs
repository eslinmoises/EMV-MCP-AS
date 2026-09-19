using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Modelling;
using Autodesk.AdvanceSteel.Services;
using Autodesk.AutoCAD.Internal;

// Advance Steel declares eObjectType nested inside FilerObject.
using eObjectType = Autodesk.AdvanceSteel.CADAccess.FilerObject.eObjectType;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// Production endpoints run in command mode (rules/transaction-safety.md section 5). The
    /// dispatcher owns the document lock; this handler never opens a document lock or transaction.
    /// Commands may commit partial work, so each report is read back from the model or file system.
    /// </summary>
    public static class ProductionCommandHandler
    {
        private const int MaxReportedMarks = 2000;

        private static readonly string[] NumberingCommandCandidates =
        {
            "AstM4Numbering", "AstM2Numbering", "AstNumbering", "AstEqualParts"
        };

        private static readonly string[] NcCommandCandidates =
        {
            "AstM4NCFiles", "AstM2NcFiles", "AstNcCreate", "AstNCExport"
        };

        // ==================================================================
        // POST production/numbering
        // ==================================================================

        public static CommandResult RunNumbering(CommandContext ctx)
        {
            var requestedHandles = ctx.GetStringList("element_handles");
            var startNumber = ctx.GetDouble("start_number", 1);
            if (startNumber < 1 || startNumber > int.MaxValue || startNumber != Math.Truncate(startNumber))
            {
                return CommandResult.Fail(
                    "INVALID_PARAMETER", "Parameter 'start_number' must be an integer greater than or equal to 1.", 400,
                    "Use a positive whole number such as 1.");
            }

            if (!TryCollectPhysicalPartHandles(requestedHandles, out var partHandles, out var unresolved))
            {
                return CommandResult.Fail(
                    "ELEMENT_NOT_FOUND", $"The requested handle is not a physical Advance Steel part: {unresolved[0]}.", 404,
                    "Call get_selected_elements to obtain valid physical-part handles from the active model.");
            }

            var keepExistingNumbers = ctx.GetBool("keep_existing_numbers", true);
            var alreadyNumbered = 0;
            if (keepExistingNumbers)
            {
                foreach (var handle in partHandles)
                {
                    var before = ReadPart(handle);
                    if (!string.IsNullOrWhiteSpace(before?.SinglePartMark)) alreadyNumbered++;
                }
            }

            var warnings = new List<string>();
            ConfigureNumbering(ctx, (int)startNumber, keepExistingNumbers, warnings);

            var engineCommand = ResolveEngineCommand(ctx, NumberingCommandCandidates);
            if (engineCommand == null)
            {
                return CommandResult.Fail(
                    "NUMBERING_ENGINE_UNAVAILABLE",
                    "No supported Advance Steel numbering command is registered in this AutoCAD session.", 503,
                    "Load the Advance Steel numbering module, or pass engine_command with the command name used by this installation.");
            }

            var commandFailure = RunEngine(ctx, engineCommand);
            if (commandFailure != null)
            {
                return CommandResult.Fail(
                    "ENGINE_COMMAND_FAILED",
                    $"Numbering command '{engineCommand}' failed. Command mode has no transaction to roll back, so the model was left as the command wrote it: {commandFailure.Message}",
                    422,
                    "Review the Advance Steel command line or modal dialogs, inspect the resulting marks, then correct the model before retrying.",
                    commandFailure.StackTrace);
            }

            var parts = ReadParts(partHandles, warnings);
            var singlePartQuantities = CountMarks(parts, part => part.SinglePartMark);
            var numberedSingleParts = parts.Count(part => !string.IsNullOrWhiteSpace(part.SinglePartMark));
            var numberedAssemblies = parts
                .Where(part => !string.IsNullOrWhiteSpace(part.AssemblyMark))
                .Select(part => part.AssemblyMark!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            var marks = new List<object>();
            foreach (var part in parts.Take(MaxReportedMarks))
            {
                var quantity = string.IsNullOrWhiteSpace(part.SinglePartMark)
                    ? 0
                    : singlePartQuantities[part.SinglePartMark!];

                marks.Add(new
                {
                    handle = part.Handle,
                    single_part_mark = EmptyAsNull(part.SinglePartMark),
                    assembly_mark = EmptyAsNull(part.AssemblyMark),
                    is_main_part = part.IsMainPart,
                    quantity
                });
            }

            if (parts.Count > MaxReportedMarks)
            {
                warnings.Add(
                    $"marks was capped at {MaxReportedMarks} entries; the numbered counts still cover all {parts.Count} physical parts.");
            }

            var conflicts = FindAssemblyMarkConflicts(parts);

            return CommandResult.Ok(new
            {
                scope = requestedHandles.Count == 0 ? "model" : "selection",
                numbered_single_parts = numberedSingleParts,
                numbered_assemblies = numberedAssemblies,
                already_numbered = alreadyNumbered,
                marks,
                conflicts,
                warnings,
                engine_command = engineCommand
            });
        }

        // ==================================================================
        // POST production/export-nc
        // ==================================================================

        public static CommandResult ExportNc(CommandContext ctx)
        {
            var requestedHandles = ctx.GetStringList("element_handles");
            if (!TryCollectPhysicalPartHandles(requestedHandles, out var partHandles, out var unresolved))
            {
                return CommandResult.Fail(
                    "ELEMENT_NOT_FOUND", $"The requested handle is not a physical Advance Steel part: {unresolved[0]}.", 404,
                    "Call get_selected_elements to obtain valid physical-part handles from the active model.");
            }

            var extension = (ctx.GetString("file_extension", "nc1") ?? "nc1").Trim().ToLowerInvariant();
            if (extension != "nc1" && extension != "nc")
            {
                return CommandResult.Fail(
                    "INVALID_PARAMETER", "Parameter 'file_extension' must be either 'nc1' or 'nc'.", 400,
                    "Use 'nc1' for DSTV NC1 files or 'nc' where the shop requires that extension.");
            }

            var parts = ReadParts(partHandles, new List<string>());
            if (!parts.Any(part => !string.IsNullOrWhiteSpace(part.SinglePartMark)))
            {
                return CommandResult.Fail(
                    "UNNUMBERED_MODEL", "No requested physical part has a single-part mark; NC export is unsafe.", 409,
                    "Run production/numbering and verify the marks before exporting NC files.");
            }

            var outputDirectoryRaw = ctx.GetString("output_directory", "./DSTV_NC1") ?? "./DSTV_NC1";
            if (!TryResolveOutputDirectory(ctx, outputDirectoryRaw, out var outputDirectory, out var resolveFailure))
            {
                return resolveFailure!;
            }

            try
            {
                Directory.CreateDirectory(outputDirectory!);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                return CommandResult.Fail(
                    "OUTPUT_DIRECTORY_UNWRITABLE", $"Cannot create or use NC output directory '{outputDirectory}': {ex.Message}", 422,
                    "Choose a writable absolute path or a folder below the saved DWG.", ex.StackTrace);
            }

            var warnings = new List<string>();
            var skipped = new List<object>();
            foreach (var part in parts.Where(part => string.IsNullOrWhiteSpace(part.SinglePartMark)))
            {
                skipped.Add(new
                {
                    handle = part.Handle,
                    reason = "UNNUMBERED_PART",
                    message = "Part has no single part mark; it would produce an untraceable NC file."
                });
            }

            var overwrite = ctx.GetBool("overwrite", true);
            if (!overwrite)
            {
                warnings.Add(
                    "overwrite=false was requested. The native NC command controls overwrite behaviour through its own settings; only files actually changed on disk are reported.");
            }

            var before = SnapshotDirectory(outputDirectory!);
            var engineCommand = ResolveEngineCommand(ctx, NcCommandCandidates);
            if (engineCommand == null)
            {
                return CommandResult.Fail(
                    "NC_EXPORT_UNAVAILABLE",
                    "No supported Advance Steel NC export command is registered in this AutoCAD session.", 503,
                    "Load the Advance Steel NC module, or pass engine_command with the command name used by this installation.");
            }

            var commandFailure = RunEngine(ctx, engineCommand);
            if (commandFailure != null)
            {
                return CommandResult.Fail(
                    "ENGINE_COMMAND_FAILED",
                    $"NC export command '{engineCommand}' failed. Command mode has no transaction to roll back, so the model was left as the command wrote it: {commandFailure.Message}",
                    422,
                    "Review the Advance Steel command line or modal dialogs, inspect the output folder, then correct the model before retrying.",
                    commandFailure.StackTrace);
            }

            var produced = ProducedFiles(before, outputDirectory!, extension);
            if (produced.Count == 0)
            {
                return CommandResult.Fail(
                    "EXPORT_FAILED", "The NC command completed but no requested NC file was created or changed in the output directory.", 422,
                    "Advance Steel writes NC files to the path in its own NC settings; configure that path to match output_directory and retry.");
            }

            var partBySinglePartMark = parts
                .Where(part => !string.IsNullOrWhiteSpace(part.SinglePartMark))
                .GroupBy(part => part.SinglePartMark!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var files = new List<object>();
            long totalBytes = 0;

            foreach (var file in produced)
            {
                var fileInfo = new FileInfo(file);
                if (!fileInfo.Exists) continue;

                totalBytes += fileInfo.Length;
                var mark = Path.GetFileNameWithoutExtension(fileInfo.Name);
                if (!partBySinglePartMark.TryGetValue(mark, out var part))
                {
                    warnings.Add(
                        $"NC file '{fileInfo.Name}' does not match a single-part mark in the export scope; it may be left over from an earlier run.");
                    files.Add(new
                    {
                        file_name = fileInfo.Name,
                        path = fileInfo.FullName,
                        element_handle = (string?)null,
                        single_part_mark = (string?)null,
                        assembly_mark = (string?)null,
                        size_bytes = fileInfo.Length
                    });
                    continue;
                }

                files.Add(new
                {
                    file_name = fileInfo.Name,
                    path = fileInfo.FullName,
                    element_handle = part.Handle,
                    single_part_mark = part.SinglePartMark,
                    assembly_mark = EmptyAsNull(part.AssemblyMark),
                    size_bytes = fileInfo.Length
                });
            }

            return CommandResult.Ok(new
            {
                output_directory = outputDirectory,
                file_extension = extension,
                exported_count = files.Count,
                total_bytes = totalBytes,
                files,
                skipped,
                warnings,
                engine_command = engineCommand
            });
        }

        // ==================================================================
        // GET production/drawing-status
        // ==================================================================

        public static CommandResult DrawingStatus(CommandContext ctx)
        {
            if (!TryCollectPhysicalPartHandles(Array.Empty<string>(), out var partHandles, out _))
            {
                return CommandResult.Fail(
                    "DRAWING_STATUS_UNAVAILABLE", "The Advance Steel model could not be enumerated for drawing status.", 503,
                    "Ensure the active drawing is an Advance Steel model and retry.");
            }

            var warnings = new List<string>();
            var parts = ReadParts(partHandles, warnings);
            var assemblies = parts
                .Where(part => !string.IsNullOrWhiteSpace(part.AssemblyMark))
                .GroupBy(part => part.AssemblyMark!, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (assemblies.Count == 0)
            {
                return CommandResult.Fail(
                    "UNNUMBERED_MODEL", "No physical part has an assembly mark; drawing status cannot be determined.", 409,
                    "Run production/numbering and verify assembly marks before checking drawings.");
            }

            var requestedMarks = ctx.GetStringList("assembly_marks");
            if (requestedMarks.Count > 0)
            {
                var requested = new HashSet<string>(requestedMarks, StringComparer.OrdinalIgnoreCase);
                assemblies = assemblies.Where(group => requested.Contains(group.Key)).ToList();
            }

            // As verified against Advance Steel 2026 assemblies, the managed DocumentManager does not
            // expose a public query method for derived drawings (such as GetDerivedDocumentsForDwg).
            // Per CONTRACT-004A §4.D, DRAWING_STATUS_UNAVAILABLE (503) is the honest answer when the
            // derived-document layer cannot be safely accessed without unmanaged/reflection hacks.
            return CommandResult.Fail(
                "DRAWING_STATUS_UNAVAILABLE",
                "The Advance Steel derived-document layer is not available in this managed session.",
                503,
                "Ensure Advance Steel Drawing Process manager and derived documents are available in this session.");
        }

        // ==================================================================
        // Numbering, NC, and command helpers
        // ==================================================================

        private static void ConfigureNumbering(
            CommandContext ctx, int startNumber, bool keepExistingNumbers, ICollection<string> warnings)
        {
            try
            {
                var parameters = EqualPartsParameters.GetCurrentParameters();
                if (parameters != null)
                {
                    parameters.SinglePartStartValue = startNumber;
                    parameters.MainPartStartValue = startNumber;
                    parameters.ReuseNumbers = keepExistingNumbers;
                    parameters.SetAsCurrent();
                }
            }
            catch (Exception ex)
            {
                warnings.Add(
                    $"Numbering settings could not be applied ({ex.Message}); the engine ran with the Advance Steel settings already configured.");
            }
        }

        private static string? ResolveEngineCommand(CommandContext ctx, IEnumerable<string> candidates)
        {
            var pinned = ctx.GetString("engine_command");
            var commands = string.IsNullOrWhiteSpace(pinned) ? candidates : new[] { pinned! };

            foreach (var command in commands)
            {
                try
                {
                    if (Convert.ToInt64(Utils.IsCommandNameInUse(command)) != 0)
                    {
                        return command;
                    }
                }
                catch (Exception)
                {
                    // Command registration probe failed; candidate cannot safely be invoked.
                }
            }

            // If a specific engine_command was requested, attempt it directly
            if (!string.IsNullOrWhiteSpace(pinned))
            {
                return pinned;
            }

            return null;
        }

        private static Exception? RunEngine(CommandContext ctx, string command)
        {
            try
            {
                ctx.Doc.Editor.Command("_" + command);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private static bool TryResolveOutputDirectory(
            CommandContext ctx, string rawDirectory, out string? outputDirectory, out CommandResult? failure)
        {
            outputDirectory = null;
            failure = null;

            try
            {
                if (Path.IsPathRooted(rawDirectory))
                {
                    outputDirectory = Path.GetFullPath(rawDirectory);
                    return true;
                }

                var dwgPath = ctx.Doc.Database.Filename;
                if (string.IsNullOrWhiteSpace(dwgPath) || !Path.IsPathRooted(dwgPath))
                {
                    failure = CommandResult.Fail(
                        "MODEL_NOT_SAVED",
                        "The active drawing has never been saved; a relative output_directory cannot be resolved.", 409,
                        "Save the DWG before exporting NC files, or pass an absolute output_directory.");
                    return false;
                }

                var dwgFolder = Path.GetDirectoryName(dwgPath);
                if (string.IsNullOrWhiteSpace(dwgFolder))
                {
                    failure = CommandResult.Fail(
                        "MODEL_NOT_SAVED", "Cannot locate the folder for the active DWG.", 409,
                        "Save the DWG to a regular directory.");
                    return false;
                }

                outputDirectory = Path.GetFullPath(Path.Combine(dwgFolder, rawDirectory));
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                failure = CommandResult.Fail(
                    "INVALID_PARAMETER", $"Invalid output_directory path '{rawDirectory}': {ex.Message}", 400,
                    "Pass a valid local folder path.");
                return false;
            }
        }

        private static Dictionary<string, DateTime> SnapshotDirectory(string directory)
        {
            var snapshot = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(directory)) return snapshot;

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                try
                {
                    snapshot[file] = File.GetLastWriteTimeUtc(file);
                }
                catch (Exception)
                {
                }
            }

            return snapshot;
        }

        private static List<string> ProducedFiles(
            IReadOnlyDictionary<string, DateTime> before, string directory, string extension)
        {
            var produced = new List<string>();
            if (!Directory.Exists(directory)) return produced;

            var pattern = "*." + extension;
            foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            {
                var info = new FileInfo(file);
                if (!before.TryGetValue(info.FullName, out var previousWrite)
                    || previousWrite != info.LastWriteTimeUtc)
                {
                    produced.Add(info.FullName);
                }
            }

            return produced;
        }

        // ==================================================================
        // Physical-part readback helpers
        // ==================================================================

        private static bool TryCollectPhysicalPartHandles(
            IReadOnlyList<string> requestedHandles, out List<string> partHandles, out List<string> unresolved)
        {
            partHandles = new List<string>();
            unresolved = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (requestedHandles.Count > 0)
            {
                foreach (var handle in requestedHandles)
                {
                    if (AsQuery.OpenByHandle(handle) is AtomicElement atomic && IsPhysicalPart(atomic))
                    {
                        var canonicalHandle = AsQuery.Safe(() => atomic.Handle, handle) ?? handle;
                        if (seen.Add(canonicalHandle)) partHandles.Add(canonicalHandle);
                    }
                    else
                    {
                        unresolved.Add(handle);
                    }
                }

                return unresolved.Count == 0;
            }

            foreach (var id in AsQuery.ModelObjectIds(eObjectType.kAtomicElem))
            {
                if (AsQuery.Open(id) is not AtomicElement atomic || !IsPhysicalPart(atomic)) continue;

                var handle = AsQuery.Safe(() => atomic.Handle, null);
                if (!string.IsNullOrWhiteSpace(handle) && seen.Add(handle)) partHandles.Add(handle);
            }

            return true;
        }

        private static List<PartSnapshot> ReadParts(IEnumerable<string> handles, ICollection<string> warnings)
        {
            var parts = new List<PartSnapshot>();
            foreach (var handle in handles)
            {
                var part = ReadPart(handle);
                if (part == null)
                {
                    warnings.Add($"Part '{handle}' could not be reopened after command execution and was omitted from the readback report.");
                    continue;
                }

                parts.Add(part);
            }

            return parts;
        }

        private static PartSnapshot? ReadPart(string handle)
        {
            if (AsQuery.OpenByHandle(handle) is not AtomicElement part || !IsPhysicalPart(part)) return null;

            return new PartSnapshot(
                AsQuery.Safe(() => part.Handle, handle) ?? handle,
                AsQuery.Safe(() => part.GetSinglePartPositionNumber(), null),
                AsQuery.Safe(() => part.GetMainPartPositionNumber(), null),
                AsQuery.Safe(() => part.IsMainPart, false),
                AsQuery.SectionName(part));
        }

        private static bool IsPhysicalPart(AtomicElement element) =>
            element is not WeldPattern && element is not BoltPattern && element is not Connector;

        private static Dictionary<string, int> CountMarks(
            IEnumerable<PartSnapshot> parts, Func<PartSnapshot, string?> markSelector)
        {
            var quantities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts)
            {
                var mark = markSelector(part);
                if (string.IsNullOrWhiteSpace(mark)) continue;

                quantities.TryGetValue(mark, out var quantity);
                quantities[mark] = quantity + 1;
            }

            return quantities;
        }

        private static List<object> FindAssemblyMarkConflicts(IEnumerable<PartSnapshot> parts)
        {
            var conflicts = new List<object>();
            foreach (var assembly in parts
                         .Where(part => part.IsMainPart && !string.IsNullOrWhiteSpace(part.AssemblyMark))
                         .GroupBy(part => part.AssemblyMark!, StringComparer.OrdinalIgnoreCase))
            {
                var sections = assembly
                    .Select(part => string.IsNullOrWhiteSpace(part.SectionName) ? "<unknown>" : part.SectionName!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (sections.Count < 2) continue;

                foreach (var part in assembly)
                {
                    conflicts.Add(new
                    {
                        handle = part.Handle,
                        mark = assembly.Key,
                        reason = $"Two geometrically different main-part sections ({string.Join(", ", sections)}) share the assembly mark {assembly.Key}."
                    });
                }
            }

            return conflicts;
        }

        private static string? EmptyAsNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        private sealed class PartSnapshot
        {
            public PartSnapshot(string handle, string? singlePartMark, string? assemblyMark, bool isMainPart, string? sectionName)
            {
                Handle = handle;
                SinglePartMark = singlePartMark;
                AssemblyMark = assemblyMark;
                IsMainPart = isMainPart;
                SectionName = sectionName;
            }

            public string Handle { get; }
            public string? SinglePartMark { get; }
            public string? AssemblyMark { get; }
            public bool IsMainPart { get; }
            public string? SectionName { get; }
        }
    }
}
