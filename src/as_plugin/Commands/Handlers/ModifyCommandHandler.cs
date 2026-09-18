using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.Modelling;

namespace EMV.AdvanceSteel.Plugin.Commands.Handlers
{
    /// <summary>
    /// POST /api/v1/elements/modify — SPEC-004 §2 <c>modify_element_properties</c>.
    ///
    /// Updates the attributes of existing elements addressed by handle. Every property is optional
    /// and only the ones present in the request are touched, but a property the element cannot
    /// carry (a rotation on a plate, a thickness on a beam) is an error rather than a silent skip:
    /// an agent that believes it rotated a member and did not would go on to detail against a model
    /// that does not exist. The dispatcher's transaction then rolls the whole batch back, which is
    /// what rules/transaction-safety.md §4 asks for.
    /// </summary>
    public static class ModifyCommandHandler
    {
        public static CommandResult Modify(CommandContext ctx)
        {
            var handles = CollectRequestedHandles(ctx);

            if (handles.Count == 0)
            {
                return CommandResult.Fail(
                    "MISSING_PARAMETER", "Parameter 'element_handle' (or 'element_handles') is required.", 400,
                    "Send {\"element_handle\": \"1B2C\", \"material\": \"S355J2\"}; call get_selected_elements "
                    + "to obtain handles from the current model.");
            }

            var requested = ReadRequestedProperties(ctx);

            if (requested.Count == 0)
            {
                return CommandResult.Fail(
                    "MISSING_PARAMETER", "No modifiable property was supplied.", 400,
                    $"Send at least one of: {string.Join(", ", SupportedProperties)}.");
            }

            var elements = new List<FilerObject>();
            foreach (var handle in handles)
            {
                if (!AsQuery.TryResolve<FilerObject>(handle, "element_handle", out var element, out var error))
                {
                    return error!;
                }

                elements.Add(element);
            }

            var results = new List<object>();
            var warnings = new List<string>();

            foreach (var element in elements)
            {
                var handle = AsQuery.Safe(() => element.Handle, null);
                var changes = new List<object>();

                foreach (var property in requested)
                {
                    if (!TryApply(element, property, out var change, out var applyError))
                    {
                        return applyError!;
                    }

                    if (change != null) changes.Add(change);
                }

                // WriteToDb() is what persists the attribute changes; without it the edits live only
                // in the opened copy and vanish when the transaction closes (contract §4.B).
                try
                {
                    element.WriteToDb();
                }
                catch (System.Exception ex)
                {
                    return CommandResult.Fail(
                        "MODIFY_FAILED",
                        $"Element '{handle}' rejected the update: {ex.Message}", 422,
                        "Verify that the values are valid for this element — a material or section name must "
                        + "exist in the active Advance Steel database. The batch was rolled back.",
                        ex.StackTrace);
                }

                results.Add(new
                {
                    handle,
                    type = AsQuery.TypeName(element),
                    changes,
                    element = AsQuery.Describe(element)
                });
            }

            // rules/advance-steel-modeling.md §2: an unknown role still applies, but it will not
            // trigger the numbering prefixes and drawing styles the detailer expects from it.
            if (requested.TryGetValue("model_role", out var role)
                && role.Text != null
                && !KnownModelRoles.Contains(role.Text))
            {
                warnings.Add(
                    $"Model role '{role.Text}' is not one of the roles in rules/advance-steel-modeling.md §2 "
                    + $"({string.Join(", ", KnownModelRoles)}). Numbering prefixes and drawing styles keyed on "
                    + "the role will not trigger for it.");
            }

            return CommandResult.Ok(new
            {
                elements = results,
                modified_count = results.Count,
                applied_properties = requested.Keys.ToArray(),
                warnings
            });
        }

        // ------------------------------------------------------------------
        // Request parsing
        // ------------------------------------------------------------------

        /// <summary>What an agent may change here. Deliberately excludes the Main Part flag:
        /// reassigning it has to go through <c>set_main_part</c>, which enforces
        /// rules/advance-steel-modeling.md §3.2.</summary>
        private static readonly string[] SupportedProperties =
        {
            "material", "coating", "model_role", "section_name", "rotation_deg", "thickness"
        };

        private static readonly HashSet<string> KnownModelRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "Column", "Beam", "Rafter", "Bracing", "Purlin",
            "BasePlate", "EndPlate", "Stiffener", "GussetPlate"
        };

        private readonly struct PropertyValue
        {
            public PropertyValue(string? text, double number)
            {
                Text = text;
                Number = number;
            }

            public string? Text { get; }
            public double Number { get; }
        }

        private static List<string> CollectRequestedHandles(CommandContext ctx)
        {
            var handles = ctx.GetStringList("element_handles");

            var single = ctx.GetString("element_handle") ?? ctx.GetString("handle");
            if (!string.IsNullOrWhiteSpace(single) && !handles.Contains(single!, StringComparer.OrdinalIgnoreCase))
            {
                handles.Insert(0, single!);
            }

            return handles;
        }

        private static Dictionary<string, PropertyValue> ReadRequestedProperties(CommandContext ctx)
        {
            var requested = new Dictionary<string, PropertyValue>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in SupportedProperties)
            {
                if (ctx.Body.ValueKind != JsonValueKind.Object
                    || !ctx.Body.TryGetProperty(name, out var raw)
                    || raw.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (raw.ValueKind == JsonValueKind.Number && raw.TryGetDouble(out var number))
                {
                    requested[name] = new PropertyValue(null, number);
                }
                else if (raw.ValueKind == JsonValueKind.String)
                {
                    var text = raw.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) requested[name] = new PropertyValue(text, double.NaN);
                }
            }

