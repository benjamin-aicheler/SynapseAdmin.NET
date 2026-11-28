using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Infrastructure.Helpers;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages;

public partial class EventForensics : IDisposable
{
    [Inject]
    public IMatrixSessionService MatrixSession { get; set; } = null!;

    [Inject]
    public IEventForensicsService EventForensicsService { get; set; } = null!;

    [Inject]
    public ISnackbar Snackbar { get; set; } = null!;

    [Inject]
    public IJSRuntime JSRuntime { get; set; } = null!;

    [Inject]
    public ITimeZoneService TimeZoneService { get; set; } = null!;

    [Inject]
    public ILogger<EventForensics> Logger { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "eventId")]
    public string? EventIdParam { get; set; }

    private string eventId = string.Empty;
    private string? _lastEventIdParam;
    private bool isLoading;
    private EventForensicsViewModel? eventData;
    // The events around the shown event (#588). They load with the event's token, so a superseded event's context is
    // discarded with it.
    private EventContextViewModel? contextData;
    private OperationResult? contextFailure;
    private bool isContextLoading;
    private readonly CancellationTokenSource _cts = new();
    // Blazor reuses this page when only the eventId query changes. `_fetchCts` is replaced on every request, so a
    // late answer for a previous event is discarded (#521).
    private CancellationTokenSource? _fetchCts;

    protected override Task OnInitializedAsync() => TimeZoneService.InitializeAsync();

    protected override async Task OnParametersSetAsync()
    {
        if (string.IsNullOrWhiteSpace(EventIdParam))
        {
            // The query was removed: clear what it selected. An event ID typed on /forensics stays.
            if (_lastEventIdParam != null)
            {
                _lastEventIdParam = null;
                CancelFetch();
                eventId = string.Empty;
                eventData = null;
                ClearContext();
                isLoading = false;
            }
            return;
        }

        if (EventIdParam == _lastEventIdParam) return;

        _lastEventIdParam = EventIdParam;
        eventId = EventIdParam.Trim();
        if (MatrixSession.Gateway is { SupportsAdminApi: true })
        {
            await LoadEventAsync(eventId);
        }
        else
        {
            CancelFetch();
            eventData = null;
            ClearContext();
            isLoading = false;
        }
    }

    private async Task FetchEvent()
    {
        if (isLoading || string.IsNullOrWhiteSpace(eventId)) return;

        await LoadEventAsync(eventId.Trim());
    }

    private async Task LoadEventAsync(string id)
    {
        var token = ResetFetchToken();
        isLoading = true;
        eventData = null;
        ClearContext();

        var result = await EventForensicsService.FetchEventAsync(id, token);
        // Discard the answer for an event that is no longer selected.
        if (token.IsCancellationRequested) return;

        isLoading = false;
        if (result.Success && result.Data != null)
        {
            eventData = result.Data;
            if (!string.IsNullOrEmpty(eventData.RoomId))
            {
                await LoadContextAsync(eventData.RoomId, eventData.EventId, token);
            }
        }
        else
        {
            Snackbar.ShowResult(result);
        }
    }

    /// <summary>
    /// Loads the events around the shown event (#588). A failure shows in the card only: the event itself loaded.
    /// </summary>
    private async Task LoadContextAsync(string roomId, string shownEventId, CancellationToken token)
    {
        ClearContext();
        isContextLoading = true;
        // The event and the progress bar show while the context loads.
        StateHasChanged();

        var result = await EventForensicsService.GetEventContextAsync(roomId, shownEventId, token);
        if (token.IsCancellationRequested) return;

        isContextLoading = false;
        if (result.Success && result.Data != null)
        {
            contextData = result.Data;
        }
        else if (!result.IsSilent)
        {
            contextFailure = result;
        }
    }

    private void ClearContext()
    {
        contextData = null;
        contextFailure = null;
        isContextLoading = false;
    }

    /// <summary>The context's rows, oldest first, with the shown event marked.</summary>
    private IEnumerable<(EventContextItemViewModel Item, bool IsShownEvent)> ContextRows()
    {
        if (contextData == null) yield break;
        foreach (var item in contextData.Before) yield return (item, false);
        if (contextData.Event != null) yield return (contextData.Event, true);
        foreach (var item in contextData.After) yield return (item, false);
    }

    private CancellationToken ResetFetchToken()
    {
        CancelFetch();
        _fetchCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        return _fetchCts.Token;
    }

    private void CancelFetch()
    {
        _fetchCts?.Cancel();
        _fetchCts?.Dispose();
        _fetchCts = null;
    }

    private async Task CopyRawJsonToClipboard()
    {
        if (eventData == null || string.IsNullOrEmpty(eventData.RawJson)) return;

        if (await JSRuntime.TryCopyToClipboardAsync(eventData.RawJson, Logger))
        {
            Snackbar.Add(L["JsonCopiedToClipboard"], Severity.Success);
        }
        else
        {
            Snackbar.Add(L["ErrorCopyingToClipboard"], Severity.Error);
        }
    }

    private bool IsLocalUser(string? userId) =>
        MatrixIdHelper.IsOnServer(userId, MatrixSession.Gateway?.ServerName);

    public void Dispose()
    {
        CancelFetch();
        _cts.Cancel();
        _cts.Dispose();
    }
}
