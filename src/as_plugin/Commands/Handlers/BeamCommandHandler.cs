using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AdvanceSteel.Geometry;
using Autodesk.AdvanceSteel.Modelling;

// Advance Steel declares this enum nested inside Beam.
using eRefAxis = Autodesk.AdvanceSteel.Modelling.Beam.eRefAxis;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// POST /api/v1/elements/beam — SPEC-002 §2 "Straight Beam Request".
    /// Runs inside the dispatcher's DocumentLock + transaction boundary.
    /// </summary>
    public static class BeamCommandHandler
    {
        /// <summary>rules/advance-steel-modeling.md §4: two points closer than this are the same point.</summary>
        private const double PointCoincidenceToleranceMm = 0.1;

        public static CommandResult Create(CommandContext ctx)
        {
            if (!TryReadPoint(ctx, "start_point", out var start, out var error)) return error!;
            if (!TryReadPoint(ctx, "end_point", out var end, out error)) return error!;

            var sectionName = ctx.GetString("section_name");
            if (string.IsNullOrWhiteSpace(sectionName))
            {
                return CommandResult.Fail(
                    "MISSING_PARAMETER", "Parameter 'section_name' is required.", 400,
                    "Send a catalogue section such as \"HEB300\", \"IPE240\" or \"HEA200\".");
            }

            var length = start.DistanceTo(end);
            if (length < PointCoincidenceToleranceMm)
            {
                return CommandResult.Fail(
                    "DEGENERATE_GEOMETRY",
                    $"start_point and end_point are {length:F3} mm apart, below the {PointCoincidenceToleranceMm} mm coincidence tolerance.",
                    400,
                    "Give the beam a non-zero length.");
            }

            var material = ctx.GetString("material", "S275JR")!;
            var modelRole = ctx.GetString("model_role", "Beam")!;
            var referenceAxis = ctx.GetString("reference_axis", "Center")!;
            var rotationDeg = ctx.GetDouble("rotation_deg", 0.0);

            if (!TryMapReferenceAxis(referenceAxis, out var refAxis))
            {
                return CommandResult.Fail(
                    "INVALID_PARAMETER", $"Unknown reference_axis '{referenceAxis}'.", 400,
                    $"Use one of: {string.Join(", ", ReferenceAxisNames)}.");
            }

            // Orientation reference: the profile's local Z. A vertical member cannot use the global
            // Z axis as its reference, so fall back to X for anything parallel to Z.
            var axis = end.Subtract(start).Normalize();
            var referenceVector = axis.IsParallelTo(Vector3d.kZAxis) ? Vector3d.kXAxis : Vector3d.kZAxis;

            StraightBeam beam;
            try
            {
                beam = new StraightBeam(sectionName, start, end, referenceVector);
                beam.WriteToDb();
            }
            catch (System.Exception ex)
            {
                return CommandResult.Fail(
                    "BEAM_CREATION_FAILED", ex.Message, 422,
                    $"Verify that '{sectionName}' exists in the active Advance Steel profile database.",
                    ex.StackTrace);
            }

            // Properties are applied after WriteToDb so the object already lives in the database.
            beam.Material = material;
            beam.Role = modelRole;
            beam.RefAxis = refAxis;

            if (Math.Abs(rotationDeg) > double.Epsilon)
            {
                beam.Angle = rotationDeg * Math.PI / 180.0;
            }

            return CommandResult.Ok(new
            {
                handle = beam.Handle,
                section_name = AsQuery.Safe(() => beam.ProfName, sectionName),
                model_role = modelRole,
                material,
                reference_axis = referenceAxis,
                rotation_deg = rotationDeg,
                length = Math.Round(AsQuery.Safe(() => beam.GetLength(), length), 3),
                length_mm = Math.Round(AsQuery.Safe(() => beam.GetLength(), length), 3),
                weight_kg = Math.Round(AsQuery.Safe(() => beam.GetWeight(0), 0.0), 3)
            });
        }

        // ------------------------------------------------------------------
        // Parameter mapping
        // ------------------------------------------------------------------

        /// <summary>
        /// Maps the agent-facing axis names of SPEC-004 onto Advance Steel's <c>eRefAxis</c>.
        /// rules/advance-steel-modeling.md §1 defines Center / TopCenter / BottomCenter.
        /// </summary>
        private static readonly Dictionary<string, eRefAxis> ReferenceAxes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Center"] = eRefAxis.kSysSys,
                ["Centre"] = eRefAxis.kSysSys,
                ["Middle"] = eRefAxis.kSysSys,
                ["TopCenter"] = eRefAxis.kUpperSys,
                ["TopCentre"] = eRefAxis.kUpperSys,
                ["Top"] = eRefAxis.kUpperSys,
                ["BottomCenter"] = eRefAxis.kLowerSys,
                ["BottomCentre"] = eRefAxis.kLowerSys,
                ["Bottom"] = eRefAxis.kLowerSys,
                ["TopLeft"] = eRefAxis.kUpperLeft,
                ["TopRight"] = eRefAxis.kUpperRight,
                ["BottomLeft"] = eRefAxis.kLowerLeft,
                ["BottomRight"] = eRefAxis.kLowerRight,
                ["MidLeft"] = eRefAxis.kMidLeft,
                ["MidRight"] = eRefAxis.kMidRight,
                ["ContourCenter"] = eRefAxis.kContourCenter
            };

        private static IEnumerable<string> ReferenceAxisNames => ReferenceAxes.Keys;

        private static bool TryMapReferenceAxis(string name, out eRefAxis axis) =>
            ReferenceAxes.TryGetValue(name, out axis);

        /// <summary>Reads a SPEC-002 Point3D — a <c>[X, Y, Z]</c> array in model units.</summary>
        internal static bool TryReadPoint(
            CommandContext ctx, string name, out Point3d point, out CommandResult? error)
        {
            point = Point3d.kOrigin;
            error = null;

            if (ctx.Body.ValueKind != JsonValueKind.Object || !ctx.Body.TryGetProperty(name, out var raw))
            {
                error = CommandResult.Fail(
                    "MISSING_PARAMETER", $"Parameter '{name}' is required.", 400,
                    $"Send '{name}' as a [X, Y, Z] array in model units (mm).");
                return false;
            }

            if (!TryReadPoint(raw, out point, out var reason))
            {
                error = CommandResult.Fail(
                    "INVALID_PARAMETER", $"Parameter '{name}' is not a valid Point3D: {reason}", 400,
                    "Expected a numeric [X, Y, Z] array, e.g. [0.0, 0.0, 4000.0].");
                return false;
            }

            return true;
        }

        internal static bool TryReadPoint(JsonElement raw, out Point3d point, out string reason)
        {
            point = Point3d.kOrigin;

            if (raw.ValueKind != JsonValueKind.Array)
            {
                reason = "expected a JSON array.";
                return false;
            }

            var coordinates = new double[3];
            var index = 0;

            foreach (var item in raw.EnumerateArray())
            {
                if (index >= 3)
                {
                    reason = "expected exactly 3 coordinates.";
                    return false;
                }

                if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out coordinates[index]))
                {
                    reason = $"coordinate at index {index} is not a number.";
                    return false;
                }

                index++;
            }

            if (index != 3)
            {
                reason = $"expected exactly 3 coordinates, got {index}.";
                return false;
            }

            point = new Point3d(coordinates[0], coordinates[1], coordinates[2]);
            reason = string.Empty;
            return true;
        }
    }
}
