using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;

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

        [Parameter]
        public string UserId { get; set; } = string.Empty;

        private UserDetailViewModel? user;
        private MudTable<UserMediaItemViewModel>? mediaTable;
        private readonly CancellationTokenSource _cts = new();

        protected override async Task OnInitializedAsync()
        {
            await LoadUserDetails();
        }

        private async Task LoadUserDetails()
        {
            var result = await UserService.GetUserDetailsAsync(UserId, _cts.Token);
            if (result.Success)
            {
                user = result.Data;
            }
            else if (result.Severity != Severity.Normal) // Don't show snackbar if cancelled
            {
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task DeactivateUser()
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["DeactivateUserTitle"], 
                L["DeactivateUserConfirmation"], 
                yesText: L["Deactivate"], cancelText: L["Cancel"]);
                
            if (confirmed == true)
            {
                var result = await UserService.DeactivateUserAsync(UserId, token: _cts.Token);
                Snackbar.Add(result.Message, result.Severity);
                if (result.Success)
                {
                    await LoadUserDetails();
                }
            }
        }

        private async Task QuarantineAllMedia()
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["QuarantineMediaTitle"], 
                L["QuarantineUserMediaConfirmation"], 
                yesText: L["Quarantine"], cancelText: L["Cancel"]);
            
            if (confirmed == true)
            {
                var result = await UserService.QuarantineMediaAsync(UserId, _cts.Token);
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task LoginAsUser()
        {
            var result = await UserService.LoginAsUserAsync(UserId, TimeSpan.FromHours(1), _cts.Token);
            Snackbar.Add(result.Message, result.Severity);
        }

        private async Task QuarantineSingleMedia(string mediaIdPart)
        {
            if (MatrixSession.Gateway == null) return;
            var mxc = $"mxc://{MatrixSession.Gateway.ServerName}/{mediaIdPart}";
            
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
                    await LoadUserDetails();
                }
            }
        }

        private async Task UnquarantineSingleMedia(string mediaIdPart)
        {
            if (MatrixSession.Gateway == null) return;
            var mxc = $"mxc://{MatrixSession.Gateway.ServerName}/{mediaIdPart}";

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
                    await LoadUserDetails();
                }
            }
        }

        private async Task DeleteSingleMedia(string mediaIdPart)
        {
            if (MatrixSession.Gateway == null) return;
            var mxc = $"mxc://{MatrixSession.Gateway.ServerName}/{mediaIdPart}";

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
                    await LoadUserDetails();
                }
            }
        }

        private async Task ToggleMediaProtection(UserMediaItemViewModel media)
        {
            if (MatrixSession.Gateway == null) return;
            var mxc = $"mxc://{MatrixSession.Gateway.ServerName}/{media.MediaId}";

            var result = media.SafeFromQuarantine
                ? await MediaService.UnprotectMediaAsync(mxc, _cts.Token)
                : await MediaService.ProtectMediaAsync(mxc, _cts.Token);

            Snackbar.Add(result.Message, result.Severity);

            if (result.Success)
            {
                media.SafeFromQuarantine = !media.SafeFromQuarantine;
                await LoadUserDetails();
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
            var mxc = $"mxc://{MatrixSession.Gateway.ServerName}/{media.MediaId}";
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

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
