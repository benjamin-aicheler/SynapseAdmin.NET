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

        /// <summary>The longest custom token Synapse accepts.</summary>
        private const int MaxTokenLength = 64;

        /// <summary>The characters besides ASCII letters and digits that Synapse accepts in a custom token.</summary>
        private const string TokenPunctuation = "._~-";

        private MudForm form = null!;
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

        /// <summary>
        /// Synapse's rule for a custom token: at most 64 characters from <c>[A-Za-z0-9._~-]</c>. A blank field is valid
        /// and lets the homeserver generate one (#548).
        /// </summary>
        private string? ValidateToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (value.Length > MaxTokenLength) return L["TokenTooLong", MaxTokenLength].Value;
            return value.All(c => char.IsAsciiLetterOrDigit(c) || TokenPunctuation.Contains(c)) ? null : L["TokenInvalidCharacters"].Value;
        }

        private async Task Submit()
        {
            // The Save button follows the form's validity, but a submit must not depend on it.
            await form.ValidateAsync();
            if (!form.IsValid) return;

            CloseWithViewModel();
        }

        /// <summary>Closes the dialog with the entered values. Tests call it directly for the expiry conversion.</summary>
        private void CloseWithViewModel()
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
