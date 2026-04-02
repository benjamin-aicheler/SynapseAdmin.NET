using Microsoft.AspNetCore.Components;
using SynapseAdmin.Services;
using SynapseAdmin.Models.ViewModels;
using MudBlazor;

namespace SynapseAdmin.Components.Pages
{
    public partial class Login
    {
        [Inject]
        public MatrixAuthenticationStateProvider AuthProvider { get; set; } = null!;

        [Inject] 
        public NavigationManager Navigation { get; set; } = null!;

        private LoginViewModel loginModel = new();
        private string? errorMessage;
        private Severity errorSeverity = Severity.Error;
        private bool isSubmitting;
        private string usernamePlaceholder = "@user:matrix.org";

        private async Task HandleLogin()
        {
            errorMessage = null;
            isSubmitting = true;

            var result = await AuthProvider.LoginAsync(loginModel.Homeserver, loginModel.Username, loginModel.Password);
            
            if (result.Success)
            {
                Navigation.NavigateTo("/");
            }
            else
            {
                errorMessage = result.Message;
                errorSeverity = result.Severity;
            }

            isSubmitting = false;
        }
    }
}
