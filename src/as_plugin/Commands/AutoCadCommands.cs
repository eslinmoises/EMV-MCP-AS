using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using EMV.AdvanceSteel.Plugin.Commands.Handlers;

// ImplicitUsings + UseWindowsForms/UseWPF make a bare 'Application' ambiguous, so the AutoCAD one
// is addressed through an alias, exactly as in Host/ExtensionApplication.cs.
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;
using AcTransaction = Autodesk.AutoCAD.DatabaseServices.Transaction;
using AsDocumentManager = Autodesk.AdvanceSteel.DocumentManagement.DocumentManager;
using AsTransaction = Autodesk.AdvanceSteel.CADAccess.Transaction;
using AsTransactionManager = Autodesk.AdvanceSteel.CADAccess.TransactionManager;

namespace EMV.AdvanceSteel.Plugin.Commands
{
    /// <summary>
    /// CONTRACT-011 - the in-session face of the plugin. Every button of the <c>EMV AI Tools</c>
    /// ribbon tab fires one of these AutoCAD commands, which run exactly the same handlers the
    /// MCP/IPC routes use, inside the same transaction boundary (rules/transaction-safety.md §3).
    /// Nothing here duplicates modelling logic: the commands only gather input from the Editor,
    /// open the transaction, and report the outcome on the command line.
    /// </summary>
    public class AutoCadCommands
    {
        // ------------------------------------------------------------------
        // Generativo
        // ------------------------------------------------------------------

        /// <summary>Ribbon: Generativo - Nave Parametrica.</summary>
        [CommandMethod("EMV_PARAMETRIC_WAREHOUSE")]
        public void ParametricWarehouseCommand()
        {
            var ed = ActiveEditor();
            if (ed == null) return;

            if (!TryGetDouble(ed, "Luz / span (mm)", 31000.0, out var span)) return;
            if (!TryGetDouble(ed, "Longitud total (mm)", 30000.0, out var length)) return;
            if (!TryGetDouble(ed, "Separacion entre porticos (mm)", 5000.0, out var baySpacing)) return;
            if (!TryGetDouble(ed, "Altura de alero (mm)", 6000.0, out var eaveHeight)) return;
            if (!TryGetDouble(ed, "Altura de cumbrera (mm)", 9500.0, out var ridgeHeight)) return;

            Execute("Nave parametrica", "elements/trussed-warehouse",
                Json(
                    ("span", span),
                    ("length", length),
                    ("bay_spacing", baySpacing),
                    ("eave_height", eaveHeight),
                    ("ridge_height", ridgeHeight)),
                TrussedWarehouseCommandHandler.CreateTrussedWarehouse,
                "\"create_grids_and_levels\": true");
        }

        /// <summary>Ribbon: Generativo - Portico Simple.</summary>
        [CommandMethod("EMV_PORTAL_FRAME")]
        public void PortalFrameCommand()
        {
            var ed = ActiveEditor();
            if (ed == null) return;

            if (!TryGetDouble(ed, "Luz / span (mm)", 12000.0, out var span)) return;
            if (!TryGetDouble(ed, "Altura de pilar (mm)", 5000.0, out var columnHeight)) return;
            if (!TryGetDouble(ed, "Altura de cumbrera (mm)", 6500.0, out var ridgeHeight)) return;

            Execute("Portico simple", "elements/portal-frame",
                Json(
                    ("span_width_mm", span),
                    ("column_height_mm", columnHeight),
                    ("ridge_height_mm", ridgeHeight)),
                PortalFrameCommandHandler.Create);
        }

        // ------------------------------------------------------------------
        // Coordinacion BIM
        // ------------------------------------------------------------------

