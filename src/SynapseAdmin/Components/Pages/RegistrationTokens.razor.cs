using LibMatrix.Homeservers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Services;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Components.Pages
{
    public partial class RegistrationTokens
    {
        [Inject] 
        public MatrixSessionService MatrixSession { get; set; } = null!;
        [Inject] 
        public RegistrationTokenService RegistrationTokenService { get; set; } = null!;
        [Inject] 
        public NavigationManager Navigation { get; set; } = null!;
        [Inject] 
        public ISnackbar Snackbar { get; set; } = null!;
        [Inject] 
        public IDialogService DialogService { get; set; } = null!;

        private List<RegistrationTokenViewModel> tokens = new();
        private bool isLoading = true;

        protected override async Task OnInitializedAsync()
        {
            await LoadTokens();
        }

        private async Task LoadTokens()
        {
            isLoading = true;
            try
            {
                tokens = await RegistrationTokenService.GetRegistrationTokensAsync();
            }
            catch (Exception ex)
            {
                Snackbar.Add(string.Format(L["ErrorLoadingTokens"], ex.Message), Severity.Error);
            }
            finally
            {
                isLoading = false;
            }
        }

        private async Task OpenCreateDialog()
        {
            var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
            var dialog = await DialogService.ShowAsync<RegistrationTokenDialog>(L["CreateToken"], options);
            var result = await dialog.Result;

            if (result != null && !result.Canceled && result.Data is RegistrationTokenViewModel viewModel)
            {
                try
                {
                    await RegistrationTokenService.CreateRegistrationTokenAsync(viewModel);
                    Snackbar.Add(L["TokenCreatedSuccessfully"], Severity.Success);
                    await LoadTokens();
                }
                catch (Exception ex)
                {
                    Snackbar.Add(string.Format(L["ErrorCreatingToken"], ex.Message), Severity.Error);
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
                try
                {
                    // Copy expiry from viewmodel to avoid logic leak in UI
                    await RegistrationTokenService.UpdateRegistrationTokenAsync(tokenObj.Token, viewModel);
                    Snackbar.Add(L["TokenUpdatedSuccessfully"], Severity.Success);
                    await LoadTokens();
                }
                catch (Exception ex)
                {
                    Snackbar.Add(string.Format(L["ErrorUpdatingToken"], ex.Message), Severity.Error);
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
                try
                {
                    await RegistrationTokenService.DeleteRegistrationTokenAsync(token);
                    Snackbar.Add(L["TokenDeletedSuccessfully"], Severity.Success);
                    await LoadTokens();
                }
                catch (Exception ex)
                {
                    Snackbar.Add(string.Format(L["ErrorDeletingToken"], ex.Message), Severity.Error);
                }
            }
        }
    }
}
