using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages;

/// <summary>
/// Asks for a new password and whether to log out all of the user's devices (off by default), and returns them as a
/// <see cref="UserPasswordResetViewModel"/>. The user page re-checks the shown user and sends the reset (#584).
/// </summary>
public partial class ResetPasswordDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    /// <summary>The user shown, as the page names it in its confirmations.</summary>
    [Parameter] public string Target { get; set; } = string.Empty;

    /// <summary>Whether the user is the signed-in admin, whose own session Synapse keeps when it logs devices out.</summary>
    [Parameter] public bool IsOwnAccount { get; set; }

    private readonly UserPasswordResetViewModel model = new();
    private MudForm form = null!;
    private bool success;

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
