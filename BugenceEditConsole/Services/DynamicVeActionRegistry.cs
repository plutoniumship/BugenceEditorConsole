namespace BugenceEditConsole.Services;

public sealed record DynamicVeActionDefinition(
    string Key,
    IReadOnlyList<string> RequiredFields,
    IReadOnlyList<string> OptionalFields,
    IReadOnlyList<string> AllowedTriggerEvents,
    string RuntimeHandlerKey,
    bool SupportsSimulation);

public static class DynamicVeActionRegistry
{
    private static readonly IReadOnlyDictionary<string, DynamicVeActionDefinition> Definitions =
        new Dictionary<string, DynamicVeActionDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["navigate"] = new(
                Key: "navigate",
                RequiredFields: new[] { "navigateUrl" },
                OptionalFields: new[] { "triggerEvent", "behavior", "validation" },
                AllowedTriggerEvents: new[] { "auto", "click", "submit" },
                RuntimeHandlerKey: "navigate",
                SupportsSimulation: true),
            ["workflow"] = new(
                Key: "workflow",
                RequiredFields: new[] { "workflowIdOrDguid" },
                OptionalFields: new[] { "triggerEvent", "behavior", "validation" },
                AllowedTriggerEvents: new[] { "auto", "click", "submit" },
                RuntimeHandlerKey: "workflow",
                SupportsSimulation: true),
            ["hybrid"] = new(
                Key: "hybrid",
                RequiredFields: new[] { "workflowIdOrDguid", "navigateUrl" },
                OptionalFields: new[] { "triggerEvent", "behavior", "validation" },
                AllowedTriggerEvents: new[] { "auto", "click", "submit" },
                RuntimeHandlerKey: "hybrid",
                SupportsSimulation: true)
        };

    public static DynamicVeActionDefinition? Get(string actionType)
    {
        if (string.IsNullOrWhiteSpace(actionType))
        {
            return null;
        }

        return Definitions.TryGetValue(actionType.Trim(), out var definition)
            ? definition
            : null;
    }

    public static IReadOnlyCollection<DynamicVeActionDefinition> All()
        => Definitions.Values.ToList().AsReadOnly();
}

