using System.Text.Json;
using System.Text.Json.Nodes;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Extensions.Mapping;

/// <summary>Maps the events of the Event Context API to rows (#588). A field of an unexpected JSON kind maps to <c>null</c>.</summary>
public static class EventContextMappingExtensions
{
    /// <summary>
    /// The context oldest first on both sides: the API returns <c>events_before</c> newest first, so the mapper lists
    /// them in reverse.
    /// </summary>
    public static EventContextViewModel ToContextViewModel(this SynapseAdminEventContextResponse context)
    {
        return new EventContextViewModel
        {
            Before = context.EventsBefore.AsEnumerable().Reverse().ToContextItemViewModels(),
            Event = context.Event?.ToContextItemViewModel(),
            After = context.EventsAfter.ToContextItemViewModels()
        };
    }

    public static EventContextItemViewModel ToContextItemViewModel(this JsonObject evt)
    {
        return new EventContextItemViewModel
        {
            EventId = StringOf(evt["event_id"]) ?? string.Empty,
            Type = StringOf(evt["type"]),
            Sender = StringOf(evt["sender"]),
            StateKey = StringOf(evt["state_key"]),
            OriginServerTs = LongOf(evt["origin_server_ts"]),
            Body = evt["content"] is JsonObject content ? StringOf(content["body"]) : null,
            IsRedacted = evt["unsigned"] is JsonObject unsigned && unsigned["redacted_because"] != null
        };
    }

    public static List<EventContextItemViewModel> ToContextItemViewModels(this IEnumerable<JsonObject> events)
    {
        return events.Select(ToContextItemViewModel).ToList();
    }

    private static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    private static long? LongOf(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<long>(out var number) ? number : null;
}
