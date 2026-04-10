using Microsoft.AspNetCore.Components;
using SynapseAdmin.Services;
using SynapseAdmin.Models.ViewModels;
using MudBlazor;
using SynapseAdmin.Models;

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

        private int selectedTabIndex
        {
            get => (int)loginModel.Method;
            set => loginModel.Method = (LoginMethod)value;
        }

        private async Task HandleLogin()
        {
            errorMessage = null;
            isSubmitting = true;

            OperationResult result;
            if (loginModel.Method == LoginMethod.Password)
            {
                result = await AuthProvider.LoginAsync(loginModel.Homeserver, loginModel.Username, loginModel.Password);
            }
            else
            {
                result = await AuthProvider.LoginWithTokenAsync(loginModel.Homeserver, loginModel.AccessToken);
            }
            
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
