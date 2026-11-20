using MudBlazor;

namespace SynapseAdmin.Components.Shared;

/// <summary>
/// The dialog option combinations the pages use. Every call returns a new instance, so dialogs never share options.
/// </summary>
public static class DialogPresets
{
    /// <summary>Small and full width; Escape closes the dialog.</summary>
    public static DialogOptions Small() => new() { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };

    /// <summary>Medium and full width; Escape closes the dialog.</summary>
    public static DialogOptions Medium() => new() { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true };

    /// <summary>Small and full width with a close button; Escape is left at the provider default (off).</summary>
    public static DialogOptions SmallWithCloseButton() => new() { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true };

    /// <summary>Medium and full width with a close button; Escape is left at the provider default (off).</summary>
    public static DialogOptions MediumWithCloseButton() => new() { CloseButton = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
}
