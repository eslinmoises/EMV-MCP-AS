using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Modelling;

// Advance Steel declares these enums nested inside the class they belong to.
using eAssemblyLocation = Autodesk.AdvanceSteel.ConstructionTypes.AtomicElement.eAssemblyLocation;
using eObjectType = Autodesk.AdvanceSteel.CADAccess.FilerObject.eObjectType;

using AsObjectId = Autodesk.AdvanceSteel.CADLink.Database.ObjectId;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// GET /api/v1/audit/assembly-integrity — SPEC-004 §1 <c>audit_assembly_integrity</c>.
    /// GET /api/v1/audit/clashes            — SPEC-004 §1 <c>detect_clashes_and_clearances</c>.
    ///
    /// Both endpoints answer the question a detailer asks before releasing a model to the shop:
    /// "will this actually fabricate?". The integrity scan enforces
    /// rules/advance-steel-modeling.md §3 — a plate that is not bound to a primary profile by a
    /// workshop weld is not part of any shop assembly, so it will silently vanish from the
    /// fabrication drawings and the NC files. The clash scan finds the geometry that would be
    /// discovered on site instead.
    /// </summary>
    public static class AuditCommandHandler
    {
        // ==================================================================
        // GET audit/assembly-integrity
        // ==================================================================

        public static CommandResult AuditAssemblyIntegrity(CommandContext ctx)
        {
            List<AtomicElement> parts;
            try
            {
                parts = CollectPhysicalParts(ctx.GetStringList("element_handles"), out var unresolved);

                if (unresolved.Count > 0 && parts.Count == 0)
                {
                    return CommandResult.Fail(
                        "HANDLE_NOT_FOUND",
                        $"None of the requested handles resolved to an Advance Steel part: {string.Join(", ", unresolved)}.",
                        404,
                        "Call get_selected_elements to obtain valid handles from the current model.");
                }
            }
            catch (System.Exception ex)
            {
                return CommandResult.Fail(
                    "AUDIT_QUERY_FAILED", ex.Message, 500,
                    "Ensure the active drawing is an Advance Steel model.",
                    ex.StackTrace);
            }

            var findings = new List<object>();
            var orphanedHandles = new List<string?>();
            var unnumberedHandles = new List<string?>();
            var errorCount = 0;
            var warningCount = 0;

            void Report(
                string code, string severity, string message,
                string? handle, string? type, string? role, string? assemblyMark, string suggestion)
            {
                if (severity == "error") errorCount++;
                else warningCount++;

                findings.Add(new
                {
                    code,
                    severity,
                    message,
                    handle,
                    type,
                    model_role = role,
                    assembly_mark = assemblyMark,
                    suggestion
                });
            }

            // Assembly mark -> how many members claim to be the Main Part. Evaluated after the
            // per-part scan so that "no Main Part" is reported once per assembly, not once per part.
            var mainPartsPerMark = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);
            var membersPerMark = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var part in parts)
            {
                var handle = AsQuery.Safe(() => part.Handle, null);
                var role = AsQuery.Safe(() => part.Role, null);
                var typeName = AsQuery.TypeName(part);
                var assemblyMark = AsQuery.Safe(() => part.GetMainPartPositionNumber(), null);
                var singlePartMark = AsQuery.Safe(() => part.GetSinglePartPositionNumber(), null);

                if (!string.IsNullOrWhiteSpace(assemblyMark))
                {
                    membersPerMark.TryGetValue(assemblyMark!, out var count);
                    membersPerMark[assemblyMark!] = count + 1;

                    if (AsQuery.Safe(() => part.IsMainPart, false))
                    {
                        if (!mainPartsPerMark.TryGetValue(assemblyMark!, out var owners))
                        {
                            owners = new List<string?>();
                            mainPartsPerMark[assemblyMark!] = owners;
                        }

                        owners.Add(handle);
                    }
                }

                // rules/advance-steel-modeling.md §2/§3.2: plates, stiffeners, clips and gussets are
                // secondary parts. They only reach the shop if a workshop weld binds them to a
                // primary profile, so they are the parts worth auditing.
                var isSecondary = part is PlateBase || AsQuery.IsSecondaryRole(role);

                if (isSecondary && !AsQuery.Safe(() => part.IsMainPart, false))
                {
                    var workshop = InspectWorkshopConnections(part);

                    if (!workshop.HasWorkshopWeld)
                    {
                        orphanedHandles.Add(handle);
                        Report(
                            "ORPHANED_PART",
                            "error",
                            $"{typeName} '{handle}' (role '{role ?? "unset"}') has no workshop weld. "
                            + "It belongs to no shop assembly and will not appear on any fabrication drawing.",
                            handle, typeName, role, assemblyMark,
                            "Add a workshop weld between this part and the primary profile it sits on, "
                            + "or delete the part if it is leftover geometry.");
                    }
                    else if (!workshop.IsWeldedToPrimaryProfile)
                    {
                        orphanedHandles.Add(handle);
                        Report(
                            "UNANCHORED_PART",
                            "error",
                            $"{typeName} '{handle}' (role '{role ?? "unset"}') is workshop-welded only to other "
                            + "secondary parts, never to a primary profile. The resulting assembly has no valid Main Part.",
                            handle, typeName, role, assemblyMark,
                            "Weld the part chain to the column shaft, rafter or girder it is meant to reinforce.");
                    }
                }

                if (string.IsNullOrWhiteSpace(singlePartMark) || string.IsNullOrWhiteSpace(assemblyMark))
                {
                    unnumberedHandles.Add(handle);
                    Report(
                        "UNNUMBERED_PART",
                        "warning",
                        $"{typeName} '{handle}' is missing its "
                        + (string.IsNullOrWhiteSpace(singlePartMark) ? "single part mark" : "assembly mark")
                        + ". Unnumbered parts are skipped by the BOM and the NC export.",
                        handle, typeName, role, assemblyMark,
                        "Run the Advance Steel numbering command over the whole model before releasing it.");
                }
            }

            foreach (var entry in membersPerMark)
            {
                mainPartsPerMark.TryGetValue(entry.Key, out var owners);
                var mainPartCount = owners?.Count ?? 0;

                if (mainPartCount == 0)
                {
                    Report(
                        "ASSEMBLY_WITHOUT_MAIN_PART",
                        "error",
                        $"Assembly '{entry.Key}' groups {entry.Value} part(s) but declares no Main Part. "
                        + "Its numbering prefix, drawing orientation and DSTV origin are undefined.",
                        null, "Assembly", null, entry.Key,
                        "Call set_main_part with the primary profile of this assembly.");
                }
                else if (mainPartCount > 1)
                {
                    Report(
                        "MULTIPLE_MAIN_PARTS",
                        "error",
                        $"Assembly '{entry.Key}' declares {mainPartCount} Main Parts "
                        + $"({string.Join(", ", owners!)}); exactly one is allowed.",
                        null, "Assembly", null, entry.Key,
                        "Clear the Main Part flag on every part except the primary profile.");
                }
            }

            return CommandResult.Ok(new
            {
                findings,
                orphaned_parts = orphanedHandles,
                unnumbered_parts = unnumberedHandles,
                summary = new
                {
                    scanned_parts = parts.Count,
                    assemblies = membersPerMark.Count,
                    orphaned_count = orphanedHandles.Count,
                    unnumbered_count = unnumberedHandles.Count,
                    error_count = errorCount,
                    warning_count = warningCount
                },
                is_model_valid = errorCount == 0
            });
        }

        private readonly struct WorkshopConnectivity
        {
            public WorkshopConnectivity(bool hasWorkshopWeld, bool isWeldedToPrimaryProfile)
            {
                HasWorkshopWeld = hasWorkshopWeld;
                IsWeldedToPrimaryProfile = isWeldedToPrimaryProfile;
            }

            public bool HasWorkshopWeld { get; }
            public bool IsWeldedToPrimaryProfile { get; }
        }

        /// <summary>
        /// Walks the workshop connections of a single part. Site connections are deliberately
        /// ignored: per rules/advance-steel-modeling.md §3.1 they never merge parts into one shop
        /// assembly, so a plate held only by a site weld is still an orphan for the shop.
        /// </summary>
        private static WorkshopConnectivity InspectWorkshopConnections(AtomicElement part)
        {
            var hasWorkshopWeld = false;
            var weldedToPrimary = false;

            foreach (var neighbour in ConnectedObjects(part, eAssemblyLocation.kInShop))
            {
                if (neighbour is WeldPattern weld)
                {
                    if (AsQuery.Safe(() => weld.AssemblyLocation, eAssemblyLocation.kUnknown) == eAssemblyLocation.kInShop)
                    {
                        hasWorkshopWeld = true;
                    }

                    continue;
                }

                // Bolts and shear studs are connection means, not parts.
                if (neighbour is BoltPattern || neighbour is Connector) continue;

                if (neighbour is AtomicElement atomic && AsQuery.IsPrimaryProfile(atomic)) weldedToPrimary = true;
            }

            return new WorkshopConnectivity(hasWorkshopWeld, hasWorkshopWeld && weldedToPrimary);
        }

        // ==================================================================
        // GET audit/clashes
        // ==================================================================

        /// <summary>Faces that merely touch are not a clash; anything below this is ignored (mm).</summary>
        private const double DefaultToleranceMm = 1.0;

        private const int DefaultMaxClashes = 200;

        /// <summary>
        /// The pairwise scan is O(n log n) after the sweep, but every candidate pair still costs an
        /// open + extents read. This cap keeps a whole-building model inside the 30 s request budget.
        /// </summary>
        private const int MaxScannedParts = 5000;

        public static CommandResult DetectClashes(CommandContext ctx)
        {
            var tolerance = Math.Max(0.0, ctx.GetDouble("tolerance", DefaultToleranceMm));
            var maxClashes = Math.Max(1, (int)ctx.GetDouble("max_clashes", DefaultMaxClashes));

            List<AtomicElement> parts;
            try
            {
                parts = CollectPhysicalParts(ctx.GetStringList("element_handles"), out var unresolved);

                if (unresolved.Count > 0 && parts.Count == 0)
                {
                    return CommandResult.Fail(
                        "HANDLE_NOT_FOUND",
                        $"None of the requested handles resolved to an Advance Steel part: {string.Join(", ", unresolved)}.",
                        404,
                        "Call get_selected_elements to obtain valid handles from the current model.");
                }
            }
            catch (System.Exception ex)
            {
                return CommandResult.Fail(
                    "CLASH_QUERY_FAILED", ex.Message, 500,
                    "Ensure the active drawing is an Advance Steel model.",
                    ex.StackTrace);
            }

            var warnings = new List<string>();

            if (parts.Count > MaxScannedParts)
            {
                warnings.Add(
                    $"The model holds {parts.Count} parts; only the first {MaxScannedParts} were scanned. "
                    + "Pass element_handles to check a specific area.");
                parts = parts.Take(MaxScannedParts).ToList();
            }

            var boxes = new List<PartBox>(parts.Count);
            foreach (var part in parts)
            {
                var box = PartBox.For(part);
                if (box != null) boxes.Add(box);
            }

            if (boxes.Count < parts.Count)
            {
                warnings.Add($"{parts.Count - boxes.Count} part(s) reported no usable bounding box and were skipped.");
            }

            // Sweep and prune along X: once a candidate's minimum X is beyond the current part's
            // maximum X, no later candidate in the sorted list can overlap it either.
            boxes.Sort((a, b) => a.MinX.CompareTo(b.MinX));

            var clashes = new List<object>();
            var truncated = false;

            for (var i = 0; i < boxes.Count && !truncated; i++)
            {
                var a = boxes[i];

                for (var j = i + 1; j < boxes.Count; j++)
                {
                    var b = boxes[j];

                    if (b.MinX - a.MaxX >= -tolerance) break;

                    var overlap = a.OverlapWith(b, tolerance);
                    if (overlap == null) continue;

                    // Parts that are welded or bolted together are supposed to share volume;
                    // reporting them would bury the real interferences in noise.
                    if (AreConnected(a, b)) continue;

                    clashes.Add(new
                    {
                        element_a = a.Describe(),
                        element_b = b.Describe(),
                        overlap_mm = new[] { overlap.Value.X, overlap.Value.Y, overlap.Value.Z },
                        overlap_volume_mm3 = Math.Round(overlap.Value.X * overlap.Value.Y * overlap.Value.Z, 3),
                        max_penetration_mm = Math.Round(
                            Math.Min(overlap.Value.X, Math.Min(overlap.Value.Y, overlap.Value.Z)), 3),
                        center = overlap.Value.Center,
                        severity = overlap.Value.X > 5.0 && overlap.Value.Y > 5.0 && overlap.Value.Z > 5.0
                            ? "error"
                            : "warning"
                    });

                    if (clashes.Count >= maxClashes)
                    {
                        truncated = true;
                        warnings.Add(
                            $"Result truncated at {maxClashes} clashes. Raise max_clashes or narrow the scan "
                            + "with element_handles.");
                        break;
                    }
                }
            }

            return CommandResult.Ok(new
            {
                clashes,
                clash_count = clashes.Count,
                truncated,
                scanned_parts = boxes.Count,
                tolerance_mm = tolerance,
                // Advance Steel 2026 exposes no managed collision-check API, so this is an
                // axis-aligned bounding-box scan: it never misses a real clash, but a pair reported
                // here may still be clear once the actual profile shapes are considered.
                method = "axis_aligned_bounding_box",
                method_note =
                    "Bounding-box interference is conservative. Connected parts (workshop or site) are excluded. "
                    + "Verify a reported pair visually with capture_viewport before reworking the model.",
                warnings
            });
        }

        /// <summary>One part's axis-aligned bounding box, cached so the sweep reads it once.</summary>
        private sealed class PartBox
        {
            private PartBox(AtomicElement part, double[] min, double[] max)
            {
                Part = part;
                Min = min;
                Max = max;
                Handle = AsQuery.Safe(() => part.Handle, null);
            }

            public AtomicElement Part { get; }
            public string? Handle { get; }
            private double[] Min { get; }
            private double[] Max { get; }

            public double MinX => Min[0];
            public double MaxX => Max[0];

            private HashSet<string>? _connected;

            public static PartBox? For(AtomicElement part)
            {
                try
                {
                    var extents = part.GeomExtents;
                    if (extents == null || !extents.IsValid) return null;

                    var min = extents.MinPoint;
                    var max = extents.MaxPoint;

                    return new PartBox(
                        part,
                        new[] { min.x, min.y, min.z },
                        new[] { max.x, max.y, max.z });
                }
                catch (System.Exception)
                {
                    return null;
                }
            }

            public readonly struct Overlap
            {
                public Overlap(double x, double y, double z, double[] center)
                {
                    X = x;
                    Y = y;
                    Z = z;
                    Center = center;
                }

                public double X { get; }
                public double Y { get; }
                public double Z { get; }
                public double[] Center { get; }
            }

            /// <summary>
            /// Overlap extent on each axis. A null result means the boxes are apart, or merely
            /// touching within <paramref name="tolerance"/>.
            /// </summary>
            public Overlap? OverlapWith(PartBox other, double tolerance)
            {
                var sizes = new double[3];
                var center = new double[3];

                for (var axis = 0; axis < 3; axis++)
                {
                    var low = Math.Max(Min[axis], other.Min[axis]);
                    var high = Math.Min(Max[axis], other.Max[axis]);
                    var size = high - low;

                    if (size <= tolerance) return null;

                    sizes[axis] = Math.Round(size, 3);
                    center[axis] = Math.Round((low + high) / 2.0, 3);
                }

                return new Overlap(sizes[0], sizes[1], sizes[2], center);
            }

            /// <summary>Handles of everything physically attached to this part, in shop or on site.</summary>
            public HashSet<string> ConnectedHandles()
            {
                if (_connected != null) return _connected;

                _connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var neighbour in ConnectedObjects(Part, eAssemblyLocation.kUnknown))
                {
                    var handle = AsQuery.Safe(() => neighbour.Handle, null);
                    if (handle != null) _connected.Add(handle);

                    // A weld or bolt sits between the two parts: follow it to the far side so the
                    // pair is recognised as connected from either end.
                    if (neighbour is not WeldPattern && neighbour is not BoltPattern && neighbour is not Connector)
                    {
                        continue;
                    }

                    if (neighbour is not AtomicElement means) continue;

                    foreach (var farSide in ConnectedObjects(means, eAssemblyLocation.kUnknown))
                    {
                        var farHandle = AsQuery.Safe(() => farSide.Handle, null);
                        if (farHandle != null) _connected.Add(farHandle);
                    }
                }

                return _connected;
            }

            public object Describe() => new
            {
                handle = Handle,
                type = AsQuery.TypeName(Part),
                model_role = AsQuery.Safe(() => Part.Role, null),
                section_name = AsQuery.SectionName(Part),
                assembly_mark = AsQuery.Safe(() => Part.GetMainPartPositionNumber(), null)
            };
        }

        private static bool AreConnected(PartBox a, PartBox b)
        {
            if (b.Handle != null && a.ConnectedHandles().Contains(b.Handle)) return true;
            if (a.Handle != null && b.ConnectedHandles().Contains(a.Handle)) return true;
            return false;
        }

        // ==================================================================
        // Shared collection helpers
        // ==================================================================

        /// <summary>
        /// The physical steel of the model: beams, plates and special parts. Welds, bolts and
        /// connectors are excluded — they are connection means, and they carry no fabricable shape.
        /// </summary>
        private static List<AtomicElement> CollectPhysicalParts(
            IReadOnlyList<string> requestedHandles, out List<string> unresolved)
        {
            unresolved = new List<string>();
            var parts = new List<AtomicElement>();

            if (requestedHandles.Count > 0)
            {
                foreach (var handle in requestedHandles)
                {
                    if (AsQuery.OpenByHandle(handle) is AtomicElement atomic && IsPhysicalPart(atomic))
                    {
                        parts.Add(atomic);
                    }
                    else
                    {
                        unresolved.Add(handle);
                    }
                }

                return parts;
            }

            foreach (var id in AsQuery.ModelObjectIds(eObjectType.kAtomicElem))
            {
                if (AsQuery.Open(id) is AtomicElement atomic && IsPhysicalPart(atomic)) parts.Add(atomic);
            }

            return parts;
        }

        private static bool IsPhysicalPart(AtomicElement element) =>
            element is not WeldPattern && element is not BoltPattern && element is not Connector;

        private static IEnumerable<FilerObject> ConnectedObjects(AtomicElement element, eAssemblyLocation location)
        {
            IEnumerable<AsObjectId>? ids;

            try
            {
                element.GetConnectedObjects(out ids, location);
            }
            catch (System.Exception)
            {
                yield break;
            }

            foreach (var id in ids ?? Enumerable.Empty<AsObjectId>())
            {
                var resolved = AsQuery.Open(id);
                if (resolved != null) yield return resolved;
            }
        }
    }
}
