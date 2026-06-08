using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages;

public partial class MediaMaintenance : IDisposable
{
    [Inject]
    public IMatrixSessionService MatrixSession { get; set; } = null!;

    [Inject]
    public IMediaService MediaService { get; set; } = null!;

    [Inject]
    public ISnackbar Snackbar { get; set; } = null!;

    [Inject]
    public IDialogService DialogService { get; set; } = null!;

    private int purgeCacheDays = 30;
    private int deleteLocalDays = 90;
    private int deleteLocalSizeMb = 10;
    private bool keepProfiles = true;
    private bool isWorking = false;

    private readonly CancellationTokenSource _cts = new();

    private async Task ConfirmPurgeRemoteCache()
    {
        var message = string.Format(L["PurgeCacheConfirmation"], purgeCacheDays);
        bool? confirmed = await DialogService.ShowMessageBoxAsync(
            L["PurgeRemoteCache"],
            message,
            yesText: L["PurgeCache"], cancelText: L["Cancel"]);

        if (confirmed == true)
        {
            isWorking = true;
            StateHasChanged();
            try
            {
                var timeSpan = TimeSpan.FromDays(purgeCacheDays);
                var beforeTs = DateTimeOffset.UtcNow.Subtract(timeSpan).ToUnixTimeMilliseconds();

                var result = await MediaService.PurgeRemoteMediaCacheAsync(beforeTs, _cts.Token);
                if (result.Success && result.Data != null)
                {
                    Snackbar.Add(string.Format(L["PurgedCacheSuccess"], result.Data.Deleted), Severity.Success);
                }
                else
                {
                    Snackbar.Add(result.Message, result.Severity);
                }
            }
            finally
            {
                isWorking = false;
                StateHasChanged();
            }
        }
    }

    private async Task ConfirmDeleteLocalMedia()
    {
        var message = string.Format(L["DeleteLocalMediaConfirmation"], deleteLocalDays, deleteLocalSizeMb);
        bool? confirmed = await DialogService.ShowMessageBoxAsync(
            L["DeleteLocalMedia"],
            message,
            yesText: L["DeleteMedia"], cancelText: L["Cancel"]);

        if (confirmed == true)
        {
            isWorking = true;
            StateHasChanged();
            try
            {
                var timeSpan = TimeSpan.FromDays(deleteLocalDays);
                var beforeTs = DateTimeOffset.UtcNow.Subtract(timeSpan).ToUnixTimeMilliseconds();
                
                var sizeGtBytes = (long)deleteLocalSizeMb * 1024 * 1024;

                var result = await MediaService.DeleteLocalMediaAsync(beforeTs, sizeGtBytes, keepProfiles, _cts.Token);
                if (result.Success && result.Data != null)
                {
                    // For the success message, we can show total deleted media count, or use the direct returned message
                    Snackbar.Add(result.Message, Severity.Success);
                }
                else
                {
                    Snackbar.Add(result.Message, result.Severity);
                }
            }
            finally
            {
                isWorking = false;
                StateHasChanged();
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
