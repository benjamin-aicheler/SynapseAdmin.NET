using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Extensions;
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
}
