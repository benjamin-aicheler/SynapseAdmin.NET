using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Extensions;
using SynapseAdmin.Infrastructure.Helpers;

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
        [Inject]
        public ITimeZoneService TimeZoneService { get; set; } = null!;
        [Inject]
        public ILogger<RoomDetails> Logger { get; set; } = null!;

        [Parameter]
        public string RoomId { get; set; } = string.Empty;

        private const string PurgeStatusActive = "active";
        private const string PurgeStatusComplete = "complete";
        private const string PurgeStatusFailed = "failed";
        private const int PurgeStatusPollIntervalMs = 5000;
        private const int MaxConsecutivePurgeStatusFailures = 5;
        private const int MessageHistoryPageSize = 50;
        private const string RoomListPath = "/rooms";

        private RoomDetailViewModel? room;
        private RoomMessagesViewModel? messages;
        private bool loadingMessages;
        private MudTable<RoomMediaItemViewModel>? localMediaTable;
        private MudTable<RoomMediaItemViewModel>? remoteMediaTable;
        private readonly CancellationTokenSource _cts = new();
        private string _searchTerm = string.Empty;
        private string _mediaViewMode = "table";

        private string? activePurgeId;
        private string? activePurgeStatus;
        private CancellationTokenSource? _pollingCts;

        // Per-selection state. `_loadCts` is replaced on every route change so that requests
        // for a previous room are cancelled and their late responses are discarded.
        // `_displayedRoomId` is the room the currently shown `room` model was loaded for;
        // destructive actions target this ID, never the (possibly newer) route parameter.
        private CancellationTokenSource? _loadCts;
        private string? _loadedRoomId;
        private string? _displayedRoomId;
        private bool isLoading = true;
        private string? errorMessage;

        protected override async Task OnParametersSetAsync()
        {
            if (_loadedRoomId == RoomId && _loadCts != null) return;

            // Reset synchronously, before the first await, so the previous room (and its
            // action buttons) is never rendered while the route already points at a new room.
            var roomId = RoomId;
            _loadedRoomId = roomId;
            StopPolling();
            var token = ResetLoadToken();
            room = null;
            _displayedRoomId = null;
            messages = null;
            loadingMessages = false;
            errorMessage = null;
            isLoading = true;
            activePurgeId = RoomService.GetActivePurgeId(roomId);
            activePurgeStatus = null;

            await TimeZoneService.InitializeAsync();
            if (token.IsCancellationRequested) return;

            await LoadRoomDetails(roomId, token);
            if (token.IsCancellationRequested) return;

            await LoadMessages(roomId, null, token);
            if (token.IsCancellationRequested) return;

            if (!string.IsNullOrEmpty(activePurgeId))
            {
                StartPolling(roomId);
            }
        }

        private CancellationToken ResetLoadToken()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            return _loadCts.Token;
        }

        private CancellationToken LoadToken => _loadCts?.Token ?? _cts.Token;

        private async Task LoadRoomDetails(string roomId, CancellationToken token)
        {
            var result = await RoomService.GetRoomDetailsAsync(roomId, token);
            // Discard responses for a selection that is no longer current.
            if (token.IsCancellationRequested || roomId != _loadedRoomId) return;

            if (result.Success && result.Data != null)
            {
                room = result.Data;
                _displayedRoomId = roomId;
                errorMessage = null;
            }
            else
            {
                room = null;
                _displayedRoomId = null;
                if (Snackbar.ShowResult(result))
                {
                    errorMessage = result.Message;
                }
            }
            isLoading = false;
        }

        private Task ReloadRoomDetails() =>
            _displayedRoomId is { } roomId ? LoadRoomDetails(roomId, LoadToken) : Task.CompletedTask;

        /// <summary>
        /// Returns the ID of the room currently shown, or null if nothing (or a stale room) is shown.
        /// </summary>
        private string? GetDisplayedTarget() =>
            room != null && !isLoading && _displayedRoomId != null && _displayedRoomId == RoomId
                ? _displayedRoomId
                : null;

        /// <summary>
        /// Re-checks after an awaited confirmation that the user is still looking at <paramref name="target"/>.
        /// </summary>
        private bool IsStillDisplayed(string target)
        {
            if (GetDisplayedTarget() == target) return true;
            Snackbar.Add(L["SelectionChangedActionAborted"], Severity.Warning);
            return false;
        }

        private static string FormatTarget(RoomDetailViewModel? model, string roomId) =>
            string.IsNullOrWhiteSpace(model?.Name) ? roomId : $"{model.Name} ({roomId})";

        private Task<TableData<RoomMediaItemViewModel>> LoadLocalMedia(TableState state, CancellationToken token) =>
            LoadMediaPage(state, room?.Media?.Local, token);

        private Task<TableData<RoomMediaItemViewModel>> LoadRemoteMedia(TableState state, CancellationToken token) =>
            LoadMediaPage(state, room?.Media?.Remote, token);

        /// <summary>
        /// Loads one page of metadata for <paramref name="media"/>, a list captured from the model before awaiting.
        /// If the room changes while the request is in flight (navigation, failed reload), the response is
        /// discarded instead of reading the new — possibly cleared — model.
        /// </summary>
        private async Task<TableData<RoomMediaItemViewModel>> LoadMediaPage(
            TableState state, List<RoomMediaItemViewModel>? media, CancellationToken token)
        {
            var empty = new TableData<RoomMediaItemViewModel> { TotalItems = 0, Items = [] };
            if (media == null) return empty;

            var model = room;
            var uris = media.Select(m => m.MediaId).Skip(state.Page * state.PageSize).Take(state.PageSize).ToList();
            if (uris.Count == 0) return new TableData<RoomMediaItemViewModel> { TotalItems = media.Count, Items = [] };

            var result = await RoomService.GetMediaMetadataBatchAsync(uris, token);
            if (token.IsCancellationRequested || !ReferenceEquals(room, model)) return empty;

            if (result.Success)
            {
                return new TableData<RoomMediaItemViewModel> { TotalItems = media.Count, Items = result.Data };
            }

            Snackbar.ShowResult(result);
            return new TableData<RoomMediaItemViewModel> { TotalItems = media.Count, Items = [] };
        }

        private Task LoadMoreMessages(string? from) =>
            _displayedRoomId is { } roomId ? LoadMessages(roomId, from, LoadToken) : Task.CompletedTask;

        private async Task LoadMessages(string roomId, string? from, CancellationToken token)
        {
            loadingMessages = true;
            var result = await RoomService.GetRoomMessagesAsync(roomId, from: from, limit: MessageHistoryPageSize, dir: "b", token: token);
            if (token.IsCancellationRequested || roomId != _loadedRoomId) return;

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
            else
            {
                Snackbar.ShowFailure(result);
            }
            loadingMessages = false;
        }

        private Task DeleteRoom() =>
            ConfirmAndDeleteRoomAsync(block: false, "DeleteRoomTitle", "DeleteRoomConfirmation", "Delete");

        private Task DeleteAndBlockRoom() =>
            ConfirmAndDeleteRoomAsync(block: true, "DeleteAndBlockRoomTitle", "DeleteAndBlockRoomConfirmation", "DeleteAndBlock");

        private async Task ConfirmAndDeleteRoomAsync(bool block, string titleKey, string confirmKey, string yesKey)
        {
            if (GetDisplayedTarget() is not { } target) return;

            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L[titleKey],
                L[confirmKey, FormatTarget(room, target)],
                yesText: L[yesKey], cancelText: L["Cancel"]);

            if (confirmed == true && IsStillDisplayed(target))
            {
                var result = await RoomService.DeleteRoomAsync(target, block, purge: true, token: _cts.Token);
                Snackbar.ShowResult(result);
                if (result.Success)
                {
                    Navigation.NavigateTo(RoomListPath);
                }
            }
        }

        private async Task QuarantineMedia()
        {
            if (GetDisplayedTarget() is not { } target) return;

            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["QuarantineMediaTitle"],
                L["QuarantineRoomMediaConfirmation", FormatTarget(room, target)],
                yesText: L["Quarantine"], cancelText: L["Cancel"]);

            if (confirmed == true && IsStillDisplayed(target))
            {
                var result = await RoomService.QuarantineMediaAsync(target, _cts.Token);
                Snackbar.ShowResult(result);
            }
        }

        private async Task BlockRoom()
        {
            if (GetDisplayedTarget() is not { } target) return;

            var result = await RoomService.BlockRoomAsync(target, true, _cts.Token);
            Snackbar.ShowResult(result);
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
                Snackbar.ShowResult(result);
                if (result.Success)
                {
                    await ReloadMediaTablesAsync();
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
                Snackbar.ShowResult(result);
                if (result.Success)
                {
                    await ReloadMediaTablesAsync();
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
                Snackbar.ShowResult(result);
                if (result.Success)
                {
                    await ReloadRoomDetails();
                    await ReloadMediaTablesAsync();
                }
            }
        }

        private async Task ReloadMediaTablesAsync()
        {
            await (localMediaTable?.ReloadServerData() ?? Task.CompletedTask);
            await (remoteMediaTable?.ReloadServerData() ?? Task.CompletedTask);
        }

        private async Task ToggleMediaProtection(RoomMediaItemViewModel media)
        {
            var result = media.SafeFromQuarantine
                ? await MediaService.UnprotectMediaAsync(media.MediaId, _cts.Token)
                : await MediaService.ProtectMediaAsync(media.MediaId, _cts.Token);

            Snackbar.ShowResult(result);

            if (result.Success)
            {
                media.SafeFromQuarantine = !media.SafeFromQuarantine;
                await (localMediaTable?.ReloadServerData() ?? Task.CompletedTask);
            }
        }

        private bool IsLocalUser(string userId) =>
            MatrixIdHelper.IsOnServer(userId, MatrixSession.Gateway?.ServerName);

        private bool IsPreviewable(string? mimeType)
        {
            if (string.IsNullOrEmpty(mimeType))
            {
                // For room media we often don't have the mime type upfront. 
                // We'll allow previewing by default if it's missing, 
                // the preview dialog handles the error if it's not actually an image/video/audio.
                return true;
            }
            return MediaHelper.IsImage(mimeType, StringComparison.CurrentCulture) ||
                   MediaHelper.IsVideo(mimeType, StringComparison.CurrentCulture) ||
                   MediaHelper.IsAudio(mimeType, StringComparison.CurrentCulture);
        }

        private async Task ShowPreview(RoomMediaItemViewModel media)
        {
            await DialogService.ShowMediaPreviewAsync(media.MediaId, media.UploadName, media.MediaId, media.MediaType, L["MediaPreview"]);
        }

        private void StopPolling()
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;
        }

        private void StartPolling(string roomId)
        {
            StopPolling();
            if (string.IsNullOrEmpty(activePurgeId)) return;

            _pollingCts = new CancellationTokenSource();
            var token = _pollingCts.Token;
            var purgeId = activePurgeId;

            // Start polling as a background task
            _ = Task.Run(() => PollPurgeStatusAsync(roomId, purgeId, token), token);
        }

        private async Task PollPurgeStatusAsync(string roomId, string purgeId, CancellationToken token)
        {
            int consecutiveFailures = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var result = await RoomService.GetPurgeHistoryStatusAsync(purgeId, token);
                    if (result.Success && result.Data != null)
                    {
                        consecutiveFailures = 0;
                        var status = result.Data.Status;
                        var error = result.Data.Error;
                        var shouldStop = false;

                        await InvokeAsync(() =>
                        {
                            if (token.IsCancellationRequested) return;
                            shouldStop = ApplyPurgeStatus(roomId, status, error);
                        });

                        if (shouldStop)
                        {
                            break;
                        }
                    }
                    else
                    {
                        if (result.Severity == Severity.Normal && token.IsCancellationRequested)
                        {
                            break;
                        }

                        consecutiveFailures++;
                        var errMsg = result.Message ?? "Unknown error";
                        Logger.LogWarning("Failed to get purge status for room {RoomId}, purge ID {PurgeId}. Attempt {Attempt} of 5. Error: {Error}", roomId.SanitizeForLogging(), purgeId.SanitizeForLogging(), consecutiveFailures, errMsg.SanitizeForLogging());

                        if (consecutiveFailures >= MaxConsecutivePurgeStatusFailures)
                        {
                            await InvokeAsync(() =>
                            {
                                ClearActivePurge(roomId);
                                Snackbar.Add(L["ErrorFetchingPurgeStatus"], Severity.Error);
                                StateHasChanged();
                            });
                            break;
                        }
                    }

                    await Task.Delay(PurgeStatusPollIntervalMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error in purge polling loop for room {RoomId}, purge ID {PurgeId}", roomId.SanitizeForLogging(), purgeId.SanitizeForLogging());
                try
                {
                    await InvokeAsync(() =>
                    {
                        ClearActivePurge(roomId);
                        Snackbar.Add(L["ErrorFetchingPurgeStatus"], Severity.Error);
                        StateHasChanged();
                    });
                }
                catch (Exception)
                {
                    // Prevent secondary crash during cleanup
                }
            }
        }

        /// <summary>
        /// Shows a polled purge status; on "complete" or "failed" clears the purge and reports it. Returns whether
        /// polling should stop.
        /// </summary>
        private bool ApplyPurgeStatus(string roomId, string? status, string? error)
        {
            activePurgeStatus = status;

            if (status == PurgeStatusComplete)
            {
                ClearActivePurge(roomId);
                Snackbar.Add(L["PurgeCompleted"], Severity.Success);
                StateHasChanged();
                return true;
            }

            if (status == PurgeStatusFailed)
            {
                ClearActivePurge(roomId);
                var errMsg = !string.IsNullOrEmpty(error)
                    ? string.Format(L["PurgeFailed"], error)
                    : string.Format(L["PurgeFailed"], L["UnknownError"]);
                Snackbar.Add(errMsg, Severity.Error);
                StateHasChanged();
                return true;
            }

            StateHasChanged();
            return false;
        }

        private void ClearActivePurge(string roomId)
        {
            RoomService.ClearActivePurgeId(roomId);
            activePurgeId = null;
            activePurgeStatus = null;
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
            if (GetDisplayedTarget() is not { } target) return;

            var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
            var parameters = new DialogParameters
            {
                { "RoomId", target },
                { "PreselectedEventId", preselectedEventId },
                { "PreselectedTimestamp", preselectedTimestamp }
            };

            var dialog = await DialogService.ShowAsync<PurgeHistoryDialog>(L["PurgeHistory"], parameters, options);
            var result = await dialog.Result;

            // The purge itself was issued for `target`; only track it here if that room is still shown.
            if (result != null && !result.Canceled && result.Data is string newPurgeId && !string.IsNullOrWhiteSpace(newPurgeId)
                && GetDisplayedTarget() == target)
            {
                activePurgeId = newPurgeId;
                activePurgeStatus = PurgeStatusActive;
                StartPolling(target);
                StateHasChanged();
            }
        }

        private void OnSearchChanged(string text)
        {
            _searchTerm = text;
        }

        private bool FilterFunc(RoomMemberViewModel member)
        {
            if (string.IsNullOrWhiteSpace(_searchTerm))
                return true;
            if (member.UserId?.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase) == true)
                return true;
            if (member.DisplayName?.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase) == true)
                return true;
            return false;
        }

        public void Dispose()
        {
            StopPolling();
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
