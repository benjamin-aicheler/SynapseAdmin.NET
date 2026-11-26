using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;

namespace SynapseAdmin.Components.Pages;

/// <summary>
/// Asks for a device's new name, prefilled with the current one, and returns it as a <see cref="string"/>. The name is
/// required and has at most <see cref="UserDeviceService.MaxDeviceDisplayNameLength"/> characters. The devices panel
/// re-checks the shown user and sends the rename (#598).
/// </summary>
public partial class RenameDeviceDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    /// <summary>The user shown, as the page names it in its confirmations.</summary>
    [Parameter] public string Target { get; set; } = string.Empty;

    [Parameter] public string DeviceId { get; set; } = string.Empty;

    [Parameter] public string? CurrentName { get; set; }

    private static int MaxLength => UserDeviceService.MaxDeviceDisplayNameLength;

    private string? displayName;

    protected override void OnInitialized() => displayName = CurrentName ?? string.Empty;

    // Validity is computed here rather than read from a MudForm, which reports a prefilled field as invalid until it
    // is validated (AGENTS.md bUnit pitfalls).
    private bool CanSave =>
        ValidateName(displayName) == null && !string.Equals(displayName, CurrentName, StringComparison.Ordinal);

    private string? ValidateName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return L["DeviceNameRequired"].Value;
        if (UserDeviceService.IsTooLong(value)) return L["DeviceNameTooLong", UserDeviceService.MaxDeviceDisplayNameLength].Value;
        return null;
    }

    private void Submit()
    {
        if (!CanSave) return;
        MudDialog.Close(DialogResult.Ok(displayName!));
    }

    private void Cancel() => MudDialog.Cancel();
}