        /// <summary>
        /// Ribbon: Coordinacion BIM - Rejillas y Niveles. Lays out the two orthogonal grid
        /// families plus the base and eave levels in a single transaction, so a partial failure
        /// never leaves half a coordination frame behind.
        /// </summary>
        [CommandMethod("EMV_GRIDS_LEVELS")]
        public void GridsAndLevelsCommand()
        {
            var doc = AcApplication.DocumentManager.MdiActiveDocument;
            var ed = doc?.Editor;
            if (doc == null || ed == null) return;

            if (!TryGetDouble(ed, "Luz / span (mm)", 31000.0, out var span)) return;
            if (!TryGetDouble(ed, "Longitud total (mm)", 30000.0, out var length)) return;
            if (!TryGetDouble(ed, "Separacion entre porticos (mm)", 5000.0, out var baySpacing)) return;
            if (!TryGetDouble(ed, "Altura de alero (mm)", 6000.0, out var eaveHeight)) return;

            int transverseAxes = Math.Max(2, (int)Math.Round(length / baySpacing) + 1);

            ed.WriteMessage("\n[EMV-MCP-AS] Rejillas y niveles...\n");

            using var session = new ModelSession(doc);

            // Transverse axes (1, 2, 3 ...) marching along the length of the building.
            var transverse = session.Run("spatial/grid",
                Json(("line_length", span), ("count", transverseAxes), ("spacing", baySpacing)),
                SpatialCommandHandler.CreateGrid,
                "\"axis_direction\": [1, 0, 0], \"spacing_direction\": [0, 1, 0], \"label_prefix\": \"1\"");

            if (!ReportStep(ed, "Ejes transversales", transverse)) { session.Abort(); return; }

            // Longitudinal axes A and B, one per column line.
            var longitudinal = session.Run("spatial/grid",
                Json(("line_length", length), ("count", 2), ("spacing", span)),
                SpatialCommandHandler.CreateGrid,
                "\"axis_direction\": [0, 1, 0], \"spacing_direction\": [1, 0, 0], \"labels\": [\"A\", \"B\"]");

            if (!ReportStep(ed, "Ejes longitudinales", longitudinal)) { session.Abort(); return; }

            var baseLevel = session.Run("spatial/level",
                Json(("elevation", 0.0)),
                SpatialCommandHandler.CreateLevel,
                "\"name\": \"Nivel 0 - Base\"");

            if (!ReportStep(ed, "Nivel base", baseLevel)) { session.Abort(); return; }

            var eaveLevel = session.Run("spatial/level",
                Json(("elevation", eaveHeight)),
                SpatialCommandHandler.CreateLevel,
                "\"name\": \"Nivel 1 - Alero\"");

            if (!ReportStep(ed, "Nivel de alero", eaveLevel)) { session.Abort(); return; }

            session.Commit();
            ed.WriteMessage("\n[EMV-MCP-AS] Rejillas y niveles: OK\n");
        }

        // ------------------------------------------------------------------
        // Auditoria
        // ------------------------------------------------------------------

        /// <summary>Ribbon: Auditoria - Detailing Doctor.</summary>
        [CommandMethod("EMV_DOCTOR")]
        public void DoctorCommand()
        {
            Execute("Detailing doctor", "audit/repair",
                "{\"fix_roles\": true, \"fix_main_parts\": true}",
                DoctorCommandHandler.Repair);
        }

        /// <summary>Ribbon: Auditoria - BOM / Computo.</summary>
        [CommandMethod("EMV_BOM")]
        public void BomCommand()
        {
            Execute("BOM / computo", "production/bom",
                "{\"group_by\": \"profile\"}",
                BomCommandHandler.GenerateBom);
        }

