using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Modelling;
using Autodesk.AdvanceSteel.Profiles;

using eObjectType = Autodesk.AdvanceSteel.CADAccess.FilerObject.eObjectType;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// Handles element querying, supported connection joint discovery, and section name validation.
    /// SPEC-004 §1: query_elements, get_supported_joints_catalog, validate_section.
    /// </summary>
    public static class QueryCommandHandler
    {
        public static CommandResult QueryElements(CommandContext ctx)
        {
            var modelRole = ctx.GetString("model_role");
            var material = ctx.GetString("material");
            var sectionName = ctx.GetString("section_name");
            var assemblyMark = ctx.GetString("assembly_mark");
            var singlePartMark = ctx.GetString("single_part_mark");
            var elementTypes = ctx.GetStringList("element_types");
            var handles = ctx.GetStringList("handles");

            var filtersApplied = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(modelRole)) filtersApplied["model_role"] = modelRole!;
            if (!string.IsNullOrWhiteSpace(material)) filtersApplied["material"] = material!;
            if (!string.IsNullOrWhiteSpace(sectionName)) filtersApplied["section_name"] = sectionName!;
            if (!string.IsNullOrWhiteSpace(assemblyMark)) filtersApplied["assembly_mark"] = assemblyMark!;
            if (!string.IsNullOrWhiteSpace(singlePartMark)) filtersApplied["single_part_mark"] = singlePartMark!;
            if (elementTypes.Count > 0) filtersApplied["element_types"] = elementTypes;
            if (handles.Count > 0) filtersApplied["handles"] = handles;

            var typeFilterSet = elementTypes.Count > 0
                ? new HashSet<string>(elementTypes, StringComparer.OrdinalIgnoreCase)
                : null;

            var candidates = new List<AtomicElement>();

            if (handles.Count > 0)
            {
                foreach (var h in handles)
                {
                    if (AsQuery.OpenByHandle(h) is AtomicElement atomic)
                    {
                        candidates.Add(atomic);
                    }
                }
            }
            else
            {
                var objectIds = AsQuery.ModelObjectIds(eObjectType.kAtomicElem);
                foreach (var id in objectIds)
                {
                    if (AsQuery.Open(id) is AtomicElement atomic)
                    {
                        candidates.Add(atomic);
                    }
                }
            }

            var results = new List<object>();

            foreach (var atomic in candidates)
            {
                var type = AsQuery.TypeName(atomic);
                if (typeFilterSet != null && !typeFilterSet.Contains(type)) continue;

                if (!string.IsNullOrWhiteSpace(modelRole))
                {
                    var role = AsQuery.Safe(() => atomic.Role, null);
                    if (role == null || role.IndexOf(modelRole!, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                if (!string.IsNullOrWhiteSpace(material))
                {
                    var mat = AsQuery.Safe(() => atomic.Material, null);
                    if (mat == null || mat.IndexOf(material!, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                if (!string.IsNullOrWhiteSpace(sectionName))
                {
                    var sect = AsQuery.SectionName(atomic);
                    if (sect == null || sect.IndexOf(sectionName!, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                if (!string.IsNullOrWhiteSpace(assemblyMark))
                {
                    var assMark = AsQuery.Safe(() => atomic.GetMainPartPositionNumber(), null);
                    if (!string.Equals(assMark, assemblyMark, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (!string.IsNullOrWhiteSpace(singlePartMark))
                {
                    var spMark = AsQuery.Safe(() => atomic.GetSinglePartPositionNumber(), null);
                    if (!string.Equals(spMark, singlePartMark, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                results.Add(AsQuery.Describe(atomic));
            }

            return CommandResult.Ok(new
            {
                elements = results,
                count = results.Count,
                filters_applied = filtersApplied
            });
        }

        public static CommandResult GetJointsCatalog(CommandContext ctx)
        {
            var joints = new List<object>
            {
                new
                {
                    joint_type = "BasePlate",
                    rule_name = "AstorJoints.BasePlate",
                    description = "Column base plate with anchor bolts, stiffeners, and grout bed.",
                    primary_roles = new[] { "Column" },
                    secondary_roles = Array.Empty<string>()
                },
                new
                {
                    joint_type = "ClipAngle",
                    rule_name = "AstorJoints.ClipAngle",
                    description = "Beam to column web/flange or beam to beam clip angle connection.",
                    primary_roles = new[] { "Column", "Beam" },
                    secondary_roles = new[] { "Beam" }
                },
                new
                {
                    joint_type = "EndPlate",
                    rule_name = "AstorJoints.EndPlate",
                    description = "Bolted end plate connection between beam and column or beam splice.",
                    primary_roles = new[] { "Column", "Beam" },
                    secondary_roles = new[] { "Beam" }
                },
                new
                {
                    joint_type = "ApexHaunch",
                    rule_name = "AstorJoints.ApexHaunch",
                    description = "Gable roof ridge apex connection with haunch reinforcement.",
                    primary_roles = new[] { "Rafter" },
                    secondary_roles = new[] { "Rafter" }
                }
            };

            return CommandResult.Ok(new
            {
                joints,
                total_count = joints.Count
            });
        }

        public static CommandResult ValidateSection(CommandContext ctx)
        {
            var sectionName = ctx.GetString("section_name");
            if (string.IsNullOrWhiteSpace(sectionName))
            {
                return CommandResult.Fail(
                    "MISSING_PARAMETER", "Parameter 'section_name' is required.", 400,
                    "Provide a section name to validate, e.g. 'HEB300', 'IPE240'.");
            }

            bool isValid = false;
            string message;

            try
            {
                var profName = ProfilesManager.GetProfTypeAsDefault(sectionName!);
                isValid = profName != null && !string.IsNullOrWhiteSpace(profName.Name);
                message = isValid
                    ? $"Section '{sectionName}' exists in AstorProfiles catalogue."
                    : $"Section '{sectionName}' was not found in AstorProfiles database.";
            }
            catch (System.Exception ex)
            {
                isValid = false;
                message = $"Validation check failed: {ex.Message}";
            }

            return CommandResult.Ok(new
            {
                section_name = sectionName,
                is_valid = isValid,
                message
            });
        }
    }
}
