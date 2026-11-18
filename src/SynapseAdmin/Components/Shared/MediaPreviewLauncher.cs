using MudBlazor;
using SynapseAdmin.Components.Pages;

namespace SynapseAdmin.Components.Shared;

/// <summary>Opens <see cref="MediaPreviewDialog"/> for the room media table, the room gallery and the user media tab.</summary>
public static class MediaPreviewLauncher
{
    /// <summary>The <c>/Media/Preview</c> URL of <paramref name="mxc"/>, with both query values escaped.</summary>
    public static string BuildPreviewUrl(string mxc, string? mediaType) =>
        $"/Media/Preview?mxc={Uri.EscapeDataString(mxc)}&mimeType={Uri.EscapeDataString(mediaType ?? "")}";

    /// <summary>
    /// Shows the preview of <paramref name="mxc"/>. The dialog title is the upload name or <paramref name="fallbackTitle"/>,
    /// and the dialog's Title parameter is the upload name or <paramref name="mediaId"/>.
    /// </summary>
    public static Task<IDialogReference> ShowMediaPreviewAsync(
        this IDialogService dialogService, string mxc, string? uploadName, string mediaId, string? mediaType, string fallbackTitle)
    {
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
        var parameters = new DialogParameters
        {
            { "Title", uploadName ?? mediaId },
            { "PreviewUrl", BuildPreviewUrl(mxc, mediaType) },
            { "MediaType", mediaType }
        };

        return dialogService.ShowAsync<MediaPreviewDialog>(uploadName ?? fallbackTitle, parameters, options);
    }
}
