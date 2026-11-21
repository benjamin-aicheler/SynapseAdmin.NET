using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Infrastructure.Helpers;
using SynapseAdmin.Interfaces;
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

        var result = await EventForensicsService.FetchEventAsync(id, token);
        // Discard the answer for an event that is no longer selected.
        if (token.IsCancellationRequested) return;

        if (result.Success && result.Data != null)
        {
            eventData = result.Data;
        }
        else
        {
            Snackbar.ShowResult(result);
        }

        isLoading = false;
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
