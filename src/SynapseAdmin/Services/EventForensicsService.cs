using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Services;

public class EventForensicsService(
    IMatrixSessionService sessionService,
    ILogger<EventForensicsService> logger,
    IStringLocalizer<SharedResources> L) : IEventForensicsService
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    /// <summary>Events around an event: Synapse returns <c>floor(limit / 2)</c> before it and the rest after (#588).</summary>
    internal const int ContextLimit = 20;

    public Task<OperationResult<EventForensicsViewModel>> FetchEventAsync(string eventId, CancellationToken cancellationToken = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingEvent",
            ex => logger.LogError(ex, "Error fetching event {EventId}", eventId.Trim().SanitizeForLogging()),
            async gateway =>
            {
                if (!gateway.SupportsAdminApi)
                {
                    return OperationResult<EventForensicsViewModel>.Failure(L["EventForensicsApiRequired"]);
                }

                if (string.IsNullOrWhiteSpace(eventId))
                {
                    return OperationResult<EventForensicsViewModel>.Failure(L["EventIdRequired"]);
                }

                var trimmedEventId = eventId.Trim();

                var response = await gateway.FetchEventAsync(trimmedEventId, cancellationToken);
                if (response?.Event == null)
                {
                    logger.LogWarning("Event {EventId} not found on server.", trimmedEventId.SanitizeForLogging());
                    return OperationResult<EventForensicsViewModel>.Failure(L["EventNotFound"], Severity.Warning);
                }

                var evt = response.Event;
                long? originServerTs = null;
                if (evt.TryGetPropertyValue("origin_server_ts", out var tsNode) && tsNode != null)
                {
                    if (tsNode.GetValueKind() == JsonValueKind.Number && tsNode.AsValue().TryGetValue<long>(out var val))
                    {
                        originServerTs = val;
                    }
                }

                var formattedJson = JsonSerializer.Serialize(response, IndentedJson);

                var vm = new EventForensicsViewModel
                {
                    EventId = evt["event_id"]?.ToString() ?? trimmedEventId,
                    RoomId = evt["room_id"]?.ToString(),
                    Sender = evt["sender"]?.ToString(),
                    Type = evt["type"]?.ToString(),
                    OriginServerTs = originServerTs,
                    Content = evt["content"] as JsonObject,
                    Unsigned = evt["unsigned"] as JsonObject,
                    RawJson = formattedJson
                };

                return OperationResult<EventForensicsViewModel>.Ok(vm, L["EventFetchedSuccessfully"]);
            });

    /// <summary>
    /// The events around <paramref name="eventId"/> in <paramref name="roomId"/> (#588), oldest first. The page loads
    /// this by itself after the event, so a refusal is logged at Debug. A context the homeserver can't find is a
    /// warning. The events are never logged.
    /// </summary>
    public Task<OperationResult<EventContextViewModel>> GetEventContextAsync(string roomId, string eventId, CancellationToken cancellationToken = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingEventContext",
            ex => logger.LogError(ex, "Error fetching the context of event {EventId} in room {RoomId}", eventId.SanitizeForLogging(), roomId.SanitizeForLogging()),
            async gateway =>
            {
                var context = await gateway.GetEventContextAsync(roomId, eventId, ContextLimit, cancellationToken);
                if (context == null)
                {
                    logger.LogDebug("Event {EventId} has no context in room {RoomId}", eventId.SanitizeForLogging(), roomId.SanitizeForLogging());
                    return OperationResult<EventContextViewModel>.Failure(L["EventContextNotFound"], Severity.Warning);
                }

                return OperationResult<EventContextViewModel>.Ok(context.ToContextViewModel());
            },
            logRefusal: _ => logger.LogDebug("The homeserver doesn't support the event context"));
}
