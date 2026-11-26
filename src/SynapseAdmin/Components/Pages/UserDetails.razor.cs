using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Infrastructure.Helpers;
using System.Net;

namespace SynapseAdmin.Components.Pages
{
    public partial class UserDetails : IDisposable
    {
        [Inject]
        public IMatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public IUserService UserService { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;
        [Inject]
        public IDialogService DialogService { get; set; } = null!;
        [Inject]
        public IMediaService MediaService { get; set; } = null!;
        [Inject]
        public IJSRuntime JSRuntime { get; set; } = null!;
        [Inject]
        public ITimeZoneService TimeZoneService { get; set; } = null!;
        [Inject]
        public ILogger<UserDetails> Logger { get; set; } = null!;

        [Parameter]
        public string UserId { get; set; } = string.Empty;

        private UserDetailViewModel? user;
        private bool isLoading = true;
        private string? errorMessage;
        private string? _loadedUserId;
        // The user the currently shown `user` model was loaded for; actions target this ID,
        // never the (possibly newer) route parameter.
        private string? _displayedUserId;
        private CancellationTokenSource? _loadCts;
        private MudTable<UserMediaItemViewModel>? mediaTable;
        private readonly CancellationTokenSource _cts = new();

        // Server-side media paging (#371): the current page and the totals across all uploads.
        // Statistics on _mediaPage describe that page only; _mediaTotals covers every upload.
        private UserMediaPageViewModel? _mediaPage;
        private UserMediaTotalsViewModel? _mediaTotals;
        private string? _mediaTotalsUserId;

        protected override async Task OnParametersSetAsync()
        {
            if (_loadedUserId == UserId && _loadCts != null) return;

            // Reset synchronously, before the first await, so the previous user (and its
            // action buttons) is never rendered while the route already points at a new user.
            var userId = UserId;
            _loadedUserId = userId;
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var token = _loadCts.Token;
            user = null;
            _displayedUserId = null;
            errorMessage = null;
            isLoading = true;
            _mediaPage = null;
            _mediaTotals = null;
            _mediaTotalsUserId = null;

            await TimeZoneService.InitializeAsync();
            if (token.IsCancellationRequested) return;

            await LoadUserDetails(userId, token);
        }

        private async Task LoadUserDetails(string userId, CancellationToken token)
        {
            var result = await UserService.GetUserDetailsAsync(userId, token);
            // Discard responses for a selection that is no longer current.
            if (token.IsCancellationRequested || userId != _loadedUserId) return;

            if (result.Success && result.Data != null)
            {
                user = result.Data;
                _displayedUserId = userId;
                errorMessage = null;
            }
            else
            {
                user = null;
                _displayedUserId = null;
                if (Snackbar.ShowResult(result))
                {
                    errorMessage = result.Message;
                }
            }
            isLoading = false;
        }

        /// <summary>
        /// Returns the ID of the user currently shown, or null if nothing (or a stale user) is shown.
        /// </summary>
        private string? GetDisplayedTarget() =>
            user != null && !isLoading && _displayedUserId != null && _displayedUserId == UserId
                ? _displayedUserId
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

        private static string FormatTarget(UserDetailViewModel? model, string userId) =>
            string.IsNullOrWhiteSpace(model?.DisplayName) ? userId : $"{model.DisplayName} ({userId})";

        /// <summary>
        /// Whether the shown user is the signed-in admin. Actions that would restrict that account (lock, suspend,
        /// remove admin, shadow-ban, deactivate) would end or restrict the session the app runs on, so they are disabled
        /// there (#584). The user IDs are compared ordinally.
        /// </summary>
        private bool IsOwnAccount =>
            GetDisplayedTarget() is { } target && string.Equals(target, MatrixSession.Gateway?.UserId, StringComparison.Ordinal);

        private Task ToggleLock() =>
            user is { } shown && !(IsOwnAccount && !shown.Locked)
                ? RunConfirmedUserAction(shown.Locked ? "UnlockUser" : "LockUser",
                    shown.Locked ? "UnlockUserConfirmation" : "LockUserConfirmation",
                    (target, token) => UserService.SetUserLockedAsync(target, !shown.Locked, token))
                : Task.CompletedTask;

        private Task ToggleSuspension() =>
            user is { } shown && !(IsOwnAccount && !shown.Suspended)
                ? RunConfirmedUserAction(shown.Suspended ? "UnsuspendUser" : "SuspendUser",
                    shown.Suspended ? "UnsuspendUserConfirmation" : "SuspendUserConfirmation",
                    (target, token) => UserService.SetUserSuspendedAsync(target, !shown.Suspended, token))
                : Task.CompletedTask;

        private Task ToggleAdmin() =>
            user is { } shown && !(IsOwnAccount && shown.Admin)
                ? RunConfirmedUserAction(shown.Admin ? "RemoveAdmin" : "MakeAdmin",
                    shown.Admin ? "RemoveAdminConfirmation" : "MakeAdminConfirmation",
                    (target, token) => UserService.SetUserAdminAsync(target, !shown.Admin, token))
                : Task.CompletedTask;

        private Task ToggleShadowBan() =>
            user is { } shown && !(IsOwnAccount && !shown.ShadowBanned)
                ? RunConfirmedUserAction(shown.ShadowBanned ? "LiftShadowBan" : "ShadowBanUser",
                    shown.ShadowBanned ? "LiftShadowBanConfirmation" : "ShadowBanUserConfirmation",
                    (target, token) => UserService.SetUserShadowBannedAsync(target, !shown.ShadowBanned, token))
                : Task.CompletedTask;

        /// <summary>
        /// Asks to confirm <paramref name="actionKey"/> for the user shown, re-checks afterwards that the user is still
        /// shown, runs <paramref name="action"/> for that user, shows the result and reloads the user after a success.
        /// The state each toggle sends is the one read when the admin clicked.
        /// </summary>
        private async Task RunConfirmedUserAction(string actionKey, string confirmationKey,
            Func<string, CancellationToken, Task<OperationResult>> action)
        {
            if (GetDisplayedTarget() is not { } target) return;

            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L[actionKey],
                L[confirmationKey, FormatTarget(user, target)],
                yesText: L[actionKey], cancelText: L["Cancel"]);

            if (confirmed == true && IsStillDisplayed(target))
            {
                var result = await action(target, _cts.Token);
                Snackbar.ShowResult(result);
                if (result.Success && _loadCts != null && GetDisplayedTarget() == target)
                {
                    await LoadUserDetails(target, _loadCts.Token);
                }
            }
        }

