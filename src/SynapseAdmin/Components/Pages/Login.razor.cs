using Microsoft.AspNetCore.Components;
using SynapseAdmin.Services;

namespace SynapseAdmin.Components.Pages
{
    public partial class Login
    {
        [Inject]
        public MatrixAuthenticationStateProvider AuthProvider { get; set; } = null!;

        [Inject] 
        public NavigationManager Navigation { get; set; } = null!;

        private LoginRequest loginModel = new();
        private string? errorMessage;
        private bool isSubmitting;
        private string usernamePlaceholder = "@user:matrix.org";

        private async Task HandleLogin()
        {
            errorMessage = null;
            isSubmitting = true;

            try
            {
                await AuthProvider.LoginAsync(loginModel.Homeserver, loginModel.Username, loginModel.Password);
                Navigation.NavigateTo("/");
            }
            catch (Exception ex)
            {
                errorMessage = $"Login failed: {ex.Message}";
            }
            finally
            {
                isSubmitting = false;
            }
        }

        public class LoginRequest
        {
            public string Homeserver { get; set; } = "https://matrix.org";
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
        }
    }
}