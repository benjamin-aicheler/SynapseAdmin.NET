using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
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

    [SupplyParameterFromQuery(Name = "eventId")]
    public string? EventIdParam { get; set; }

    private string eventId = string.Empty;
    private bool isLoading;
    private EventForensicsViewModel? eventData;
    private readonly CancellationTokenSource _cts = new();

    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrWhiteSpace(EventIdParam))
        {
            eventId = EventIdParam.Trim();
            if (MatrixSession.Gateway is { SupportsAdminApi: true })
            {
                await FetchEvent();
            }
        }
    }

    private async Task FetchEvent()
    {
        if (string.IsNullOrWhiteSpace(eventId)) return;

        isLoading = true;
        eventData = null;

        var result = await EventForensicsService.FetchEventAsync(eventId.Trim(), _cts.Token);

        if (result.Success && result.Data != null)
        {
            eventData = result.Data;
        }
        else if (result.Severity != Severity.Normal)
        {
            Snackbar.Add(result.Message, result.Severity);
        }

        isLoading = false;
    }

    private async Task CopyRawJsonToClipboard()
    {
        if (eventData == null || string.IsNullOrEmpty(eventData.RawJson)) return;

        try
        {
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", eventData.RawJson);
            Snackbar.Add(L["JsonCopiedToClipboard"], Severity.Success);
        }
        catch
        {
            Snackbar.Add(L["ErrorCopyingToClipboard"], Severity.Error);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
