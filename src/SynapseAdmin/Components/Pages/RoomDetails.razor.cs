using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace SynapseAdmin.Components.Pages
{
    public partial class RoomDetails : IDisposable
    {
        [Inject]
        public IMatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public IRoomService RoomService { get; set; } = null!;
        [Inject]
        public IMediaService MediaService { get; set; } = null!;
        [Inject]
        public NavigationManager Navigation { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;
        [Inject]
        public IDialogService DialogService { get; set; } = null!;

        [Parameter]
        public string RoomId { get; set; } = string.Empty;

        private RoomDetailViewModel? room;
        private RoomMessagesViewModel? messages;
        private bool loadingMessages;
        private MudTable<RoomMediaItemViewModel>? localMediaTable;
        private MudTable<RoomMediaItemViewModel>? remoteMediaTable;
        private readonly CancellationTokenSource _cts = new();

        private string? activePurgeId;
        private string? activePurgeStatus;
        private CancellationTokenSource? _pollingCts;

        protected override async Task OnParametersSetAsync()
        {
            StopPolling();
            activePurgeId = RoomService.GetActivePurgeId(RoomId);
            activePurgeStatus = null;

            await LoadRoomDetails();
            await LoadMessages();

            if (!string.IsNullOrEmpty(activePurgeId))
            {
                StartPolling();
            }
        }

        private async Task LoadRoomDetails()
        {
            var result = await RoomService.GetRoomDetailsAsync(RoomId, _cts.Token);
            if (result.Success)
            {
                room = result.Data;
            }
            else if (result.Severity != Severity.Normal)
            {
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task<TableData<RoomMediaItemViewModel>> LoadLocalMedia(TableState state, CancellationToken token)
        {
            if (room?.Media?.Local == null) return new TableData<RoomMediaItemViewModel> { TotalItems = 0, Items = [] };

            var uris = room.Media.Local.Select(m => m.MediaId).Skip(state.Page * state.PageSize).Take(state.PageSize).ToList();
            if (uris.Count == 0) return new TableData<RoomMediaItemViewModel> { TotalItems = room.Media.Local.Count, Items = [] };

            var result = await RoomService.GetMediaMetadataBatchAsync(uris, token);
            if (result.Success)
            {
                return new TableData<RoomMediaItemViewModel> { TotalItems = room.Media.Local.Count, Items = result.Data };
            }
            else
            {
                if (result.Severity != Severity.Normal) Snackbar.Add(result.Message, result.Severity);
                return new TableData<RoomMediaItemViewModel> { TotalItems = room.Media.Local.Count, Items = [] };
            }
        }

        private async Task<TableData<RoomMediaItemViewModel>> LoadRemoteMedia(TableState state, CancellationToken token)
        {
            if (room?.Media?.Remote == null) return new TableData<RoomMediaItemViewModel> { TotalItems = 0, Items = [] };

            var uris = room.Media.Remote.Select(m => m.MediaId).Skip(state.Page * state.PageSize).Take(state.PageSize).ToList();
            if (uris.Count == 0) return new TableData<RoomMediaItemViewModel> { TotalItems = room.Media.Remote.Count, Items = [] };

            var result = await RoomService.GetMediaMetadataBatchAsync(uris, token);
            if (result.Success)
            {
                return new TableData<RoomMediaItemViewModel> { TotalItems = room.Media.Remote.Count, Items = result.Data };
            }
            else
            {
                if (result.Severity != Severity.Normal) Snackbar.Add(result.Message, result.Severity);
                return new TableData<RoomMediaItemViewModel> { TotalItems = room.Media.Remote.Count, Items = [] };
            }
        }

        private async Task LoadMessages(string? from = null)
        {
            loadingMessages = true;
            var result = await RoomService.GetRoomMessagesAsync(RoomId, from: from, limit: 50, dir: "b", token: _cts.Token);
            if (result.Success && result.Data != null)
            {
                if (from == null || messages == null)
                {
                    messages = result.Data;
                }
                else
                {
                    messages.Messages.AddRange(result.Data.Messages);
                    messages.EndToken = result.Data.EndToken;
                }
            }
            else if (!result.Success && result.Severity != Severity.Normal)
            {
                Snackbar.Add(result.Message, result.Severity);
            }
            loadingMessages = false;
        }

        private async Task DeleteRoom()
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["DeleteRoomTitle"], 
                L["DeleteRoomConfirmation"], 
                yesText: L["Delete"], cancelText: L["Cancel"]);
            
            if (confirmed == true)
            {
                var result = await RoomService.DeleteRoomAsync(RoomId, block: false, purge: true, token: _cts.Token);
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task DeleteAndBlockRoom()
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["DeleteAndBlockRoomTitle"], 
                L["DeleteAndBlockRoomConfirmation"], 
                yesText: L["DeleteAndBlock"], cancelText: L["Cancel"]);
            
            if (confirmed == true)
            {
                var result = await RoomService.DeleteRoomAsync(RoomId, block: true, purge: true, token: _cts.Token);
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task QuarantineMedia()
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["QuarantineMediaTitle"], 
                L["QuarantineRoomMediaConfirmation"], 
                yesText: L["Quarantine"], cancelText: L["Cancel"]);
            
            if (confirmed == true)
            {
                var result = await RoomService.QuarantineMediaAsync(RoomId, _cts.Token);
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task BlockRoom()
        {
            var result = await RoomService.BlockRoomAsync(RoomId, true, _cts.Token);
            Snackbar.Add(result.Message, result.Severity);
        }

        private async Task QuarantineSingleMedia(string mxc)
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["QuarantineMediaTitle"],
                L["QuarantineMediaConfirmation"],
                yesText: L["Quarantine"], cancelText: L["Cancel"]);

            if (confirmed == true)
            {
                var result = await MediaService.QuarantineMediaAsync(mxc, _cts.Token);
                Snackbar.Add(result.Message, result.Severity);
                if (result.Success)
                {
                    await (localMediaTable?.ReloadServerData() ?? Task.CompletedTask);
                    await (remoteMediaTable?.ReloadServerData() ?? Task.CompletedTask);
                }
            }
        }

        private async Task UnquarantineSingleMedia(string mxc)
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["UnquarantineMediaTitle"],
                L["UnquarantineMediaConfirmation"],
                yesText: L["Unquarantine"], cancelText: L["Cancel"]);

            if (confirmed == true)
            {
                var result = await MediaService.UnquarantineMediaAsync(mxc, _cts.Token);
                Snackbar.Add(result.Message, result.Severity);
                if (result.Success)
                {
                    await (localMediaTable?.ReloadServerData() ?? Task.CompletedTask);
                    await (remoteMediaTable?.ReloadServerData() ?? Task.CompletedTask);
                }
            }
        }

        private async Task DeleteSingleMedia(string mxc)
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["DeleteMediaTitle"],
                L["DeleteMediaConfirmation"],
                yesText: L["Delete"], cancelText: L["Cancel"]);

            if (confirmed == true)
            {
                var result = await MediaService.DeleteMediaAsync(mxc, _cts.Token);
                Snackbar.Add(result.Message, result.Severity);
                if (result.Success)
                {
                    await LoadRoomDetails();
                    await (localMediaTable?.ReloadServerData() ?? Task.CompletedTask);
                    await (remoteMediaTable?.ReloadServerData() ?? Task.CompletedTask);
                }
            }
        }

        private bool IsPreviewable(string? mimeType)
        {
            if (string.IsNullOrEmpty(mimeType))
            {
                // For room media we often don't have the mime type upfront. 
                // We'll allow previewing by default if it's missing, 
                // the preview dialog handles the error if it's not actually an image/video/audio.
                return true; 
            }
            return mimeType.StartsWith("image/") || mimeType.StartsWith("video/") || mimeType.StartsWith("audio/");
        }

        private async Task ShowPreview(RoomMediaItemViewModel media)
        {
            var mxc = media.MediaId;
            var previewUrl = $"/Media/Preview?mxc={Uri.EscapeDataString(mxc)}&mimeType={Uri.EscapeDataString(media.MediaType ?? "")}";
            
            var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
            var parameters = new DialogParameters
            {
                { "Title", media.UploadName ?? media.MediaId },
                { "PreviewUrl", previewUrl },
                { "MediaType", media.MediaType }
            };

            await DialogService.ShowAsync<MediaPreviewDialog>(media.UploadName ?? L["MediaPreview"], parameters, options);
        }

        private void StopPolling()
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;
        }

        private void StartPolling()
        {
            if (string.IsNullOrEmpty(activePurgeId)) return;
            
            _pollingCts = new CancellationTokenSource();
            var token = _pollingCts.Token;

            // Start polling as a background task
            Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        var result = await RoomService.GetPurgeHistoryStatusAsync(activePurgeId, token);
                        if (result.Success && result.Data != null)
                        {
                            activePurgeStatus = result.Data.Status;
                            await InvokeAsync(StateHasChanged);

                            if (activePurgeStatus == "complete")
                            {
                                RoomService.ClearActivePurgeId(RoomId);
                                activePurgeId = null;
                                activePurgeStatus = null;
                                Snackbar.Add(L["PurgeCompleted"], Severity.Success);
                                await InvokeAsync(StateHasChanged);
                                break;
                            }
                            else if (activePurgeStatus == "failed")
                            {
                                RoomService.ClearActivePurgeId(RoomId);
                                activePurgeId = null;
                                activePurgeStatus = null;
                                var errMsg = !string.IsNullOrEmpty(result.Data.Error) 
                                    ? string.Format(L["PurgeFailed"], result.Data.Error)
                                    : string.Format(L["PurgeFailed"], L["UnknownError"]);
                                Snackbar.Add(errMsg, Severity.Error);
                                await InvokeAsync(StateHasChanged);
                                break;
                            }
                        }
                        
                        await Task.Delay(5000, token);
                    }
                }
                catch (TaskCanceledException)
                {
                    // Ignore cancellation
                }
                catch (Exception)
                {
                    // Ignore to prevent circuit crash
                }
            }, token);
        }

        private async Task OpenPurgeHistoryDialog()
        {
            await OpenPurgeHistoryDialogInternal(null, null);
        }

        private async Task OpenPurgeHistoryDialogForMessage(string eventId)
        {
            await OpenPurgeHistoryDialogInternal(eventId, null);
        }

        private async Task OpenPurgeHistoryDialogInternal(string? preselectedEventId, DateTimeOffset? preselectedTimestamp)
        {
            var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
            var parameters = new DialogParameters
            {
                { "RoomId", RoomId },
                { "PreselectedEventId", preselectedEventId },
                { "PreselectedTimestamp", preselectedTimestamp }
            };

            var dialog = await DialogService.ShowAsync<PurgeHistoryDialog>(L["PurgeHistory"], parameters, options);
            var result = await dialog.Result;

            if (result != null && !result.Canceled && result.Data is string newPurgeId)
            {
                activePurgeId = newPurgeId;
                activePurgeStatus = "active";
                StartPolling();
                StateHasChanged();
            }
        }

        public void Dispose()
        {
            StopPolling();
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
