using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages
{
    public partial class RegistrationTokenDialog
    {
        [Inject] 
        public IDialogService DialogService { get; set; } = null!;
        [Inject] 
        public ISnackbar Snackbar { get; set; } = null!;

        [CascadingParameter] 
        IMudDialogInstance MudDialog { get; set; } = null!;

        [Parameter]
        public bool IsEdit { get; set; } = false;

        [Parameter]
        public string? ExistingToken { get; set; }

        [Parameter]
        public int? ExistingUsesAllowed { get; set; }

        [Parameter]
        public DateTime? ExistingExpiryDate { get; set; }

        private MudForm? form;
        private bool success = true;
        
        private string? token;
        private int? usesAllowed;
        private DateTime? expiryDate;

        protected override void OnInitialized()
        {
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
                ExpiryTime = expiryDate.HasValue ? ((DateTimeOffset)expiryDate.Value).ToUnixTimeMilliseconds() : null
            };
            MudDialog.Close(DialogResult.Ok(viewModel));
        }
    }
}
