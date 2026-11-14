using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages
{
    public partial class RegistrationTokenDialog
    {
        [CascadingParameter] 
        IMudDialogInstance MudDialog { get; set; } = null!;

        [Parameter]
        public bool IsEdit { get; set; } = false;

        [Parameter]
        public string? ExistingToken { get; set; }

        [Parameter]
        public int? ExistingUsesAllowed { get; set; }

        [Inject]
        public ITimeZoneService TimeZoneService { get; set; } = null!;

        /// <summary>Existing expiry as wall-clock time in the user's time zone (see <see cref="ITimeZoneService"/>).</summary>
        [Parameter]
        public DateTime? ExistingExpiryDate { get; set; }

        private bool success = true;
        
        private string? token;
        private int? usesAllowed;
        private DateTime? expiryDate;

        protected override async Task OnInitializedAsync()
        {
            await TimeZoneService.InitializeAsync();
            if (IsEdit)
            {
                token = ExistingToken;
                usesAllowed = ExistingUsesAllowed;
                expiryDate = ExistingExpiryDate;
            }
        }

        private void Cancel()
        {
            MudDialog.Cancel();
        }

        private void Submit()
        {
            var viewModel = new RegistrationTokenViewModel
            {
                Token = string.IsNullOrWhiteSpace(token) ? string.Empty : token,
                UsesAllowed = usesAllowed,
                // The picked date is wall-clock time in the user's time zone, not the server's (#393).
                ExpiryTime = expiryDate.HasValue ? TimeZoneService.ToDateTimeOffset(expiryDate.Value).ToUnixTimeMilliseconds() : null
            };
            MudDialog.Close(DialogResult.Ok(viewModel));
        }
    }
}
