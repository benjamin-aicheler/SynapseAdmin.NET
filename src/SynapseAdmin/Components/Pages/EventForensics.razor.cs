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

    protected override Task OnInitializedAsync() => TimeZoneService.InitializeAsync();

    protected override async Task OnParametersSetAsync()
    {
        if (!string.IsNullOrWhiteSpace(EventIdParam) && EventIdParam != _lastEventIdParam)
        {
            _lastEventIdParam = EventIdParam;
            eventId = EventIdParam.Trim();
            if (MatrixSession.Gateway is { SupportsAdminApi: true })
            {
                await FetchEvent();
            }
        }
    }

    private async Task FetchEvent()
    {
        if (isLoading || string.IsNullOrWhiteSpace(eventId)) return;

        isLoading = true;
        eventData = null;

        var result = await EventForensicsService.FetchEventAsync(eventId.Trim(), _cts.Token);

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
        _cts.Cancel();
        _cts.Dispose();
    }
}