        /// <summary>
        /// Asks for a new password in <see cref="ResetPasswordDialog"/> and sends it for the user shown, if that user is
        /// still shown once the dialog closes. Not for a deactivated account, which Reactivate sets a password for (#584).
        /// </summary>
        private async Task OpenResetPasswordDialog()
        {
            if (GetDisplayedTarget() is not { } target || user is not { Deactivated: false }) return;

            var parameters = new DialogParameters
            {
                { nameof(ResetPasswordDialog.Target), FormatTarget(user, target) },
                { nameof(ResetPasswordDialog.IsOwnAccount), IsOwnAccount }
            };
            var dialog = await DialogService.ShowAsync<ResetPasswordDialog>(L["ResetPassword"], parameters, DialogPresets.Small());
            var result = await dialog.Result;

            if (result is { Canceled: false, Data: UserPasswordResetViewModel model } && IsStillDisplayed(target))
            {
                Snackbar.ShowResult(await UserService.ResetPasswordAsync(target, model.Password, model.LogoutDevices, _cts.Token));
            }
        }

        /// <summary>
        /// Asks for an optional new password in <see cref="ReactivateUserDialog"/> and reactivates the deactivated user
        /// shown, if still shown once the dialog closes, then reloads the user (#584).
        /// </summary>
        private async Task OpenReactivateDialog()
        {
            if (GetDisplayedTarget() is not { } target || user is not { Deactivated: true }) return;

            var parameters = new DialogParameters { { nameof(ReactivateUserDialog.Target), FormatTarget(user, target) } };
            var dialog = await DialogService.ShowAsync<ReactivateUserDialog>(L["ReactivateUser"], parameters, DialogPresets.Small());
            var result = await dialog.Result;

            if (result is { Canceled: false, Data: UserReactivateViewModel model } && IsStillDisplayed(target))
            {
                var outcome = await UserService.ReactivateUserAsync(target, model.Password, _cts.Token);
                Snackbar.ShowResult(outcome);
                if (outcome.Success && _loadCts != null && GetDisplayedTarget() == target)
                {
                    await LoadUserDetails(target, _loadCts.Token);
                }
            }
        }

