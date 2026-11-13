using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Controllers;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Issue #370: conditional media requests (If-None-Match) must only be answered with 304 after the session is
/// authorized and the media is confirmed to still exist; protected media must be revalidated on every use.
/// </summary>
public class MediaControllerConditionalTests
{
    private const string Mxc = "mxc://example.com/media123";
    private const string ExpectedCacheControl = "private, no-cache";

    public enum Endpoint { Download, Preview, Avatar }

    private sealed class TrackingStream(byte[] data) : MemoryStream(data)
    {
        public bool IsDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ConfigurableMediaService : IMediaService
    {
        public Func<OperationResult<MediaDownload>> StreamResult { get; set; } =
            () => OperationResult<MediaDownload>.Ok(new MediaDownload
            {
                Status = MediaDownloadStatus.Full,
                Content = new TrackingStream([1, 2, 3, 4])
            });

        public List<TrackingStream> OpenedStreams { get; } = [];
        public int StreamFetchCount { get; private set; }
        public int MetadataFetchCount { get; private set; }

        public Task<OperationResult<MediaDownload>> OpenMediaAsync(string mxc, MediaByteRange? range = null, CancellationToken token = default)
        {
            StreamFetchCount++;
            var result = StreamResult();
            if (result.Data?.Content is TrackingStream tracking) OpenedStreams.Add(tracking);
            return Task.FromResult(result);
        }

        public Task<OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>> GetMediaMetadataAsync(string mxc, CancellationToken token = default)
        {
            MetadataFetchCount++;
            return Task.FromResult(OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Failure("no metadata"));
        }

        public Task<OperationResult> QuarantineMediaAsync(string mxc, CancellationToken token = default) => throw new NotImplementedException();
        public Task<OperationResult> UnquarantineMediaAsync(string mxc, CancellationToken token = default) => throw new NotImplementedException();
        public Task<OperationResult> DeleteMediaAsync(string mxc, CancellationToken token = default) => throw new NotImplementedException();
        public Task<OperationResult> ProtectMediaAsync(string mxc, CancellationToken token = default) => throw new NotImplementedException();
        public Task<OperationResult> UnprotectMediaAsync(string mxc, CancellationToken token = default) => throw new NotImplementedException();
        public Task<OperationResult<SynapseAdminPurgeMediaCacheResponse>> PurgeRemoteMediaCacheAsync(long beforeTs, CancellationToken token = default) => throw new NotImplementedException();
        public Task<OperationResult<SynapseAdminDeleteMediaResponse>> DeleteLocalMediaAsync(long beforeTs, long sizeGt, bool keepProfiles, CancellationToken token = default) => throw new NotImplementedException();
    }

    private sealed class ConfigurableSessionService : IMatrixSessionService
    {
        public bool RestoreSucceeds { get; set; } = true;
        public IMatrixGateway? Gateway => null;
        public bool IsLoggedIn => RestoreSucceeds;
        public Task<OperationResult> LoginAsync(string homeserver, string username, string password) => throw new NotImplementedException();
        public Task<OperationResult> LoginWithTokenAsync(string homeserver, string accessToken) => throw new NotImplementedException();
        public Task<OperationResult> RestoreSessionAsync(string homeserver, string accessToken, bool force = false) =>
            Task.FromResult(RestoreSucceeds ? OperationResult.Ok() : OperationResult.Failure("revoked"));
        public Task<string?> GetServerVersionAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public void Logout() { }
    }

    private readonly ConfigurableMediaService _media = new();
    private readonly ConfigurableSessionService _session = new();

    private MediaController CreateController(string? ifNoneMatch, bool withSessionClaims = true)
    {
        Claim[] claims = withSessionClaims
            ? [new(ClaimTypes.NameIdentifier, "@admin:example.com"), new("Homeserver", "https://example.com"), new("AccessToken", "syt_token")]
            : [new(ClaimTypes.NameIdentifier, "@admin:example.com")];

        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };
        if (ifNoneMatch != null) httpContext.Request.Headers.IfNoneMatch = ifNoneMatch;

