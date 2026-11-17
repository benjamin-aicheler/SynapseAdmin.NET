using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Infrastructure.Helpers;
using SynapseAdmin.Models;
using Xunit;

namespace SynapseAdmin.Tests;

public class HelperAndOperationResultTests
{
    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("text/html; charset=utf-8", ".html")]
    [InlineData("IMAGE/JPEG", ".jpg")]
    [InlineData("application/octet-stream", ".bin")]
    [InlineData(null, ".bin")]
    [InlineData("", ".bin")]
    public void GetExtensionFromMediaType_HandlesParametersCaseAndFallbacks(string? contentType, string expectedExtension)
    {
        Assert.Equal(expectedExtension, MediaHelper.GetExtensionFromMediaType(contentType));
    }

    [Fact]
    public void MediaHelper_MxcParsingAndFormatting_WorksCorrectly()
    {
        Assert.Equal("abc123xyz", MediaHelper.GetMediaIdFromMxc("mxc://example.com/abc123xyz"));
        Assert.Equal("rawId", MediaHelper.GetMediaIdFromMxc("rawId"));
        Assert.Equal("media", MediaHelper.GetMediaIdFromMxc(null));

        Assert.Equal("mxc://example.com/abc123xyz", MediaHelper.ToMxcUri("abc123xyz", "example.com"));
        Assert.Equal("mxc://example.com/abc123xyz", MediaHelper.ToMxcUri("mxc://example.com/abc123xyz", "other.com"));
        Assert.Equal(string.Empty, MediaHelper.ToMxcUri(null, "example.com"));
    }

    private static readonly string SoftHyphen = ((char)0xAD).ToString();

    public static TheoryData<string?, bool, bool, bool> TopLevelTypeCases => new()
    {
        // media type, then whether it has the top-level type "image" under Ordinal, OrdinalIgnoreCase and CurrentCulture
        { null, false, false, false },
        { "", false, false, false },
        { "image/png", true, true, true },
        { "IMAGE/PNG", false, true, false },
        { SoftHyphen + "image/png", false, false, true }, // a culture-aware comparison ignores the soft hyphen
        { "image", false, false, false },
        { "video/mp4", false, false, false },
    };

    [Theory]
    [MemberData(nameof(TopLevelTypeCases))]
    public void HasTopLevelType_UsesTheGivenComparison(string? mediaType, bool ordinal, bool ignoreCase, bool currentCulture) =>
        Assert.Equal(
            (ordinal, ignoreCase, currentCulture),
            (MediaHelper.HasTopLevelType(mediaType, "image", StringComparison.Ordinal),
             MediaHelper.HasTopLevelType(mediaType, "image", StringComparison.OrdinalIgnoreCase),
             MediaHelper.HasTopLevelType(mediaType, "image", StringComparison.CurrentCulture)));

    [Theory]
    [InlineData("image/png", true, false, false)]
    [InlineData("video/mp4", false, true, false)]
    [InlineData("audio/ogg", false, false, true)]
    [InlineData("text/plain", false, false, false)]
    public void IsImageVideoAudio_CheckTheirOwnTopLevelType(string mediaType, bool image, bool video, bool audio) =>
        Assert.Equal(
            (image, video, audio),
            (MediaHelper.IsImage(mediaType, StringComparison.Ordinal),
             MediaHelper.IsVideo(mediaType, StringComparison.Ordinal),
             MediaHelper.IsAudio(mediaType, StringComparison.Ordinal)));

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1572864L, "1.5 MB")]
    [InlineData(1073741824L, "1 GB")]
    public void FormatSize_FormatsBytesHumanReadably(long bytes, string expected)
    {
        var currentCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            Assert.Equal(expected, bytes.FormatSize());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = currentCulture;
        }
    }

    [Fact]
    public void OperationResult_CancelledUsesNormalSeverity_ForSilentUiSuppression()
    {
        var nonGenericCancelled = OperationResult.Cancelled();
        Assert.False(nonGenericCancelled.Success);
        Assert.Equal(Severity.Normal, nonGenericCancelled.Severity);

        var genericCancelled = OperationResult<string>.Cancelled();
        Assert.False(genericCancelled.Success);
        Assert.Null(genericCancelled.Data);
        Assert.Equal(Severity.Normal, genericCancelled.Severity);

        var ok = OperationResult<int>.Ok(42, "Done");
        Assert.True(ok.Success);
        Assert.Equal(42, ok.Data);
        Assert.Equal(Severity.Success, ok.Severity);
    }
}