        /// <summary>
        /// Edits the shown user's display name or removes the avatar in <see cref="UserProfileDialog"/>, and sends what
        /// changed if the user is still shown once the dialog closes, then reloads the user (#584).
        /// </summary>
        private async Task OpenProfileDialog()
        {
            if (GetDisplayedTarget() is not { } target || user == null) return;

            var parameters = new DialogParameters
            {
                { nameof(UserProfileDialog.Target), FormatTarget(user, target) },
                { nameof(UserProfileDialog.CurrentDisplayName), user.DisplayName },
                { nameof(UserProfileDialog.HasAvatar), !string.IsNullOrEmpty(user.AvatarUrl) }
            };
            var dialog = await DialogService.ShowAsync<UserProfileDialog>(L["EditProfile"], parameters, DialogPresets.Small());
            var result = await dialog.Result;

            if (result is { Canceled: false, Data: UserProfileEditViewModel { HasChanges: true } changes } && IsStillDisplayed(target))
            {
                var outcome = await UserService.UpdateUserProfileAsync(target, changes, _cts.Token);
                Snackbar.ShowResult(outcome);
                if (outcome.Success && _loadCts != null && GetDisplayedTarget() == target)
                {
                    await LoadUserDetails(target, _loadCts.Token);
                }
            }
        }

        private async Task DeactivateUser()
        {
            if (GetDisplayedTarget() is not { } target || IsOwnAccount) return;

            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["DeactivateUserTitle"],
                L["DeactivateUserConfirmation", FormatTarget(user, target)],
                yesText: L["Deactivate"], cancelText: L["Cancel"]);

            if (confirmed == true && IsStillDisplayed(target))
            {
                var result = await UserService.DeactivateUserAsync(target, token: _cts.Token);
                Snackbar.ShowResult(result);
                if (result.Success && _loadCts != null && GetDisplayedTarget() == target)
                {
                    await LoadUserDetails(target, _loadCts.Token);
                }
            }
        }

        private async Task OpenRenewAccountDialog()
        {
            if (GetDisplayedTarget() is not { } target || user == null) return;

            var options = DialogPresets.Small();
            var parameters = new DialogParameters
            {
                { nameof(RenewAccountDialog.UserId), target },
                { nameof(RenewAccountDialog.CurrentExpirationTimestamp), user.ExpirationTsDateTime }
            };

            var dialog = await DialogService.ShowAsync<RenewAccountDialog>(L["RenewAccount"], parameters, options);
            var result = await dialog.Result;

            // The renewal was issued for `target`; only patch the model if that user is still shown.
            if (result is { Canceled: false, Data: long newExpirationTs } && user != null && GetDisplayedTarget() == target)
            {
                user.ExpirationTs = newExpirationTs;
                StateHasChanged();
            }
        }

        private async Task QuarantineAllMedia()
        {
            if (GetDisplayedTarget() is not { } target) return;

            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["QuarantineMediaTitle"],
                L["QuarantineUserMediaConfirmation", FormatTarget(user, target)],
                yesText: L["Quarantine"], cancelText: L["Cancel"]);

