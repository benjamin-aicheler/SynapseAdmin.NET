using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Components.Pages;

public partial class BackgroundUpdates : IDisposable
{
    [Inject]
    public IMatrixSessionService MatrixSession { get; set; } = null!;

    [Inject]
    public IBackgroundUpdatesService BackgroundUpdatesService { get; set; } = null!;

    [Inject]
    public ISnackbar Snackbar { get; set; } = null!;

    [Inject]
    public IDialogService DialogService { get; set; } = null!;

    private SynapseAdminBackgroundUpdatesStatusResponse? statusResponse;
    private bool isLoading = true;
    private bool isToggling = false;
    private bool isTriggering = false;

    private readonly CancellationTokenSource _cts = new();

    protected override async Task OnInitializedAsync()
    {
        if (MatrixSession.Gateway is { SupportsAdminApi: true })
        {
            await LoadStatus();
        }
    }

    private async Task LoadStatus()
    {
        isLoading = true;
        var result = await BackgroundUpdatesService.GetStatusAsync(_cts.Token);
        if (result.Success)
        {
            statusResponse = result.Data;
        }
        else if (result.Severity != Severity.Normal)
        {
            Snackbar.Add(result.Message, result.Severity);
        }
        isLoading = false;
    }

    private async Task ToggleEnabled()
    {
        if (statusResponse == null || isToggling) return;

        bool nextState = !statusResponse.Enabled;
        string confirmTitle = nextState ? L["ResumeUpdatesConfirmation"] : L["PauseUpdatesConfirmation"];
        string confirmMessage = nextState ? L["ResumeUpdatesMessage"] : L["PauseUpdatesWarning"];
        string yesBtn = nextState ? L["ResumeBackgroundUpdates"] : L["PauseBackgroundUpdates"];

        bool? confirmed = await DialogService.ShowMessageBoxAsync(
            confirmTitle,
            confirmMessage,
            yesText: yesBtn,
            cancelText: L["Cancel"]
        );

        if (confirmed == true)
        {
            isToggling = true;
            var result = await BackgroundUpdatesService.SetEnabledAsync(nextState, _cts.Token);
            if (result.Success && result.Data != null)
            {
                if (statusResponse != null)
                {
                    statusResponse.Enabled = result.Data.Enabled;
                }
                Snackbar.Add(L["BackgroundUpdatesStatus"] + ": " + (result.Data.Enabled ? L["Active"] : L["Paused"]), Severity.Success);
                await LoadStatus();
            }
            else if (result.Severity != Severity.Normal)
            {
                Snackbar.Add(result.Message, result.Severity);
            }
            isToggling = false;
        }
    }

    private async Task TriggerJob(string jobName, string displayName)
    {
        if (isTriggering) return;

        bool? confirmed = await DialogService.ShowMessageBoxAsync(
            L["TriggerJobConfirmation"],
            string.Format(L["TriggerJobMessage"], displayName),
            yesText: L["Trigger"],
            cancelText: L["Cancel"]
        );

        if (confirmed == true)
        {
            isTriggering = true;
            var result = await BackgroundUpdatesService.StartJobAsync(jobName, _cts.Token);
            if (result.Success)
            {
                Snackbar.Add(result.Message, Severity.Success);
                await LoadStatus();
            }
            else if (result.Severity != Severity.Normal)
            {
                Snackbar.Add(result.Message, result.Severity);
            }
            isTriggering = false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
