using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Geometry;
using Autodesk.AdvanceSteel.Modelling;

using eAssemblyLocation = Autodesk.AdvanceSteel.ConstructionTypes.AtomicElement.eAssemblyLocation;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// POST /api/v1/elements/bolt — SPEC-004 §2 <c>create_bolt_pattern</c>.
    /// Rectangular bolt pattern connecting two or more structural parts.
    /// Runs inside DocumentLock + Transaction boundary.
    /// </summary>
    public static class BoltCommandHandler
    {
        public static CommandResult Create(CommandContext ctx)
        {
            var connectedHandles = ctx.GetStringList("connected_handles");
            if (connectedHandles.Count == 0)
            {
                return CommandResult.Fail(
                    "MISSING_PARAMETER", "Parameter 'connected_handles' is required.", 400,
                    "Provide at least one element handle to connect with bolts.");
            }

            if (!BeamCommandHandler.TryReadPoint(ctx, "origin", out var origin, out var error)) return error!;

            var normal = TryReadVector(ctx, "normal", Vector3d.kZAxis);
            if (normal.IsZeroLength())
            {
                normal = Vector3d.kZAxis;
            }
            else
            {
                normal = normal.Normalize();
            }

            var boltStandard = ctx.GetString("bolt_standard", "DIN 931")!;
            var boltGrade = ctx.GetString("bolt_grade", "8.8")!;
            var boltDiameter = ctx.GetDouble("bolt_diameter_mm", 20.0);
            if (boltDiameter <= 0.0)
            {
                return CommandResult.Fail(
                    "INVALID_PARAMETER", $"Bolt diameter must be positive; received {boltDiameter}.", 400);
            }

            var nx = (int)ctx.GetDouble("nx", 2.0);
            var ny = (int)ctx.GetDouble("ny", 2.0);
            if (nx < 1 || ny < 1)
            {
                return CommandResult.Fail(
                    "INVALID_PARAMETER", $"Bolt counts 'nx' and 'ny' must be >= 1; received nx={nx}, ny={ny}.", 400);
            }

            var dx = ctx.GetDouble("dx", 70.0);
            var dy = ctx.GetDouble("dy", 70.0);
            var isSiteBolt = ctx.GetBool("is_site_bolt", true);

            // Resolve connected elements
            var connectedObjects = new List<FilerObject>();
            foreach (var handle in connectedHandles)
            {
                var obj = AsQuery.OpenByHandle(handle);
                if (obj == null)
                {
                    return CommandResult.Fail(
                        "HANDLE_NOT_FOUND",
                        $"No Advance Steel object has handle '{handle}'.",
                        404,
                        "Verify connected part handles with get_selected_elements.");
                }
                connectedObjects.Add(obj);
            }

            // Build coordinate system vectors on plane perpendicular to normal
            Vector3d vX;
            if (Math.Abs(normal.DotProduct(Vector3d.kZAxis)) < 0.99)
            {
                vX = normal.CrossProduct(Vector3d.kZAxis).Normalize();
            }
            else
            {
                vX = normal.CrossProduct(Vector3d.kYAxis).Normalize();
            }
            var vY = vX.CrossProduct(normal).Normalize();

            // Compute corner points from center origin and spacing
            var totalX = nx > 1 ? (nx - 1) * dx : dx;
            var totalY = ny > 1 ? (ny - 1) * dy : dy;

            var pt1 = origin - (vX * (totalX / 2.0)) - (vY * (totalY / 2.0));
            var pt2 = origin + (vX * (totalX / 2.0)) + (vY * (totalY / 2.0));

            FinitRectScrewBoltPattern boltPattern;
            try
            {
                boltPattern = new FinitRectScrewBoltPattern(pt1, pt2, vX, vY);
                boltPattern.WriteToDb();

                boltPattern.Standard = boltStandard;
                boltPattern.Grade = boltGrade;
                boltPattern.ScrewDiameter = boltDiameter;
                boltPattern.Nx = nx;
                boltPattern.Ny = ny;
                boltPattern.Dx = dx;
                boltPattern.Dy = dy;

                var location = isSiteBolt ? eAssemblyLocation.kOnSite : eAssemblyLocation.kInShop;
                boltPattern.Connect(connectedObjects.ToArray(), location);
            }
            catch (System.Exception ex)
            {
                return CommandResult.Fail(
                    "BOLT_CREATION_FAILED", ex.Message, 422,
                    "Failed to create bolt pattern in Advance Steel.",
                    ex.StackTrace);
            }

            var totalCount = nx * ny;

            return CommandResult.Ok(new
            {
                handle = boltPattern.Handle,
                bolt_standard = boltStandard,
                bolt_grade = boltGrade,
                bolt_diameter_mm = boltDiameter,
                count = totalCount,
                connected_handles = connectedHandles,
                is_site_bolt = isSiteBolt
            });
        }

        private static Vector3d TryReadVector(CommandContext ctx, string name, Vector3d fallback)
        {
            if (ctx.Body.ValueKind == JsonValueKind.Object
                && ctx.Body.TryGetProperty(name, out var raw)
                && raw.ValueKind == JsonValueKind.Array)
            {
                var coords = new List<double>();
                foreach (var item in raw.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var d))
                    {
                        coords.Add(d);
                    }
                }
                if (coords.Count == 3)
                {
                    return new Vector3d(coords[0], coords[1], coords[2]);
                }
            }
            return fallback;
        }
    }
}