            if (confirmed == true && IsStillDisplayed(target))
            {
                var result = await UserService.QuarantineMediaAsync(target, _cts.Token);
                Snackbar.ShowResult(result);
                if (result.Success && GetDisplayedTarget() == target)
                {
                    await ReloadMedia();
                }
            }
        }

        private async Task LoginAsUser()
        {
            if (GetDisplayedTarget() is not { } target) return;

            var result = await UserService.LoginAsUserAsync(target, TimeSpan.FromHours(1), _cts.Token);
            if (result.Success && !string.IsNullOrEmpty(result.Data))
            {
                if (await JSRuntime.TryCopyToClipboardAsync(result.Data, Logger))
                {
                    Snackbar.Add(result.Message + " " + L["TokenCopiedToClipboard"], result.Severity);
                }
                else
                {
                    Snackbar.Add(result.Message, result.Severity);
                }

                var encodedLabel = WebUtility.HtmlEncode(L["AccessToken"]);
                var encodedToken = WebUtility.HtmlEncode(result.Data);

                await DialogService.ShowMessageBoxAsync(
                    L["LoginAsUserShadow"],
                    (MarkupString)$"<b>{encodedLabel}:</b><br/><code style='word-break: break-all;'>{encodedToken}</code>",
                    yesText: L["Close"]);
            }
            else
            {
                Snackbar.ShowResult(result);
            }
        }

        /// <summary>
        /// MudTable ServerData callback: loads one page of the displayed user's uploads (newest first) and, once per
        /// user or after a change, the totals across all uploads. Responses for a user that is no longer displayed are discarded.
        /// </summary>
        private async Task<TableData<UserMediaItemViewModel>> LoadMediaPage(TableState state, CancellationToken token)
        {
            var empty = new TableData<UserMediaItemViewModel> { TotalItems = 0, Items = [] };
            if (GetDisplayedTarget() is not { } target) return empty;

            var pageTask = UserService.GetUserMediaPageAsync(target, state.Page * state.PageSize, state.PageSize, token);
            var totalsTask = _mediaTotalsUserId == target ? null : UserService.GetUserMediaTotalsAsync(target, token);

            var page = await pageTask;
            var totals = totalsTask == null ? null : await totalsTask;
            if (token.IsCancellationRequested || GetDisplayedTarget() != target) return empty;

            if (totals != null)
            {
                // Totals are optional: if unavailable, only the server's upload count is shown.
                _mediaTotals = totals.Success ? totals.Data : null;
                _mediaTotalsUserId = target;
            }

            if (!page.Success || page.Data == null)
            {
                _mediaPage = null;
                Snackbar.ShowResult(page);
                StateHasChanged();
                return empty;
            }

            _mediaPage = page.Data;
            StateHasChanged();
            return new TableData<UserMediaItemViewModel>
            {
                TotalItems = (int)Math.Min(page.Data.TotalCount, int.MaxValue),
                Items = page.Data.Items
            };
        }

        /// <summary>Reloads the current media page and the totals after an upload was changed or removed.</summary>
        private async Task ReloadMedia(bool itemRemoved = false)
        {
            _mediaTotalsUserId = null;
            if (mediaTable == null) return;

            // Removing the only item of a later page would leave an empty page; step back instead.
            if (itemRemoved && _mediaPage is { Items.Count: <= 1 } && mediaTable.CurrentPage > 0)
            {
                mediaTable.NavigateTo(Page.Previous);
                return;
            }

            await mediaTable.ReloadServerData();
        }

        /// <summary>The MXC URI of one of the user's uploads, or null without a session.</summary>
        private string? ResolveMxc(string mediaId)
        {
            if (MatrixSession.Gateway == null) return null;
            return MediaHelper.ToMxcUri(mediaId, MatrixSession.Gateway.ServerName);
        }

        /// <summary>Applies <paramref name="patch"/> to the upload if it is on the shown page, and re-renders.</summary>
        private void PatchMediaItem(string mediaId, Action<UserMediaItemViewModel> patch)
        {
            var item = _mediaPage?.Items.FirstOrDefault(m => m.MediaId == mediaId);
            if (item != null)
            {
                patch(item);
                StateHasChanged();
            }
        }

        private async Task QuarantineSingleMedia(string mediaIdPart)
        {
            if (ResolveMxc(mediaIdPart) is not { } mxc) return;

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
                    PatchMediaItem(mediaIdPart, item => item.QuarantinedBy = "Admin");
                }
            }
        }

        private async Task UnquarantineSingleMedia(string mediaIdPart)
        {
            if (ResolveMxc(mediaIdPart) is not { } mxc) return;

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
                    PatchMediaItem(mediaIdPart, item => item.QuarantinedBy = null);
                }
            }
        }

        private async Task DeleteSingleMedia(string mediaIdPart)
        {
            if (ResolveMxc(mediaIdPart) is not { } mxc) return;

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
                    await ReloadMedia(itemRemoved: true);
                }
            }
        }

        private async Task ToggleMediaProtection(UserMediaItemViewModel media)
        {
            if (ResolveMxc(media.MediaId) is not { } mxc) return;

            var result = media.SafeFromQuarantine
                ? await MediaService.UnprotectMediaAsync(mxc, _cts.Token)
                : await MediaService.ProtectMediaAsync(mxc, _cts.Token);

            Snackbar.ShowResult(result);

            if (result.Success)
            {
                media.SafeFromQuarantine = !media.SafeFromQuarantine;
                StateHasChanged();
            }
        }

        private bool IsPreviewable(string? mimeType)
        {
            if (string.IsNullOrEmpty(mimeType)) return false;
            return MediaHelper.IsImage(mimeType, StringComparison.CurrentCulture) ||
                   MediaHelper.IsVideo(mimeType, StringComparison.CurrentCulture) ||
                   MediaHelper.IsAudio(mimeType, StringComparison.CurrentCulture);
        }

        private async Task ShowPreview(UserMediaItemViewModel media)
        {
            if (ResolveMxc(media.MediaId) is not { } mxc) return;
            await DialogService.ShowMediaPreviewAsync(mxc, media.UploadName, media.MediaId, media.MediaType, L["MediaPreview"]);
        }

        private async Task ToggleExperimentalFeature(UserExperimentalFeatureViewModel feature, bool enabled)
        {
            if (feature.IsUpdating || feature.Enabled == enabled) return;
            if (GetDisplayedTarget() is not { } target) return;

            feature.IsUpdating = true;
            StateHasChanged();

            var updates = new Dictionary<string, bool>
            {
                [feature.FeatureKey] = enabled
            };

            var result = await UserService.SetUserExperimentalFeaturesAsync(target, updates, _cts.Token);

            Snackbar.ShowResult(result);

            if (result.Success)
            {
                feature.Enabled = enabled;
            }

            feature.IsUpdating = false;
            StateHasChanged();
        }

        public void Dispose()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
