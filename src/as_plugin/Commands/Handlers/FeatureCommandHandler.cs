using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Geometry;
using Autodesk.AdvanceSteel.Modelling;

// Advance Steel declares these enums nested inside the class they belong to.
using eCornerType = Autodesk.AdvanceSteel.Modelling.BeamNotch.eBeamNotchCornerType;
using eEnd = Autodesk.AdvanceSteel.Modelling.Beam.eEnd;
using eSide = Autodesk.AdvanceSteel.Modelling.Beam.eSide;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// POST /api/v1/elements/cut — SPEC-004 §2 <c>apply_beam_cut_or_notch</c>.
    ///
    /// A cut in Advance Steel is never a boolean against the solid: it is a <em>processing</em>
    /// (<see cref="FeatureObject"/>) owned by the member, which is what makes it survive a profile
    /// change and what carries it into the DSTV/NC output. The three shapes a detailer needs are:
    /// <list type="bullet">
    ///   <item><description><c>shortening</c> — cut the member back at one end, square or mitred
    ///   (<see cref="BeamShortening"/>).</description></item>
    ///   <item><description><c>notch</c> — cope the flange/web at one end
    ///   (<see cref="BeamNotch2Ortho"/>, or <see cref="BeamNotchEx"/> when the cope is skewed).</description></item>
    ///   <item><description><c>contour</c> — a rectangular, circular or polygonal pocket
    ///   (<see cref="BeamMultiContourNotch"/>).</description></item>
    /// </list>
    /// Runs inside the dispatcher's DocumentLock + transaction boundary; a feature that cannot be
    /// attached rolls the member back untouched (rules/transaction-safety.md §4).
    /// </summary>
    public static class FeatureCommandHandler
    {
        /// <summary>rules/advance-steel-modeling.md §4: below this a dimension is not a cut.</summary>
        private const double MinimumDimensionMm = 0.1;

        public static CommandResult Apply(CommandContext ctx)
        {
            var handle = ctx.GetString("element_handle") ?? ctx.GetString("handle");

            if (!AsQuery.TryResolve<Beam>(handle, "element_handle", out var beam, out var resolveError))
            {
                return resolveError!;
            }

            var cutType = ctx.GetString("cut_type", "shortening")!;

            if (!JointCommandHandler.TryReadEnd(ctx.GetString("end", "Start")!, out var end, out var endError))
            {
                return endError!;
            }

            var lengthBefore = AsQuery.Safe(() => beam.GetLength(), 0.0);

            FeatureObject feature;
            string normalizedType;
            CommandResult? buildError;

            switch (cutType.Trim().ToLowerInvariant())
            {
                case "shortening":
                case "shorten":
                case "plane":
                case "miter":
                case "mitre":
                case "trim":
                    normalizedType = "shortening";
                    if (!TryBuildShortening(ctx, beam, end, out feature!, out buildError)) return buildError!;
                    break;

                case "notch":
                case "cope":
                case "recess":
                    normalizedType = "notch";
                    if (!TryBuildNotch(ctx, end, out feature!, out buildError)) return buildError!;
                    break;

                case "contour":
                case "box":
                case "rectangle":
                case "circle":
                case "polygon":
                case "pocket":
                    normalizedType = "contour";
                    if (!TryBuildContourNotch(ctx, beam, end, cutType, out feature!, out buildError))
                    {
                        return buildError!;
                    }

                    break;

                default:
                    return CommandResult.Fail(
                        "INVALID_PARAMETER", $"Unknown cut_type '{cutType}'.", 400,
                        "Use \"shortening\" (square or mitred end cut), \"notch\" (flange/web cope) "
                        + "or \"contour\" (rectangular, circular or polygonal pocket).");
            }

            string? featureHandle;
            try
            {
                featureHandle = AttachFeature(beam, feature);
            }
            catch (System.Exception ex)
            {
                return CommandResult.Fail(
                    "FEATURE_CREATION_FAILED", ex.Message, 422,
                    "Verify that the cut lies on the member and that its dimensions are smaller than the "
                    + "profile. The member was rolled back.",
                    ex.StackTrace);
            }

            var lengthAfter = AsQuery.Safe(() => beam.GetLength(), lengthBefore);

            return CommandResult.Ok(new
            {
                feature_handle = featureHandle,
                feature_type = AsQuery.TypeName(feature),
                cut_type = normalizedType,
                element_handle = AsQuery.Safe(() => beam.Handle, null),
                section_name = AsQuery.SectionName(beam),
                end = end.ToString(),
                length_before_mm = Math.Round(lengthBefore, 3),
                length_mm = Math.Round(lengthAfter, 3),
                feature_count = (int)AsQuery.Safe(() => beam.NumFeatures(), (short)0),
                weight_kg = Math.Round(AsQuery.Safe(() => beam.GetWeight(0), 0.0), 3)
            });
        }

        // ------------------------------------------------------------------
        // shortening — square and mitred end cuts
        // ------------------------------------------------------------------

        private static bool TryBuildShortening(
            CommandContext ctx, Beam beam, eEnd end, out FeatureObject? feature, out CommandResult? error)
        {
            feature = null;
            error = null;

            if (!TryReadOptionalVector(ctx, "plane_normal", out var planeNormal, out error)) return false;
            if (!JointCommandHandler.TryReadOptionalPoint(ctx, "plane_point", out var planePoint, out error))
            {
                return false;
            }

            var length = ctx.GetDouble("length", double.NaN);
            var hasPlane = planePoint != null && planeNormal != null;

            if (double.IsNaN(length) && !hasPlane)
            {
                error = CommandResult.Fail(
                    "MISSING_PARAMETER",
                    "A shortening needs either 'length' (mm to remove from the end) or both 'plane_point' "
                    + "and 'plane_normal'.",
                    400,
                    "Send e.g. {\"cut_type\": \"shortening\", \"end\": \"Start\", \"length\": 150.0}, or a "
                    + "cutting plane as {\"plane_point\": [0,0,0], \"plane_normal\": [0,0,1]}.");
                return false;
            }

            if (!double.IsNaN(length))
            {
                if (length <= MinimumDimensionMm)
                {
                    error = CommandResult.Fail(
                        "INVALID_PARAMETER",
                        $"Shortening length {length} mm is at or below the {MinimumDimensionMm} mm tolerance.",
                        400,
                        "Give the cut a positive length in millimetres.");
                    return false;
                }

                var beamLength = AsQuery.Safe(() => beam.GetLength(), 0.0);
                if (beamLength > 0.0 && length >= beamLength)
                {
                    error = CommandResult.Fail(
                        "INVALID_PARAMETER",
                        $"Shortening length {length} mm is not shorter than the member itself ({beamLength:F1} mm).",
                        422,
                        "A shortening removes material from one end; use modify_element_properties or delete "
                        + "the member if you meant to replace it.");
                    return false;
                }
            }

            var shortening = new BeamShortening(end, double.IsNaN(length) ? 0.0 : length);

            // An explicit cutting plane wins over the length: Set() re-derives the offset from the
            // plane, which is what a mitre against another member needs.
            if (hasPlane)
            {
                shortening.Set(planePoint!, planeNormal!, end);
            }

            var angleOnY = ctx.GetDouble("angle_y_deg", 0.0);
            var angleOnZ = ctx.GetDouble("angle_z_deg", 0.0);

            if (Math.Abs(angleOnY) > double.Epsilon) shortening.AngleOnY = ToRadians(angleOnY);
            if (Math.Abs(angleOnZ) > double.Epsilon) shortening.AngleOnZ = ToRadians(angleOnZ);

            feature = shortening;
            return true;
        }

        // ------------------------------------------------------------------
        // notch — flange and web copes
        // ------------------------------------------------------------------

        private static bool TryBuildNotch(
            CommandContext ctx, eEnd end, out FeatureObject? feature, out CommandResult? error)
        {
            feature = null;
            error = null;

            if (!TryReadSide(ctx.GetString("side", "Upper")!, out var side, out error)) return false;

            var length = ctx.GetDouble("length", double.NaN);
            var depth = ctx.GetDouble("depth", double.NaN);

            if (double.IsNaN(length) || double.IsNaN(depth))
            {
                error = CommandResult.Fail(
                    "MISSING_PARAMETER", "A notch needs both 'length' and 'depth' in millimetres.", 400,
                    "Send e.g. {\"cut_type\": \"notch\", \"end\": \"Start\", \"side\": \"Upper\", "
                    + "\"length\": 120.0, \"depth\": 40.0}.");
                return false;
            }

            if (length <= MinimumDimensionMm || depth <= MinimumDimensionMm)
            {
                error = CommandResult.Fail(
                    "INVALID_PARAMETER",
                    $"Notch length {length} mm / depth {depth} mm must both exceed the "
                    + $"{MinimumDimensionMm} mm tolerance.",
                    400,
                    "Give the notch positive dimensions in millimetres.");
                return false;
            }

            var axisAngle = ctx.GetDouble("axis_angle_deg", 0.0);
            var zAngle = ctx.GetDouble("z_angle_deg", 0.0);
            var xAngle = ctx.GetDouble("x_angle_deg", 0.0);
            var isSkewed = Math.Abs(axisAngle) > double.Epsilon
                           || Math.Abs(zAngle) > double.Epsilon
                           || Math.Abs(xAngle) > double.Epsilon;

            // An orthogonal cope is the common case and the one Advance Steel can regenerate
            // cheaply; the extended notch is only needed once the cope is skewed.
            BeamNotch notch = isSkewed
                ? new BeamNotchEx(end, side, length, depth)
                : new BeamNotch2Ortho(end, side, length, depth);

            if (notch is BeamNotchEx skewed)
            {
                skewed.AxisAngle = ToRadians(axisAngle);
                skewed.ZAngle = ToRadians(zAngle);
                skewed.XAngle = ToRadians(xAngle);
            }

            var cornerType = ctx.GetString("corner_type");
            if (!string.IsNullOrWhiteSpace(cornerType))
            {
                if (!TryReadCornerType(cornerType!, out var corner, out error)) return false;

                var radius = ctx.GetDouble("corner_radius", 0.0);
                if (corner != eCornerType.kStraight && radius <= 0.0)
                {
                    error = CommandResult.Fail(
                        "INVALID_PARAMETER",
                        $"corner_type '{cornerType}' needs a positive 'corner_radius'.", 400,
                        "Send the fillet radius in millimetres, e.g. 10.0.");
                    return false;
                }

                notch.SetCorner(corner, radius);
            }

            feature = notch;
            return true;
        }

        // ------------------------------------------------------------------
        // contour — rectangular, circular and polygonal pockets
        // ------------------------------------------------------------------

        private static bool TryBuildContourNotch(
            CommandContext ctx, Beam beam, eEnd end, string requestedShape,
            out FeatureObject? feature, out CommandResult? error)
        {
            feature = null;
            error = null;

            if (!TryReadOptionalVector(ctx, "normal", out var normal, out error)) return false;
            if (!TryReadOptionalVector(ctx, "x_axis", out var xAxis, out error)) return false;

            // The contour is extruded along its normal. Cutting across the section is by far the
            // common case, so the member's own axis is the default.
            normal ??= BeamAxis(beam);
            xAxis ??= AsQuery.Safe(() => normal!.GetPerpVector(), Vector3d.kXAxis);

            if (AsQuery.Safe(() => normal!.IsZeroLength(), false))
            {
                error = CommandResult.Fail(
                    "INVALID_PARAMETER", "Parameter 'normal' is a zero-length vector.", 400,
                    "Send the extrusion direction of the cut as a non-zero [X, Y, Z] vector.");
                return false;
            }

            if (ctx.Body.ValueKind == JsonValueKind.Object
                && ctx.Body.TryGetProperty("contour_points", out var rawContour)
                && rawContour.ValueKind == JsonValueKind.Array)
            {
                if (!TryReadContour(rawContour, out var contour, out error)) return false;

                feature = new BeamMultiContourNotch(beam, end, contour, normal!, xAxis!);
                return true;
            }

            if (!JointCommandHandler.TryReadOptionalPoint(ctx, "center", out var center, out error)) return false;

            if (center == null)
            {
                error = CommandResult.Fail(
                    "MISSING_PARAMETER",
                    "A contour cut needs 'center' (with 'radius', or 'length' and 'width') or "
                    + "'contour_points'.",
                    400,
                    "Send e.g. {\"cut_type\": \"box\", \"center\": [0,0,2000], \"length\": 200.0, "
                    + "\"width\": 100.0} or a closed [[x,y,z], ...] contour as 'contour_points'.");
                return false;
            }

            var radius = ctx.GetDouble("radius", double.NaN);
            if (!double.IsNaN(radius))
            {
                if (radius <= MinimumDimensionMm)
                {
                    error = CommandResult.Fail(
                        "INVALID_PARAMETER",
                        $"Circular cut radius {radius} mm is at or below the {MinimumDimensionMm} mm tolerance.",
                        400,
                        "Give the hole a positive radius in millimetres.");
                    return false;
                }

                feature = new BeamMultiContourNotch(beam, end, center, normal!, xAxis!, radius);
                return true;
            }

            var cutLength = ctx.GetDouble("length", double.NaN);
            var cutWidth = ctx.GetDouble("width", double.NaN);

            if (double.IsNaN(cutLength) || double.IsNaN(cutWidth))
            {
                error = CommandResult.Fail(
                    "MISSING_PARAMETER",
                    $"A '{requestedShape}' cut around 'center' needs either 'radius', or both 'length' "
                    + "and 'width'.",
                    400,
                    "Send the pocket size in millimetres, e.g. {\"length\": 200.0, \"width\": 100.0}.");
                return false;
            }

            if (cutLength <= MinimumDimensionMm || cutWidth <= MinimumDimensionMm)
            {
                error = CommandResult.Fail(
                    "INVALID_PARAMETER",
                    $"Cut length {cutLength} mm / width {cutWidth} mm must both exceed the "
                    + $"{MinimumDimensionMm} mm tolerance.",
                    400,
                    "Give the pocket positive dimensions in millimetres.");
                return false;
            }

            feature = new BeamMultiContourNotch(beam, end, center, normal!, xAxis!, cutLength, cutWidth);
            return true;
        }

        private static bool TryReadContour(
            JsonElement raw, out Point3d[] contour, out CommandResult? error)
        {
            contour = Array.Empty<Point3d>();
            error = null;

            var points = new List<Point3d>();
            var index = 0;

            foreach (var item in raw.EnumerateArray())
            {
                if (!BeamCommandHandler.TryReadPoint(item, out var parsed, out var reason))
                {
                    error = CommandResult.Fail(
                        "INVALID_PARAMETER", $"Point {index} of 'contour_points' is invalid: {reason}", 400,
                        "Every entry must be a numeric [X, Y, Z] array.");
                    return false;
                }

                points.Add(parsed);
                index++;
            }

            if (points.Count < 3)
            {
                error = CommandResult.Fail(
                    "INVALID_PARAMETER",
                    $"'contour_points' holds {points.Count} point(s); a contour cut needs at least 3.",
                    400,
                    "Send the closed outline of the pocket as [[x,y,z], [x,y,z], [x,y,z], ...].");
                return false;
            }

            contour = points.ToArray();
            return true;
        }

        // ------------------------------------------------------------------
        // Attaching the feature to its owner
        // ------------------------------------------------------------------

        /// <summary>
        /// Puts the processing in the database and binds it to the member whose body it cuts.
        /// <see cref="AtomicElement.AddFeature"/> is what makes the member recompute its solid and
        /// what carries the cut into the NC output; <see cref="Autodesk.AdvanceSteel.CADAccess.FilerObject.WriteToDb"/>
        /// is the fallback for the features that are constructed without a reference to their owner.
        /// </summary>
        private static string? AttachFeature(Beam owner, FeatureObject feature)
        {
            try
            {
                owner.AddFeature(feature);
            }
            catch (System.Exception)
            {
                // Not yet in the database: persist it first, then bind it.
                feature.WriteToDb();
                owner.AddFeature(feature);
                return AsQuery.Safe(() => feature.Handle, null);
            }

            var handle = AsQuery.Safe(() => feature.Handle, null);
            if (!string.IsNullOrWhiteSpace(handle)) return handle;

            feature.WriteToDb();
            return AsQuery.Safe(() => feature.Handle, null);
        }

        // ------------------------------------------------------------------
        // Parameter mapping
        // ------------------------------------------------------------------

        private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

        private static Vector3d BeamAxis(Beam beam)
        {
            try
            {
                var axis = beam.GetPointAtEnd().Subtract(beam.GetPointAtStart());
                return axis.IsZeroLength() ? Vector3d.kZAxis : axis.Normalize();
            }
            catch (System.Exception)
            {
                return Vector3d.kZAxis;
            }
        }

        private static bool TryReadSide(string name, out eSide side, out CommandResult? error)
        {
            error = null;

            switch (name.Trim().ToLowerInvariant())
            {
                case "upper":
                case "top":
                    side = eSide.kUpper;
                    return true;
                case "lower":
                case "bottom":
                    side = eSide.kLower;
                    return true;
                default:
                    side = eSide.kUpper;
                    error = CommandResult.Fail(
                        "INVALID_PARAMETER", $"Unknown side '{name}'.", 400,
                        "Use \"Upper\" or \"Lower\".");
                    return false;
            }
        }

        private static bool TryReadCornerType(string name, out eCornerType corner, out CommandResult? error)
        {
            error = null;

            switch (name.Trim().ToLowerInvariant())
            {
                case "straight":
                case "square":
                    corner = eCornerType.kStraight;
                    return true;
                case "round":
                case "radius":
                case "fillet":
                    corner = eCornerType.kRound;
                    return true;
                case "boringout":
                case "boring":
                case "drilled":
                    corner = eCornerType.kBoringOut;
                    return true;
                default:
                    corner = eCornerType.kStraight;
                    error = CommandResult.Fail(
                        "INVALID_PARAMETER", $"Unknown corner_type '{name}'.", 400,
                        "Use \"Straight\", \"Round\" or \"BoringOut\".");
                    return false;
            }
        }

        /// <summary>Reads a direction vector that the caller may legitimately omit.</summary>
        private static bool TryReadOptionalVector(
            CommandContext ctx, string name, out Vector3d? vector, out CommandResult? error)
        {
            vector = null;
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
                    "INVALID_PARAMETER", $"Parameter '{name}' is not a valid [X, Y, Z] vector: {reason}", 400,
                    "Expected three numbers, e.g. [0.0, 0.0, 1.0].");
                return false;
            }

            vector = new Vector3d(parsed.x, parsed.y, parsed.z);
            return true;
        }
    }
}
