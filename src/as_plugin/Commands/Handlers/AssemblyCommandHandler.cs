using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Modelling;

// Advance Steel declares these enums nested inside the class they belong to.
using eAssemblyLocation = Autodesk.AdvanceSteel.ConstructionTypes.AtomicElement.eAssemblyLocation;
using eObjectType = Autodesk.AdvanceSteel.CADAccess.FilerObject.eObjectType;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// GET  /api/v1/assembly/main-part      — SPEC-002 §3 "Main Part Inspection Response".
    /// POST /api/v1/assembly/set-main-part  — SPEC-004 §2 <c>set_main_part</c>.
    ///
    /// rules/advance-steel-modeling.md §3.2: every shop assembly has exactly one Main Part, and it
    /// must be the primary profile. A stiffener, clip angle or end plate carrying the Main Part
    /// flag corrupts the assembly prefix, the fabrication drawing orientation and the DSTV origin,
    /// so that case is reported as invalid rather than silently accepted.
    /// </summary>
    public static class AssemblyCommandHandler
    {
        // ------------------------------------------------------------------
        // GET assembly/main-part
        // ------------------------------------------------------------------

        public static CommandResult InspectMainPart(CommandContext ctx)
        {
            var requested = ctx.GetString("assembly_or_element_handle")
                            ?? ctx.GetString("handle")
                            ?? ctx.GetString("assembly_mark");

            if (string.IsNullOrWhiteSpace(requested))
            {
                return CommandResult.Fail(
                    "MISSING_PARAMETER", "Parameter 'assembly_or_element_handle' is required.", 400,
                    "Send an element handle, or an assembly mark such as \"C1\", "
                    + "either as ?assembly_or_element_handle=... or in the JSON body.");
            }

            var members = ResolveAssemblyMembers(requested!, out var resolutionError);
            if (members.Count == 0)
            {
                return resolutionError ?? CommandResult.Fail(
                    "HANDLE_NOT_FOUND", $"No assembly found for '{requested}'.", 404,
                    "Call get_selected_elements to obtain a valid handle.");
            }

            var mainParts = new List<AtomicElement>();
            foreach (var member in members)
            {
                if (AsQuery.Safe(() => member.IsMainPart, false)) mainParts.Add(member);
            }

            var mainPart = mainParts.Count > 0 ? mainParts[0] : null;
            var assemblyMark = mainPart != null
                ? AsQuery.Safe(() => mainPart.GetMainPartPositionNumber(), null)
                : AsQuery.Safe(() => members[0].GetMainPartPositionNumber(), null);

            var attached = new List<object>();
            foreach (var member in members)
            {
                if (mainPart != null && ReferenceEquals(member, mainPart)) continue;

                attached.Add(new
                {
                    handle = AsQuery.Safe(() => member.Handle, null),
                    role = AsQuery.Safe(() => member.Role, null),
                    section_name = AsQuery.SectionName(member),
                    weld_handle = FirstWorkshopWeldHandle(member),
                    weight_kg = Math.Round(AsQuery.WeightKg(member), 3)
                });
            }

            var validation = Validate(mainPart, mainParts.Count);

            return CommandResult.Ok(new
            {
                assembly_mark = assemblyMark,
                main_part_handle = mainPart == null ? null : AsQuery.Safe(() => mainPart.Handle, null),
                main_part_role = mainPart == null ? null : AsQuery.Safe(() => mainPart.Role, null),
                main_part_section = mainPart == null ? null : AsQuery.SectionName(mainPart),
                main_part_type = mainPart == null ? null : AsQuery.TypeName(mainPart),
                main_part_count = mainParts.Count,
                attached_parts = attached,
                member_count = members.Count,
                is_valid_main_part = validation.IsValid,
                warning = validation.Warning,
                suggestion = validation.Suggestion
            });
        }

        private readonly struct Validation
        {
            public Validation(bool isValid, string? warning, string? suggestion)
            {
                IsValid = isValid;
                Warning = warning;
                Suggestion = suggestion;
            }

            public bool IsValid { get; }
            public string? Warning { get; }
            public string? Suggestion { get; }
        }

        private static Validation Validate(AtomicElement? mainPart, int mainPartCount)
        {
            if (mainPart == null)
            {
                return new Validation(
                    false,
                    "The assembly has no Main Part. Its numbering prefix, drawing orientation and DSTV origin are undefined.",
                    "Assign the primary profile as Main Part with set_main_part.");
            }

            if (mainPartCount > 1)
            {
                return new Validation(
                    false,
                    $"The assembly declares {mainPartCount} Main Parts; exactly one is allowed.",
                    "Clear the Main Part flag on every part except the primary profile.");
            }

            var role = AsQuery.Safe(() => mainPart.Role, null);

            if (!AsQuery.IsPrimaryProfile(mainPart))
            {
                var typeName = AsQuery.TypeName(mainPart);
                return new Validation(
                    false,
                    $"The Main Part is a {typeName} (role '{role ?? "unset"}'), not a primary profile. "
                    + "Plates, stiffeners and clip angles must never be the Main Part.",
                    "Reassign the Main Part to the column shaft, rafter or girder of this assembly.");
            }

            if (AsQuery.IsSecondaryRole(role))
            {
                return new Validation(
                    false,
                    $"The Main Part carries the secondary model role '{role}'. "
                    + "Secondary roles drive the wrong numbering prefix and drawing style.",
                    "Set the model role of the Main Part to a primary role such as Column, Beam or Rafter.");
            }

            return new Validation(true, null, null);
        }

        // ------------------------------------------------------------------
        // POST assembly/set-main-part
        // ------------------------------------------------------------------

        public static CommandResult SetMainPart(CommandContext ctx)
        {
            var assemblyHandle = ctx.GetString("assembly_handle");
            var newMainPartHandle = ctx.GetString("new_main_part_handle");

            if (string.IsNullOrWhiteSpace(newMainPartHandle))
            {
                return CommandResult.Fail(
                    "MISSING_PARAMETER", "Parameter 'new_main_part_handle' is required.", 400,
                    "Send the handle of the profile that should become the Main Part.");
            }

            if (AsQuery.OpenByHandle(newMainPartHandle!) is not AtomicElement newMainPart)
            {
                return CommandResult.Fail(
                    "HANDLE_NOT_FOUND",
                    $"Handle '{newMainPartHandle}' does not resolve to an Advance Steel part.", 404,
                    "Call get_selected_elements to obtain a valid handle.");
            }

            if (!AsQuery.IsPrimaryProfile(newMainPart))
            {
                return CommandResult.Fail(
                    "INVALID_MAIN_PART",
                    $"Handle '{newMainPartHandle}' is a {AsQuery.TypeName(newMainPart)}, not a primary profile.",
                    422,
                    "rules/advance-steel-modeling.md §3.2 forbids plates, stiffeners and clip angles as Main Part. "
                    + "Pick the column shaft, rafter or girder instead.");
            }

            var role = AsQuery.Safe(() => newMainPart.Role, null);
            if (AsQuery.IsSecondaryRole(role))
            {
                return CommandResult.Fail(
                    "INVALID_MAIN_PART",
                    $"Handle '{newMainPartHandle}' carries the secondary model role '{role}'.",
                    422,
                    "Change the model role to a primary role (Column, Beam, Rafter) before making it the Main Part.");
            }

            // Scope: members of the target assembly, so the flag is cleared where it must be.
            var scopeHandle = string.IsNullOrWhiteSpace(assemblyHandle) ? newMainPartHandle! : assemblyHandle!;
            var members = ResolveAssemblyMembers(scopeHandle, out _);

            var demoted = new List<string?>();
            foreach (var member in members)
            {
                if (ReferenceEquals(member, newMainPart)) continue;
                if (!AsQuery.Safe(() => member.IsMainPart, false)) continue;

                member.IsMainPart = false;
                demoted.Add(AsQuery.Safe(() => member.Handle, null));
            }

            newMainPart.IsMainPart = true;

            return CommandResult.Ok(new
            {
                assembly_mark = AsQuery.Safe(() => newMainPart.GetMainPartPositionNumber(), null),
                main_part_handle = AsQuery.Safe(() => newMainPart.Handle, newMainPartHandle),
                main_part_role = role,
                demoted_handles = demoted,
                success = true
            });
        }

        // ------------------------------------------------------------------
        // Assembly resolution
        // ------------------------------------------------------------------

        /// <summary>
        /// Resolves an element handle or an assembly mark into the parts of one shop assembly.
        /// Assembly membership follows workshop connections only, per
        /// rules/advance-steel-modeling.md §3.1.
        /// </summary>
        private static List<AtomicElement> ResolveAssemblyMembers(string identifier, out CommandResult? error)
        {
            error = null;

            var seed = AsQuery.OpenByHandle(identifier);
            if (seed is AtomicElement atomic)
            {
                return WorkshopCluster(atomic);
            }

            if (seed != null)
            {
                error = CommandResult.Fail(
                    "INVALID_HANDLE",
                    $"Handle '{identifier}' resolves to a {AsQuery.TypeName(seed)}, which is not an assembly member.",
                    422,
                    "Send the handle of a beam or plate, or an assembly mark such as \"C1\".");
                return new List<AtomicElement>();
            }

            // Not a handle: treat it as an assembly mark and scan the model for members.
            var byMark = MembersByAssemblyMark(identifier);
            if (byMark.Count == 0)
            {
                error = CommandResult.Fail(
                    "ASSEMBLY_NOT_FOUND",
                    $"'{identifier}' matches neither an element handle nor an assembly mark in this model.",
                    404,
                    "Run the Advance Steel numbering command first, or pass an element handle instead.");
            }

            return byMark;
        }

        private static List<AtomicElement> MembersByAssemblyMark(string mark)
        {
            var members = new List<AtomicElement>();

            foreach (var id in AsQuery.ModelObjectIds(eObjectType.kAtomicElem))
            {
                if (AsQuery.Open(id) is not AtomicElement element) continue;
                if (element is WeldPattern || element is BoltPattern || element is Connector) continue;

                var elementMark = AsQuery.Safe(() => element.GetMainPartPositionNumber(), null);
                if (string.Equals(elementMark, mark, StringComparison.OrdinalIgnoreCase)) members.Add(element);
            }

            return members;
        }

        /// <summary>
        /// Breadth-first walk over workshop connections starting at <paramref name="seed"/>.
        /// Site-welded neighbours are deliberately not followed: they belong to another assembly.
        /// </summary>
        private static List<AtomicElement> WorkshopCluster(AtomicElement seed)
        {
            var members = new List<AtomicElement>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<AtomicElement>();

            queue.Enqueue(seed);
            var seedHandle = AsQuery.Safe(() => seed.Handle, null);
            if (seedHandle != null) visited.Add(seedHandle);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                members.Add(current);

                foreach (var neighbour in WorkshopNeighbours(current))
                {
                    var handle = AsQuery.Safe(() => neighbour.Handle, null);
                    if (handle == null || !visited.Add(handle)) continue;
                    queue.Enqueue(neighbour);
                }
            }

            return members;
        }

        private static IEnumerable<AtomicElement> WorkshopNeighbours(AtomicElement element)
        {
            IEnumerable<Autodesk.AdvanceSteel.CADLink.Database.ObjectId>? ids;

            try
            {
                element.GetConnectedObjects(out ids, eAssemblyLocation.kInShop);
            }
            catch (System.Exception)
            {
                yield break;
            }

            foreach (var id in ids ?? Enumerable.Empty<Autodesk.AdvanceSteel.CADLink.Database.ObjectId>())
            {
                var resolved = AsQuery.Open(id);

                // Connection means (welds, bolts, shear studs) are the edges of the graph, not nodes.
                if (resolved is WeldPattern || resolved is BoltPattern || resolved is Connector) continue;
                if (resolved is AtomicElement neighbour) yield return neighbour;
            }
        }

        private static string? FirstWorkshopWeldHandle(AtomicElement element)
        {
            IEnumerable<Autodesk.AdvanceSteel.CADLink.Database.ObjectId>? ids;

            try
            {
                element.GetConnectedObjects(out ids, eAssemblyLocation.kInShop);
            }
            catch (System.Exception)
            {
                return null;
            }

            foreach (var id in ids ?? Enumerable.Empty<Autodesk.AdvanceSteel.CADLink.Database.ObjectId>())
            {
                if (AsQuery.Open(id) is not WeldPattern weld) continue;
                if (AsQuery.Safe(() => weld.AssemblyLocation, eAssemblyLocation.kUnknown) != eAssemblyLocation.kInShop)
                {
                    continue;
                }

                return AsQuery.Safe(() => weld.Handle, null);
            }

            return null;
        }
    }
}
