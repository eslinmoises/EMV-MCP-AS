using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Modelling;

using eObjectType = Autodesk.AdvanceSteel.CADAccess.FilerObject.eObjectType;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// Autonomous "Detailing Doctor" engine for automated diagnosis and repair of modeling defects.
    /// SPEC-004 §1: apply_detailing_repairs (POST /api/v1/audit/repair).
    /// </summary>
    public static class DoctorCommandHandler
    {
        public static CommandResult Repair(CommandContext ctx)
        {
            var elementHandles = ctx.GetStringList("element_handles");
            bool fixRoles = ctx.GetBool("fix_roles", true);
            bool fixMainParts = ctx.GetBool("fix_main_parts", true);

            var targets = new List<AtomicElement>();

            if (elementHandles.Count > 0)
            {
                foreach (var h in elementHandles)
                {
                    if (AsQuery.OpenByHandle(h) is AtomicElement atomic)
                    {
                        targets.Add(atomic);
                    }
                }
            }
            else
            {
                var objectIds = AsQuery.ModelObjectIds(eObjectType.kAtomicElem);
                foreach (var id in objectIds)
                {
                    if (AsQuery.Open(id) is AtomicElement atomic)
                    {
                        targets.Add(atomic);
                    }
                }
            }

            int repairsApplied = 0;
            var rolesUpdated = new List<object>();
            var mainPartsAssigned = new List<object>();
            var warnings = new List<string>();

            // 1. Repair missing or unassigned model roles
            if (fixRoles)
            {
                foreach (var atomic in targets)
                {
                    var currentRole = AsQuery.Safe(() => atomic.Role, null);
                    if (string.IsNullOrWhiteSpace(currentRole) ||
                        string.Equals(currentRole, "None", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(currentRole, "Standard", StringComparison.OrdinalIgnoreCase))
                    {
                        string inferredRole = "Beam";

                        if (atomic is StraightBeam beam)
                        {
                            var ptStart = AsQuery.Safe<Autodesk.AdvanceSteel.Geometry.Point3d>(() => beam.GetPointAtStart(), Autodesk.AdvanceSteel.Geometry.Point3d.kOrigin);
                            var ptEnd = AsQuery.Safe<Autodesk.AdvanceSteel.Geometry.Point3d>(() => beam.GetPointAtEnd(), Autodesk.AdvanceSteel.Geometry.Point3d.kOrigin);
                            var len = ptStart.DistanceTo(ptEnd);
                            if (len > 0.1)
                            {
                                var dir = ptEnd.Subtract(ptStart);
                                dir.Normalize();
                                if (Math.Abs(dir.z) > 0.75)
                                {
                                    inferredRole = "Column";
                                }
                                else if (Math.Abs(dir.z) > 0.05)
                                {
                                    inferredRole = "Rafter";
                                }
                                else
                                {
                                    inferredRole = "Beam";
                                }
                            }
                        }
                        else if (atomic is PlateBase plate)
                        {
                            inferredRole = "Plate";
                            var normal = AsQuery.Safe<Autodesk.AdvanceSteel.Geometry.Vector3d>(() => plate.PlateNormal, Autodesk.AdvanceSteel.Geometry.Vector3d.kZAxis);
                            if (Math.Abs(normal.z) > 0.8)
                            {
                                inferredRole = "BasePlate";
                            }
                        }

                        try
                        {
                            atomic.Role = inferredRole;
                            repairsApplied++;
                            rolesUpdated.Add(new
                            {
                                handle = atomic.Handle,
                                previous_role = currentRole ?? "empty",
                                assigned_role = inferredRole
                            });
                        }
                        catch (System.Exception ex)
                        {
                            warnings.Add($"Failed to update role on handle {atomic.Handle}: {ex.Message}");
                        }
                    }
                }
            }

            // 2. Repair missing Main Parts on assemblies
            if (fixMainParts)
            {
                var assemblyGroups = new Dictionary<string, List<AtomicElement>>(StringComparer.OrdinalIgnoreCase);

                foreach (var atomic in targets)
                {
                    var assMark = AsQuery.Safe(() => atomic.GetMainPartPositionNumber(), null);
                    if (string.IsNullOrWhiteSpace(assMark)) continue;

                    if (!assemblyGroups.TryGetValue(assMark, out var list))
                    {
                        list = new List<AtomicElement>();
                        assemblyGroups[assMark] = list;
                    }
                    list.Add(atomic);
                }

                foreach (var kvp in assemblyGroups)
                {
                    var assMark = kvp.Key;
                    var members = kvp.Value;

                    bool hasMainPart = members.Any(m => AsQuery.Safe(() => m.IsMainPart, false));

                    if (!hasMainPart && members.Count > 0)
                    {
                        // Designate the heaviest primary profile as main part
                        var primaryProfiles = members.Where(AsQuery.IsPrimaryProfile).ToList();
                        var candidates = primaryProfiles.Count > 0 ? primaryProfiles : members;

                        var best = candidates.OrderByDescending(AsQuery.WeightKg).First();

                        try
                        {
                            best.IsMainPart = true;
                            repairsApplied++;
                            mainPartsAssigned.Add(new
                            {
                                assembly_mark = assMark,
                                main_part_handle = best.Handle,
                                type = AsQuery.TypeName(best)
                            });
                        }
                        catch (System.Exception ex)
                        {
                            warnings.Add($"Failed to set main part on assembly {assMark}: {ex.Message}");
                        }
                    }
                }
            }

            return CommandResult.Ok(new
            {
                repairs_applied = repairsApplied,
                roles_updated = rolesUpdated,
                main_parts_assigned = mainPartsAssigned,
                orphans_resolved = 0,
                warnings
            });
        }
    }
}