        /// <summary>
        /// Ribbon: Auditoria - Captura 3D. The handler hands the frame back as base64; the
        /// command writes it to a PNG so the detailer gets a file, not a blob on the prompt.
        /// </summary>
        [CommandMethod("EMV_VIEWPORT")]
        public void ViewportCommand()
        {
            var result = Execute("Captura 3D", "viewport/capture",
                "{\"width\": 1600, \"height\": 900}",
                ViewportCommandHandler.Capture);

            var ed = ActiveEditor();
            if (ed == null || result == null || !result.Success) return;

            var base64 = ReadStringProperty(result.Data, "image_base64");
            if (string.IsNullOrEmpty(base64)) return;

            try
            {
                var path = Path.Combine(
                    Path.GetTempPath(),
                    $"emv_viewport_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                File.WriteAllBytes(path, Convert.FromBase64String(base64!));
                ed.WriteMessage($"\n[EMV-MCP-AS] Captura guardada en: {path}\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[EMV-MCP-AS] No se pudo guardar la captura: {ex.Message}\n");
            }
        }

        // ------------------------------------------------------------------
        // Transaction boundary
        // ------------------------------------------------------------------

        /// <summary>
        /// Runs a single handler under the document lock plus the AutoCAD/Advance Steel
        /// transaction pair, then prints the outcome on the command line.
        /// </summary>
        private static CommandResult? Execute(
            string title,
            string route,
            string bodyJson,
            Func<CommandContext, CommandResult> handler,
            string? extraJson = null)
        {
            var doc = AcApplication.DocumentManager.MdiActiveDocument;
            var ed = doc?.Editor;
            if (doc == null || ed == null) return null;

            ed.WriteMessage($"\n[EMV-MCP-AS] {title}...\n");

            CommandResult result;
            using (var session = new ModelSession(doc))
            {
                result = session.Run(route, bodyJson, handler, extraJson);
                if (result.Success) session.Commit(); else session.Abort();
            }

            Report(ed, title, result);
            return result;
        }

        /// <summary>
        /// One document lock, one AutoCAD transaction and one Advance Steel transaction shared by
        /// every handler call of a command, so multi-step commands commit or roll back as a unit.
        /// Mirrors CommandDispatcher's boundary; handlers never open their own.
        /// </summary>
        private sealed class ModelSession : IDisposable
        {
            private readonly Document _doc;
            private readonly DocumentLock _docLock;
            private readonly AcTransaction _acadTrans;
            private readonly AsTransaction? _asTrans;
            private bool _settled;

            public ModelSession(Document doc)
            {
                _doc = doc;
                _docLock = doc.LockDocument();
                _acadTrans = doc.TransactionManager.StartTransaction();
                _asTrans = StartAdvanceSteelTransaction();
            }

            public CommandResult Run(
                string route,
                string bodyJson,
                Func<CommandContext, CommandResult> handler,
                string? extraJson = null)
            {
                try
                {
                    var context = new CommandContext(_doc, _acadTrans, "POST", route, Merge(bodyJson, extraJson));
                    return handler(context);
                }
                catch (System.Exception ex)
                {
                    return CommandResult.Fail(
                        "TRANSACTION_ABORTED", ex.Message, 500,
                        "The model was rolled back. Review the command parameters and the model state.",
                        ex.StackTrace);
                }
            }

            public void Commit()
            {
                if (_settled) return;
                _settled = true;
                try { _asTrans?.Commit(); } catch (System.Exception) { /* AS layer already unwound */ }
                _acadTrans.Commit();
            }

            public void Abort()
            {
                if (_settled) return;
                _settled = true;
                TryAbort(_asTrans);
                TryAbort(_acadTrans);
            }

            public void Dispose()
            {
                Abort();
                try { _asTrans?.Dispose(); } catch (System.Exception) { /* already unwinding */ }
                try { _acadTrans.Dispose(); } catch (System.Exception) { /* already unwinding */ }
                try { _docLock.Dispose(); } catch (System.Exception) { /* already unwinding */ }
            }
        }

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
                // Plain DWG without an Advance Steel database: AutoCAD-only commands such as the
                // viewport capture still work, AS-specific ones fail with their own error.
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

        // ------------------------------------------------------------------
        // Editor input and reporting
        // ------------------------------------------------------------------

        private static Editor? ActiveEditor() => AcApplication.DocumentManager.MdiActiveDocument?.Editor;

        private static bool TryGetDouble(Editor ed, string message, double defaultValue, out double value)
        {
            var options = new PromptDoubleOptions($"\n{message}")
            {
                DefaultValue = defaultValue,
                UseDefaultValue = true,
                AllowNegative = false,
                AllowZero = false
            };

            var result = ed.GetDouble(options);
            value = result.Value;

            if (result.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n[EMV-MCP-AS] Cancelado por el usuario.\n");
                return false;
            }

            return true;
        }

        private static void Report(Editor ed, string title, CommandResult result)
        {
            ed.WriteMessage($"\n[EMV-MCP-AS] {title}: {(result.Success ? "OK" : "FALLO")}\n");
            foreach (var line in DescribeScalars(result.Success ? result.Data : result.Error))
            {
                ed.WriteMessage($"  - {line}\n");
            }
        }

        /// <summary>Reports one step of a multi-step command; returns false so the caller can roll back.</summary>
        private static bool ReportStep(Editor ed, string step, CommandResult result)
        {
            if (result.Success)
            {
                ed.WriteMessage($"  + {step}: OK\n");
                return true;
            }

            ed.WriteMessage($"  ! {step}: FALLO\n");
            foreach (var line in DescribeScalars(result.Error))
            {
                ed.WriteMessage($"    - {line}\n");
            }

            return false;
        }

        /// <summary>
        /// Flattens the scalar fields of a handler's anonymous payload for the command line.
        /// Collections are skipped: handle lists belong in the MCP response, not on the prompt.
        /// </summary>
        private static IEnumerable<string> DescribeScalars(object? payload)
        {
            if (payload == null) yield break;

            foreach (var property in payload.GetType().GetProperties())
            {
                object? value;
                try { value = property.GetValue(payload); }
                catch (System.Exception) { continue; }

                if (value == null) continue;

                if (value is string text)
                {
                    if (text.Length > 160) text = text.Substring(0, 160) + "...";
                    yield return $"{property.Name}: {text}";
                }
                else if (value is bool || value is int || value is long || value is double || value is decimal)
                {
                    yield return $"{property.Name}: {Convert.ToString(value, CultureInfo.InvariantCulture)}";
                }
            }
        }

        private static string? ReadStringProperty(object? payload, string name)
        {
            try { return payload?.GetType().GetProperty(name)?.GetValue(payload) as string; }
            catch (System.Exception) { return null; }
        }

        // ------------------------------------------------------------------
        // Body building
        // ------------------------------------------------------------------

        /// <summary>Builds the JSON body a handler expects out of the numeric prompt answers.</summary>
        private static string Json(params (string Name, double Value)[] fields)
        {
            var parts = new List<string>(fields.Length);
            foreach (var (name, value) in fields)
            {
                parts.Add($"\"{name}\": {value.ToString("0.####", CultureInfo.InvariantCulture)}");
            }

            return "{" + string.Join(", ", parts) + "}";
        }

        /// <summary>Splices pre-rendered JSON fragments (vectors, labels, names) into a numeric body.</summary>
        private static string Merge(string bodyJson, string? extraJson)
        {
            if (string.IsNullOrWhiteSpace(extraJson)) return bodyJson;

            var trimmed = bodyJson.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
            {
                return "{" + extraJson + "}";
            }

            var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
            return inner.Length == 0
                ? "{" + extraJson + "}"
                : "{" + inner + ", " + extraJson + "}";
        }
    }
}
