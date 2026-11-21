using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages
{
    public partial class RegistrationTokenDialog
    {
        [CascadingParameter]
        private IMudDialogInstance MudDialog { get; set; } = null!;

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

        /// <summary>
        /// The existing expiry as stored (Unix ms). Saved unchanged while the date is untouched, because converting
        /// <see cref="ExistingExpiryDate"/> back can't tell the two occurrences of an ambiguous wall-clock time apart (#520).
        /// </summary>
        [Parameter]
        public long? ExistingExpiryTime { get; set; }

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

        private long? GetExpiryTime()
        {
            if (!expiryDate.HasValue) return null;
            // An untouched expiry keeps its stored instant, also the second occurrence of an ambiguous time (#520).
            if (IsEdit && ExistingExpiryTime.HasValue && expiryDate == ExistingExpiryDate) return ExistingExpiryTime;
            // The picked date is wall-clock time in the user's time zone, not the server's (#393).
            return TimeZoneService.ToDateTimeOffset(expiryDate.Value).ToUnixTimeMilliseconds();
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
                ExpiryTime = GetExpiryTime()
            };
            MudDialog.Close(DialogResult.Ok(viewModel));
        }
    }
}
