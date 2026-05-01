using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages
{
    public partial class RoomDetails
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

        protected override async Task OnParametersSetAsync()
        {
            await LoadRoomDetails();
            await LoadMessages();
        }

        private async Task LoadRoomDetails()
        {
            var result = await RoomService.GetRoomDetailsAsync(RoomId);
            if (result.Success)
            {
                room = result.Data;
            }
            else
            {
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task<TableData<RoomMediaItemViewModel>> LoadLocalMedia(TableState state, CancellationToken token)
        {
            if (room?.Media?.Local == null) return new TableData<RoomMediaItemViewModel> { TotalItems = 0, Items = [] };

            var uris = room.Media.Local.Select(m => m.MediaId).Skip(state.Page * state.PageSize).Take(state.PageSize).ToList();
            if (uris.Count == 0) return new TableData<RoomMediaItemViewModel> { TotalItems = room.Media.Local.Count, Items = [] };

            var result = await RoomService.GetMediaMetadataBatchAsync(uris);
            if (result.Success)
            {
                return new TableData<RoomMediaItemViewModel> { TotalItems = room.Media.Local.Count, Items = result.Data };
            }
            else
            {
                Snackbar.Add(result.Message, result.Severity);
                return new TableData<RoomMediaItemViewModel> { TotalItems = room.Media.Local.Count, Items = [] };
            }
        }

        private async Task<TableData<RoomMediaItemViewModel>> LoadRemoteMedia(TableState state, CancellationToken token)
        {
            if (room?.Media?.Remote == null) return new TableData<RoomMediaItemViewModel> { TotalItems = 0, Items = [] };

            var uris = room.Media.Remote.Select(m => m.MediaId).Skip(state.Page * state.PageSize).Take(state.PageSize).ToList();
            if (uris.Count == 0) return new TableData<RoomMediaItemViewModel> { TotalItems = room.Media.Remote.Count, Items = [] };

            var result = await RoomService.GetMediaMetadataBatchAsync(uris);
            if (result.Success)
            {
                return new TableData<RoomMediaItemViewModel> { TotalItems = room.Media.Remote.Count, Items = result.Data };
            }
            else
            {
                Snackbar.Add(result.Message, result.Severity);
                return new TableData<RoomMediaItemViewModel> { TotalItems = room.Media.Remote.Count, Items = [] };
            }
        }

        private async Task LoadMessages(string? from = null)
        {
            loadingMessages = true;
            var result = await RoomService.GetRoomMessagesAsync(RoomId, from: from, limit: 50, dir: "b");
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
            else if (!result.Success)
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
                var result = await RoomService.DeleteRoomAsync(RoomId, block: false, purge: true);
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
                var result = await RoomService.DeleteRoomAsync(RoomId, block: true, purge: true);
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
                var result = await RoomService.QuarantineMediaAsync(RoomId);
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task BlockRoom()
        {
            var result = await RoomService.BlockRoomAsync(RoomId, true);
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
                var result = await MediaService.QuarantineMediaAsync(mxc);
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
                var result = await MediaService.UnquarantineMediaAsync(mxc);
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
                var result = await MediaService.DeleteMediaAsync(mxc);
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
    }
}
