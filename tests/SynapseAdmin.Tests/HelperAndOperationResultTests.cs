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
