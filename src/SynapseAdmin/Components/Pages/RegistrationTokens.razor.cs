using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages
{
    public partial class RegistrationTokens
    {
        [Inject] 
        public IMatrixSessionService MatrixSession { get; set; } = null!;
        [Inject] 
        public IRegistrationTokenService RegistrationTokenService { get; set; } = null!;
        [Inject] 
        public NavigationManager Navigation { get; set; } = null!;
        [Inject] 
        public ISnackbar Snackbar { get; set; } = null!;
        [Inject] 
        public IDialogService DialogService { get; set; } = null!;
        [Inject]
        public IJSRuntime JSRuntime { get; set; } = null!;

        private List<RegistrationTokenViewModel> tokens = new();
        private bool isLoading = true;

        protected override async Task OnInitializedAsync()
        {
            await LoadTokens();
        }

        private async Task LoadTokens()
        {
            isLoading = true;
            var result = await RegistrationTokenService.GetRegistrationTokensAsync();
            if (result.Success && result.Data != default)
            {
                tokens = result.Data;
            }
            else
            {
                Snackbar.Add(result.Message, result.Severity);
            }
            isLoading = false;
        }

        private async Task OpenCreateDialog()
        {
            var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
            var dialog = await DialogService.ShowAsync<RegistrationTokenDialog>(L["CreateToken"], options);
            var result = await dialog.Result;

            if (result != null && !result.Canceled && result.Data is RegistrationTokenViewModel viewModel)
            {
                var createResult = await RegistrationTokenService.CreateRegistrationTokenAsync(viewModel);
                Snackbar.Add(createResult.Message, createResult.Severity);
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
                { "ExistingExpiryDate", tokenObj.ExpiryTimeDateTime }
            };

            var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
            var dialog = await DialogService.ShowAsync<RegistrationTokenDialog>(L["EditToken"], parameters, options);
            var result = await dialog.Result;

            if (result != null && !result.Canceled && result.Data is RegistrationTokenViewModel viewModel)
            {
                var updateResult = await RegistrationTokenService.UpdateRegistrationTokenAsync(tokenObj.Token, viewModel);
                Snackbar.Add(updateResult.Message, updateResult.Severity);
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
                var deleteResult = await RegistrationTokenService.DeleteRegistrationTokenAsync(token);
                Snackbar.Add(deleteResult.Message, deleteResult.Severity);
                if (deleteResult.Success)
                {
                    await LoadTokens();
                }
            }
        }

        private async Task CopyToClipboard(string text)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", text);
                Snackbar.Add(L["TokenCopiedToClipboard"], Severity.Success);
            }
            catch (Exception)
            {
                Snackbar.Add(L["ErrorCopyingToClipboard"], Severity.Error);
            }
        }
    }
}
