using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Infrastructure.Helpers;

namespace SynapseAdmin.Components.Pages
{
    public partial class UserDetails : IDisposable
    {
        [Inject]
        public IMatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public IUserService UserService { get; set; } = null!;
        [Inject]
        public NavigationManager Navigation { get; set; } = null!;
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
                if (result.Severity != Severity.Normal)
                {
                    errorMessage = result.Message;
                    Snackbar.Add(result.Message, result.Severity);
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

        private async Task DeactivateUser()
        {
            if (GetDisplayedTarget() is not { } target) return;

            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["DeactivateUserTitle"],
                L["DeactivateUserConfirmation", FormatTarget(user, target)],
                yesText: L["Deactivate"], cancelText: L["Cancel"]);

            if (confirmed == true && IsStillDisplayed(target))
            {
                var result = await UserService.DeactivateUserAsync(target, token: _cts.Token);
                if (result.Severity != Severity.Normal)
                {
                    Snackbar.Add(result.Message, result.Severity);
                }
                if (result.Success && _loadCts != null && GetDisplayedTarget() == target)
                {
                    await LoadUserDetails(target, _loadCts.Token);
                }
            }
        }

        private async Task OpenRenewAccountDialog()
        {
            if (GetDisplayedTarget() is not { } target || user == null) return;

            var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
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
                if (result.Severity != Severity.Normal)
                {
                    Snackbar.Add(result.Message, result.Severity);
                }
            }
        }

        private async Task LoginAsUser()
        {
            if (GetDisplayedTarget() is not { } target) return;

            var result = await UserService.LoginAsUserAsync(target, TimeSpan.FromHours(1), _cts.Token);
            if (result.Success && !string.IsNullOrEmpty(result.Data))
            {
                try
                {
                    await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", result.Data);
                    Snackbar.Add(result.Message + " " + L["TokenCopiedToClipboard"], result.Severity);
                }
                catch
                {
                    Snackbar.Add(result.Message, result.Severity);
                }

                var encodedLabel = System.Net.WebUtility.HtmlEncode(L["AccessToken"]);
                var encodedToken = System.Net.WebUtility.HtmlEncode(result.Data);

                await DialogService.ShowMessageBoxAsync(
                    L["LoginAsUserShadow"],
                    (MarkupString)$"<b>{encodedLabel}:</b><br/><code style='word-break: break-all;'>{encodedToken}</code>",
                    yesText: L["Close"]);
            }
            else if (result.Severity != Severity.Normal)
            {
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task QuarantineSingleMedia(string mediaIdPart)
        {
            if (MatrixSession.Gateway == null) return;
            var mxc = MediaHelper.ToMxcUri(mediaIdPart, MatrixSession.Gateway.ServerName);
            
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["QuarantineMediaTitle"],
                L["QuarantineMediaConfirmation"],
                yesText: L["Quarantine"], cancelText: L["Cancel"]);

            if (confirmed == true)
            {
                var result = await MediaService.QuarantineMediaAsync(mxc, _cts.Token);
                if (result.Severity != Severity.Normal)
                {
                    Snackbar.Add(result.Message, result.Severity);
                }
                if (result.Success)
                {
                    var item = user?.Media?.Media.FirstOrDefault(m => m.MediaId == mediaIdPart);
                    if (item != null)
                    {
                        item.QuarantinedBy = "Admin";
                        StateHasChanged();
                    }
                }
            }
        }

        private async Task UnquarantineSingleMedia(string mediaIdPart)
        {
            if (MatrixSession.Gateway == null) return;
            var mxc = MediaHelper.ToMxcUri(mediaIdPart, MatrixSession.Gateway.ServerName);

            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["UnquarantineMediaTitle"],
                L["UnquarantineMediaConfirmation"],
                yesText: L["Unquarantine"], cancelText: L["Cancel"]);

            if (confirmed == true)
            {
                var result = await MediaService.UnquarantineMediaAsync(mxc, _cts.Token);
                if (result.Severity != Severity.Normal)
                {
                    Snackbar.Add(result.Message, result.Severity);
                }
                if (result.Success)
                {
                    var item = user?.Media?.Media.FirstOrDefault(m => m.MediaId == mediaIdPart);
                    if (item != null)
                    {
                        item.QuarantinedBy = null;
                        StateHasChanged();
                    }
                }
            }
        }

        private async Task DeleteSingleMedia(string mediaIdPart)
        {
            if (MatrixSession.Gateway == null) return;
            var mxc = MediaHelper.ToMxcUri(mediaIdPart, MatrixSession.Gateway.ServerName);

            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["DeleteMediaTitle"],
                L["DeleteMediaConfirmation"],
                yesText: L["Delete"], cancelText: L["Cancel"]);

            if (confirmed == true)
            {
                var result = await MediaService.DeleteMediaAsync(mxc, _cts.Token);
                if (result.Severity != Severity.Normal)
                {
                    Snackbar.Add(result.Message, result.Severity);
                }
                if (result.Success && user?.Media != null)
                {
                    var item = user.Media.Media.FirstOrDefault(m => m.MediaId == mediaIdPart);
                    if (item != null)
                    {
                        user.Media.Media.Remove(item);
                        user.Media.TotalCount = Math.Max(0, user.Media.TotalCount - 1);
                        user.Media.TotalSize = Math.Max(0, user.Media.TotalSize - item.MediaLength);
                        StateHasChanged();
                    }
                }
            }
        }

        private async Task ToggleMediaProtection(UserMediaItemViewModel media)
        {
            if (MatrixSession.Gateway == null) return;
            var mxc = MediaHelper.ToMxcUri(media.MediaId, MatrixSession.Gateway.ServerName);

            var result = media.SafeFromQuarantine
                ? await MediaService.UnprotectMediaAsync(mxc, _cts.Token)
                : await MediaService.ProtectMediaAsync(mxc, _cts.Token);

            if (result.Severity != Severity.Normal)
            {
                Snackbar.Add(result.Message, result.Severity);
            }

            if (result.Success)
            {
                media.SafeFromQuarantine = !media.SafeFromQuarantine;
                StateHasChanged();
            }
        }

        private bool IsPreviewable(string? mimeType)
        {
            if (string.IsNullOrEmpty(mimeType)) return false;
            return mimeType.StartsWith("image/") || mimeType.StartsWith("video/") || mimeType.StartsWith("audio/");
        }

        private async Task ShowPreview(UserMediaItemViewModel media)
        {
            if (MatrixSession.Gateway == null) return;
            var mxc = MediaHelper.ToMxcUri(media.MediaId, MatrixSession.Gateway.ServerName);
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

            if (result.Severity != Severity.Normal)
            {
                Snackbar.Add(result.Message, result.Severity);
            }

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
