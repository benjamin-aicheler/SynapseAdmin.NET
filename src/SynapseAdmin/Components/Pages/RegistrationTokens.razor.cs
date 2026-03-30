using LibMatrix.Homeservers;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;
using SynapseAdmin.Models.ViewModels;

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
                Snackbar.Add($"Error loading tokens: {ex.Message}", Severity.Error);
            }
            finally
            {
                isLoading = false;
            }
        }

        private async Task OpenCreateDialog()
        {
            var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
            var dialog = await DialogService.ShowAsync<RegistrationTokenDialog>("Create Token", options);
            var result = await dialog.Result;

            if (result != null && !result.Canceled && result.Data is RegistrationTokenViewModel viewModel)
            {
                try
                {
                    await RegistrationTokenService.CreateRegistrationTokenAsync(viewModel);
                    Snackbar.Add("Token created successfully.", Severity.Success);
                    await LoadTokens();
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Error creating token: {ex.Message}", Severity.Error);
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
            var dialog = await DialogService.ShowAsync<RegistrationTokenDialog>("Edit Token", parameters, options);
            var result = await dialog.Result;

            if (result != null && !result.Canceled && result.Data is RegistrationTokenViewModel viewModel)
            {
                try
                {
                    // Copy expiry from viewmodel to avoid logic leak in UI
                    await RegistrationTokenService.UpdateRegistrationTokenAsync(tokenObj.Token, viewModel);
                    Snackbar.Add("Token updated successfully.", Severity.Success);
                    await LoadTokens();
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Error updating token: {ex.Message}", Severity.Error);
                }
            }
        }

        private async Task DeleteToken(string token)
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                "Delete Token",
                $"Are you sure you want to delete the token '{token}'?",
                yesText: "Delete", cancelText: "Cancel");

            if (confirmed == true)
            {
                try
                {
                    await RegistrationTokenService.DeleteRegistrationTokenAsync(token);
                    Snackbar.Add("Token deleted successfully.", Severity.Success);
                    await LoadTokens();
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Error deleting token: {ex.Message}", Severity.Error);
                }
            }
        }
    }
}
