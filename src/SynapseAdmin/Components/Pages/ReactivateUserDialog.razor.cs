using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages;

/// <summary>
/// Asks for an optional new password for a deactivated account and returns it as a <see cref="UserReactivateViewModel"/>.
/// Synapse doesn't check whether reactivation needs a password, so the dialog doesn't require one, and says what
/// happens without one (#584). The user page re-checks the shown user and sends the reactivation.
/// </summary>
public partial class ReactivateUserDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    /// <summary>The user shown, as the page names it in its confirmations.</summary>
    [Parameter] public string Target { get; set; } = string.Empty;

    private readonly UserReactivateViewModel model = new();
    private MudForm form = null!;
    private bool success = true;

    private string? ValidatePassword(string? value) =>
        string.IsNullOrEmpty(value) || value.Length >= UserPasswordResetViewModel.MinPasswordLength ? null : L["PasswordTooShort"].Value;

    private async Task Submit()
    {
        await form.ValidateAsync();
        if (form.IsValid)
        {
            MudDialog.Close(DialogResult.Ok(model));
        }
    }

    private void Cancel() => MudDialog.Cancel();
}
