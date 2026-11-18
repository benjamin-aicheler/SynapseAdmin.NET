using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The preview dialog that the preview buttons of the room media table and the room gallery open. The dialog title
/// falls back to "MediaPreview", the Title parameter to the media ID, and the preview URL escapes both query values.
/// </summary>
public class MediaPreviewLaunchTests : DetailPageTestBase
{
    private const string RoomId = "!a:hs";
    private RoomMediaItemViewModel _item = new();

    public MediaPreviewLaunchTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(s => s
            .On(nameof(IRoomService.GetRoomDetailsAsync), _ => Task.FromResult(OperationResult<RoomDetailViewModel>.Ok(new RoomDetailViewModel
            {
                RoomId = RoomId,
                Name = "Alpha Lounge",
                Media = new RoomMediaViewModel { Local = [new() { MediaId = _item.MediaId }] }
            })))
            .On(nameof(IRoomService.GetRoomMessagesAsync), _ =>
                Task.FromResult(OperationResult<RoomMessagesViewModel>.Ok(new RoomMessagesViewModel())))
            .On(nameof(IRoomService.GetMediaMetadataBatchAsync), _ =>
                Task.FromResult(OperationResult<List<RoomMediaItemViewModel>>.Ok([_item])))));
    }

    public static TheoryData<string, string?, string?, string> Items => new()
    {
        // media ID, upload name, media type, preview URL
        { "mxc://hs/a+b", "cat & dog.svg", "image/svg+xml", "/Media/Preview?mxc=mxc%3A%2F%2Fhs%2Fa%2Bb&mimeType=image%2Fsvg%2Bxml" },
        { "mxc://hs/plain", null, null, "/Media/Preview?mxc=mxc%3A%2F%2Fhs%2Fplain&mimeType=" },
    };

    [Theory]
    [MemberData(nameof(Items))]
    public void RoomMediaTable_OpensThePreview(string mediaId, string? uploadName, string? mediaType, string previewUrl)
    {
        _item = new RoomMediaItemViewModel { MediaId = mediaId, UploadName = uploadName, MediaType = mediaType };
        var cut = Render<RoomDetails>(p => p.Add(x => x.RoomId, RoomId));
        cut.WaitForAssertion(() => Assert.Contains("Alpha Lounge", cut.Markup));
        cut.FindAll(".mud-tab").First(t => t.TextContent.Trim() == Text("Media")).Click();

        cut.WaitForElement($"button[title='{Text("Preview")}']").Click();

        cut.WaitForAssertion(() => AssertPreviewShown(uploadName ?? Text("MediaPreview"), uploadName ?? mediaId, previewUrl, mediaType));
    }

    [Theory]
    [MemberData(nameof(Items))]
    public void Gallery_OpensThePreview(string mediaId, string? uploadName, string? mediaType, string previewUrl)
    {
        _item = new RoomMediaItemViewModel { MediaId = mediaId, UploadName = uploadName, MediaType = mediaType };
        var cut = Render<RoomGalleryGrid>(p => p
            .Add(x => x.RoomId, RoomId)
            .Add(x => x.Media, new RoomMediaViewModel { Local = [new() { MediaId = mediaId }] }));

        cut.WaitForElement($"button[title='{Text("Preview")}']").Click();

        cut.WaitForAssertion(() => AssertPreviewShown(uploadName ?? Text("MediaPreview"), uploadName ?? mediaId, previewUrl, mediaType));
    }

    [Theory]
    [InlineData("mxc://hs/a+b c", "image/svg+xml", "/Media/Preview?mxc=mxc%3A%2F%2Fhs%2Fa%2Bb%20c&mimeType=image%2Fsvg%2Bxml")]
    [InlineData("mxc://hs/x?y&z=1#f", null, "/Media/Preview?mxc=mxc%3A%2F%2Fhs%2Fx%3Fy%26z%3D1%23f&mimeType=")]
    public void BuildPreviewUrl_EscapesBothQueryValues(string mxc, string? mediaType, string expected) =>
        Assert.Equal(expected, MediaPreviewLauncher.BuildPreviewUrl(mxc, mediaType));

    private string Text(string key) => Services.GetRequiredService<IStringLocalizer<SharedResources>>()[key].Value;
}
