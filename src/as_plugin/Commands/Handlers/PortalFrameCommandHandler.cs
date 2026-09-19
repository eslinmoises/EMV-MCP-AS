using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Geometry;
using Autodesk.AdvanceSteel.Modelling;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// Generative parametric macro for complete 3D structural portal frames.
    /// SPEC-004 §2: create_portal_frame (POST /api/v1/elements/portal-frame).
    /// </summary>
    public static class PortalFrameCommandHandler
    {
        public static CommandResult Create(CommandContext ctx)
        {
            double span = ctx.GetDouble("span_width_mm", 12000.0);
            double colHeight = ctx.GetDouble("column_height_mm", 5000.0);
            double ridgeHeight = ctx.GetDouble("ridge_height_mm", 6500.0);
            string colSection = ctx.GetString("column_section", "HEB300")!;
            string rafterSection = ctx.GetString("rafter_section", "IPE360")!;
            string material = ctx.GetString("material", "S275JR")!;
            bool includeBasePlates = ctx.GetBool("include_base_plates", true);

            if (span < 1000.0 || colHeight < 1000.0 || ridgeHeight <= colHeight)
            {
                return CommandResult.Fail(
                    "INVALID_DIMENSIONS",
                    $"Invalid portal frame dimensions: span={span} mm, colHeight={colHeight} mm, ridgeHeight={ridgeHeight} mm. Ridge must exceed column height.",
                    400,
                    "Ensure span >= 1000, column_height >= 1000, and ridge_height > column_height.");
            }

            Point3d origin = Point3d.kOrigin;
            if (ctx.Body.ValueKind == JsonValueKind.Object && ctx.Body.TryGetProperty("origin", out var originProp))
            {
                if (originProp.ValueKind == JsonValueKind.Array && originProp.GetArrayLength() >= 3)
                {
                    origin = new Point3d(
                        originProp[0].GetDouble(),
                        originProp[1].GetDouble(),
                        originProp[2].GetDouble());
                }
            }

            double ox = origin.x;
            double oy = origin.y;
            double oz = origin.z;

            // 1. Column points
            var colLeftStart = new Point3d(ox, oy, oz);
            var colLeftEnd = new Point3d(ox, oy, oz + colHeight);

            var colRightStart = new Point3d(ox + span, oy, oz);
            var colRightEnd = new Point3d(ox + span, oy, oz + colHeight);

            // 2. Rafter points
            var apex = new Point3d(ox + span / 2.0, oy, oz + ridgeHeight);

            // Helper to instantiate beam
            StraightBeam MakeBeam(Point3d start, Point3d end, string section, string role)
            {
                var axis = end.Subtract(start).Normalize();
                var refVector = axis.IsParallelTo(Vector3d.kZAxis) ? Vector3d.kXAxis : Vector3d.kZAxis;
                var beam = new StraightBeam(section, start, end, refVector);
                beam.RefAxis = Beam.eRefAxis.kSysSys;
                beam.Material = material;
                beam.Role = role;
                beam.WriteToDb();
                return beam;
            }

            var col1 = MakeBeam(colLeftStart, colLeftEnd, colSection, "Column");
            var col2 = MakeBeam(colRightStart, colRightEnd, colSection, "Column");
            var raf1 = MakeBeam(colLeftEnd, apex, rafterSection, "Rafter");
            var raf2 = MakeBeam(apex, colRightEnd, rafterSection, "Rafter");

            var basePlates = new List<string>();
            var boltPatterns = new List<string>();
            double totalWeight = AsQuery.WeightKg(col1) + AsQuery.WeightKg(col2) + AsQuery.WeightKg(raf1) + AsQuery.WeightKg(raf2);

            if (includeBasePlates)
            {
                void MakeBasePlateAndBolts(Point3d center, Beam column)
                {
                    double halfW = 200.0;
                    double thk = 25.0;
                    var pts = new[]
                    {
                        new Point3d(center.x - halfW, center.y - halfW, center.z),
                        new Point3d(center.x + halfW, center.y - halfW, center.z),
                        new Point3d(center.x + halfW, center.y + halfW, center.z),
                        new Point3d(center.x - halfW, center.y + halfW, center.z)
                    };

                    var plane = new Plane(pts[0], Vector3d.kZAxis);
                    var plate = new Plate(plane, pts, thk);
                    plate.Material = material;
                    plate.Role = "BasePlate";
                    plate.WriteToDb();
                    basePlates.Add(plate.Handle);
                    totalWeight += AsQuery.WeightKg(plate);

                    // Bolt Pattern
                    double halfBolt = 140.0;
                    var ptC1 = new Point3d(center.x - halfBolt, center.y - halfBolt, center.z);
                    var ptC2 = new Point3d(center.x + halfBolt, center.y + halfBolt, center.z);
                    var pattern = new FinitRectScrewBoltPattern(ptC1, ptC2, Vector3d.kXAxis, Vector3d.kYAxis);
                    pattern.WriteToDb();
                    pattern.Standard = "DIN 931";
                    pattern.Grade = "8.8";
                    pattern.ScrewDiameter = 20.0;
                    pattern.Nx = 2;
                    pattern.Ny = 2;
                    pattern.Dx = 280.0;
                    pattern.Dy = 280.0;
                    pattern.Connect(new FilerObject[] { column, plate }, AtomicElement.eAssemblyLocation.kOnSite);
                    boltPatterns.Add(pattern.Handle);
                }

                MakeBasePlateAndBolts(colLeftStart, col1);
                MakeBasePlateAndBolts(colRightStart, col2);
            }

            return CommandResult.Ok(new
            {
                left_column = new
                {
                    handle = col1.Handle,
                    section = colSection,
                    height_mm = colHeight,
                    weight_kg = Math.Round(AsQuery.WeightKg(col1), 2)
                },
                right_column = new
                {
                    handle = col2.Handle,
                    section = colSection,
                    height_mm = colHeight,
                    weight_kg = Math.Round(AsQuery.WeightKg(col2), 2)
                },
                left_rafter = new
                {
                    handle = raf1.Handle,
                    section = rafterSection,
                    length_mm = Math.Round(colLeftEnd.DistanceTo(apex), 1),
                    weight_kg = Math.Round(AsQuery.WeightKg(raf1), 2)
                },
                right_rafter = new
                {
                    handle = raf2.Handle,
                    section = rafterSection,
                    length_mm = Math.Round(apex.DistanceTo(colRightEnd), 1),
                    weight_kg = Math.Round(AsQuery.WeightKg(raf2), 2)
                },
                base_plates = basePlates,
                bolt_patterns = boltPatterns,
                total_weight_kg = Math.Round(totalWeight, 2)
            });
        }
    }
}
