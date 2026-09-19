using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Modelling;

// Advance Steel declares these enums nested inside the class they belong to.
using eAssemblyLocation = Autodesk.AdvanceSteel.ConstructionTypes.AtomicElement.eAssemblyLocation;
using eObjectType = Autodesk.AdvanceSteel.CADAccess.FilerObject.eObjectType;
using eSeamPosition = Autodesk.AdvanceSteel.Modelling.WeldPattern.eSeamPosition;
using eWeldType = Autodesk.AdvanceSteel.Modelling.WeldPattern.eWeldType;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// GET /api/v1/assembly/verify-welds — SPEC-002 §3 "Weld Verification Response".
    ///
    /// rules/advance-steel-modeling.md §3.1 is the rule that matters here: a Workshop weld binds
    /// its parts into the same shop assembly, a Site weld does not. Reporting that distinction
    /// correctly is what lets an agent reason about fabrication scope.
    /// </summary>
    public static class WeldCommandHandler
    {
        public static CommandResult VerifyWelds(CommandContext ctx)
        {
            var requestedHandles = ctx.GetStringList("element_handles");

            List<WeldPattern> welds;
            try
            {
                welds = requestedHandles.Count > 0
                    ? CollectWeldsForElements(requestedHandles, out var unresolved)
                    : CollectAllWelds(out unresolved);

                if (requestedHandles.Count > 0 && unresolved.Count == requestedHandles.Count)
                {
                    return CommandResult.Fail(
                        "HANDLE_NOT_FOUND",
                        $"None of the requested handles resolved to an Advance Steel element: {string.Join(", ", unresolved)}.",
                        404,
                        "Call get_selected_elements to obtain valid handles from the current model.");
                }
            }
            catch (System.Exception ex)
            {
                return CommandResult.Fail(
                    "WELD_QUERY_FAILED", ex.Message, 500,
                    "Ensure the active drawing is an Advance Steel model.",
                    ex.StackTrace);
            }

            var reports = new List<object>();
            var workshopCount = 0;
            var siteCount = 0;

            foreach (var weld in welds)
            {
                var report = Inspect(weld);
                reports.Add(report.Payload);

                if (report.Location == eAssemblyLocation.kInShop) workshopCount++;
                else if (report.Location == eAssemblyLocation.kOnSite
                         || report.Location == eAssemblyLocation.kSiteDrill) siteCount++;
            }

            return CommandResult.Ok(new
            {
                welds = reports,
                total_workshop_welds = workshopCount,
                total_site_welds = siteCount,
                total_welds = reports.Count
            });
        }

        // ------------------------------------------------------------------
        // Collection
        // ------------------------------------------------------------------

        private static List<WeldPattern> CollectAllWelds(out List<string> unresolved)
        {
            unresolved = new List<string>();
            var welds = new List<WeldPattern>();

            foreach (var id in AsQuery.ModelObjectIds(eObjectType.kWeldPattern))
            {
                if (AsQuery.Open(id) is WeldPattern weld) welds.Add(weld);
            }

            return welds;
        }

        /// <summary>
        /// Walks out from the requested elements to the welds attached to them. A weld touching
        /// two requested elements is reported once.
        /// </summary>
        private static List<WeldPattern> CollectWeldsForElements(
            IEnumerable<string> handles, out List<string> unresolved)
        {
            unresolved = new List<string>();
            var welds = new List<WeldPattern>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var handle in handles)
            {
                var element = AsQuery.OpenByHandle(handle);
                if (element == null)
                {
                    unresolved.Add(handle);
                    continue;
                }

                if (element is WeldPattern directWeld)
                {
                    AddOnce(welds, seen, directWeld);
                    continue;
                }

                if (element is not AtomicElement atomic)
                {
                    unresolved.Add(handle);
                    continue;
                }

                foreach (var neighbour in ConnectedObjects(atomic))
                {
                    if (neighbour is WeldPattern weld) AddOnce(welds, seen, weld);
                }
            }

            return welds;
        }

        private static void AddOnce(List<WeldPattern> welds, HashSet<string> seen, WeldPattern weld)
        {
            var handle = AsQuery.Safe(() => weld.Handle, null);
            if (handle != null && !seen.Add(handle)) return;
            welds.Add(weld);
        }

        private static IEnumerable<FilerObject> ConnectedObjects(AtomicElement element)
        {
            IEnumerable<Autodesk.AdvanceSteel.CADLink.Database.ObjectId>? ids;

            try
            {
                element.GetConnectedObjects(out ids, eAssemblyLocation.kUnknown);
            }
            catch (System.Exception)
            {
                yield break;
            }

            foreach (var id in ids ?? Enumerable.Empty<Autodesk.AdvanceSteel.CADLink.Database.ObjectId>())
            {
                var resolved = AsQuery.Open(id);
                if (resolved != null) yield return resolved;
            }
        }

        // ------------------------------------------------------------------
        // Inspection
        // ------------------------------------------------------------------

        private readonly struct WeldReport
        {
            public WeldReport(object payload, eAssemblyLocation location)
            {
                Payload = payload;
                Location = location;
            }

            public object Payload { get; }
            public eAssemblyLocation Location { get; }
        }

        private static WeldReport Inspect(WeldPattern weld)
        {
            var location = AsQuery.Safe(() => weld.AssemblyLocation, eAssemblyLocation.kUnknown);
            var connected = ConnectedElements(weld);

            var mainPart = FindMainPart(connected);
            var attached = new List<object>();
            string? mainPartHandle = mainPart == null ? null : AsQuery.Safe(() => mainPart.Handle, null);
            string? connectedPartHandle = null;

            foreach (var part in connected)
            {
                var handle = AsQuery.Safe(() => part.Handle, null);
                if (handle != null && handle == mainPartHandle) continue;

                connectedPartHandle ??= handle;
                attached.Add(new
                {
                    handle,
                    role = AsQuery.Safe(() => part.Role, null),
                    assembly_mark = AsQuery.Safe(() => part.GetMainPartPositionNumber(), null)
                });
            }

            // rules/advance-steel-modeling.md §3.1: only a workshop weld merges the connected
            // parts into one shop assembly. A site weld leaves them in separate assemblies even
            // when their assembly marks happen to match.
            var isSameAssembly = location == eAssemblyLocation.kInShop && SharesAssembly(connected);

            var payload = new
            {
                weld_handle = AsQuery.Safe(() => weld.Handle, null),
                weld_type = WeldTypeName(weld),
                throat_thickness = ThroatThickness(weld),
                location = AsQuery.LocationName(location),
                location_raw = location.ToString(),
                main_part_handle = mainPartHandle,
                connected_part_handle = connectedPartHandle,
                connected_parts = attached,
                connected_part_count = connected.Count,
                is_same_assembly = isSameAssembly,
                is_workshop_weld = location == eAssemblyLocation.kInShop,
                prefix = AsQuery.Safe(() => weld.Prefix, null),
                single_seam_length = AsQuery.Safe(() => weld.SingleSeamLength, 0.0),
                is_closed = AsQuery.Safe(() => weld.IsClosed, false)
            };

            return new WeldReport(payload, location);
        }

        private static List<AtomicElement> ConnectedElements(WeldPattern weld)
        {
            var parts = new List<AtomicElement>();
            if (weld == null) return parts;

            try
            {
                var weldHandle = AsQuery.Safe(() => weld.Handle, null);
                if (string.IsNullOrWhiteSpace(weldHandle)) return parts;

                foreach (var id in AsQuery.ModelObjectIds(eObjectType.kAtomicElem))
                {
                    if (AsQuery.Open(id) is AtomicElement atomic)
                    {
                        if (atomic is WeldPattern || atomic is BoltPattern) continue;

                        IEnumerable<Autodesk.AdvanceSteel.CADLink.Database.ObjectId>? connIds;
                        try
                        {
                            atomic.GetConnectedObjects(out connIds, eAssemblyLocation.kUnknown);
                        }
                        catch
                        {
                            continue;
                        }

                        if (connIds == null) continue;

                        foreach (var cId in connIds)
                        {
                            var connectedObj = AsQuery.Open(cId);
                            if (connectedObj is WeldPattern wp &&
                                string.Equals(AsQuery.Safe(() => wp.Handle, null), weldHandle, StringComparison.OrdinalIgnoreCase))
                            {
                                parts.Add(atomic);
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback gracefully without throwing
            }

            return parts;
        }

        private static AtomicElement? FindMainPart(IEnumerable<AtomicElement> parts)
        {
            AtomicElement? fallback = null;

            foreach (var part in parts)
            {
                if (AsQuery.Safe(() => part.IsMainPart, false)) return part;
                fallback ??= part;
            }

            return fallback;
        }

        private static bool SharesAssembly(IReadOnlyList<AtomicElement> parts)
        {
            if (parts.Count < 2) return parts.Count == 1;

            var reference = AsQuery.Safe(() => parts[0].GetMainPartPositionNumber(), null);
            if (string.IsNullOrWhiteSpace(reference)) return false;

            for (var i = 1; i < parts.Count; i++)
            {
                var mark = AsQuery.Safe(() => parts[i].GetMainPartPositionNumber(), null);
                if (!string.Equals(reference, mark, StringComparison.OrdinalIgnoreCase)) return false;
            }

            return true;
        }

        /// <summary>
        /// Throat thickness ("garganta"). Advance Steel keeps an upper and a lower seam; the upper
        /// seam is the main one, and the effective throat overrides the nominal seam thickness when
        /// the detailer has set it.
        /// </summary>
        private static double ThroatThickness(WeldPattern weld)
        {
            var effective = AsQuery.Safe(() => weld.MainEffectiveThroat, 0.0);
            if (effective > 0.0) return Math.Round(effective, 3);

            var upperSeam = AsQuery.Safe(() => weld.GetSeamThickness(eSeamPosition.kUpper), 0.0);
            if (upperSeam > 0.0) return Math.Round(upperSeam, 3);

            return Math.Round(AsQuery.Safe(() => weld.Thickness, 0.0), 3);
        }

        /// <summary>Maps <c>eWeldType</c> onto the Fillet / Butt / … vocabulary used by SPEC-002.</summary>
        private static string WeldTypeName(WeldPattern weld)
        {
            var type = AsQuery.Safe(() => weld.GetWeldType(eSeamPosition.kUpper), eWeldType.kIWeld);

            return type switch
            {
                eWeldType.kTWeld => "Fillet",
                eWeldType.kDoubleFilletWeld => "DoubleFillet",
                eWeldType.kIWeld => "Butt",
                eWeldType.kDIWeld => "Butt",
                eWeldType.kVWeld => "Butt",
                eWeldType.kHVWeld => "Butt",
                eWeldType.kYWeld => "Butt",
                eWeldType.kHYWeld => "Butt",
                eWeldType.kUWeld => "Butt",
                eWeldType.kHUWeld => "Butt",
                eWeldType.kXWeld => "Butt",
                eWeldType.kKWeld => "Butt",
                eWeldType.kSquareEdgeWeld => "Butt",
                eWeldType.kHalfSquareEdgeWeld => "Butt",
                eWeldType.kFlangeButtWelding => "Butt",
                eWeldType.kSpotWeld => "Spot",
                eWeldType.kPerforationWeld => "Plug",
                eWeldType.kSeamWeld => "Seam",
                eWeldType.kLineWeld => "Seam",
                _ => type.ToString()
            };
        }
    }
}
