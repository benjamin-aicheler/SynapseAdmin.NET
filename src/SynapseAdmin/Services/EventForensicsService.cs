using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Services;

public class EventForensicsService(
    IMatrixSessionService sessionService,
    ILogger<EventForensicsService> logger,
    IStringLocalizer<SharedResources> L) : IEventForensicsService
{
    private IMatrixGateway? Gateway => sessionService.Gateway;

    public async Task<OperationResult<EventForensicsViewModel>> FetchEventAsync(string eventId, CancellationToken cancellationToken = default)
    {
        if (Gateway == null)
        {
            return OperationResult<EventForensicsViewModel>.Failure(L["NotAuthenticated"]);
        }

        if (!Gateway.SupportsAdminApi)
        {
            return OperationResult<EventForensicsViewModel>.Failure(L["EventForensicsApiRequired"]);
        }

        if (string.IsNullOrWhiteSpace(eventId))
        {
            return OperationResult<EventForensicsViewModel>.Failure(L["EventIdRequired"]);
        }

        var trimmedEventId = eventId.Trim();

        try
        {
            var response = await Gateway.FetchEventAsync(trimmedEventId, cancellationToken);
            if (response?.Event == null)
            {
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

            var formattedJson = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });

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
        }
        catch (OperationCanceledException)
        {
            return OperationResult<EventForensicsViewModel>.Cancelled();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("Event {EventId} not found on server.", trimmedEventId.SanitizeForLogging());
            return OperationResult<EventForensicsViewModel>.Failure(L["EventNotFound"], Severity.Warning);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching event {EventId}", trimmedEventId.SanitizeForLogging());
            return OperationResult<EventForensicsViewModel>.Failure(L["ErrorFetchingEvent"]);
        }
    }
}
