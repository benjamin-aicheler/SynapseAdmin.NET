using System.Reflection;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Components.Shared;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// How the room and user media tables and the room gallery classify media types. The tables compare culture-aware
/// and case-sensitively, so an ignorable soft hyphen in front still counts; the gallery compares ordinally, ignoring
/// case. A missing type is previewable in the room table only. The preview dialog is covered by
/// <c>Components/MediaPreviewDialogTests</c>, the preview sandbox by <c>MediaControllerConditionalTests</c>.
/// </summary>
public class MediaTypeClassificationTests
{
    private static readonly string SoftHyphen = ((char)0xAD).ToString();

    public static TheoryData<string?, bool, bool> PreviewableCases => new()
    {
        // media type, RoomDetails, UserDetails
        { null, true, false },
        { "", true, false },
        { "image/png", true, true },
        { "video/mp4", true, true },
        { "audio/ogg", true, true },
        { "application/pdf", false, false },
        { "IMAGE/PNG", false, false },
        { SoftHyphen + "image/png", true, true },
    };

    [Theory]
    [MemberData(nameof(PreviewableCases))]
    public void MediaTables_PreviewImageVideoAndAudio(string? mediaType, bool room, bool user)
    {
        Assert.Equal(room, Classify(typeof(RoomDetails), new RoomDetails(), "IsPreviewable", mediaType));
        Assert.Equal(user, Classify(typeof(UserDetails), new UserDetails(), "IsPreviewable", mediaType));
    }

    public static TheoryData<string?, string?> GalleryCases => new()
    {
        // media type, the gallery check that matches it (none for files)
        { null, null },
        { "", null },
        { "image/png", "IsImageType" },
        { "IMAGE/PNG", "IsImageType" },
        { "Video/MP4", "IsVideoType" },
        { "audio/ogg", "IsAudioType" },
        { "application/pdf", null },
        { SoftHyphen + "image/png", null },
    };

    [Theory]
    [MemberData(nameof(GalleryCases))]
    public void Gallery_ClassifiesIgnoringCase(string? mediaType, string? match)
    {
        string[] checks = ["IsImageType", "IsVideoType", "IsAudioType"];
        Assert.Equal(match is null ? [] : [match], checks.Where(check => Classify(typeof(RoomGalleryGrid), null, check, mediaType)));
    }

    private static bool Classify(Type component, object? instance, string method, string? mediaType) =>
        (bool)component.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!
            .Invoke(instance, [mediaType])!;
}
