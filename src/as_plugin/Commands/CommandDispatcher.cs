using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using EMV.AdvanceSteel.Plugin.Commands.Handlers;

using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;
using AcTransaction = Autodesk.AutoCAD.DatabaseServices.Transaction;
using AsDocumentManager = Autodesk.AdvanceSteel.DocumentManagement.DocumentManager;
using AsTransaction = Autodesk.AdvanceSteel.CADAccess.Transaction;
using AsTransactionManager = Autodesk.AdvanceSteel.CADAccess.TransactionManager;

// Advance Steel declares these enums nested inside the class they belong to.
using eAssemblyLocation = Autodesk.AdvanceSteel.ConstructionTypes.AtomicElement.eAssemblyLocation;
using eObjectType = Autodesk.AdvanceSteel.CADAccess.FilerObject.eObjectType;
using eOpenMode = Autodesk.AdvanceSteel.CADAccess.FilerObject.eOpenMode;

namespace EMV.AdvanceSteel.Plugin.Commands
{
    public class CommandResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; } = 200;
        public object? Data { get; set; }
        public object? Error { get; set; }

        public static CommandResult Ok(object? data) => new() { Success = true, StatusCode = 200, Data = data };

        public static CommandResult Fail(
            string code,
            string message,
            int statusCode = 400,
            string? suggestion = null,
            string? details = null) =>
            new()
            {
                Success = false,
                StatusCode = statusCode,
                Error = new { code, message, details, suggestion }
            };
    }

    /// <summary>
    /// Everything a handler needs to touch the model. Handed to the handlers already inside the
    /// DocumentLock + AutoCAD transaction + Advance Steel transaction boundary (see
    /// rules/transaction-safety.md); handlers never open their own.
    /// </summary>
    public sealed class CommandContext
    {
        public CommandContext(Document doc, AcTransaction acadTransaction, string method, string path, string bodyJson)
        {
            Doc = doc;
            AcadTransaction = acadTransaction;
            Method = method;
            Path = path;
            BodyJson = bodyJson;
        }

        /// <summary>Creates the command-mode context, which deliberately has no outer AutoCAD transaction.</summary>
        public CommandContext(Document doc, string method, string path, string bodyJson)
        {
            Doc = doc;
            AcadTransaction = null!;
            Method = method;
            Path = path;
            BodyJson = bodyJson;
        }

        public Document Doc { get; }
        public AcTransaction AcadTransaction { get; }
        public string Method { get; }
        public string Path { get; }
        public string BodyJson { get; }

        private JsonElement? _body;

        /// <summary>Parsed request body. An empty or malformed body yields an empty object.</summary>
        public JsonElement Body
        {
            get
            {
                if (_body == null)
                {
                    try
                    {
                        using var parsed = JsonDocument.Parse(
                            string.IsNullOrWhiteSpace(BodyJson) ? "{}" : BodyJson);
                        _body = parsed.RootElement.Clone();
                    }
                    catch (JsonException)
                    {
                        using var empty = JsonDocument.Parse("{}");
                        _body = empty.RootElement.Clone();
                    }
                }

                return _body.Value;
            }
        }

        /// <summary>Query-string arguments of the request URL, e.g. ?handle=1B2C.</summary>
        public IReadOnlyDictionary<string, string> Query { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string? GetString(string name, string? fallback = null)
        {
            if (Body.ValueKind == JsonValueKind.Object
                && Body.TryGetProperty(name, out var prop)
                && prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            if (Query.TryGetValue(name, out var fromQuery) && !string.IsNullOrWhiteSpace(fromQuery))
            {
                return fromQuery;
            }

            return fallback;
        }

        public double GetDouble(string name, double fallback)
        {
            if (Body.ValueKind == JsonValueKind.Object
                && Body.TryGetProperty(name, out var prop)
                && prop.ValueKind == JsonValueKind.Number
                && prop.TryGetDouble(out var value))
            {
                return value;
            }

            if (Query.TryGetValue(name, out var raw)
                && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }

        public bool GetBool(string name, bool fallback)
        {
            if (Body.ValueKind == JsonValueKind.Object
                && Body.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
            }

            if (Query.TryGetValue(name, out var raw))
            {
                if (bool.TryParse(raw, out var parsed)) return parsed;
                if (raw == "1") return true;
                if (raw == "0") return false;
            }

            return fallback;
        }

        public List<string> GetStringList(string name)
        {
            var values = new List<string>();

            if (Body.ValueKind == JsonValueKind.Object
                && Body.TryGetProperty(name, out var prop)
                && prop.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prop.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) values.Add(value!);
                    }
                }
            }

            if (values.Count == 0 && Query.TryGetValue(name, out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                foreach (var part in raw.Split(','))
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length > 0) values.Add(trimmed);
                }
            }

            return values;
        }
    }

    public static class CommandDispatcher
    {
        /// <summary>SPEC-001: 30 s per request. Also caps how long an HTTP thread waits for AutoCAD.</summary>
        private const int RequestTimeoutMs = 30_000;

        /// <summary>
        /// Entry point called from the HttpListener worker thread. The AutoCAD/Advance Steel
        /// database is single-threaded (rules/transaction-safety.md §1), so the real work is
        /// marshalled onto the AutoCAD application context and this thread only awaits the result.
        /// </summary>
        public static Task<CommandResult> DispatchAsync(string method, string path, string bodyJson)
        {
            var tcs = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                AcApplication.DocumentManager.ExecuteInApplicationContext(
                    _ =>
                    {
                        try
                        {
                            tcs.TrySetResult(ExecuteOnMainThread(method, path, bodyJson));
                        }
                        catch (System.Exception ex)
                        {
                            tcs.TrySetResult(CommandResult.Fail(
                                "DISPATCH_ERROR", ex.Message, 500,
                                "Unexpected failure inside the AutoCAD application context.",
                                ex.StackTrace));
                        }
                    },
                    null);
            }
            catch (System.Exception ex)
            {
                tcs.TrySetResult(CommandResult.Fail(
                    "DISPATCH_ERROR", ex.Message, 500,
                    "Could not queue the command onto the AutoCAD main thread.",
                    ex.StackTrace));
            }

            return WithTimeoutAsync(tcs.Task);
        }

        private static async Task<CommandResult> WithTimeoutAsync(Task<CommandResult> work)
        {
            using var cts = new CancellationTokenSource();
            var timeout = Task.Delay(RequestTimeoutMs, cts.Token);

            if (await Task.WhenAny(work, timeout).ConfigureAwait(false) == work)
            {
                cts.Cancel();
                return await work.ConfigureAwait(false);
            }

            return CommandResult.Fail(
                "REQUEST_TIMEOUT",
                $"Advance Steel did not answer within {RequestTimeoutMs} ms.",
                504,
                "AutoCAD is likely busy with a modal dialog or a running command. Finish it and retry.");
        }

        // ------------------------------------------------------------------
        // Main-thread execution
        // ------------------------------------------------------------------

        private static CommandResult ExecuteOnMainThread(string method, string path, string bodyJson)
        {
            var route = NormalizeRoute(path);
            var query = ParseQuery(path);

            var doc = AcApplication.DocumentManager.MdiActiveDocument;

            // /health must answer even with no drawing open, so the MCP server can distinguish
            // "add-in not loaded" from "no model open".
            if (route == "health")
            {
                return HandleHealth(doc);
            }

            if (doc == null)
            {
                return CommandResult.Fail(
                    "NO_ACTIVE_DOCUMENT", "No active drawing open in Advance Steel.", 503,
                    "Open an Advance Steel model in AutoCAD and retry.");
            }

            if (!IsKnownRoute(route))
            {
                return CommandResult.Fail(
                    "ENDPOINT_NOT_FOUND", $"Unknown endpoint: {path}", 404,
                    "Check docs/specs/004-mcp-tools-specification.md for the supported tool surface.");
            }

            // Command-mode engines create and commit their own transactions. Holding only the
            // document lock avoids a nested-transaction deadlock (rules/transaction-safety.md section 5).
            if (CommandRoutes.Contains(route))
            {
                using (var docLock = doc.LockDocument())
                {
                    try
                    {
                        var context = new CommandContext(doc, method, route, bodyJson) { Query = query };
                        return Route(context);
                    }
                    catch (System.Exception ex)
                    {
                        return CommandResult.Fail(
                            "COMMAND_MODE_FAILED", ex.Message, 500,
                            "The command-mode route left the model as the Advance Steel command wrote it.",
                            ex.StackTrace);
                    }
                }
            }

            // Atomic transaction pattern (rules/transaction-safety.md §3). The AutoCAD transaction
            // is the outer boundary; the Advance Steel transaction wraps the AS database inside it
            // so that a failure rolls back both and never leaves dangling entities.
            using (var docLock = doc.LockDocument())
            using (var acadTrans = doc.TransactionManager.StartTransaction())
            {
                AsTransaction? asTrans = null;
                try
                {
                    asTrans = StartAdvanceSteelTransaction();

                    var context = new CommandContext(doc, acadTrans, method, route, bodyJson) { Query = query };
                    var result = Route(context);

                    if (result.Success)
                    {
                        asTrans?.Commit();
                        acadTrans.Commit();
                    }
                    else
                    {
                        // A structured failure is still a failure: never half-commit a model change.
                        asTrans?.Abort();
                        acadTrans.Abort();
                    }

                    return result;
                }
                catch (System.Exception ex)
                {
                    TryAbort(asTrans);
                    TryAbort(acadTrans);

                    return CommandResult.Fail(
                        "TRANSACTION_ABORTED", ex.Message, 500,
                        "The model was rolled back. Review the command parameters and the model state.",
                        ex.StackTrace);
                }
                finally
                {
                    asTrans?.Dispose();
                }
            }
        }

        private static CommandResult Route(CommandContext ctx) => ctx.Path switch
        {
            "elements/selected" => SelectionQuery.GetSelectedElements(ctx),
            "elements/query" => QueryCommandHandler.QueryElements(ctx),
            "elements/joints-catalog" => QueryCommandHandler.GetJointsCatalog(ctx),
            "elements/validate-section" => QueryCommandHandler.ValidateSection(ctx),
            "elements/beam" => BeamCommandHandler.Create(ctx),
            "elements/plate" => PlateCommandHandler.Create(ctx),
            "elements/bolt" => BoltCommandHandler.Create(ctx),
            "elements/poly-beam" => PolyBeamCommandHandler.Create(ctx),
            "elements/joint" => JointCommandHandler.Create(ctx),
            "elements/cut" => FeatureCommandHandler.Apply(ctx),
            "elements/modify" => ModifyCommandHandler.Modify(ctx),
            "assembly/verify-welds" => WeldCommandHandler.VerifyWelds(ctx),
            "assembly/main-part" => AssemblyCommandHandler.InspectMainPart(ctx),
            "assembly/set-main-part" => AssemblyCommandHandler.SetMainPart(ctx),
            "spatial/ucs-grids" => SpatialCommandHandler.GetUcsAndGrids(ctx),
            "spatial/box" => SpatialCommandHandler.QueryBox(ctx),
            "audit/assembly-integrity" => AuditCommandHandler.AuditAssemblyIntegrity(ctx),
            "audit/clashes" => AuditCommandHandler.DetectClashes(ctx),
            "viewport/capture" => ViewportCommandHandler.Capture(ctx),
            "script/execute" => ScriptRoute(ctx),
            "production/numbering" => ProductionCommandHandler.RunNumbering(ctx),
            "production/export-nc" => ProductionCommandHandler.ExportNc(ctx),
            "production/drawing-status" => ProductionCommandHandler.DrawingStatus(ctx),
            "production/bom" => BomCommandHandler.GenerateBom(ctx),
            _ => CommandResult.Fail("ENDPOINT_NOT_FOUND", $"Unknown endpoint: {ctx.Path}", 404)
        };

        private static CommandResult ScriptRoute(CommandContext ctx)
        {
            var code = ctx.GetString("script_code");
            if (string.IsNullOrWhiteSpace(code))
            {
                return CommandResult.Fail(
                    "MISSING_PARAMETER", "Parameter 'script_code' is required.", 400,
                    "Send {\"script_code\": \"...\"} as the JSON body.");
            }

            return Scripting.RoslynEvaluator.Evaluate(ctx.Doc, code!);
        }

        private static readonly HashSet<string> KnownRoutes = new(StringComparer.OrdinalIgnoreCase)
        {
            "elements/selected",
            "elements/query",
            "elements/joints-catalog",
            "elements/validate-section",
            "elements/beam",
            "elements/plate",
            "elements/bolt",
            "elements/poly-beam",
            "elements/joint",
            "elements/cut",
            "elements/modify",
            "assembly/verify-welds",
            "assembly/main-part",
            "assembly/set-main-part",
            "spatial/ucs-grids",
            "spatial/box",
            "audit/assembly-integrity",
            "audit/clashes",
            "viewport/capture",
            "script/execute",
            "production/numbering",
            "production/export-nc",
            "production/drawing-status",
            "production/bom"
        };

        private static readonly HashSet<string> CommandRoutes = new(StringComparer.OrdinalIgnoreCase)
        {
            "production/numbering",
            "production/export-nc",
            "production/drawing-status"
        };

        private static bool IsKnownRoute(string route) => KnownRoutes.Contains(route);

        /// <summary>Turns "/api/v1/elements/beam?x=1" into "elements/beam".</summary>
        private static string NormalizeRoute(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;

            var queryStart = path.IndexOf('?');
            if (queryStart >= 0) path = path.Substring(0, queryStart);

            const string prefix = "/api/v1/";
            var index = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (index >= 0) path = path.Substring(index + prefix.Length);

            return path.Trim('/').ToLowerInvariant();
        }

        private static Dictionary<string, string> ParseQuery(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(path)) return result;

            var queryStart = path.IndexOf('?');
            if (queryStart < 0 || queryStart == path.Length - 1) return result;

            foreach (var pair in path.Substring(queryStart + 1).Split('&'))
            {
                if (pair.Length == 0) continue;
                var separator = pair.IndexOf('=');
                if (separator < 0)
                {
                    result[Uri.UnescapeDataString(pair)] = string.Empty;
                }
                else
                {
                    var key = Uri.UnescapeDataString(pair.Substring(0, separator));
                    var value = Uri.UnescapeDataString(pair.Substring(separator + 1).Replace('+', ' '));
                    result[key] = value;
                }
            }

            return result;
        }

        // ------------------------------------------------------------------
        // /health
        // ------------------------------------------------------------------

        private static CommandResult HandleHealth(Document? doc)
        {
            if (doc == null)
            {
                return CommandResult.Ok(new
                {
                    status = "online",
                    as_version = AsQuery.AdvanceSteelVersion(),
                    acad_version = AcApplication.Version.ToString(),
                    active_dwg = (string?)null,
                    units = "Unknown",
                    element_count = (object?)null
                });
            }

            object? counts = null;
            string units = "Unknown";

            try
            {
                using (doc.LockDocument())
                using (var acadTrans = doc.TransactionManager.StartTransaction())
                {
                    AsTransaction? asTrans = null;
                    try
                    {
                        asTrans = StartAdvanceSteelTransaction();
                        counts = AsQuery.CountModelElements();
                        units = AsQuery.ModelUnits(doc);
                        asTrans?.Commit();
                        acadTrans.Commit();
                    }
                    catch
                    {
                        TryAbort(asTrans);
                        TryAbort(acadTrans);
                        throw;
                    }
                    finally
                    {
                        asTrans?.Dispose();
                    }
                }
            }
            catch (System.Exception)
            {
                // A health probe must never fail the whole endpoint: report online without counts.
                counts = null;
            }

            return CommandResult.Ok(new
            {
                status = "online",
                as_version = AsQuery.AdvanceSteelVersion(),
                acad_version = AcApplication.Version.ToString(),
                active_dwg = doc.Name,
                units,
                element_count = counts
            });
        }

        // ------------------------------------------------------------------
        // Transaction helpers
        // ------------------------------------------------------------------

        private static AsTransaction? StartAdvanceSteelTransaction()
        {
            try
            {
                var asDoc = AsDocumentManager.GetCurrentDocument();
                return asDoc == null
                    ? AsTransactionManager.StartTransaction()
                    : AsTransactionManager.StartTransaction(asDoc);
            }
            catch (System.Exception)
            {
                // Drawing without an Advance Steel database (plain DWG): AutoCAD-only commands
                // such as viewport capture still work, AS-specific ones fail with their own error.
                return null;
            }
        }

        private static void TryAbort(AsTransaction? trans)
        {
            try { trans?.Abort(); } catch (System.Exception) { /* already unwinding */ }
        }

        private static void TryAbort(AcTransaction? trans)
        {
            try { trans?.Abort(); } catch (System.Exception) { /* already unwinding */ }
        }
    }

    /// <summary>
    /// Read-only lookups against the Advance Steel database, shared by the handlers.
    /// Every member assumes it is already inside the dispatcher's transaction boundary.
    /// </summary>
    internal static class AsQuery
    {
        internal static string AdvanceSteelVersion()
        {
            try
            {
                return Autodesk.AdvanceSteel.Utils.VersionInformation.GetMajorVersion();
            }
            catch (System.Exception)
            {
                return "Unknown";
            }
        }

        internal static string ModelUnits(Document doc)
        {
            try
            {
                return doc.Database.Insunits switch
                {
                    Autodesk.AutoCAD.DatabaseServices.UnitsValue.Millimeters => "Metric",
                    Autodesk.AutoCAD.DatabaseServices.UnitsValue.Centimeters => "Metric",
                    Autodesk.AutoCAD.DatabaseServices.UnitsValue.Meters => "Metric",
                    Autodesk.AutoCAD.DatabaseServices.UnitsValue.Inches => "Imperial",
                    Autodesk.AutoCAD.DatabaseServices.UnitsValue.Feet => "Imperial",
                    _ => "Unknown"
                };
            }
            catch (System.Exception)
            {
                return "Unknown";
            }
        }

        internal static object CountModelElements() => new
        {
            beams = CountOf(eObjectType.kBeam),
            plates = CountOf(eObjectType.kPlateBase),
            welds = CountOf(eObjectType.kWeldPattern),
            bolts = CountOf(eObjectType.kBoltPattern)
        };

        private static int CountOf(eObjectType baseClass)
        {
            try
            {
                return ModelObjectIds(baseClass).Length;
            }
            catch (System.Exception)
            {
                return 0;
            }
        }

        /// <summary>All model object ids whose class derives from <paramref name="baseClass"/>.</summary>
        internal static Autodesk.AdvanceSteel.CADLink.Database.ObjectId[] ModelObjectIds(eObjectType baseClass)
        {
            using var filter = new Autodesk.AdvanceSteel.CADAccess.ClassTypeFilter(false);
            filter.AppendAcceptedBaseClass(baseClass);
            Autodesk.AdvanceSteel.CADAccess.DatabaseManager.GetModelObjectIds(out var ids, filter);
            return ids ?? Array.Empty<Autodesk.AdvanceSteel.CADLink.Database.ObjectId>();
        }

        /// <summary>Resolves an Advance Steel handle (as reported by <c>FilerObject.Handle</c>).</summary>
        internal static Autodesk.AdvanceSteel.CADAccess.FilerObject? OpenByHandle(string handle)
        {
            if (string.IsNullOrWhiteSpace(handle)) return null;

            try
            {
                return Autodesk.AdvanceSteel.CADAccess.FilerObject.GetFilerObjectByHandle(handle.Trim());
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Resolves a handle onto a model object of the kind the caller needs. The three failure
        /// modes are kept apart on purpose, because the agent fixes each one differently: a missing
        /// parameter is a malformed request, a handle nobody knows means the agent must re-read the
        /// model, and a handle that names the wrong kind of object means it must pick another
        /// element.
        /// </summary>
        internal static bool TryResolve<T>(
            string? handle, string parameterName, out T element, out CommandResult? error)
            where T : class
        {
            element = null!;
            error = null;

            if (string.IsNullOrWhiteSpace(handle))
            {
                error = CommandResult.Fail(
                    "MISSING_PARAMETER", $"Parameter '{parameterName}' is required.", 400,
                    $"Send the Advance Steel handle of the element, e.g. {{\"{parameterName}\": \"1B2C\"}}.");
                return false;
            }

            var resolved = OpenByHandle(handle);
            if (resolved == null)
            {
                error = CommandResult.Fail(
                    "HANDLE_NOT_FOUND", $"No Advance Steel object has handle '{handle}'.", 404,
                    "Call get_selected_elements to obtain valid handles from the current model.");
                return false;
            }

            if (resolved is not T typed)
            {
                error = CommandResult.Fail(
                    "INVALID_ELEMENT_TYPE",
                    $"Handle '{handle}' is a {TypeName(resolved)}, which cannot be used as a {typeof(T).Name}.",
                    422,
                    "Pick an element of the required kind; get_selected_elements reports the type behind "
                    + "every handle.");
                return false;
            }

            element = typed;
            return true;
        }

        internal static Autodesk.AdvanceSteel.CADAccess.FilerObject? Open(
            Autodesk.AdvanceSteel.CADLink.Database.ObjectId id)
        {
            try
            {
                return Autodesk.AdvanceSteel.CADAccess.DatabaseManager.Open(id, eOpenMode.kNormal);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>Workshop / Site / Unknown, as SPEC-002 spells it.</summary>
        internal static string LocationName(eAssemblyLocation location) => location switch
        {
            eAssemblyLocation.kInShop => "Workshop",
            eAssemblyLocation.kOnSite => "Site",
            eAssemblyLocation.kSiteDrill => "Site",
            _ => "Unknown"
        };

        /// <summary>
        /// rules/advance-steel-modeling.md §3.2: only a primary profile may carry the Main Part.
        /// Plates, stiffeners and other secondary parts are invalid choices.
        /// </summary>
        internal static bool IsPrimaryProfile(Autodesk.AdvanceSteel.CADAccess.FilerObject element) =>
            element is Autodesk.AdvanceSteel.Modelling.Beam;

        private static readonly string[] SecondaryRoles =
        {
            "BasePlate", "EndPlate", "Stiffener", "GussetPlate", "Plate", "Clip", "Cleat", "Angle"
        };

        internal static bool IsSecondaryRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return false;

            foreach (var secondary in SecondaryRoles)
            {
                if (role!.IndexOf(secondary, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        internal static string TypeName(Autodesk.AdvanceSteel.CADAccess.FilerObject element)
        {
            try
            {
                return element.Type().ToString();
            }
            catch (System.Exception)
            {
                return element.GetType().Name;
            }
        }

        internal static string? SectionName(Autodesk.AdvanceSteel.CADAccess.FilerObject element)
        {
            try
            {
                return element is Autodesk.AdvanceSteel.Modelling.Beam beam ? beam.ProfName : null;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        internal static double WeightKg(Autodesk.AdvanceSteel.ConstructionTypes.AtomicElement element)
        {
            try
            {
                return element switch
                {
                    Autodesk.AdvanceSteel.Modelling.Beam beam => beam.GetWeight(0),
                    Autodesk.AdvanceSteel.Modelling.PlateBase plate => plate.GetWeight(),
                    Autodesk.AdvanceSteel.ConstructionTypes.MainAlias alias => alias.GetStandardWeight(),
                    _ => 0.0
                };
            }
            catch (System.Exception)
            {
                return 0.0;
            }
        }

        /// <summary>Summary of one element, shaped like SPEC-002 ElementDetails.</summary>
        internal static object Describe(Autodesk.AdvanceSteel.CADAccess.FilerObject element)
        {
            var atomic = element as Autodesk.AdvanceSteel.ConstructionTypes.AtomicElement;
            var construction = element as Autodesk.AdvanceSteel.ConstructionTypes.ConstructionElement;

            return new
            {
                handle = Safe(() => element.Handle, null),
                type = TypeName(element),
                model_role = Safe(() => construction?.Role, null),
                section_name = SectionName(element),
                material = Safe(() => atomic?.Material, null),
                assembly_mark = Safe(() => atomic?.GetMainPartPositionNumber(), null),
                single_part_mark = Safe(() => atomic?.GetSinglePartPositionNumber(), null),
                is_main_part = atomic != null && Safe(() => atomic.IsMainPart, false),
                weight_kg = atomic == null ? 0.0 : Math.Round(WeightKg(atomic), 3)
            };
        }

        internal static T Safe<T>(Func<T> getter, T fallback)
        {
            try
            {
                return getter();
            }
            catch (System.Exception)
            {
                return fallback;
            }
        }
    }

    /// <summary>GET /api/v1/elements/selected — what the user has picked in the viewport.</summary>
    internal static class SelectionQuery
    {
        internal static CommandResult GetSelectedElements(CommandContext ctx)
        {
            var editor = ctx.Doc.Editor;
            var selection = editor.SelectImplied();

            if (selection.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK || selection.Value == null)
            {
                return CommandResult.Ok(new
                {
                    elements = Array.Empty<object>(),
                    count = 0,
                    note = "No elements are currently selected in the Advance Steel viewport."
                });
            }

            var elements = new List<object>();

            foreach (Autodesk.AutoCAD.EditorInput.SelectedObject? selected in selection.Value)
            {
                if (selected == null) continue;

                var filerObject = ResolveCadEntity(selected.ObjectId);
                if (filerObject == null) continue;

                elements.Add(AsQuery.Describe(filerObject));
            }

            return CommandResult.Ok(new { elements, count = elements.Count });
        }

        /// <summary>Maps an AutoCAD entity id onto the Advance Steel object behind it.</summary>
        internal static Autodesk.AdvanceSteel.CADAccess.FilerObject? ResolveCadEntity(
            Autodesk.AutoCAD.DatabaseServices.ObjectId acadId)
        {
            try
            {
                var asCadId = new Autodesk.AdvanceSteel.CADLink.Database.ObjectId(acadId.OldIdPtr);
                var filerId = Autodesk.AdvanceSteel.CADAccess.DatabaseManager.GetFilerObjectId(asCadId);
                return filerId.IsNull() ? null : AsQuery.Open(filerId);
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
