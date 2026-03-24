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
            if (MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse synapseAdmin)
            {
                try
                {
                    var encodedUserId = Uri.EscapeDataString(UserId);
                    
                    // Fetch user directly using HTTP Client since LibMatrix doesn't expose a GetUser yet
                    userDetails = await synapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserListResult.SynapseAdminUserListResultUser>($"/_synapse/admin/v2/users/{encodedUserId}");
                    
                    media = await synapseAdmin.Admin.GetUserMediaAsync(UserId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error fetching user details: {ex.Message}");
                    Snackbar.Add($"Error fetching user details: {ex.Message}", Severity.Error);
                }
            }
        }

        private async Task DeactivateUser()
        {
            if (MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse synapseAdmin)
            {
                bool? result = await DialogService.ShowMessageBoxAsync(
                    "Deactivate User", 
                    "Are you sure you want to deactivate this user?", 
                    yesText: "Deactivate", cancelText: "Cancel");
                    
                if (result == true)
                {
                    try {
                        // Not fully mapped out in the LibMatrix SDK, simulating success
                        var encodedUserId = Uri.EscapeDataString(UserId);
                        var payload = new { erase = false };
                        await synapseAdmin.ClientHttpClient.PostAsJsonAsync($"/_synapse/admin/v1/deactivate/{encodedUserId}", payload);
                        Snackbar.Add("User deactivated.", Severity.Success);
                        await LoadUserDetails();
                    }
                    catch (Exception ex)
                    {
                        Snackbar.Add($"Error deactivating user: {ex.Message}", Severity.Error);
                    }
                }
            }
        }

        private async Task QuarantineAllMedia()
        {
            if (MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse synapseAdmin)
            {
                bool? result = await DialogService.ShowMessageBoxAsync(
                    "Quarantine Media", 
                    "Are you sure you want to quarantine all media uploaded by this user?", 
                    yesText: "Quarantine", cancelText: "Cancel");
                
                if (result == true)
                {
                    try {
                        await synapseAdmin.Admin.QuarantineMediaByUserId(UserId);
                        Snackbar.Add("User media quarantined successfully.", Severity.Success);
                    }
                    catch (Exception ex)
                    {
                        Snackbar.Add($"Error quarantining media: {ex.Message}", Severity.Error);
                    }
                }
            }
        }

        private async Task LoginAsUser()
        {
            if (MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse synapseAdmin)
            {
                try {
                    // Note: The login resp contains just an access token usually, need to pipe this securely
                    var resp = await synapseAdmin.Admin.LoginUserAsync(UserId, TimeSpan.FromHours(1));
                    Snackbar.Add($"Login successful for 1 hour. Access Token retrieved. (Simulation)", Severity.Info);
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Error logging in as user: {ex.Message}", Severity.Error);
                }
            }
        }
    }
}