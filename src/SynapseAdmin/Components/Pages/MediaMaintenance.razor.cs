using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Extensions;
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
    private bool confirmUnboundedSize;
    private bool isWorking;

    private readonly CancellationTokenSource _cts = new();

    private void OnDeleteLocalSizeMbChanged(int value)
    {
        deleteLocalSizeMb = value;
        confirmUnboundedSize = false;
    }

    private async Task ConfirmPurgeRemoteCache()
    {
        if (purgeCacheDays < 1) return;

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
                    Snackbar.Add(result.Message, Severity.Success);
                }
                else
                {
                    Snackbar.ShowResult(result);
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
        if (deleteLocalDays < 1 || deleteLocalSizeMb < 0 || (deleteLocalSizeMb == 0 && !confirmUnboundedSize))
        {
            return;
        }

        bool? confirmed = await DialogService.ShowMessageBoxAsync(
            L["DeleteLocalMedia"],
            DeleteLocalMediaConfirmationMessage(),
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
                    Snackbar.Add(result.Message, Severity.Success);
                }
                else
                {
                    Snackbar.ShowResult(result);
                }
            }
            finally
            {
                isWorking = false;
                confirmUnboundedSize = false;
                StateHasChanged();
            }
        }
    }

    /// <summary>
    /// What the deletion will do, as Synapse runs it (#542): the criteria (no minimum for 0 MB), what happens to media
    /// used as a profile picture or room avatar, and that it can't be undone.
    /// </summary>
    private string DeleteLocalMediaConfirmationMessage()
    {
        var criteria = deleteLocalSizeMb == 0
            ? string.Format(L["DeleteLocalMediaConfirmationAnySize"], deleteLocalDays)
            : string.Format(L["DeleteLocalMediaConfirmation"], deleteLocalDays, deleteLocalSizeMb);
        var avatars = keepProfiles ? L["DeleteLocalMediaKeepsAvatars"] : L["DeleteLocalMediaDeletesAvatars"];
        return $"{criteria} {avatars} {L["DeleteLocalMediaIrreversible"]}";
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
