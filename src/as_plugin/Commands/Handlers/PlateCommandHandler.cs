using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AdvanceSteel.Geometry;
using Autodesk.AdvanceSteel.Modelling;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// POST /api/v1/elements/plate — SPEC-002 §2 "Plate Request".
    /// Advance Steel models a contour plate with <c>Modelling.Plate(plane, vertices, thickness)</c>;
    /// the contour must be closed, coplanar and at least 3 points.
    /// </summary>
    public static class PlateCommandHandler
    {
        /// <summary>rules/advance-steel-modeling.md §4.</summary>
        private const double CoplanarityToleranceMm = 0.05;
        private const double MinimumThicknessMm = 3.0;
        private const double PointCoincidenceToleranceMm = 0.1;

        public static CommandResult Create(CommandContext ctx)
        {
            if (!TryReadContour(ctx, out var points, out var contourError)) return contourError!;

            var thickness = ctx.GetDouble("thickness", double.NaN);
            if (double.IsNaN(thickness))
            {
                return CommandResult.Fail(
                    "MISSING_PARAMETER", "Parameter 'thickness' is required.", 400,
                    "Send the plate thickness in mm, e.g. 20.0.");
            }

            if (thickness < MinimumThicknessMm)
            {
                return CommandResult.Fail(
                    "INVALID_PARAMETER",
                    $"Thickness {thickness} mm is below the minimum of {MinimumThicknessMm} mm.",
                    400,
                    $"Use a thickness of at least {MinimumThicknessMm} mm.");
            }

            if (!TryBuildPlane(points, out var plane, out var planeError)) return planeError!;

            var material = ctx.GetString("material", "S275JR")!;
            var modelRole = ctx.GetString("model_role", "Plate")!;

            Plate plate;
            try
            {
                plate = new Plate(plane, points, thickness);
                plate.WriteToDb();
            }
            catch (System.Exception ex)
            {
                return CommandResult.Fail(
                    "PLATE_CREATION_FAILED", ex.Message, 422,
                    "Verify that the contour is closed, convex-orderable and coplanar.",
                    ex.StackTrace);
            }

            plate.Material = material;
            plate.Role = modelRole;

            var areaMm2 = AsQuery.Safe(() => plate.GetArea(), 0.0);

            return CommandResult.Ok(new
            {
                handle = plate.Handle,
                model_role = modelRole,
                material,
                thickness_mm = Math.Round(AsQuery.Safe(() => plate.Thickness, thickness), 3),
                area_m2 = Math.Round(areaMm2 / 1_000_000.0, 6),
                weight_kg = Math.Round(AsQuery.Safe(() => plate.GetWeight(), 0.0), 3),
                contour_point_count = points.Length
            });
        }

        // ------------------------------------------------------------------
        // Contour validation
        // ------------------------------------------------------------------

        private static bool TryReadContour(CommandContext ctx, out Point3d[] points, out CommandResult? error)
        {
            points = Array.Empty<Point3d>();
            error = null;

            if (ctx.Body.ValueKind != JsonValueKind.Object
                || !ctx.Body.TryGetProperty("contour_points", out var raw)
                || raw.ValueKind != JsonValueKind.Array)
            {
                error = CommandResult.Fail(
                    "MISSING_PARAMETER", "Parameter 'contour_points' is required.", 400,
                    "Send at least 3 coplanar points as [[x,y,z], ...].");
                return false;
            }

            var parsed = new List<Point3d>();
            var index = 0;

            foreach (var item in raw.EnumerateArray())
            {
                if (!BeamCommandHandler.TryReadPoint(item, out var point, out var reason))
                {
                    error = CommandResult.Fail(
                        "INVALID_PARAMETER", $"contour_points[{index}] is not a valid Point3D: {reason}", 400,
                        "Each contour point must be a numeric [X, Y, Z] array.");
                    return false;
                }

                parsed.Add(point);
                index++;
            }

            // Advance Steel closes the contour itself; a repeated last point would create a
            // zero-length edge, so drop it.
            if (parsed.Count > 1
                && parsed[0].DistanceTo(parsed[parsed.Count - 1]) < PointCoincidenceToleranceMm)
            {
                parsed.RemoveAt(parsed.Count - 1);
            }

            if (parsed.Count < 3)
            {
                error = CommandResult.Fail(
                    "INVALID_PARAMETER",
                    $"A contour plate needs at least 3 distinct points, got {parsed.Count}.",
                    400,
                    "Send at least 3 non-coincident coplanar points.");
                return false;
            }

            points = parsed.ToArray();
            return true;
        }

        /// <summary>
        /// Derives the plate's definition plane from the first three non-collinear vertices and
        /// verifies every remaining vertex lies on it within the coplanarity tolerance.
        /// </summary>
        private static bool TryBuildPlane(Point3d[] points, out Plane plane, out CommandResult? error)
        {
            plane = Plane.kXYPlane;
            error = null;

            Vector3d normal = Vector3d.kZAxis;
            var found = false;

            for (var i = 2; i < points.Length && !found; i++)
            {
                var u = points[i - 1].Subtract(points[0]);
                var v = points[i].Subtract(points[0]);
                var candidate = u.CrossProduct(v);

                // A cross product this short means the three points are collinear.
                if (candidate.GetNormal().IsEqualTo(Vector3d.kIdentity)) continue;

                try
                {
                    normal = candidate.Normalize();
                    found = true;
                }
                catch (System.Exception)
                {
                    // Degenerate triple: keep scanning.
                }
            }

            if (!found)
            {
                error = CommandResult.Fail(
                    "DEGENERATE_GEOMETRY", "All contour points are collinear; no plate plane can be derived.", 400,
                    "Provide a contour that encloses an area.");
                return false;
            }

            plane = new Plane(points[0], normal);

            foreach (var point in points)
            {
                var distance = Math.Abs(plane.GetSignedDistanceTo(point));
                if (distance > CoplanarityToleranceMm)
                {
                    error = CommandResult.Fail(
                        "NON_COPLANAR_CONTOUR",
                        $"Contour point [{point.x}, {point.y}, {point.z}] lies {distance:F4} mm off the plate plane "
                        + $"(tolerance {CoplanarityToleranceMm} mm).",
                        400,
                        "Project the contour onto a single plane before sending it.");
                    return false;
                }
            }

            return true;
        }
    }
}
