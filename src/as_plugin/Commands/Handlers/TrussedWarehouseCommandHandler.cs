using System;
using System.Collections.Generic;
using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.CADLink.Database;
using Autodesk.AdvanceSteel.Geometry;
using Autodesk.AdvanceSteel.Modelling;
using Autodesk.AdvanceSteel.Profiles;
using Autodesk.AutoCAD.Geometry;

// Distinct aliases for Point3d and Vector3d
using AsPoint3d = Autodesk.AdvanceSteel.Geometry.Point3d;
using AsVector3d = Autodesk.AdvanceSteel.Geometry.Vector3d;
using AsMatrix3d = Autodesk.AdvanceSteel.Geometry.Matrix3d;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// POST /api/v1/elements/trussed-warehouse — SPEC-004 §9 <c>create_trussed_warehouse</c>.
    /// Generates complete multi-bay industrial warehouse parameterized from real fabrication geometry (version1.dwg).
    /// </summary>
    public static class TrussedWarehouseCommandHandler
    {
        private const string DefaultProfile = "RHS_Sections_square_c nach DIN#@§@#Q90X3";
        private const string DefaultMaterial = "S235JR";

        public static CommandResult CreateTrussedWarehouse(CommandContext ctx)
        {
            var span = ctx.GetDouble("span", 31000.0);
            var length = ctx.GetDouble("length", 30000.0);
            var baySpacing = ctx.GetDouble("bay_spacing", 5000.0);
            var eaveHeight = ctx.GetDouble("eave_height", 6000.0);
            var ridgeHeight = ctx.GetDouble("ridge_height", 9500.0);
            var columnWidth = ctx.GetDouble("column_width", 1000.0);
            var trussDepth = ctx.GetDouble("truss_depth", 2000.0);
            var profile = ctx.GetString("profile") ?? DefaultProfile;
            var material = ctx.GetString("material") ?? DefaultMaterial;
            var createGridsAndLevels = ctx.GetBool("create_grids_and_levels", true);

            if (span <= 5000.0 || length <= 1000.0 || baySpacing <= 500.0 || eaveHeight <= 1000.0 || ridgeHeight <= eaveHeight)
            {
                return CommandResult.Fail(
                    "INVALID_PARAMETER",
                    "Invalid warehouse dimensions: ensure span >= 5000, ridge_height > eave_height, and positive bay spacing.",
                    400);
            }

            int numBays = (int)Math.Round(length / baySpacing);
            int frameCount = numBays + 1;

            var createdColumnHandles = new List<string>();
            var createdRafterHandles = new List<string>();
            var allBarHandles = new List<string>();
            var basePlateHandles = new List<string>();

            // Reference vector: kYAxis ensures profiles align vertically and do not twist
            var refVector = AsVector3d.kYAxis;

            try
            {
                for (int f = 0; f < frameCount; f++)
                {
                    double y = f * baySpacing;

                    // 1. Columns (Double Chord Lattice)
                    // Left Column: Outer chord (0, y), Inner chord (columnWidth, y)
                    var cLeftOuter = CreateBeam(new AsPoint3d(0, y, 0), new AsPoint3d(0, y, eaveHeight), profile, material, "Column", refVector);
                    var cLeftInner = CreateBeam(new AsPoint3d(columnWidth, y, 0), new AsPoint3d(columnWidth, y, eaveHeight), profile, material, "Column", refVector);
                    createdColumnHandles.Add(cLeftOuter.Handle);
                    createdColumnHandles.Add(cLeftInner.Handle);
                    allBarHandles.Add(cLeftOuter.Handle);
                    allBarHandles.Add(cLeftInner.Handle);

                    // Right Column: Inner chord (span - columnWidth, y), Outer chord (span, y)
                    var cRightInner = CreateBeam(new AsPoint3d(span - columnWidth, y, 0), new AsPoint3d(span - columnWidth, y, eaveHeight), profile, material, "Column", refVector);
                    var cRightOuter = CreateBeam(new AsPoint3d(span, y, 0), new AsPoint3d(span, y, eaveHeight), profile, material, "Column", refVector);
                    createdColumnHandles.Add(cRightInner.Handle);
                    createdColumnHandles.Add(cRightOuter.Handle);
                    allBarHandles.Add(cRightOuter.Handle);
                    allBarHandles.Add(cRightInner.Handle);

                    // Column Lacing (horizontal struts and 45-deg diagonals every 1.0 m)
                    int colPanels = (int)Math.Floor(eaveHeight / 1000.0);
                    for (int p = 0; p < colPanels; p++)
                    {
                        double z1 = p * 1000.0;
                        double z2 = (p + 1) * 1000.0;

                        // Left column struts & diagonals
                        var sLeft = CreateBeam(new AsPoint3d(0, y, z2), new AsPoint3d(columnWidth, y, z2), profile, material, "TieBeam", refVector);
                        var dLeft = CreateBeam(new AsPoint3d(0, y, z1), new AsPoint3d(columnWidth, y, z2), profile, material, "Bracing", refVector);
                        allBarHandles.Add(sLeft.Handle);
                        allBarHandles.Add(dLeft.Handle);

                        // Right column struts & diagonals
                        var sRight = CreateBeam(new AsPoint3d(span - columnWidth, y, z2), new AsPoint3d(span, y, z2), profile, material, "TieBeam", refVector);
                        var dRight = CreateBeam(new AsPoint3d(span - columnWidth, y, z1), new AsPoint3d(span, y, z2), profile, material, "Bracing", refVector);
                        allBarHandles.Add(sRight.Handle);
                        allBarHandles.Add(dRight.Handle);
                    }

                    // 2. Double-Pitch Roof Truss
                    double apexX = span / 2.0;
                    double upperEaveZ = eaveHeight + trussDepth;
                    double lowerEaveZ = eaveHeight;
                    double upperRidgeZ = ridgeHeight;
                    double lowerRidgeZ = ridgeHeight - trussDepth;

                    // Left Rafter Chords
                    var rLeftUpper = CreateBeam(new AsPoint3d(0, y, upperEaveZ), new AsPoint3d(apexX, y, upperRidgeZ), profile, material, "Rafter", refVector);
                    var rLeftLower = CreateBeam(new AsPoint3d(columnWidth, y, lowerEaveZ), new AsPoint3d(apexX, y, lowerRidgeZ), profile, material, "Rafter", refVector);
                    createdRafterHandles.Add(rLeftUpper.Handle);
                    createdRafterHandles.Add(rLeftLower.Handle);
                    allBarHandles.Add(rLeftUpper.Handle);
                    allBarHandles.Add(rLeftLower.Handle);

                    // Right Rafter Chords
                    var rRightUpper = CreateBeam(new AsPoint3d(apexX, y, upperRidgeZ), new AsPoint3d(span, y, upperEaveZ), profile, material, "Rafter", refVector);
                    var rRightLower = CreateBeam(new AsPoint3d(apexX, y, lowerRidgeZ), new AsPoint3d(span - columnWidth, y, lowerEaveZ), profile, material, "Rafter", refVector);
                    createdRafterHandles.Add(rRightUpper.Handle);
                    createdRafterHandles.Add(rRightLower.Handle);
                    allBarHandles.Add(rRightUpper.Handle);
                    allBarHandles.Add(rRightLower.Handle);

                    // Roof Truss Webbing (Vertical Struts and Warren Diagonals)
                    int panelsPerHalf = 8;
                    double dxLeft = (apexX - columnWidth) / panelsPerHalf;
                    for (int i = 0; i <= panelsPerHalf; i++)
                    {
                        double xL = columnWidth + i * dxLeft;
                        double tL = (double)i / panelsPerHalf;
                        double zLower = lowerEaveZ + tL * (lowerRidgeZ - lowerEaveZ);
                        double zUpper = (eaveHeight + trussDepth) + ((xL - 0.0) / apexX) * (upperRidgeZ - (eaveHeight + trussDepth));

                        // Vertical strut
                        var vStrut = CreateBeam(new AsPoint3d(xL, y, zLower), new AsPoint3d(xL, y, zUpper), profile, material, "TieBeam", refVector);
                        allBarHandles.Add(vStrut.Handle);

                        // Diagonal to next panel
                        if (i < panelsPerHalf)
                        {
                            double xNext = columnWidth + (i + 1) * dxLeft;
                            double tNext = (double)(i + 1) / panelsPerHalf;
                            double zUpperNext = (eaveHeight + trussDepth) + ((xNext - 0.0) / apexX) * (upperRidgeZ - (eaveHeight + trussDepth));
                            var diag = CreateBeam(new AsPoint3d(xL, y, zLower), new AsPoint3d(xNext, y, zUpperNext), profile, material, "Bracing", refVector);
                            allBarHandles.Add(diag.Handle);
                        }
                    }

                    // Symmetrical Right Half Webbing
                    double dxRight = (span - columnWidth - apexX) / panelsPerHalf;
                    for (int i = 0; i <= panelsPerHalf; i++)
                    {
                        double xR = apexX + i * dxRight;
                        double tR = (double)i / panelsPerHalf;
                        double zLower = lowerRidgeZ - tR * (lowerRidgeZ - lowerEaveZ);
                        double zUpper = upperRidgeZ - ((xR - apexX) / (span - apexX)) * (upperRidgeZ - upperEaveZ);

                        // Vertical strut
                        var vStrut = CreateBeam(new AsPoint3d(xR, y, zLower), new AsPoint3d(xR, y, zUpper), profile, material, "TieBeam", refVector);
                        allBarHandles.Add(vStrut.Handle);

                        // Diagonal
                        if (i < panelsPerHalf)
                        {
                            double xNext = apexX + (i + 1) * dxRight;
                            double tNext = (double)(i + 1) / panelsPerHalf;
                            double zLowerNext = lowerRidgeZ - tNext * (lowerRidgeZ - lowerEaveZ);
                            var diag = CreateBeam(new AsPoint3d(xR, y, zUpper), new AsPoint3d(xNext, y, zLowerNext), profile, material, "Bracing", refVector);
                            allBarHandles.Add(diag.Handle);
                        }
                    }

                    // 3. Base Plates on Column Bases
                    basePlateHandles.Add(CreateBasePlate(new AsPoint3d(0, y, 0), 400.0, 400.0, 25.0, material));
                    basePlateHandles.Add(CreateBasePlate(new AsPoint3d(columnWidth, y, 0), 400.0, 400.0, 25.0, material));
                    basePlateHandles.Add(CreateBasePlate(new AsPoint3d(span - columnWidth, y, 0), 400.0, 400.0, 25.0, material));
                    basePlateHandles.Add(CreateBasePlate(new AsPoint3d(span, y, 0), 400.0, 400.0, 25.0, material));
                }

                object? gridsResult = null;
                var levelsResult = new List<object>();

                // 4. Automatic Grids and Levels Creation
                if (createGridsAndLevels)
                {
                    try
                    {
                        // Transverse Grid along Y (Axes 1..N)
                        var csTrans = new AsMatrix3d();
                        csTrans.SetCoordSystem(new AsPoint3d(0, 0, 0), AsVector3d.kXAxis, AsVector3d.kYAxis, AsVector3d.kZAxis);
                        var gridY = new Grid1D(csTrans, span, length, frameCount);
                        gridY.WriteToDb();

                        // Longitudinal Grid along X (Axes A..B)
                        var csLong = new AsMatrix3d();
                        csLong.SetCoordSystem(new AsPoint3d(0, 0, 0), AsVector3d.kYAxis, AsVector3d.kXAxis, AsVector3d.kZAxis);
                        var gridX = new Grid1D(csLong, length, span, 2);
                        gridX.WriteToDb();

                        gridsResult = new
                        {
                            transverse_handle = gridY.Handle,
                            longitudinal_handle = gridX.Handle,
                            axes_count = frameCount + 2
                        };

                        // Storeys / Building Levels
                        var mgr = Autodesk.AdvanceSteel.BuildingStructure.BuildingStructureManager.getBuildingStructureManager();
                        var bso = mgr?.CurrentBSO;
                        var tree = bso?.LevelTreeObject;
                        if (tree != null)
                        {
                            var l0 = Autodesk.AdvanceSteel.BuildingStructure.LevelObject.Create(tree, "Nivel 0.00m (Cimentación)", 0.0, null, null);
                            l0.WriteToDb();
                            levelsResult.Add(new { name = "Nivel 0.00m (Cimentación)", elevation = 0.0, handle = l0.Handle });

                            var lEave = Autodesk.AdvanceSteel.BuildingStructure.LevelObject.Create(tree, "Nivel +6.00m (Alero)", eaveHeight, null, l0);
                            lEave.WriteToDb();
                            levelsResult.Add(new { name = "Nivel +6.00m (Alero)", elevation = eaveHeight, handle = lEave.Handle });

                            var lRidge = Autodesk.AdvanceSteel.BuildingStructure.LevelObject.Create(tree, "Nivel +9.50m (Cumbrera)", ridgeHeight, null, lEave);
                            lRidge.WriteToDb();
                            levelsResult.Add(new { name = "Nivel +9.50m (Cumbrera)", elevation = ridgeHeight, handle = lRidge.Handle });
                        }
                    }
                    catch (Exception)
                    {
                        // Grids/levels creation warning non-fatal
                    }
                }

                return CommandResult.Ok(new
                {
                    frames_count = frameCount,
                    total_bars = allBarHandles.Count,
                    columns = new { count = createdColumnHandles.Count, handles = createdColumnHandles },
                    rafters = new { count = createdRafterHandles.Count, handles = createdRafterHandles },
                    base_plates = new { count = basePlateHandles.Count, handles = basePlateHandles },
                    all_bars_count = allBarHandles.Count,
                    grids_created = gridsResult,
                    levels_created = levelsResult
                });
            }
            catch (Exception ex)
            {
                return CommandResult.Fail(
                    "WAREHOUSE_CREATION_FAILED",
                    ex.Message,
                    500,
                    "Error generating parametric trussed warehouse in Advance Steel.",
                    ex.StackTrace);
            }
        }

        private static StraightBeam CreateBeam(AsPoint3d start, AsPoint3d end, string section, string material, string role, AsVector3d refVector)
        {
            var beam = new StraightBeam(section, start, end, refVector);
            beam.Material = material;
            beam.Role = role;
            beam.RefAxis = Beam.eRefAxis.kUpperSys;
            beam.WriteToDb();
            return beam;
        }

        private static string CreateBasePlate(AsPoint3d center, double dx, double dy, double thickness, string material)
        {
            var halfX = dx / 2.0;
            var halfY = dy / 2.0;
            var pts = new[]
            {
                new AsPoint3d(center.x - halfX, center.y - halfY, center.z),
                new AsPoint3d(center.x + halfX, center.y - halfY, center.z),
                new AsPoint3d(center.x + halfX, center.y + halfY, center.z),
                new AsPoint3d(center.x - halfX, center.y + halfY, center.z)
            };

            var plate = new Plate(new Autodesk.AdvanceSteel.Geometry.Plane(center, AsVector3d.kZAxis), pts, thickness);
            plate.Material = material;
            plate.Role = "BasePlate";
            plate.WriteToDb();
            return plate.Handle;
        }
    }
}
