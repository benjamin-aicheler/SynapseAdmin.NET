using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Models.Responses;
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
        private const int RoomDeletionPollIntervalMs = 5000;
        private const int MaxConsecutiveRoomDeletionStatusFailures = 5;
        private const int MessageHistoryPageSize = 50;

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

        // The room deletion shown in the banner (#518): set while the homeserver reports it as scheduled or active.
        private string? activeDeleteId;
        private RoomDeletionState activeDeletionState;
        private CancellationTokenSource? _deletionPollingCts;

        // The room's block state (#586). Null while it is unknown (loading, or the read failed): the page then offers both
        // Block and Unblock. Each read gets a number, and only the latest one applies, so a slow first read can't
        // overwrite the state read again after an action.
        private RoomBlockStatusViewModel? blockStatus;
        private int _blockStatusRequest;

        /// <summary>A deletion or a purge is running: every room action is disabled until it ends (#518).</summary>
        private bool IsRoomOperationRunning => activeDeleteId != null || !string.IsNullOrEmpty(activePurgeId);

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
            StopDeletionPolling();
            var token = ResetLoadToken();
            room = null;
            _displayedRoomId = null;
            messages = null;
            loadingMessages = false;
            errorMessage = null;
            isLoading = true;
            activePurgeId = RoomService.GetActivePurgeId(roomId);
            activePurgeStatus = null;
            activeDeleteId = null;
            activeDeletionState = RoomDeletionState.Unknown;
            blockStatus = null;

            await TimeZoneService.InitializeAsync();
            if (token.IsCancellationRequested) return;

            await LoadRoomDetails(roomId, token);
            if (token.IsCancellationRequested) return;

            await LoadBlockStatusAsync(roomId, token);
            if (token.IsCancellationRequested) return;

            await LoadMessages(roomId, null, token);
            if (token.IsCancellationRequested) return;

            if (!string.IsNullOrEmpty(activePurgeId))
            {
                StartPolling(roomId);
            }

            await FindRunningDeletionAsync(roomId, token);
        }

        /// <summary>
        /// Shows the banner for a deletion the homeserver reports as scheduled or running, also one started in another
        /// session or tool (#518). A failed lookup shows nothing: the service logged it, and the page works without it.
        /// </summary>
        private async Task FindRunningDeletionAsync(string roomId, CancellationToken token)
        {
            if (_displayedRoomId != roomId) return;

            var result = await RoomService.GetActiveRoomDeletionAsync(roomId, token);
            if (token.IsCancellationRequested || roomId != _loadedRoomId || _displayedRoomId != roomId) return;

            if (result is { Success: true, Data: { IsRunning: true } deletion } && activeDeleteId == null)
            {
                activeDeleteId = deletion.DeleteId;
                activeDeletionState = deletion.State;
                StartDeletionPolling(roomId);
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

        /// <summary>
        /// Reads whether the shown room is blocked (#586). A failed read leaves the state unknown and reports the failure.
        /// </summary>
        private async Task LoadBlockStatusAsync(string roomId, CancellationToken token)
        {
            if (_displayedRoomId != roomId) return;

            var request = ++_blockStatusRequest;
            var result = await RoomService.GetRoomBlockStatusAsync(roomId, token);
            if (token.IsCancellationRequested || request != _blockStatusRequest || roomId != _loadedRoomId || _displayedRoomId != roomId) return;

            if (result is { Success: true, Data: { } status })
            {
                blockStatus = status;
            }
            else
            {
                blockStatus = null;
                if (result != null) Snackbar.ShowFailure(result);
            }
        }

        private Task ReloadBlockStatus() =>
            _displayedRoomId is { } roomId ? LoadBlockStatusAsync(roomId, LoadToken) : Task.CompletedTask;

        /// <summary>
        /// Reads the block state again after a deletion that ended without deleting the room, which Delete &amp; Block
        /// may still have blocked. Runs outside an event handler, so it renders by itself.
        /// </summary>
        private async Task RefreshBlockStatusAfterDeletionAsync()
        {
            await ReloadBlockStatus();
            if (!_cts.IsCancellationRequested) StateHasChanged();
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

                // The homeserver only starts the deletion: stay on the page and follow it, as after a purge (#518).
                // Only if the room is still the one shown; without a delete_id there is nothing to follow.
                if (result.Success && !string.IsNullOrWhiteSpace(result.Data) && GetDisplayedTarget() == target)
                {
                    activeDeleteId = result.Data;
                    activeDeletionState = RoomDeletionState.Scheduled;
                    StartDeletionPolling(target);
                    StateHasChanged();
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

        private Task BlockRoom() => ConfirmAndSetBlockAsync(block: true);

        private Task UnblockRoom() => ConfirmAndSetBlockAsync(block: false);

        /// <summary>
        /// Blocks or unblocks the shown room after a confirmation that names it, then reads its block state again (#586).
        /// </summary>
        private async Task ConfirmAndSetBlockAsync(bool block)
        {
            if (GetDisplayedTarget() is not { } target) return;

            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L[block ? "BlockRoomTitle" : "UnblockRoomTitle"],
                L[block ? "BlockRoomConfirmation" : "UnblockRoomConfirmation", FormatTarget(room, target)],
                yesText: L[block ? "Block" : "Unblock"], cancelText: L["Cancel"]);

            if (confirmed != true || !IsStillDisplayed(target)) return;

            var result = await RoomService.BlockRoomAsync(target, block, _cts.Token);
            Snackbar.ShowResult(result);

            if (result.Success && GetDisplayedTarget() == target)
            {
                await LoadBlockStatusAsync(target, LoadToken);
            }
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
            var poller = new StatusPoller<SynapseAdminPurgeHistoryStatusResponse>
            {
                FetchAsync = t => RoomService.GetPurgeHistoryStatusAsync(purgeId, t),
                InvokeAsync = InvokeAsync,
                IsCurrent = () => IsCurrentPoll(purgeId, token),
                Apply = status => ApplyPurgeStatus(roomId, purgeId, status.Status),
                GiveUp = () => GiveUpOnPurge(roomId, purgeId),
                IntervalMs = PurgeStatusPollIntervalMs,
                MaxConsecutiveFailures = MaxConsecutivePurgeStatusFailures,
                LogFailedAttempt = (attempt, error) => Logger.LogWarning(
                    "Failed to get purge status for room {RoomId}, purge ID {PurgeId}. Attempt {Attempt} of {MaxAttempts}. Error: {Error}",
                    roomId.SanitizeForLogging(), purgeId.SanitizeForLogging(), attempt, MaxConsecutivePurgeStatusFailures, error.SanitizeForLogging()),
                LogUnexpectedError = ex => Logger.LogError(ex, "Unexpected error in purge polling loop for room {RoomId}, purge ID {PurgeId}",
                    roomId.SanitizeForLogging(), purgeId.SanitizeForLogging()),
                LogCleanupFailure = ex => Logger.LogDebug(ex, "Purge polling cleanup failed for room {RoomId} (circuit likely gone)",
                    roomId.SanitizeForLogging())
            };

            // Start polling as a background task
            _ = Task.Run(() => poller.RunAsync(token), token);
        }

        private void GiveUpOnPurge(string roomId, string purgeId)
        {
            ClearActivePurge(roomId, purgeId);
            Snackbar.Add(L["ErrorFetchingPurgeStatus"], Severity.Error);
            StateHasChanged();
        }

        /// <summary>
        /// Shows a polled purge status; on "complete" or "failed" clears the purge and reports it. Returns whether
        /// polling should stop. A failure shows only a localized message: the homeserver's error text is logged by
        /// <c>RoomService</c> (#518).
        /// </summary>
        private bool ApplyPurgeStatus(string roomId, string purgeId, string? status)
        {
            activePurgeStatus = status;

            if (status == PurgeStatusComplete)
            {
                ClearActivePurge(roomId, purgeId);
                Snackbar.Add(L["PurgeCompleted"], Severity.Success);
                StateHasChanged();
                return true;
            }

            if (status == PurgeStatusFailed)
            {
                ClearActivePurge(roomId, purgeId);
                Snackbar.Add(L["PurgeFailed"], Severity.Error);
                StateHasChanged();
                return true;
            }

            StateHasChanged();
            return false;
        }

        private void ClearActivePurge(string roomId, string purgeId)
        {
            // Only this purge: another session may already track a newer purge for the room (#522).
            RoomService.ClearActivePurgeId(roomId, purgeId);
            activePurgeId = null;
            activePurgeStatus = null;
        }

        /// <summary>
        /// Whether a poll's answer still belongs to the shown purge. Navigating or starting a new purge cancels the
        /// previous poll on the dispatcher (<see cref="StopPolling"/>), so call this on the dispatcher: a late answer
        /// for another room or an older purge then changes no field, cache entry or snackbar (#522).
        /// </summary>
        private bool IsCurrentPoll(string purgeId, CancellationToken token) =>
            !token.IsCancellationRequested && activePurgeId == purgeId;

        private void StopDeletionPolling()
        {
            _deletionPollingCts?.Cancel();
            _deletionPollingCts?.Dispose();
            _deletionPollingCts = null;
        }

        /// <summary>
        /// Checks the shown deletion now and then every <see cref="RoomDeletionPollIntervalMs"/> until it ends, the page
        /// moves on, or <see cref="MaxConsecutiveRoomDeletionStatusFailures"/> checks in a row fail (#518).
        /// </summary>
        private void StartDeletionPolling(string roomId)
        {
            StopDeletionPolling();
            if (activeDeleteId is not { } deleteId) return;

            _deletionPollingCts = new CancellationTokenSource();
            var token = _deletionPollingCts.Token;
            var poller = new StatusPoller<RoomDeletionStatusViewModel>
            {
                FetchAsync = t => RoomService.GetRoomDeletionStatusAsync(deleteId, t),
                InvokeAsync = InvokeAsync,
                IsCurrent = () => IsCurrentDeletionPoll(deleteId, token),
                Apply = status => ApplyRoomDeletionStatus(status.State),
                GiveUp = GiveUpOnDeletion,
                IntervalMs = RoomDeletionPollIntervalMs,
                MaxConsecutiveFailures = MaxConsecutiveRoomDeletionStatusFailures,
                LogFailedAttempt = (attempt, error) => Logger.LogWarning(
                    "Failed to get room deletion status for room {RoomId}, delete ID {DeleteId}. Attempt {Attempt} of {MaxAttempts}. Error: {Error}",
                    roomId.SanitizeForLogging(), deleteId.SanitizeForLogging(), attempt, MaxConsecutiveRoomDeletionStatusFailures, error.SanitizeForLogging()),
                LogUnexpectedError = ex => Logger.LogError(ex, "Unexpected error in room deletion polling loop for room {RoomId}, delete ID {DeleteId}",
                    roomId.SanitizeForLogging(), deleteId.SanitizeForLogging()),
                LogCleanupFailure = ex => Logger.LogDebug(ex, "Room deletion polling cleanup failed for room {RoomId} (circuit likely gone)",
                    roomId.SanitizeForLogging())
            };
            _ = Task.Run(() => poller.RunAsync(token), token);
        }

        /// <summary>
        /// Shows a checked deletion status; when the deletion has ended, removes the banner and reports how. Returns
        /// whether checking should stop. Call it on the dispatcher, after <see cref="IsCurrentDeletionPoll"/>.
        /// </summary>
        private bool ApplyRoomDeletionStatus(RoomDeletionState state)
        {
            switch (state)
            {
                case RoomDeletionState.Scheduled or RoomDeletionState.Active:
                    activeDeletionState = state;
                    StateHasChanged();
                    return false;

                case RoomDeletionState.Complete:
                    // The room is gone: show it as deleted instead of its last data, and stop following its purge.
                    ClearActiveDeletion();
                    StopPolling();
                    activePurgeId = null;
                    activePurgeStatus = null;
                    room = null;
                    _displayedRoomId = null;
                    messages = null;
                    errorMessage = L["RoomDeleted"];
                    Snackbar.Add(L["RoomDeletedSuccessfully"], Severity.Success);
                    break;

                case RoomDeletionState.Failed:
                    // Synapse's error text is only in the log (RoomService); the page shows a localized message.
                    ClearActiveDeletion();
                    Snackbar.Add(L["RoomDeletionFailed"], Severity.Error);
                    _ = RefreshBlockStatusAfterDeletionAsync();
                    break;

                default:
                    ClearActiveDeletion();
                    Snackbar.Add(L["RoomDeletionStatusUnknown"], Severity.Warning);
                    _ = RefreshBlockStatusAfterDeletionAsync();
                    break;
            }

            StateHasChanged();
            return true;
        }

        private void GiveUpOnDeletion()
        {
            ClearActiveDeletion();
            Snackbar.Add(L["ErrorFetchingRoomDeletionStatus"], Severity.Error);
            StateHasChanged();
            _ = RefreshBlockStatusAfterDeletionAsync();
        }

        private void ClearActiveDeletion()
        {
            activeDeleteId = null;
            activeDeletionState = RoomDeletionState.Unknown;
        }

        /// <summary>
        /// Whether a check's answer still belongs to the deletion shown. Navigating or a newer deletion cancels the previous
        /// check on the dispatcher (<see cref="StopDeletionPolling"/>), so call this on the dispatcher (as for purge, #522).
        /// </summary>
        private bool IsCurrentDeletionPoll(string deleteId, CancellationToken token) =>
            !token.IsCancellationRequested && activeDeleteId == deleteId;

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

            var options = DialogPresets.Medium();
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

        private Task MakeRoomAdmin() => OpenRoomUserActionAsync(RoomUserAction.MakeAdmin);

        private Task JoinUser() => OpenRoomUserActionAsync(RoomUserAction.Join);

        /// <summary>
        /// Asks for the user of a room recovery action (#587). The dialog names the shown room and is the confirmation;
        /// afterwards the page re-checks that the room is still shown, sends the action and, after a success, loads the
        /// room again so that its members and state events (the power levels) are current.
        /// </summary>
        private async Task OpenRoomUserActionAsync(RoomUserAction action)
        {
            if (GetDisplayedTarget() is not { } target) return;

            var gateway = MatrixSession.Gateway;
            var parameters = new DialogParameters
            {
                [nameof(RoomUserActionDialog.Action)] = action,
                [nameof(RoomUserActionDialog.RoomLabel)] = FormatTarget(room, target),
                [nameof(RoomUserActionDialog.SignedInUserId)] = gateway?.UserId,
                [nameof(RoomUserActionDialog.ServerName)] = gateway?.ServerName,
                [nameof(RoomUserActionDialog.JoinedMemberIds)] =
                    (IReadOnlyCollection<string>)(room?.Members.Select(m => m.UserId).ToList() ?? []),
            };
            var title = L[action == RoomUserAction.Join ? "JoinUserTitle" : "MakeRoomAdminTitle"];

            var dialog = await DialogService.ShowAsync<RoomUserActionDialog>(title, parameters, DialogPresets.Small());
            var result = await dialog.Result;
            if (result is not { Canceled: false, Data: string userId } || !IsStillDisplayed(target)) return;

            var outcome = action == RoomUserAction.Join
                ? await RoomService.JoinUserToRoomAsync(target, userId, _cts.Token)
                : await RoomService.MakeRoomAdminAsync(target, userId, _cts.Token);
            Snackbar.ShowResult(outcome);

            if (outcome.Success && GetDisplayedTarget() == target)
            {
                await ReloadRoomDetails();
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
            StopDeletionPolling();
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
