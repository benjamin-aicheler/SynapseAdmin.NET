using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages
{
    public partial class RegistrationTokens : IDisposable
    {
        [Inject]
        public IMatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public IRegistrationTokenService RegistrationTokenService { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;
        [Inject]
        public IDialogService DialogService { get; set; } = null!;
        [Inject]
        public IJSRuntime JSRuntime { get; set; } = null!;
        [Inject]
        public ITimeZoneService TimeZoneService { get; set; } = null!;
        [Inject]
        public ILogger<RegistrationTokens> Logger { get; set; } = null!;

        private List<RegistrationTokenViewModel> tokens = new();
        private bool isLoading = true;
        private readonly CancellationTokenSource _cts = new();

        protected override async Task OnInitializedAsync()
        {
            await TimeZoneService.InitializeAsync();
            await LoadTokens();
        }

        private async Task LoadTokens()
        {
            isLoading = true;
            var result = await RegistrationTokenService.GetRegistrationTokensAsync(_cts.Token);
            if (result.Success && result.Data != default)
            {
                tokens = result.Data;
            }
            else
            {
                Snackbar.ShowResult(result);
            }
            isLoading = false;
        }

        private async Task OpenCreateDialog()
        {
            var options = DialogPresets.Small();
            var dialog = await DialogService.ShowAsync<RegistrationTokenDialog>(L["CreateToken"], options);
            var result = await dialog.Result;

            if (result != null && !result.Canceled && result.Data is RegistrationTokenViewModel viewModel)
            {
                var createResult = await RegistrationTokenService.CreateRegistrationTokenAsync(viewModel, _cts.Token);
                Snackbar.ShowResult(createResult);
                if (createResult.Success)
                {
                    await LoadTokens();
                }
            }
        }

        private async Task OpenEditDialog(RegistrationTokenViewModel tokenObj)
        {
            var parameters = new DialogParameters
            {
                { "IsEdit", true },
                { "ExistingToken", tokenObj.Token },
                { "ExistingUsesAllowed", tokenObj.UsesAllowed },
                // The dialog edits wall-clock time in the user's time zone.
                { "ExistingExpiryDate", tokenObj.ExpiryTimeDateTime is { } expiry ? TimeZoneService.ToLocal(expiry).DateTime : null },
                // The stored instant, which an unchanged edit sends back as is (#520).
                { "ExistingExpiryTime", tokenObj.ExpiryTime }
            };

            var options = DialogPresets.Small();
            var dialog = await DialogService.ShowAsync<RegistrationTokenDialog>(L["EditToken"], parameters, options);
            var result = await dialog.Result;

            if (result != null && !result.Canceled && result.Data is RegistrationTokenViewModel viewModel)
            {
                var updateResult = await RegistrationTokenService.UpdateRegistrationTokenAsync(tokenObj.Token, viewModel, _cts.Token);
                Snackbar.ShowResult(updateResult);
                if (updateResult.Success)
                {
                    await LoadTokens();
                }
            }
        }

        private async Task DeleteToken(string token)
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["DeleteToken"],
                string.Format(L["DeleteTokenConfirmation"], token),
                yesText: L["Delete"], cancelText: L["Cancel"]);

            if (confirmed == true)
            {
                var deleteResult = await RegistrationTokenService.DeleteRegistrationTokenAsync(token, _cts.Token);
                Snackbar.ShowResult(deleteResult);
                if (deleteResult.Success)
                {
                    await LoadTokens();
                }
            }
        }

        private async Task CopyToClipboard(string text)
        {
            if (await JSRuntime.TryCopyToClipboardAsync(text, Logger))
            {
                Snackbar.Add(L["TokenCopiedToClipboard"], Severity.Success);
            }
            else
            {
                Snackbar.Add(L["ErrorCopyingToClipboard"], Severity.Error);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
