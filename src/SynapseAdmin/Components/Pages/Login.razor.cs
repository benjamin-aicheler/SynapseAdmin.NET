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
        public IAuthCookieService AuthCookie { get; set; } = null!;

        [Parameter]
        [SupplyParameterFromQuery]
        public string? Error { get; set; }

        private LoginViewModel loginModel = new();
        private string? errorMessage;
        private Severity errorSeverity = Severity.Error;
        private bool isSubmitting;
        private string usernamePlaceholder = "@user:matrix.org";

        protected override void OnInitialized()
        {
            if (Error == "SessionBridgeFailed")
            {
                errorMessage = L["ErrorSessionBridgeFailed"];
                errorSeverity = Severity.Error;
            }
        }

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
                var username = AuthProvider.GetUsername();

                // Hand the session to the HTTP auth cookie via the browser-bound bridge (no secrets in URLs).
                var cookieResult = await AuthCookie.EstablishCookieSessionAsync(homeserver, token!, userId!, username!);
                if (cookieResult.Success)
                {
                    Navigation.NavigateTo("/", forceLoad: true);
                }
                else if (cookieResult.Severity != Severity.Normal)
                {
                    Navigation.NavigateTo("/login?Error=SessionBridgeFailed", forceLoad: true);
                }
            }
            else if (result.Severity != Severity.Normal)
            {
                errorMessage = result.Message;
                errorSeverity = result.Severity;
            }

            isSubmitting = false;
        }
    }
}
