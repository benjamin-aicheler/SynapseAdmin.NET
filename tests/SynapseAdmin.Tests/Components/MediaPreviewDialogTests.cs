using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SynapseAdmin.Components.Pages;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Which element the preview dialog shows for a media type. The prefix checks are culture-aware and case-sensitive
/// (<see cref="string.StartsWith(string)"/>): an ignorable soft hyphen in front still counts, upper case doesn't.
/// </summary>
public sealed class MediaPreviewDialogTests : BunitContext, IAsyncLifetime
{
    private static readonly string SoftHyphen = ((char)0xAD).ToString();

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    public MediaPreviewDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddLocalization();
    }

    public static TheoryData<string?, string> Cases => new()
    {
        { "image/png", "img" },
        { "video/mp4", "video" },
        { "audio/ogg", "audio" },
        { "application/pdf", ".mud-alert" },
        { null, ".mud-alert" },
        { "IMAGE/PNG", ".mud-alert" },
        { SoftHyphen + "image/png", "img" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ShowsTheElementForTheMediaType(string? mediaType, string expected)
    {
        var provider = Render<MudDialogProvider>();
        var parameters = new DialogParameters<MediaPreviewDialog>
        {
            { x => x.Title, "Preview" },
            { x => x.PreviewUrl, "/Media/Preview?mxc=x" },
            { x => x.MediaType, mediaType },
        };

        await provider.InvokeAsync(() => Services.GetRequiredService<IDialogService>().ShowAsync<MediaPreviewDialog>("Preview", parameters));

        var content = provider.WaitForElement(".mud-dialog-content");
        string[] candidates = ["img", "video", "audio", ".mud-alert"];
        Assert.Equal([expected], candidates.Where(selector => content.QuerySelector(selector) != null));
    }
}
