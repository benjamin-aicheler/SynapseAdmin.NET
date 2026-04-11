using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace SynapseAdmin.Components.Pages;

public partial class MediaPreviewDialog
{
    [CascadingParameter] 
    private IMudDialogInstance MudDialog { get; set; } = default!;

    [Parameter] 
    public string Title { get; set; } = string.Empty;

    [Parameter] 
    public string PreviewUrl { get; set; } = string.Empty;

    [Parameter] 
    public string? MediaType { get; set; }

    private void Close() => MudDialog.Close();
}
