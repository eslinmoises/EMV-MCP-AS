using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Modelling;
using Autodesk.AutoCAD.DatabaseServices;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// Runs Advance Steel shop-drawing processes on the AutoCAD UI thread.
    /// Unlike numbering and NC export, CONTRACT-015 requires the drawing process to own both
    /// the document lock and transaction. The native command is therefore executed synchronously
    /// while both scopes are alive; SendStringToExecute must not be used here because it queues the
    /// command until after those scopes have been disposed.
    /// </summary>
    public static class DrawingProductionHandler
    {
        private const string DefaultDrawingStyle = "Standard";
        private const string DefaultSheetSize = "A3";
        // The trailing underscore is part of the AS 2026 global command name (AstProcessesData.xml).
        private const string DefaultEngineCommand = "AstM4CommDetailingProc_";

        public static CommandResult GenerateShopDrawings(CommandContext ctx)
        {
            var handles = ctx.GetStringList("assembly_handles")
                .Where(handle => !string.IsNullOrWhiteSpace(handle))
                .Select(handle => handle.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (handles.Count == 0)
            {
                return CommandResult.Fail(
                    "MISSING_PARAMETER", "Parameter 'assembly_handles' must contain at least one part handle.", 400,
                    "Pass handles returned by get_selected_elements after automatic numbering.");
            }

            var drawingStyle = ctx.GetString("drawing_style", DefaultDrawingStyle)?.Trim();
            var sheetSize = ctx.GetString("sheet_size", DefaultSheetSize)?.Trim().ToUpperInvariant();
            var engineCommand = ctx.GetString("engine_command", DefaultEngineCommand)?.Trim();
            var prototypePath = ctx.GetString("prototype_path");

            if (string.IsNullOrWhiteSpace(drawingStyle))
            {
                return InvalidParameter("drawing_style", "a non-empty installed Advance Steel drawing style");
            }

            if (string.IsNullOrWhiteSpace(sheetSize))
            {
                return InvalidParameter("sheet_size", "a non-empty sheet size such as A3");
            }

            if (string.IsNullOrWhiteSpace(engineCommand)
                || engineCommand.Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
            {
                return InvalidParameter("engine_command", "an AutoCAD command name containing only letters, digits, or underscores");
            }

            if (!string.IsNullOrWhiteSpace(prototypePath) && !File.Exists(prototypePath))
            {
                return CommandResult.Fail(
                    "DRAWING_PROTOTYPE_MISSING", $"Drawing prototype '{prototypePath}' does not exist.", 422,
                    "Install the required prototype or pass an existing absolute prototype_path.");
            }

            try
            {
                using var docLock = ctx.Doc.LockDocument();
                using var transaction = ctx.Doc.TransactionManager.StartTransaction();

                var parts = new List<DrawingPart>();
                var selectionIds = new List<ObjectId>();
                foreach (var handle in handles)
                {
                    if (AsQuery.OpenByHandle(handle) is not AtomicElement part || IsNonPhysicalPart(part))
                    {
                        transaction.Abort();
                        return CommandResult.Fail(
                            "ELEMENT_NOT_FOUND", $"Handle '{handle}' is not a physical Advance Steel part.", 404,
                            "Use a main-part or single-part handle returned by get_selected_elements.");
                    }

                    var singlePartMark = Safe(() => part.GetSinglePartPositionNumber());
                    var assemblyMark = Safe(() => part.GetMainPartPositionNumber());
                    var isMainPart = Safe(() => part.IsMainPart, false);
                    var drawingNumber = isMainPart ? assemblyMark : singlePartMark;
                    if (string.IsNullOrWhiteSpace(drawingNumber))
                    {
                        transaction.Abort();
                        return CommandResult.Fail(
                            "UNNUMBERED_MODEL",
                            $"Part '{handle}' has no {(isMainPart ? "assembly" : "single-part")} mark; a traceable drawing cannot be generated.",
                            409,
                            "Run production/numbering and verify the assigned marks before generating drawings.");
                    }

                    ObjectId objectId;
                    try
                    {
                        objectId = ctx.Doc.Database.GetObjectId(false, new Handle(Convert.ToInt64(handle, 16)), 0);
                    }
                    catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is Autodesk.AutoCAD.Runtime.Exception)
                    {
                        transaction.Abort();
                        return CommandResult.Fail(
                            "ELEMENT_NOT_FOUND", $"Handle '{handle}' cannot be selected in the active DWG.", 404,
                            "Refresh the model handles and retry against the active document.");
                    }

                    selectionIds.Add(objectId);
                    parts.Add(new DrawingPart(handle, singlePartMark, assemblyMark, isMainPart, drawingNumber!));
                }

                ctx.Doc.Editor.SetImpliedSelection(selectionIds.ToArray());
                try
                {
                    // The process command must finish before the transaction and document lock are released.
                    ctx.Doc.Editor.Command("_" + engineCommand, drawingStyle, sheetSize);
                }
                catch (Exception ex)
                {
                    transaction.Abort();
                    var missingTemplate = MentionsMissingTemplate(ex.Message);
                    return CommandResult.Fail(
                        missingTemplate ? "DRAWING_TEMPLATE_MISSING" : "DRAWING_PROCESS_FAILED",
                        missingTemplate
                            ? $"Drawing style/prototype '{drawingStyle}' ({sheetSize}) is unavailable: {ex.Message}"
                            : $"Advance Steel drawing process '{engineCommand}' failed: {ex.Message}",
                        422,
                        missingTemplate
                            ? "Install the requested drawing style and prototype, or select one available in Drawing Process Manager."
                            : "Review the Advance Steel command line and drawing-process configuration, then retry.",
                        ex.StackTrace);
                }
                finally
                {
                    ctx.Doc.Editor.SetImpliedSelection(Array.Empty<ObjectId>());
                }

                transaction.Commit();
                var drawings = parts.Select(part => new
                {
                    assembly_handle = part.Handle,
                    drawing_type = part.IsMainPart ? "kMainPart" : "kSinglePart",
                    single_part_mark = EmptyAsNull(part.SinglePartMark),
                    assembly_mark = EmptyAsNull(part.AssemblyMark),
                    drawing_number = part.DrawingNumber,
                    drawing_style = drawingStyle,
                    sheet_size = sheetSize,
                    status = "generated"
                }).ToList();

                return CommandResult.Ok(new
                {
                    requested_count = handles.Count,
                    generated_count = drawings.Count,
                    drawings,
                    engine_command = engineCommand,
                    warnings = Array.Empty<string>()
                });
            }
            catch (Exception ex)
            {
                return CommandResult.Fail(
                    "DRAWING_PROCESS_FAILED", $"Shop drawing generation failed: {ex.Message}", 422,
                    "Verify that the active DWG is writable and that the drawing process is installed.", ex.StackTrace);
            }
        }

        private static bool IsNonPhysicalPart(AtomicElement element) =>
            element is WeldPattern || element is BoltPattern || element is Connector;

        private static bool MentionsMissingTemplate(string message)
        {
            var value = message ?? string.Empty;
            return value.Contains("template", StringComparison.OrdinalIgnoreCase)
                || value.Contains("prototype", StringComparison.OrdinalIgnoreCase)
                || value.Contains("style", StringComparison.OrdinalIgnoreCase)
                || value.Contains("not found", StringComparison.OrdinalIgnoreCase);
        }

        private static CommandResult InvalidParameter(string name, string expected) =>
            CommandResult.Fail(
                "INVALID_PARAMETER", $"Parameter '{name}' must be {expected}.", 400,
                $"Correct '{name}' and retry.");

        private static T? Safe<T>(Func<T> getter) where T : class
        {
            try { return getter(); }
            catch (Exception) { return null; }
        }

        private static bool Safe(Func<bool> getter, bool fallback)
        {
            try { return getter(); }
            catch (Exception) { return fallback; }
        }

        private static string? EmptyAsNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        private sealed record DrawingPart(
            string Handle,
            string? SinglePartMark,
            string? AssemblyMark,
            bool IsMainPart,
            string DrawingNumber);
    }
}
