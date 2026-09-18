using System;
using System.Collections.Generic;
using Autodesk.AdvanceSteel.BuildingStructure;
using Autodesk.AdvanceSteel.Modelling;
using Autodesk.AutoCAD.ApplicationServices;

// Advance Steel declares these enums nested inside the class they belong to.
using eGridLocation = Autodesk.AdvanceSteel.Modelling.GridElement.eLocation;
using eObjectType = Autodesk.AdvanceSteel.CADAccess.FilerObject.eObjectType;

// Both AutoCAD and Advance Steel ship a Point3d / Matrix3d; keep them apart explicitly.
using AcMatrix3d = Autodesk.AutoCAD.Geometry.Matrix3d;
using AcPoint3d = Autodesk.AutoCAD.Geometry.Point3d;
using AcVector3d = Autodesk.AutoCAD.Geometry.Vector3d;
using AsCurve3d = Autodesk.AdvanceSteel.Geometry.Curve3d;
using AsPoint3d = Autodesk.AdvanceSteel.Geometry.Point3d;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// GET /api/v1/spatial/ucs-grids — SPEC-004 §1 <c>get_ucs_and_grids</c>.
    ///
    /// rules/advance-steel-modeling.md §1 is the reason this endpoint exists: every coordinate an
    /// agent sends is meaningless until it knows whether the model is being driven from the WCS or
    /// from a rotated UCS. The response therefore always reports the active UCS first, then the
    /// structural grids and building levels an agent can snap new geometry to.
    /// </summary>
    public static class SpatialCommandHandler
    {
        public static CommandResult GetUcsAndGrids(CommandContext ctx)
        {
            object activeUcs;
            try
            {
                activeUcs = ReadActiveUcs(ctx);
            }
            catch (System.Exception ex)
            {
                return CommandResult.Fail(
                    "UCS_QUERY_FAILED", ex.Message, 500,
                    "The active drawing did not return a usable coordinate system.",
                    ex.StackTrace);
            }

            var gridAxes = new List<object>();
            var gridWarnings = new List<string>();
            CollectGrids(gridAxes, gridWarnings);

            var levels = CollectLevels(out var levelsAreDefault);

            return CommandResult.Ok(new
            {
                active_ucs = activeUcs,
                grid_axes = gridAxes,
                grid_axis_count = gridAxes.Count,
                levels,
                level_count = levels.Count,
                levels_are_default = levelsAreDefault,
                warnings = gridWarnings
            });
        }

        // ------------------------------------------------------------------
        // Active UCS
        // ------------------------------------------------------------------

        /// <summary>
        /// The editor's current UCS is the one the user is actually drawing in, which is what an
        /// agent must mirror. The WCS is reported alongside it so coordinates can be converted
        /// without a second round trip.
        /// </summary>
        private static object ReadActiveUcs(CommandContext ctx)
        {
            AcMatrix3d ucs = ctx.Doc.Editor.CurrentUserCoordinateSystem;
            var cs = ucs.CoordinateSystem3d;

            var origin = cs.Origin;
            var xAxis = cs.Xaxis;
            var yAxis = cs.Yaxis;
            var zAxis = cs.Zaxis;

            var isWorld = IsNear(origin, AcPoint3d.Origin)
                          && IsNear(xAxis, AcVector3d.XAxis)
                          && IsNear(yAxis, AcVector3d.YAxis)
                          && IsNear(zAxis, AcVector3d.ZAxis);

            return new
            {
                name = UcsName(ctx, isWorld),
                origin = ToArray(origin),
                x_axis = ToArray(xAxis),
                y_axis = ToArray(yAxis),
                z_axis = ToArray(zAxis),
                is_world = isWorld
            };
        }

        /// <summary>
        /// Named UCS as stored in the drawing's UCS table. An unnamed (ad-hoc) UCS has no record,
        /// which is not an error: the axes above already describe it fully.
        /// </summary>
        private static string UcsName(CommandContext ctx, bool isWorld)
        {
            if (isWorld) return "World";

            try
            {
                var ucsId = ctx.Doc.Database.Ucsname;
                if (ucsId.IsNull) return "Unnamed";

                var record = ctx.AcadTransaction.GetObject(
                    ucsId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead)
                    as Autodesk.AutoCAD.DatabaseServices.UcsTableRecord;

                return string.IsNullOrWhiteSpace(record?.Name) ? "Unnamed" : record!.Name;
            }
            catch (System.Exception)
            {
                return "Unnamed";
            }
        }

        // ------------------------------------------------------------------
        // Structural grids
        // ------------------------------------------------------------------

        /// <summary>
        /// Advance Steel stores a grid as one <see cref="Grid"/> object holding N
        /// <see cref="GridElement"/> axes; the individual axis lines are not model objects of
        /// their own. kGrid covers both Grid1D and GridCircle, so one query is enough.
        /// </summary>
        private static void CollectGrids(List<object> gridAxes, List<string> warnings)
        {
            Autodesk.AdvanceSteel.CADLink.Database.ObjectId[] gridIds;

            try
            {
                gridIds = AsQuery.ModelObjectIds(eObjectType.kGrid);
            }
            catch (System.Exception ex)
            {
                warnings.Add($"Grid query failed: {ex.Message}");
                return;
            }

            foreach (var id in gridIds)
            {
                if (AsQuery.Open(id) is not Grid grid) continue;

                var gridHandle = AsQuery.Safe(() => grid.Handle, null);
                var gridType = AsQuery.Safe(() => grid.GridType.ToString(), "kUndefined");
                var coordinateSystem = AsQuery.Safe(() => grid.CS, null);

                GridElement[]? elements = null;
                try
                {
                    grid.GetAllElements(out elements);
                }
                catch (System.Exception ex)
                {
                    warnings.Add($"Grid '{gridHandle}' did not return its axes: {ex.Message}");
                    continue;
                }

                foreach (var element in elements ?? Array.Empty<GridElement>())
                {
                    if (element == null) continue;

                    var axis = DescribeAxis(element, coordinateSystem, gridHandle, gridType);
                    if (axis != null) gridAxes.Add(axis);
                }
            }
        }

        private static object? DescribeAxis(
            GridElement element,
            Autodesk.AdvanceSteel.Geometry.Matrix3d? coordinateSystem,
            string? gridHandle,
            string gridType)
        {
            AsCurve3d curve = null!;

            try
            {
                // The axis curve is stored in the grid's own coordinate system; passing the grid CS
                // lifts it into world coordinates, which is what the agent needs.
                element.GetCurve(
                    ref curve,
                    coordinateSystem ?? Autodesk.AdvanceSteel.Geometry.Matrix3d.kIdentity);
            }
            catch (System.Exception)
            {
                return null;
            }

            if (curve == null) return null;

            var start = EndpointOf(curve, atStart: true);
            var end = EndpointOf(curve, atStart: false);

            return new
            {
                name = AxisName(element),
                start = ToArray(start),
                end = ToArray(end),
                length = AsQuery.Safe(() => Math.Round(curve.GetLength(), 3), 0.0),
                grid_handle = gridHandle,
                grid_type = gridType
            };
        }

        /// <summary>
        /// A closed axis (a circular grid ring) has no endpoints; that case yields null rather
        /// than a fabricated coordinate.
        /// </summary>
        private static AsPoint3d? EndpointOf(AsCurve3d curve, bool atStart)
        {
            try
            {
                var found = atStart
                    ? curve.HasStartPoint(out var point)
                    : curve.HasEndPoint(out point);

                return found ? point : null;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The axis label ("A", "B", "1", "2"). Advance Steel can annotate either end of the axis,
        /// so both slots are checked before giving up.
        /// </summary>
        private static string? AxisName(GridElement element)
        {
            foreach (var location in new[] { eGridLocation.kStart, eGridLocation.kEnd, eGridLocation.kMidArc })
            {
                var text = AsQuery.Safe(() => element.GetTextAt(location), null);
                if (!string.IsNullOrWhiteSpace(text)) return text!.Trim();
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Building levels
        // ------------------------------------------------------------------

        /// <summary>
        /// Building levels live in the Advance Steel "Building Structure" tree, not in the model
        /// space. A model detailed without that tree simply has no levels, in which case the ±0.00
        /// model datum is reported so an agent always has one elevation to work from.
        /// </summary>
        private static List<object> CollectLevels(out bool areDefault)
        {
            var levels = new List<object>();
            areDefault = false;

            Autodesk.AdvanceSteel.CADLink.Database.ObjectId[] levelIds;

            try
            {
                levelIds = AsQuery.ModelObjectIds(eObjectType.kLevelObject);
            }
            catch (System.Exception)
            {
                levelIds = Array.Empty<Autodesk.AdvanceSteel.CADLink.Database.ObjectId>();
            }

            foreach (var id in levelIds)
            {
                if (AsQuery.Open(id) is not LevelObject level) continue;

                levels.Add(new
                {
                    name = LevelName(level),
                    elevation = LevelElevation(level),
                    handle = AsQuery.Safe(() => level.Handle, null)
                });
            }

            if (levels.Count == 0)
            {
                areDefault = true;
                levels.Add(new
                {
                    name = "+0.00",
                    elevation = 0.0,
                    handle = (string?)null
                });
            }

            return levels;
        }

        private static string? LevelName(LevelObject level)
        {
            var name = AsQuery.Safe(() => level.Parent?.GetStructureItemName(level), null);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        /// <summary>
        /// The altitude of the level's main working plane is the elevation a detailer reads off the
        /// drawing. Levels defined relative to the one below expose it only through the absolute
        /// height calculation, which is used as the fallback.
        /// </summary>
        private static double LevelElevation(LevelObject level)
        {
            var altitude = AsQuery.Safe(() => level.MainWorkingPlane?.getAltitude(), null);
            if (altitude.HasValue) return Math.Round(altitude.Value, 3);

            try
            {
                level.CalculateMainPlaneAbsoluteHeight(out var height);
                return Math.Round(height, 3);
            }
            catch (System.Exception)
            {
                return 0.0;
            }
        }

        // ------------------------------------------------------------------
        // Conversion helpers
        // ------------------------------------------------------------------

        private const double Tolerance = 1e-9;

        private static double[] ToArray(AcPoint3d point) =>
            new[] { Math.Round(point.X, 4), Math.Round(point.Y, 4), Math.Round(point.Z, 4) };

        private static double[] ToArray(AcVector3d vector) =>
            new[] { Math.Round(vector.X, 6), Math.Round(vector.Y, 6), Math.Round(vector.Z, 6) };

        private static double[]? ToArray(AsPoint3d? point) =>
            point == null
                ? null
                : new[] { Math.Round(point.x, 4), Math.Round(point.y, 4), Math.Round(point.z, 4) };

        private static bool IsNear(AcPoint3d a, AcPoint3d b) =>
            Math.Abs(a.X - b.X) < Tolerance && Math.Abs(a.Y - b.Y) < Tolerance && Math.Abs(a.Z - b.Z) < Tolerance;

        private static bool IsNear(AcVector3d a, AcVector3d b) =>
            Math.Abs(a.X - b.X) < Tolerance && Math.Abs(a.Y - b.Y) < Tolerance && Math.Abs(a.Z - b.Z) < Tolerance;
    }
}