        return new MediaController(_media, _session, NullLogger<MediaController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static Task<IActionResult> Invoke(MediaController controller, Endpoint endpoint) => endpoint switch
    {
        Endpoint.Download => controller.Download(Mxc, "photo.png", "image/png"),
        Endpoint.Preview => controller.Preview(Mxc, "image/png"),
        _ => controller.Avatar(Mxc)
    };

    /// <summary>The ETag a browser holds from an earlier successful response.</summary>
    private async Task<string> GetCachedETag()
    {
        var file = Assert.IsType<FileStreamResult>(await CreateController(null).Download(Mxc, "photo.png", "image/png"));
        return file.EntityTag!.Tag.ToString();
    }

    private static int StatusOf(IActionResult result) => result switch
    {
        StatusCodeResult s => s.StatusCode,
        ObjectResult o => o.StatusCode ?? 200,
        FileResult => 200,
        _ => throw new InvalidOperationException(result.GetType().Name)
    };

    public static TheoryData<Endpoint> Endpoints => new() { Endpoint.Download, Endpoint.Preview, Endpoint.Avatar };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task MatchingETag_ForDeletedMedia_Returns404(Endpoint endpoint)
    {
        var etag = await GetCachedETag();
        _media.StreamResult = () => OperationResult<MediaDownload>.Failure("not found");

        var result = await Invoke(CreateController(etag), endpoint);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task MatchingETag_ForQuarantinedMedia_Returns404(Endpoint endpoint)
    {
        var etag = await GetCachedETag();
        // Synapse refuses to serve quarantined media; MediaService surfaces that as a failed stream result.
        _media.StreamResult = () => OperationResult<MediaDownload>.Failure("Media is quarantined", MudBlazor.Severity.Warning);

        var result = await Invoke(CreateController(etag), endpoint);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task WildcardValidator_ForMissingMedia_Returns404(Endpoint endpoint)
    {
        _media.StreamResult = () => OperationResult<MediaDownload>.Failure("not found");

        var result = await Invoke(CreateController("*"), endpoint);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task MatchingETag_WithRevokedSession_Returns401WithoutFetchingMedia(Endpoint endpoint)
    {
        var etag = await GetCachedETag();
        var fetchesBefore = _media.StreamFetchCount;
        _session.RestoreSucceeds = false;

        var result = await Invoke(CreateController(etag), endpoint);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusOf(result));
        Assert.Equal(fetchesBefore, _media.StreamFetchCount);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task MatchingETag_WithoutSessionClaims_Returns401(Endpoint endpoint)
    {
        var etag = await GetCachedETag();

        var result = await Invoke(CreateController(etag, withSessionClaims: false), endpoint);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusOf(result));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task MatchingETag_ForExistingMedia_Returns304_AndReleasesTheUpstreamStream(Endpoint endpoint)
    {
        var etag = await GetCachedETag();
        var metadataBefore = _media.MetadataFetchCount;
        var controller = CreateController(etag);

        var result = await Invoke(controller, endpoint);

        Assert.Equal(StatusCodes.Status304NotModified, StatusOf(result));
        Assert.Equal(etag, controller.Response.Headers.ETag.ToString());
        Assert.Equal(ExpectedCacheControl, controller.Response.Headers.CacheControl.ToString());
        // Existence was established by opening the stream, which is released unread.
        Assert.True(_media.OpenedStreams[^1].IsDisposed);
        // No extra metadata round trip on revalidation (Avatar requests carry no MIME type).
        Assert.Equal(metadataBefore, _media.MetadataFetchCount);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task WildcardValidator_ForExistingMedia_Returns304(Endpoint endpoint)
    {
        var result = await Invoke(CreateController("*"), endpoint);

        Assert.Equal(StatusCodes.Status304NotModified, StatusOf(result));
        Assert.True(_media.OpenedStreams[^1].IsDisposed);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task FullResponse_RequiresRevalidation(Endpoint endpoint)
    {
        var controller = CreateController(null);

        var result = await Invoke(controller, endpoint);

        Assert.IsType<FileStreamResult>(result);
        Assert.Equal(ExpectedCacheControl, controller.Response.Headers.CacheControl.ToString());
    }
}
