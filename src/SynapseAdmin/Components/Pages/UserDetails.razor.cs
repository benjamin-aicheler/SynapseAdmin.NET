using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;

namespace SynapseAdmin.Components.Pages
{
    public partial class UserDetails
    {
        [Inject]
        public MatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public UserService UserService { get; set; } = null!;
        [Inject]
        public NavigationManager Navigation { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;
        [Inject]
        public IDialogService DialogService { get; set; } = null!;

        [Parameter]
        public string UserId { get; set; } = string.Empty;

        private SynapseAdminUserListResult.SynapseAdminUserListResultUser? userDetails;
        private SynapseAdminUserMediaResult? media;

        protected override async Task OnInitializedAsync()
        {
            await LoadUserDetails();
        }

        private async Task LoadUserDetails()
        {
            try
            {
                var vm = await UserService.GetUserDetailsAsync(UserId);
                if (vm != null)
                {
                    userDetails = vm.Details;
                    media = vm.Media;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching user details: {ex.Message}");
                Snackbar.Add($"Error fetching user details: {ex.Message}", Severity.Error);
            }
        }

        private async Task DeactivateUser()
        {
            bool? result = await DialogService.ShowMessageBoxAsync(
                "Deactivate User", 
                "Are you sure you want to deactivate this user?", 
                yesText: "Deactivate", cancelText: "Cancel");
                
            if (result == true)
            {
                try {
                    await UserService.DeactivateUserAsync(UserId);
                    Snackbar.Add("User deactivated.", Severity.Success);
                    await LoadUserDetails();
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Error deactivating user: {ex.Message}", Severity.Error);
                }
            }
        }

        private async Task QuarantineAllMedia()
        {
            bool? result = await DialogService.ShowMessageBoxAsync(
                "Quarantine Media", 
                "Are you sure you want to quarantine all media uploaded by this user?", 
                yesText: "Quarantine", cancelText: "Cancel");
            
            if (result == true)
            {
                try {
                    await UserService.QuarantineMediaAsync(UserId);
                    Snackbar.Add("User media quarantined successfully.", Severity.Success);
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Error quarantining media: {ex.Message}", Severity.Error);
                }
            }
        }

        private async Task LoginAsUser()
        {
            try {
                var accessToken = await UserService.LoginAsUserAsync(UserId, TimeSpan.FromHours(1));
                if (!string.IsNullOrEmpty(accessToken))
                {
                    Snackbar.Add($"Login successful for 1 hour. Access Token retrieved. (Simulation)", Severity.Info);
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error logging in as user: {ex.Message}", Severity.Error);
            }
        }
    }
}
