using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages;

/// <summary>
/// Edits the display name and offers to remove the avatar, and returns only what the admin changed as a
/// <see cref="UserProfileEditViewModel"/>. The app has no upload path, so an avatar can only be removed. The user page
/// re-checks the shown user and sends the edit (#584).
/// </summary>
public partial class UserProfileDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    /// <summary>The user shown, as the page names it in its confirmations.</summary>
    [Parameter] public string Target { get; set; } = string.Empty;

    [Parameter] public string? CurrentDisplayName { get; set; }

    /// <summary>Whether the user has an avatar; "Remove avatar" is offered only then.</summary>
    [Parameter] public bool HasAvatar { get; set; }

    private string? displayName;
    private bool removeAvatar;

    protected override void OnInitialized() => displayName = CurrentDisplayName ?? string.Empty;

    // No display name and an empty one are the same to the admin.
    private bool DisplayNameChanged => !string.Equals(displayName ?? string.Empty, CurrentDisplayName ?? string.Empty, StringComparison.Ordinal);

    private bool HasChanges => DisplayNameChanged || removeAvatar;

    private void Submit()
    {
        if (!HasChanges) return;

        MudDialog.Close(DialogResult.Ok(new UserProfileEditViewModel
        {
            DisplayName = DisplayNameChanged ? displayName ?? string.Empty : null,
            RemoveAvatar = removeAvatar
        }));
    }

    private void Cancel() => MudDialog.Cancel();
}
