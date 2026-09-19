using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AdvanceSteel.Geometry;
using Autodesk.AdvanceSteel.Modelling;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// POST /api/v1/elements/poly-beam — SPEC-004 §2 <c>create_poly_beam</c>.
    /// Continuous multi-segment polybeam or curved member.
    /// Runs inside DocumentLock + Transaction boundary.
    /// </summary>
    public static class PolyBeamCommandHandler
    {
        private const double PointCoincidenceToleranceMm = 0.1;

        public static CommandResult Create(CommandContext ctx)
        {
            var sectionName = ctx.GetString("section_name");
            if (string.IsNullOrWhiteSpace(sectionName))
            {
                return CommandResult.Fail(
                    "MISSING_PARAMETER", "Parameter 'section_name' is required.", 400,
                    "Send a profile section name such as 'HEA200' or 'IPE240'.");
            }

            if (!TryReadPoints(ctx, "points", out var points, out var error)) return error!;

            if (points.Count < 2)
            {
                return CommandResult.Fail(
                    "INVALID_PARAMETER",
                    $"Parameter 'points' must contain at least 2 vertices; received {points.Count}.",
                    400,
                    "Provide 2 or more [x, y, z] points defining the polybeam trajectory.");
            }

            // Verify adjacent points are not coincident
            for (var i = 1; i < points.Count; i++)
            {
                var dist = points[i - 1].DistanceTo(points[i]);
                if (dist < PointCoincidenceToleranceMm)
                {
                    return CommandResult.Fail(
                        "DEGENERATE_GEOMETRY",
                        $"Vertices {i - 1} and {i} are {dist:F3} mm apart, below the {PointCoincidenceToleranceMm} mm tolerance.",
                        400,
                        "Ensure consecutive vertices are not coincident.");
                }
            }

            var material = ctx.GetString("material", "S275JR")!;
            var modelRole = ctx.GetString("model_role", "Beam")!;

            var ptsArray = points.ToArray();
            var vInfo = new VertexInfo[ptsArray.Length];
            for (var i = 0; i < vInfo.Length; i++)
            {
                vInfo[i] = new VertexInfo();
            }

            Polyline3d polyline;
            try
            {
                polyline = new Polyline3d(ptsArray, vInfo, false, true);
            }
            catch (System.Exception ex)
            {
                return CommandResult.Fail(
                    "GEOMETRY_CREATION_FAILED", ex.Message, 422,
                    "Could not construct 3D polyline from the specified points.",
                    ex.StackTrace);
            }

            // Reference orientation vector: perpendicular to initial segment
            var seg = ptsArray[1].Subtract(ptsArray[0]).Normalize();
            var zVector = seg.IsParallelTo(Vector3d.kZAxis) ? Vector3d.kXAxis : Vector3d.kZAxis;

            PolyBeam polyBeam;
            try
            {
                polyBeam = new PolyBeam(sectionName, polyline, zVector);
                polyBeam.WriteToDb();
            }
            catch (System.Exception ex)
            {
                return CommandResult.Fail(
                    "POLYBEAM_CREATION_FAILED", ex.Message, 422,
                    $"Verify that '{sectionName}' exists in the active Advance Steel profile database.",
                    ex.StackTrace);
            }

            polyBeam.Material = material;
            polyBeam.Role = modelRole;

            var lengthMm = AsQuery.Safe(() => polyBeam.GetLength(), 0.0);
            var weightKg = AsQuery.Safe(() => polyBeam.GetWeight(0), 0.0);

            return CommandResult.Ok(new
            {
                handle = polyBeam.Handle,
                section_name = AsQuery.Safe(() => polyBeam.ProfName, sectionName),
                model_role = modelRole,
                material,
                length_mm = Math.Round(lengthMm, 3),
                weight_kg = Math.Round(weightKg, 3),
                vertex_count = ptsArray.Length
            });
        }

        private static bool TryReadPoints(
            CommandContext ctx, string name, out List<Point3d> points, out CommandResult? error)
        {
            points = new List<Point3d>();
            error = null;

            if (ctx.Body.ValueKind != JsonValueKind.Object || !ctx.Body.TryGetProperty(name, out var raw))
            {
                error = CommandResult.Fail(
                    "MISSING_PARAMETER", $"Parameter '{name}' is required.", 400,
                    $"Send '{name}' as a JSON array of [x, y, z] points.");
                return false;
            }

            if (raw.ValueKind != JsonValueKind.Array)
            {
                error = CommandResult.Fail(
                    "INVALID_PARAMETER", $"Parameter '{name}' must be an array of points.", 400);
                return false;
            }

            var index = 0;
            foreach (var item in raw.EnumerateArray())
            {
                if (!BeamCommandHandler.TryReadPoint(item, out var pt, out var reason))
                {
                    error = CommandResult.Fail(
                        "INVALID_PARAMETER", $"Vertex at index {index} is not a valid Point3D: {reason}", 400);
                    return false;
                }
                points.Add(pt);
                index++;
            }

            return true;
        }
    }
}