            return requested;
        }

        // ------------------------------------------------------------------
        // Property application
        // ------------------------------------------------------------------

        private static bool TryApply(
            FilerObject element,
            KeyValuePair<string, PropertyValue> property,
            out object? change,
            out CommandResult? error)
        {
            change = null;
            error = null;

            var name = property.Key;
            var value = property.Value;

            switch (name.ToLowerInvariant())
            {
                case "material":
                    return TrySetText(
                        element, name, value, out change, out error,
                        atomic => atomic.Material, (atomic, text) => atomic.Material = text);

                case "coating":
                    return TrySetText(
                        element, name, value, out change, out error,
                        atomic => atomic.Coating, (atomic, text) => atomic.Coating = text);

                case "model_role":
                {
                    if (element is not ConstructionElement construction)
                    {
                        error = NotApplicable(element, name, "a construction element");
                        return false;
                    }

                    if (value.Text == null)
                    {
                        error = ExpectedText(name);
                        return false;
                    }

                    var before = AsQuery.Safe(() => construction.Role, null);
                    construction.Role = value.Text;
                    change = Describe(name, before, value.Text);
                    return true;
                }

                case "section_name":
                {
                    if (element is not Beam beam)
                    {
                        error = NotApplicable(element, name, "a profile");
                        return false;
                    }

                    if (value.Text == null)
                    {
                        error = ExpectedText(name);
                        return false;
                    }

                    var before = AsQuery.Safe(() => beam.ProfName, null);
                    try
                    {
                        beam.ProfName = value.Text;
                    }
                    catch (System.Exception ex)
                    {
                        error = CommandResult.Fail(
                            "INVALID_PARAMETER", $"Section '{value.Text}' was rejected: {ex.Message}", 422,
                            "Use a section that exists in the active Advance Steel profile database, "
                            + "e.g. \"HEB300\", \"IPE240\".");
                        return false;
                    }

                    change = Describe(name, before, AsQuery.Safe(() => beam.ProfName, value.Text));
                    return true;
                }

                case "rotation_deg":
                {
                    if (element is not Beam beam)
                    {
                        error = NotApplicable(element, name, "a profile");
                        return false;
                    }

                    if (double.IsNaN(value.Number) || double.IsInfinity(value.Number))
                    {
                        error = CommandResult.Fail(
                            "INVALID_PARAMETER", $"Parameter '{name}' must be a finite number of degrees.", 400,
                            "Send e.g. {\"rotation_deg\": 90.0}.");
                        return false;
                    }

                    var before = Math.Round(AsQuery.Safe(() => beam.Angle, 0.0) * 180.0 / Math.PI, 3);
                    beam.Angle = value.Number * Math.PI / 180.0;
                    change = Describe(name, before, Math.Round(value.Number, 3));
                    return true;
                }

                case "thickness":
                {
                    if (element is not PlateBase plate)
                    {
                        error = NotApplicable(element, name, "a plate");
                        return false;
                    }

                    // rules/advance-steel-modeling.md §4: thinner than this is not a fabricable plate.
                    if (double.IsNaN(value.Number) || value.Number < 3.0)
                    {
                        error = CommandResult.Fail(
                            "INVALID_PARAMETER",
                            $"Thickness {value.Number} mm is below the 3.0 mm minimum plate thickness.", 400,
                            "Use a thickness of at least 3.0 mm.");
                        return false;
                    }

                    var before = Math.Round(AsQuery.Safe(() => plate.Thickness, 0.0), 3);
                    plate.Thickness = value.Number;
                    change = Describe(name, before, Math.Round(value.Number, 3));
                    return true;
                }

                default:
                    error = CommandResult.Fail(
                        "INVALID_PARAMETER", $"Property '{name}' cannot be modified.", 400,
                        $"Supported properties: {string.Join(", ", SupportedProperties)}.");
                    return false;
            }
        }

        private static bool TrySetText(
            FilerObject element,
            string name,
            PropertyValue value,
            out object? change,
            out CommandResult? error,
            Func<AtomicElement, string?> read,
            Action<AtomicElement, string> write)
        {
            change = null;
            error = null;

            if (element is not AtomicElement atomic)
            {
                error = NotApplicable(element, name, "a physical part");
                return false;
            }

            if (value.Text == null)
            {
                error = ExpectedText(name);
                return false;
            }

            var before = AsQuery.Safe(() => read(atomic), null);
            write(atomic, value.Text);
            change = Describe(name, before, value.Text);
            return true;
        }

        private static object Describe(string property, object? before, object? after) =>
            new { property, before, after };

        private static CommandResult NotApplicable(FilerObject element, string property, string requiredKind) =>
            CommandResult.Fail(
                "PROPERTY_NOT_APPLICABLE",
                $"'{property}' cannot be set on {AsQuery.TypeName(element)} "
                + $"'{AsQuery.Safe(() => element.Handle, null)}'; it only applies to {requiredKind}.",
                422,
                "Drop the property, or address an element that carries it. The batch was rolled back so no "
                + "element was modified.");

        private static CommandResult ExpectedText(string property) =>
            CommandResult.Fail(
                "INVALID_PARAMETER", $"Parameter '{property}' must be a non-empty string.", 400,
                $"Send e.g. {{\"{property}\": \"S355J2\"}}.");
    }
}
