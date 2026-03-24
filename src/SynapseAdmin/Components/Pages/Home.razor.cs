using LibMatrix.Homeservers;
using Microsoft.AspNetCore.Components;
using SynapseAdmin.Services;

namespace SynapseAdmin.Components.Pages
{
    public partial class Home
    {
        [Inject]
        public MatrixSessionService MatrixSession { get; set; } = null!;

        private bool isSynapse;

        protected override void OnInitialized()
        {
            if (MatrixSession.IsLoggedIn)
            {
                isSynapse = MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse;
            }
        }
    }
}