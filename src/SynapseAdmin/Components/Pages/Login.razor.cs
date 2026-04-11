using Microsoft.AspNetCore.Components;
using SynapseAdmin.Services;
using SynapseAdmin.Models.ViewModels;
using MudBlazor;
using SynapseAdmin.Models;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages
{
    public partial class Login
    {
        [Inject]
        public MatrixAuthenticationStateProvider AuthProvider { get; set; } = null!;

        [Inject] 
        public NavigationManager Navigation { get; set; } = null!;

        [Inject]
        public ISessionBridgeService BridgeService { get; set; } = null!;

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
                var homeserver = loginModel.Homeserver;
                var token = loginModel.Method == LoginMethod.Password 
                    ? AuthProvider.GetAccessToken() 
                    : loginModel.AccessToken;
                var userId = AuthProvider.GetUserId();

                // Use the bridge to hide sensitive data from the URL
                var key = BridgeService.CreateBridge(homeserver, token!, userId!);

                var url = $"/Auth/SignIn?key={Uri.EscapeDataString(key)}&redirectUri={Uri.EscapeDataString("/")}";
                Navigation.NavigateTo(url, forceLoad: true);
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
