using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Geometry;
using Autodesk.AdvanceSteel.Modelling;

// Advance Steel declares these enums nested inside the class they belong to.
using eEnd = Autodesk.AdvanceSteel.Modelling.Beam.eEnd;
using eNodeStatus = Autodesk.AdvanceSteel.ConstructionTypes.UserAutoConstructionObject.NodeStatus;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// POST /api/v1/elements/joint — SPEC-004 §2 <c>create_standard_joint</c>.
    ///
    /// Advance Steel applies a parametric connection by instantiating a rule from its rule
    /// database (<c>AstorRules</c>) against a set of driving members and the point where the user
    /// would have clicked. <see cref="UserAutoConstructionObject"/> is the managed entry point for
    /// that: it resolves the rule, generates the plates, bolts, welds and processings, and stays
    /// alive as the object that regenerates them when a driver moves.
    ///
    /// Runs inside the dispatcher's DocumentLock + transaction boundary
    /// (rules/transaction-safety.md §3), so a rule that reports an unsupported node rolls the whole
    /// connection back instead of leaving half a joint in the model.
    /// </summary>
    public static class JointCommandHandler
    {
        public static CommandResult Create(CommandContext ctx)
        {
            var jointType = ctx.GetString("joint_type");
            var explicitRule = ctx.GetString("rule_name");

            if (!TryResolveRuleCandidates(jointType, explicitRule, out var candidates, out var ruleError))
            {
                return ruleError!;
            }

            if (!AsQuery.TryResolve<FilerObject>(
                    ctx.GetString("primary_handle"), "primary_handle", out var primary, out var primaryError))
            {
                return primaryError!;
            }

            if (!TryReadEnd(ctx.GetString("primary_end", "Start")!, out var primaryEnd, out var endError))
            {
                return endError!;
            }

            if (!TryReadOptionalPoint(ctx, "connection_point", out var connectionPoint, out var pointError))
            {
                return pointError!;
            }

            var secondaries = new List<FilerObject>();
            foreach (var handle in ctx.GetStringList("secondary_handles"))
            {
                if (!AsQuery.TryResolve<FilerObject>(handle, "secondary_handles", out var secondary, out var error))
                {
                    return error!;
                }

                secondaries.Add(secondary);
            }

            // The driver point of the primary member is what tells the rule which end of the member
            // it is connecting; the secondaries are then picked where they meet that point.
            var primaryPoint = DriverPoint(primary, connectionPoint, primaryEnd);

            var drivers = new List<Tuple<FilerObject, Point3d>> { Tuple.Create(primary, primaryPoint) };
            foreach (var secondary in secondaries)
            {
                drivers.Add(Tuple.Create(
                    secondary, DriverPoint(secondary, connectionPoint ?? primaryPoint, eEnd.kStart)));
            }

            if (!TryReadPointList(ctx, "input_points", out var additionalPoints, out var listError))
            {
                return listError!;
            }

            UserAutoConstructionObject? joint = null;
            string? appliedRule = null;
            var attempts = new List<string>();

            foreach (var rule in candidates)
            {
                try
                {
                    var attempt = new UserAutoConstructionObject(rule, drivers, additionalPoints);
                    attempt.WriteToDb();
                    joint = attempt;
                    appliedRule = rule;
                    break;
                }
                catch (System.Exception ex)
                {
                    attempts.Add(rule + ": " + ex.Message);
                }
            }

            if (joint == null)
            {
                return CommandResult.Fail(
                    "JOINT_RULE_NOT_FOUND",
                    $"None of the rule names tried for '{jointType ?? explicitRule}' could be instantiated.",
                    422,
                    "Pass the exact Advance Steel rule name as 'rule_name'. Rule names live in the AstorRules "
                    + "database of the active country pack and are shown per connection in the Connection "
                    + $"Vault. Known aliases: {string.Join(", ", RuleAliases.Keys)}.",
                    string.Join(" | ", attempts));
            }

            // Ask the rule to build its parts now, while still inside the transaction, so that a
            // failure is rolled back together with everything else.
            TryRun(() => joint.UpdateDrivenConstruction());

            var nodeStatus = AsQuery.Safe(() => joint.CheckStatus, eNodeStatus.NodeUnknownStatus);

            var created = new List<object>();
            foreach (var element in Enumerate(() => joint.CreatedObjects))
            {
                created.Add(AsQuery.Describe(element));
            }

            if (nodeStatus == eNodeStatus.NodeNotSupported || created.Count == 0)
            {
                return CommandResult.Fail(
                    "JOINT_NOT_APPLICABLE",
                    $"Rule '{appliedRule}' reported '{nodeStatus}' and produced {created.Count} part(s) for the "
                    + "given members, so no connection was created.",
                    422,
                    "Check that the members really meet at the connection point, that their section types are "
                    + "supported by this rule, and that 'primary_end' / 'connection_point' name the node you "
                    + "mean. The model was rolled back.");
            }

            var warnings = new List<string>();

            // rules/advance-steel-modeling.md §3.2: the shop assembly the joint produces takes its
            // prefix and its DSTV origin from the primary profile, so a joint driven by a plate is
            // worth flagging even when the rule accepts it.
            if (!AsQuery.IsPrimaryProfile(primary))
            {
                warnings.Add(
                    $"The primary driver '{AsQuery.Safe(() => primary.Handle, null)}' is a "
                    + $"{AsQuery.TypeName(primary)}, not a profile. Verify the Main Part of the resulting "
                    + "assembly with inspect_main_part.");
            }

            if (nodeStatus == eNodeStatus.NodeUnknownStatus)
            {
                warnings.Add(
                    "Advance Steel could not classify the node. The parts were created; verify them with "
                    + "capture_viewport or audit_assembly_integrity.");
            }

            return CommandResult.Ok(new
            {
                handle = AsQuery.Safe(() => joint.Handle, null),
                rule_name = appliedRule,
                joint_type = jointType ?? appliedRule,
                node_status = nodeStatus.ToString(),
                primary_handle = AsQuery.Safe(() => primary.Handle, null),
                secondary_handles = secondaries.Select(s => AsQuery.Safe(() => s.Handle, null)).ToArray(),
                connection_point = new[] { primaryPoint.x, primaryPoint.y, primaryPoint.z },
                created_objects = created,
                created_count = created.Count,
                warnings
            });
        }

        // ------------------------------------------------------------------
        // Rule resolution
        // ------------------------------------------------------------------

        /// <summary>
        /// Agent-facing connection names mapped onto Advance Steel rule names, each with the
        /// fallbacks that cover the same detail in a different country pack. The names were read
        /// out of the <c>RULE_*</c> tables of the Advance Steel 2026 <c>AstorRules</c> database;
        /// which of them a given installation ships depends on its country pack, hence a candidate
        /// list rather than a single name.
        /// </summary>
        private static readonly Dictionary<string, string[]> RuleAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["BasePlate"] = new[] { "BasePlate", "BasePlateExtended", "AnchorBasePlate" },
                ["AnchorBasePlate"] = new[] { "AnchorBasePlate", "BasePlate" },
                ["TubeBasePlate"] = new[] { "TubeBasePlate", "BasePlate" },
                ["CornerBasePlate"] = new[] { "CornerBasePlate", "CornerBase" },
                ["ClipAngle"] = new[] { "USClipAngleNew", "DoubleSideClipAngle" },
                ["DoubleClipAngle"] = new[] { "DoubleSideClipAngle", "USClipAngleNew" },
                ["EndPlate"] = new[] { "SingleSideEndPlate", "DoubleSideEndPlate", "EndPlatewithBolts" },
                ["DoubleEndPlate"] = new[] { "DoubleSideEndPlate", "SingleSideEndPlate" },
                ["GableEndPlate"] = new[] { "GableWallEndPlate_New" },
                ["MomentEndPlate"] = new[] { "MomentEndPlate", "MomentConnection" },
                ["ApexHaunch"] = new[] { "ApexJoint", "ApexDoubleHaunch", "FlangeHaunch" },
                ["FlangeHaunch"] = new[] { "FlangeHaunch", "HaunchPlateFlange" },
                ["WebHaunch"] = new[] { "HaunchPlateWeb" },
                ["ShearPlate"] = new[] { "ShearPlateNew", "ShearPlate" },
                ["GussetPlate"] = new[] { "SingleGusset", "DoubleGusset", "TripleGusset" },
                ["CapPlate"] = new[] { "CapPlate", "CapPlateCover" },
                ["ColumnBeamSeat"] = new[] { "ColumnBeamSeatT", "ColumnBeamSeatAngle" },
                ["Splice"] = new[] { "SpliceJoint", "MomentColumnSplice", "FrontPlateSplice" },
                ["ContourNotch"] = new[] { "ContourNotch" }
            };

        private static bool TryResolveRuleCandidates(
            string? jointType, string? explicitRule, out string[] candidates, out CommandResult? error)
        {
            error = null;

            // An explicit rule name is authoritative: the alias table only covers the common
            // details, and a detailer who knows the rule must not be second-guessed.
            if (!string.IsNullOrWhiteSpace(explicitRule))
            {
                candidates = new[] { explicitRule!.Trim() };
                return true;
            }

            if (string.IsNullOrWhiteSpace(jointType))
            {
                candidates = Array.Empty<string>();
                error = CommandResult.Fail(
                    "MISSING_PARAMETER", "Parameter 'joint_type' (or 'rule_name') is required.", 400,
                    $"Use one of the known joint types: {string.Join(", ", RuleAliases.Keys)}; "
                    + "or send the exact Advance Steel rule name as 'rule_name'.");
                return false;
            }

            if (!RuleAliases.TryGetValue(jointType!.Trim(), out var mapped))
            {
                candidates = Array.Empty<string>();
                error = CommandResult.Fail(
                    "INVALID_PARAMETER", $"Unknown joint_type '{jointType}'.", 400,
                    $"Use one of: {string.Join(", ", RuleAliases.Keys)}; "
                    + "or send the exact Advance Steel rule name as 'rule_name'.");
                return false;
            }

            candidates = mapped;
            return true;
        }

        // ------------------------------------------------------------------
        // Driver points
        // ------------------------------------------------------------------

        /// <summary>
        /// The point Advance Steel would have received from the user's pick. For a profile it is
        /// snapped onto the system line, because that is what the rules measure their set-outs
        /// against; anything else falls back to the element centre.
        /// </summary>
        private static Point3d DriverPoint(FilerObject element, Point3d? requested, eEnd end)
        {
            if (element is Beam beam)
            {
                if (requested != null)
                {
                    return AsQuery.Safe(() => beam.GetClosestPointToSystemline(requested, false), requested);
                }

                return end switch
                {
                    eEnd.kEnd => AsQuery.Safe(() => beam.GetPointAtEnd(), Point3d.kOrigin),
                    eEnd.kMid => AsQuery.Safe(() => beam.GetPointAtStart(beam.GetLength() / 2.0), Point3d.kOrigin),
                    _ => AsQuery.Safe(() => beam.GetPointAtStart(), Point3d.kOrigin)
                };
            }

            if (requested != null) return requested;

            return element is ConstructionElement construction
                ? AsQuery.Safe(() => construction.CenterPoint, Point3d.kOrigin)
                : Point3d.kOrigin;
        }

        /// <summary>Maps the agent-facing end names of SPEC-004 onto Advance Steel's <c>eEnd</c>.</summary>
        internal static bool TryReadEnd(string name, out eEnd end, out CommandResult? error)
        {
            error = null;

            switch (name.Trim().ToLowerInvariant())
            {
                case "start":
                case "begin":
                    end = eEnd.kStart;
                    return true;
                case "end":
                case "finish":
                    end = eEnd.kEnd;
                    return true;
                case "mid":
                case "middle":
                case "center":
                case "centre":
                    end = eEnd.kMid;
                    return true;
                default:
                    end = eEnd.kStart;
                    error = CommandResult.Fail(
                        "INVALID_PARAMETER", $"Unknown beam end '{name}'.", 400,
                        "Use \"Start\", \"End\" or \"Mid\".");
                    return false;
            }
        }

        // ------------------------------------------------------------------
        // Optional geometry parameters
        // ------------------------------------------------------------------

        /// <summary>Reads a Point3D that the caller may legitimately omit.</summary>
        internal static bool TryReadOptionalPoint(
            CommandContext ctx, string name, out Point3d? point, out CommandResult? error)
        {
            point = null;
            error = null;

            if (ctx.Body.ValueKind != JsonValueKind.Object
                || !ctx.Body.TryGetProperty(name, out var raw)
                || raw.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (!BeamCommandHandler.TryReadPoint(raw, out var parsed, out var reason))
            {
                error = CommandResult.Fail(
                    "INVALID_PARAMETER", $"Parameter '{name}' is not a valid Point3D: {reason}", 400,
                    "Expected a numeric [X, Y, Z] array, e.g. [0.0, 0.0, 4000.0].");
                return false;
            }

            point = parsed;
            return true;
        }

        private static bool TryReadPointList(
            CommandContext ctx, string name, out List<Point3d> points, out CommandResult? error)
        {
            points = new List<Point3d>();
            error = null;

            if (ctx.Body.ValueKind != JsonValueKind.Object || !ctx.Body.TryGetProperty(name, out var raw))
            {
                return true;
            }

            if (raw.ValueKind != JsonValueKind.Array)
            {
                error = CommandResult.Fail(
                    "INVALID_PARAMETER", $"Parameter '{name}' must be an array of [X, Y, Z] points.", 400,
                    "Send e.g. [[0.0, 0.0, 0.0], [0.0, 0.0, 4000.0]].");
                return false;
            }

            var index = 0;
            foreach (var item in raw.EnumerateArray())
            {
                if (!BeamCommandHandler.TryReadPoint(item, out var parsed, out var reason))
                {
                    error = CommandResult.Fail(
                        "INVALID_PARAMETER", $"Point {index} of '{name}' is invalid: {reason}", 400,
                        "Every entry must be a numeric [X, Y, Z] array.");
                    return false;
                }

                points.Add(parsed);
                index++;
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Advance Steel call guards
        // ------------------------------------------------------------------

        private static void TryRun(Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception)
            {
                // Best effort: the caller judges the result by the node status and the created parts.
            }
        }

        private static IEnumerable<FilerObject> Enumerate(Func<IEnumerable<FilerObject>> getter)
        {
            IEnumerable<FilerObject>? elements;

            try
            {
                elements = getter();
            }
            catch (System.Exception)
            {
                yield break;
            }

            foreach (var element in elements ?? Enumerable.Empty<FilerObject>())
            {
                if (element != null) yield return element;
            }
        }
    }
}
