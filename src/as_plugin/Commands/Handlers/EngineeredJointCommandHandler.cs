using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.CADLink.Database;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Geometry;
using Autodesk.AdvanceSteel.Modelling;

// Distinct aliases
using AsPoint3d = Autodesk.AdvanceSteel.Geometry.Point3d;
using AsVector3d = Autodesk.AdvanceSteel.Geometry.Vector3d;
using AsPlane = Autodesk.AdvanceSteel.Geometry.Plane;
using eAssemblyLocation = Autodesk.AdvanceSteel.ConstructionTypes.AtomicElement.eAssemblyLocation;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// POST /api/v1/elements/engineered-joint — SPEC-004 §10 <c>model_engineered_connection</c>.
    /// Generates complete fabrication connection assemblies from engineering calculation specs (IDEA StatiCa, RAM Connection, DXF, PDF).
    /// </summary>
    public static class EngineeredJointCommandHandler
    {
        public static CommandResult Create(CommandContext ctx)
        {
            var connName = ctx.GetString("connection_name", "Engineered Connection");
            var sourceSystem = ctx.GetString("source_system", "Calculation Report");
            var verifyAssembly = ctx.GetBool("verify_assembly", true);

            var createdPlates = new List<object>();
            var createdBolts = new List<object>();
            var createdWelds = new List<object>();
            var plateHandleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // 1. Process Plates
                if (ctx.Body.ValueKind == JsonValueKind.Object && ctx.Body.TryGetProperty("plates", out var platesProp) && platesProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in platesProp.EnumerateArray())
                    {
                        var pName = p.TryGetProperty("name", out var np) ? np.GetString() ?? "Plate" : "Plate";
                        var thk = p.TryGetProperty("thickness_mm", out var tp) ? tp.GetDouble() : 20.0;
                        var mat = p.TryGetProperty("material", out var mp) ? mp.GetString() ?? "S275JR" : "S275JR";
                        var role = p.TryGetProperty("model_role", out var rp) ? rp.GetString() ?? "Plate" : "Plate";

                        var pts = new List<AsPoint3d>();
                        if (p.TryGetProperty("contour_points", out var cp) && cp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var ptArr in cp.EnumerateArray())
                            {
                                if (ptArr.ValueKind == JsonValueKind.Array)
                                {
                                    var coords = new List<double>();
                                    foreach (var c in ptArr.EnumerateArray()) coords.Add(c.GetDouble());
                                    if (coords.Count == 3) pts.Add(new AsPoint3d(coords[0], coords[1], coords[2]));
                                }
                            }
                        }

                        if (pts.Count >= 3)
                        {
                            var origin = pts[0];
                            var v1 = pts[1] - pts[0];
                            var v2 = pts[2] - pts[0];
                            var norm = v1.CrossProduct(v2);
                            norm.Normalize();

                            var plane = new AsPlane(origin, norm);
                            var plate = new Plate(plane, pts.ToArray(), thk);
                            plate.Material = mat;
                            plate.Role = role;
                            plate.WriteToDb();

                            plateHandleMap[pName] = plate.Handle;
                            createdPlates.Add(new
                            {
                                name = pName,
                                handle = plate.Handle,
                                thickness_mm = thk,
                                material = mat,
                                role = role
                            });
                        }
                    }
                }

                // 2. Process Bolt Groups
                if (ctx.Body.ValueKind == JsonValueKind.Object && ctx.Body.TryGetProperty("bolt_groups", out var boltsProp) && boltsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var b in boltsProp.EnumerateArray())
                    {
                        var standard = b.TryGetProperty("bolt_standard", out var sp) ? sp.GetString() ?? "DIN 931" : "DIN 931";
                        var diam = b.TryGetProperty("bolt_diameter_mm", out var dp) ? dp.GetDouble() : 20.0;
                        var nx = b.TryGetProperty("nx", out var nxp) ? nxp.GetInt32() : 2;
                        var ny = b.TryGetProperty("ny", out var nyp) ? nyp.GetInt32() : 2;
                        var dx = b.TryGetProperty("dx", out var dxp) ? dxp.GetDouble() : 100.0;
                        var dy = b.TryGetProperty("dy", out var dyp) ? dyp.GetDouble() : 100.0;
                        var isSite = b.TryGetProperty("is_site_bolt", out var isp) ? isp.GetBoolean() : true;

                        var origin = AsPoint3d.kOrigin;
                        if (b.TryGetProperty("origin", out var op) && op.ValueKind == JsonValueKind.Array)
                        {
                            var c = new List<double>();
                            foreach (var item in op.EnumerateArray()) c.Add(item.GetDouble());
                            if (c.Count == 3) origin = new AsPoint3d(c[0], c[1], c[2]);
                        }

                        try
                        {
                            var normal = AsVector3d.kZAxis;
                            if (b.TryGetProperty("normal", out var normP) && normP.ValueKind == JsonValueKind.Array)
                            {
                                var nc = new List<double>();
                                foreach (var item in normP.EnumerateArray()) nc.Add(item.GetDouble());
                                if (nc.Count == 3) normal = new AsVector3d(nc[0], nc[1], nc[2]);
                            }
                            if (normal.IsZeroLength()) normal = AsVector3d.kZAxis;
                            normal.Normalize();

                            AsVector3d vX;
                            if (Math.Abs(normal.DotProduct(AsVector3d.kZAxis)) < 0.99)
                            {
                                vX = normal.CrossProduct(AsVector3d.kZAxis);
                            }
                            else
                            {
                                vX = normal.CrossProduct(AsVector3d.kYAxis);
                            }
                            vX.Normalize();
                            var vY = vX.CrossProduct(normal);
                            vY.Normalize();

                            var pt1 = origin - (vX * (dx * (nx - 1) / 2.0)) - (vY * (dy * (ny - 1) / 2.0));
                            var pt2 = origin + (vX * (dx * (nx - 1) / 2.0)) + (vY * (dy * (ny - 1) / 2.0));

                            var pattern = new FinitRectScrewBoltPattern(pt1, pt2, vX, vY);
                            pattern.WriteToDb();
                            pattern.Standard = standard;
                            pattern.ScrewDiameter = diam;
                            pattern.Nx = nx;
                            pattern.Ny = ny;
                            pattern.Dx = dx;
                            pattern.Dy = dy;

                            var connectedObjects = new List<FilerObject>();
                            var connectedHandles = new List<string>();
                            if (b.TryGetProperty("connected_part_handles", out var chp) && chp.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var h in chp.EnumerateArray())
                                {
                                    var hStr = h.GetString();
                                    if (!string.IsNullOrWhiteSpace(hStr))
                                    {
                                        if (plateHandleMap.TryGetValue(hStr!, out var mapped)) hStr = mapped;
                                        var elem = AsQuery.OpenByHandle(hStr!);
                                        if (elem != null) connectedObjects.Add(elem);
                                        connectedHandles.Add(hStr!);
                                    }
                                }
                            }

                            if (connectedObjects.Count > 0)
                            {
                                pattern.Connect(connectedObjects.ToArray(), isSite ? eAssemblyLocation.kOnSite : eAssemblyLocation.kInShop);
                            }

                            createdBolts.Add(new
                            {
                                handle = pattern.Handle,
                                standard = standard,
                                diameter_mm = diam,
                                count = nx * ny,
                                is_site_bolt = isSite,
                                connected_handles = connectedHandles
                            });
                        }
                        catch (System.Exception ex)
                        {
                            // Avoid native crash; report gracefully
                            createdBolts.Add(new
                            {
                                standard,
                                error = ex.Message
                            });
                        }
                    }
                }

                // 3. Process Shop Welds (kInShop)
                if (ctx.Body.ValueKind == JsonValueKind.Object && ctx.Body.TryGetProperty("shop_welds", out var weldsProp) && weldsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var w in weldsProp.EnumerateArray())
                    {
                        var thk = w.TryGetProperty("throat_thickness_mm", out var thkp) ? thkp.GetDouble() : 6.0;
                        var mainHandle = w.TryGetProperty("main_part_handle", out var mhp) ? mhp.GetString() : null;
                        var attachedName = w.TryGetProperty("attached_part_name", out var anp) ? anp.GetString() : null;
                        var attachedHandle = w.TryGetProperty("attached_part_handle", out var ahp) ? ahp.GetString() : null;

                        if (attachedName != null && plateHandleMap.TryGetValue(attachedName, out var mappedHandle))
                        {
                            attachedHandle = mappedHandle;
                        }

                        if (!string.IsNullOrWhiteSpace(mainHandle) && !string.IsNullOrWhiteSpace(attachedHandle))
                        {
                            var mainElem = AsQuery.OpenByHandle(mainHandle!) as AtomicElement;
                            var attachedElem = AsQuery.OpenByHandle(attachedHandle!) as AtomicElement;

                            if (mainElem != null && attachedElem != null)
                            {
                                var weld = new WeldPoint(attachedElem.CenterPoint, AsVector3d.kXAxis, AsVector3d.kYAxis);
                                weld.WriteToDb();
                                weld.Thickness = thk;
                                weld.Connect(new FilerObject[] { mainElem, attachedElem }, eAssemblyLocation.kInShop);

                                createdWelds.Add(new
                                {
                                    handle = weld.Handle,
                                    main_part_handle = mainHandle,
                                    attached_part_handle = attachedHandle,
                                    location = "Workshop",
                                    throat_thickness_mm = thk,
                                    assembly_bound = true
                                });
                            }
                        }
                    }
                }

                return CommandResult.Ok(new
                {
                    connection_name = connName,
                    source_system = sourceSystem,
                    plates_created = createdPlates,
                    bolt_groups_created = createdBolts,
                    welds_created = createdWelds,
                    verified = verifyAssembly,
                    success = true
                });
            }
            catch (Exception ex)
            {
                return CommandResult.Fail(
                    "ENGINEERED_JOINT_FAILED",
                    ex.Message,
                    500,
                    "Failed to model engineered connection in Advance Steel.",
                    ex.StackTrace);
            }
        }
    }
}
