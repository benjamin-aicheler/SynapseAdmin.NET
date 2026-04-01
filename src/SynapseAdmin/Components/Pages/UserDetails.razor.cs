using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages
{
    public partial class UserDetails
    {
        [Inject]
        public MatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public IUserService UserService { get; set; } = null!;
        [Inject]
        public NavigationManager Navigation { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;
        [Inject]
        public IDialogService DialogService { get; set; } = null!;

        [Parameter]
        public string UserId { get; set; } = string.Empty;

        private UserDetailViewModel? user;

        protected override async Task OnInitializedAsync()
        {
            await LoadUserDetails();
        }

        private async Task LoadUserDetails()
        {
            var result = await UserService.GetUserDetailsAsync(UserId);
            if (result.Success)
            {
                user = result.Data;
            }
            else
            {
                Snackbar.Add(result.ErrorMessage ?? "Error fetching user details", Severity.Error);
            }
        }

        private async Task DeactivateUser()
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                "Deactivate User", 
                "Are you sure you want to deactivate this user?", 
                yesText: "Deactivate", cancelText: "Cancel");
                
            if (confirmed == true)
            {
                var result = await UserService.DeactivateUserAsync(UserId);
                if (result.Success)
                {
                    Snackbar.Add("User deactivated.", Severity.Success);
                    await LoadUserDetails();
                }
                else
                {
                    Snackbar.Add(result.ErrorMessage ?? "Error deactivating user", Severity.Error);
                }
            }
        }

        private async Task QuarantineAllMedia()
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                "Quarantine Media", 
                "Are you sure you want to quarantine all media uploaded by this user?", 
                yesText: "Quarantine", cancelText: "Cancel");
            
            if (confirmed == true)
            {
                var result = await UserService.QuarantineMediaAsync(UserId);
                if (result.Success)
                {
                    Snackbar.Add("User media quarantined successfully.", Severity.Success);
                }
                else
                {
                    Snackbar.Add(result.ErrorMessage ?? "Error quarantining media", Severity.Error);
                }
            }
        }

        private async Task LoginAsUser()
        {
            var result = await UserService.LoginAsUserAsync(UserId, TimeSpan.FromHours(1));
            if (result.Success && !string.IsNullOrEmpty(result.Data))
            {
                Snackbar.Add($"Login successful for 1 hour. Access Token retrieved. (Simulation)", Severity.Info);
            }
            else
            {
                Snackbar.Add(result.ErrorMessage ?? "Error logging in as user", Severity.Error);
            }
        }
    }
}
