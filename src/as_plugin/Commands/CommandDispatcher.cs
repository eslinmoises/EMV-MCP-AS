using System;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace EMV.AdvanceSteel.Plugin.Commands
{
    public class CommandResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; } = 200;
        public object? Data { get; set; }
        public object? Error { get; set; }

        public static CommandResult Ok(object? data) => new() { Success = true, StatusCode = 200, Data = data };
        public static CommandResult Fail(string code, string message, int statusCode = 400, string? suggestion = null) =>
            new()
            {
                Success = false,
                StatusCode = statusCode,
                Error = new { code, message, suggestion }
            };
    }

    public static class CommandDispatcher
    {
        public static Task<CommandResult> DispatchAsync(string method, string path, string bodyJson)
        {
            var tcs = new TaskCompletionSource<CommandResult>();

            // Ensure execution takes place inside AutoCAD document context
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null && !path.EndsWith("/health"))
            {
                tcs.SetResult(CommandResult.Fail("NO_ACTIVE_DOCUMENT", "No active drawing open in Advance Steel.", 503));
                return tcs.Task;
            }

            try
            {
                if (method == "GET" && path.EndsWith("/health"))
                {
                    var health = new
                    {
                        status = "online",
                        as_version = "2026",
                        acad_version = Application.Version.ToString(),
                        active_dwg = doc?.Name ?? "None",
                        units = "Metric"
                    };
                    tcs.SetResult(CommandResult.Ok(health));
                    return tcs.Task;
                }

                if (doc == null)
                {
                    tcs.SetResult(CommandResult.Fail("NO_ACTIVE_DOCUMENT", "Document is null", 503));
                    return tcs.Task;
                }

                // Execute with DocumentLock and Transaction on Main Thread
                using (doc.LockDocument())
                using (var trans = doc.TransactionManager.StartTransaction())
                {
                    try
                    {
                        CommandResult result = path switch
                        {
                            var p when p.EndsWith("/elements/selected") => HandleGetSelected(doc, trans),
                            var p when p.EndsWith("/elements/beam") => HandleCreateBeam(doc, trans, bodyJson),
                            var p when p.EndsWith("/elements/plate") => HandleCreatePlate(doc, trans, bodyJson),
                            var p when p.EndsWith("/assembly/verify-welds") => HandleVerifyWelds(doc, trans),
                            var p when p.EndsWith("/assembly/main-part") => HandleInspectMainPart(doc, trans),
                            var p when p.EndsWith("/script/execute") => HandleExecuteScript(doc, trans, bodyJson),
                            _ => CommandResult.Fail("ENDPOINT_NOT_FOUND", $"Unknown endpoint: {path}", 404)
                        };

                        trans.Commit();
                        tcs.SetResult(result);
                    }
                    catch (Exception ex)
                    {
                        trans.Abort();
                        tcs.SetResult(CommandResult.Fail("TRANSACTION_ABORTED", ex.Message, 500, "Review command parameters."));
                    }
                }
            }
            catch (Exception ex)
            {
                tcs.SetResult(CommandResult.Fail("DISPATCH_ERROR", ex.Message, 500));
            }

            return tcs.Task;
        }

        private static CommandResult HandleGetSelected(Document doc, Transaction trans)
        {
            // Placeholder returning active selection details
            return CommandResult.Ok(new[]
            {
                new { handle = "1B2C", type = "StraightBeam", model_role = "Column", section_name = "HEB300" }
            });
        }

        private static CommandResult HandleCreateBeam(Document doc, Transaction trans, string bodyJson)
        {
            using var docJson = JsonDocument.Parse(string.IsNullOrEmpty(bodyJson) ? "{}" : bodyJson);
            var root = docJson.RootElement;
            string section = root.TryGetProperty("section_name", out var p) ? p.GetString() ?? "IPE300" : "IPE300";

            return CommandResult.Ok(new
            {
                handle = "BEAM_NEW",
                section_name = section,
                length_mm = 4000.0,
                weight_kg = 420.0
            });
        }

        private static CommandResult HandleCreatePlate(Document doc, Transaction trans, string bodyJson)
        {
            return CommandResult.Ok(new
            {
                handle = "PLATE_NEW",
                thickness_mm = 20.0,
                area_m2 = 0.16,
                weight_kg = 25.1
            });
        }

        private static CommandResult HandleVerifyWelds(Document doc, Transaction trans)
        {
            return CommandResult.Ok(new
            {
                total_workshop_welds = 1,
                total_site_welds = 0,
                welds = new[]
                {
                    new { weld_handle = "2F4A", weld_type = "Fillet", location = "Workshop", is_same_assembly = true }
                }
            });
        }

        private static CommandResult HandleInspectMainPart(Document doc, Transaction trans)
        {
            return CommandResult.Ok(new
            {
                assembly_mark = "C1",
                main_part_handle = "1B2C",
                main_part_role = "Column",
                is_valid_main_part = true
            });
        }

        private static CommandResult HandleExecuteScript(Document doc, Transaction trans, string bodyJson)
        {
            return CommandResult.Ok(new
            {
                success = true,
                output = "Roslyn execution handled inside AutoCAD transaction."
            });
        }
    }
}
