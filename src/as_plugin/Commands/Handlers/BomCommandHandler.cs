using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Modelling;

using eObjectType = Autodesk.AdvanceSteel.CADAccess.FilerObject.eObjectType;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// Generates Bill of Materials (BOM) / Material Takeoff (MTO) reports from the 3D model.
    /// SPEC-004 §4: get_bill_of_materials (POST /api/v1/production/bom).
    /// </summary>
    public static class BomCommandHandler
    {
        public static CommandResult GenerateBom(CommandContext ctx)
        {
            var elementHandles = ctx.GetStringList("element_handles");
            var groupBy = ctx.GetString("group_by") ?? "profile";

            var beams = new List<Beam>();
            var plates = new List<PlateBase>();
            var bolts = new List<ScrewBoltPattern>();
            int elementsScanned = 0;

            if (elementHandles.Count > 0)
            {
                foreach (var h in elementHandles)
                {
                    var obj = AsQuery.OpenByHandle(h);
                    if (obj == null) continue;
                    elementsScanned++;

                    if (obj is Beam b) beams.Add(b);
                    else if (obj is PlateBase p) plates.Add(p);
                    else if (obj is ScrewBoltPattern sbp) bolts.Add(sbp);
                }
            }
            else
            {
                var atomicIds = AsQuery.ModelObjectIds(eObjectType.kAtomicElem);
                foreach (var id in atomicIds)
                {
                    var obj = AsQuery.Open(id);
                    if (obj == null) continue;
                    elementsScanned++;

                    if (obj is Beam b) beams.Add(b);
                    else if (obj is PlateBase p) plates.Add(p);
                }

                var boltIds = AsQuery.ModelObjectIds(eObjectType.kBoltPattern);
                foreach (var id in boltIds)
                {
                    var obj = AsQuery.Open(id);
                    if (obj is ScrewBoltPattern sbp)
                    {
                        elementsScanned++;
                        bolts.Add(sbp);
                    }
                }
            }

            double totalWeightKg = 0.0;
            double totalCoatingAreaM2 = 0.0;

            // 1. Process Beams
            var beamGroups = new Dictionary<string, (string Section, string Material, int Count, double LengthMm, double WeightKg, double CoatingM2)>();

            foreach (var b in beams)
            {
                var section = AsQuery.Safe(() => b.ProfName, "Unknown") ?? "Unknown";
                var material = AsQuery.Safe(() => b.Material, "S275JR") ?? "S275JR";
                double len = AsQuery.Safe(() => b.GetLength(), 0.0);
                double wt = AsQuery.Safe(() => b.GetWeight(0), 0.0);
                double paint = AsQuery.Safe(() => b.GetPaintArea(), 0.0);

                totalWeightKg += wt;
                totalCoatingAreaM2 += paint;

                var key = $"{section}::{material}";
                if (!beamGroups.TryGetValue(key, out var agg))
                {
                    beamGroups[key] = (section, material, 1, len, wt, paint);
                }
                else
                {
                    beamGroups[key] = (section, material, agg.Count + 1, agg.LengthMm + len, agg.WeightKg + wt, agg.CoatingM2 + paint);
                }
            }

            var linearMembersList = beamGroups.Values.Select(v => new
            {
                section_name = v.Section,
                material = v.Material,
                count = v.Count,
                total_length_mm = Math.Round(v.LengthMm, 1),
                total_weight_kg = Math.Round(v.WeightKg, 2),
                coating_area_m2 = Math.Round(v.CoatingM2, 3)
            }).ToList();

            // 2. Process Plates
            var plateGroups = new Dictionary<string, (double Thickness, string Material, int Count, double AreaM2, double WeightKg)>();

            foreach (var p in plates)
            {
                double thk = AsQuery.Safe(() => p.Thickness, 0.0);
                var material = AsQuery.Safe(() => p.Material, "S275JR") ?? "S275JR";
                double area = AsQuery.Safe(() => p.GetArea(), 0.0);
                // Advance Steel area is usually mm^2, convert to m^2 if > 100
                if (area > 100.0) area /= 1e6;
                double wt = AsQuery.Safe(() => p.GetWeight(), 0.0);

                totalWeightKg += wt;

                var key = $"{thk:F1}::{material}";
                if (!plateGroups.TryGetValue(key, out var agg))
                {
                    plateGroups[key] = (thk, material, 1, area, wt);
                }
                else
                {
                    plateGroups[key] = (thk, material, agg.Count + 1, agg.AreaM2 + area, agg.WeightKg + wt);
                }
            }

            var platesList = plateGroups.Values.Select(v => new
            {
                thickness_mm = Math.Round(v.Thickness, 1),
                material = v.Material,
                count = v.Count,
                total_area_m2 = Math.Round(v.AreaM2, 4),
                total_weight_kg = Math.Round(v.WeightKg, 2)
            }).ToList();

            // 3. Process Bolts
            var boltGroups = new Dictionary<string, (string Standard, string Grade, double Diameter, int Count, double WeightKg)>();

            foreach (var sbp in bolts)
            {
                var standard = AsQuery.Safe(() => sbp.Standard, "DIN 931") ?? "DIN 931";
                var grade = AsQuery.Safe(() => sbp.Grade, "8.8") ?? "8.8";
                double diam = AsQuery.Safe(() => sbp.ScrewDiameter, 20.0);
                int count = sbp is CountableScrewBoltPattern cbp ? (cbp.Nx * cbp.Ny) : 1;
                double wt = AsQuery.Safe(() => sbp.GetWeight(), 0.0);

                totalWeightKg += wt;

                var key = $"{standard}::{grade}::{diam:F1}";
                if (!boltGroups.TryGetValue(key, out var agg))
                {
                    boltGroups[key] = (standard, grade, diam, count, wt);
                }
                else
                {
                    boltGroups[key] = (standard, grade, diam, agg.Count + count, agg.WeightKg + wt);
                }
            }

            var boltsList = boltGroups.Values.Select(v => new
            {
                bolt_standard = v.Standard,
                bolt_grade = v.Grade,
                bolt_diameter_mm = Math.Round(v.Diameter, 1),
                count = v.Count
            }).ToList();

            double totalTonnage = Math.Round(totalWeightKg / 1000.0, 3);

            return CommandResult.Ok(new
            {
                total_weight_kg = Math.Round(totalWeightKg, 2),
                total_tonnage = totalTonnage,
                total_coating_area_m2 = Math.Round(totalCoatingAreaM2, 3),
                linear_members = linearMembersList,
                plates = platesList,
                bolts = boltsList,
                group_by = groupBy,
                elements_scanned = elementsScanned
            });
        }
    }
}
