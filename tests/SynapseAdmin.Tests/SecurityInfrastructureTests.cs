using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Controllers;
using SynapseAdmin.Extensions;
using SynapseAdmin.Infrastructure.DataProtection;
using SynapseAdmin.Infrastructure.Gateways;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Resources;
using SynapseAdmin.Services;
using Xunit;

namespace SynapseAdmin.Tests;

public class SecurityInfrastructureTests
{
    [Fact]
    public void LoginRateLimiter_BlocksAfterFiveFailedAttempts_PerIpAndIdentifier_AndResets()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var limiter = new LoginRateLimiter(cache);

        const string ip = "192.0.2.10";
        const string identifier = "https://matrix.example.com|@Admin:example.com";

        for (var i = 0; i < 5; i++)
        {
            Assert.True(limiter.IsAllowed(ip, identifier));
            limiter.RecordFailedAttempt(ip, identifier);
        }

        // Blocked by both IP and case-normalized identifier
        Assert.False(limiter.IsAllowed(ip, "https://other.example.com|@user:example.com"));
        Assert.False(limiter.IsAllowed("198.51.100.5", identifier.ToUpperInvariant()));

        // Unrelated IP + identifier remains allowed
        Assert.True(limiter.IsAllowed("198.51.100.5", "https://matrix.example.com|@other:example.com"));

        // Reset clears both buckets
        limiter.ResetAttempts(ip, identifier);
        Assert.True(limiter.IsAllowed(ip, identifier));
    }

    [Fact]
    public async Task MatrixSessionService_ConcurrentLoginBurst_AllowsAtMostFiveAttemptsThroughGateway()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var limiter = new LoginRateLimiter(new MemoryLoginRateLimitStore(cache));
        var authGateway = new BurstFailingAuthGateway();
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.99");
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
        var localizer = new FakeAuthStringLocalizer();

        const int concurrentRequests = 25;

        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => Task.Run(async () =>
            {
                var sessionService = new MatrixSessionService(
                    authGateway,
                    limiter,
                    httpContextAccessor,
                    NullLogger<MatrixSessionService>.Instance,
                    localizer);

                return await sessionService.LoginAsync("https://matrix.example.com", "admin", "wrong-password");
            }))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Exactly 5 attempts may reach the upstream gateway; the remaining 20 must be blocked atomically by TryAcquire
        Assert.Equal(5, authGateway.GatewayCallCount);
        Assert.Equal(5, results.Count(r => !r.Success && r.Message == "LoginFailed"));
        Assert.Equal(concurrentRequests - 5, results.Count(r => !r.Success && r.Message == "TooManyLoginAttempts"));
    }

    [Fact]
    public void SessionBridgeService_ConsumesTokenOnlyOnce()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var bridge = new SessionBridgeService(cache, NullLogger<SessionBridgeService>.Instance);

        var key = bridge.CreateBridge("https://matrix.example.com", "syt_secret_token", "@admin:example.com", "admin");

        Assert.False(string.IsNullOrWhiteSpace(key));
        Assert.True(bridge.TryConsumeBridge(key, out var data));
        Assert.Equal("https://matrix.example.com", data.Homeserver);
        Assert.Equal("syt_secret_token", data.AccessToken);
        Assert.Equal("@admin:example.com", data.UserId);
        Assert.Equal("admin", data.Username);

        // Replay must fail
        Assert.False(bridge.TryConsumeBridge(key, out var replayData));
        Assert.Equal(string.Empty, replayData.AccessToken);
    }

    [Fact]
    public void AesXmlEncryptorAndDecryptor_RoundTripsXml_AndRejectsTampering()
    {
        const string passphrase = "test-passphrase-for-data-protection";
        var originalXml = new XElement("key",
            new XAttribute("id", "1234"),
            new XElement("value", "super-secret-key-material"));

        var encryptor = new AesXmlEncryptor(passphrase);
        var encryptedInfo = encryptor.Encrypt(originalXml);

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["DP_PASSPHRASE"] = passphrase })
                .Build())
            .BuildServiceProvider();

        var decryptor = new AesXmlDecryptor(services);
        var decryptedXml = decryptor.Decrypt(encryptedInfo.EncryptedElement);

        Assert.Equal(
            originalXml.ToString(SaveOptions.DisableFormatting),
            decryptedXml.ToString(SaveOptions.DisableFormatting));

        // Tamper with the authentication tag
        var tampered = new XElement(encryptedInfo.EncryptedElement);
        var tagBytes = Convert.FromBase64String(tampered.Element("tag")!.Value);
        tagBytes[0] ^= 0xFF;
        tampered.Element("tag")!.Value = Convert.ToBase64String(tagBytes);

        Assert.ThrowsAny<CryptographicException>(() => decryptor.Decrypt(tampered));
    }

    [Fact]
    public void SanitizeForLogging_RemovesNewlinesControlChars_AndTruncatesLongInput()
    {
        var malicious = "user1\r\n2026-10-31 [INF] Forged log entry\t\u001b[31mRed";
        var sanitized = malicious.SanitizeForLogging();

        Assert.NotNull(sanitized);
        Assert.DoesNotContain("\r", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", sanitized, StringComparison.Ordinal);
        Assert.Contains("?[31mRed", sanitized, StringComparison.Ordinal);

        var oversized = new string('A', 2000);
        var truncated = oversized.SanitizeForLogging(maxLength: 64);
        Assert.Equal(new string('A', 64) + "...(truncated)", truncated);
    }

    [Fact]
    public async Task MediaController_DownloadAndPreview_SetCacheControlAndETag_AndReturn304OnMatchingIfNoneMatch()
    {
        const string mxc = "mxc://example.com/media123";
        var mediaService = new FakeMediaService();
        var sessionService = new FakeRestorableSessionService();

        var downloadController = CreateAuthenticatedMediaController(mediaService, sessionService);
        var downloadResult = await downloadController.Download(mxc, "photo.png", "image/png");

        var downloadFile = Assert.IsType<FileStreamResult>(downloadResult);
        Assert.Equal("private, max-age=300", downloadController.Response.Headers.CacheControl.ToString());
        Assert.Equal("nosniff", downloadController.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.NotNull(downloadFile.EntityTag);
        Assert.Equal(1, mediaService.StreamFetchCount);

        // Conditional request on Download with matching If-None-Match returns 304 without fetching stream again
        var conditionalDownloadController = CreateAuthenticatedMediaController(mediaService, sessionService);
        conditionalDownloadController.Request.Headers.IfNoneMatch = downloadFile.EntityTag.Tag.ToString();
        var notModifiedResult = await conditionalDownloadController.Download(mxc, "photo.png", "image/png");

        var statusCodeResult = Assert.IsType<StatusCodeResult>(notModifiedResult);
        Assert.Equal(StatusCodes.Status304NotModified, statusCodeResult.StatusCode);
        Assert.Equal("private, max-age=300", conditionalDownloadController.Response.Headers.CacheControl.ToString());
        Assert.Equal(downloadFile.EntityTag.Tag.ToString(), conditionalDownloadController.Response.Headers.ETag.ToString());
        Assert.Equal(1, mediaService.StreamFetchCount);

        // Preview sets identical Cache-Control and ETag
        var previewController = CreateAuthenticatedMediaController(mediaService, sessionService);
        var previewResult = await previewController.Preview(mxc, "image/png");

        var previewFile = Assert.IsType<FileStreamResult>(previewResult);
        Assert.Equal("private, max-age=300", previewController.Response.Headers.CacheControl.ToString());
        Assert.Equal(downloadFile.EntityTag, previewFile.EntityTag);
    }

    [Fact]
    public async Task MatrixAuthGateway_SlowHomeserverCreation_DoesNotBlockDifferentHomeserverKey_AndDeduplicatesSameKey()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var slowStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlowTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var gatewayFactory = new TestableMatrixAuthGateway(cache, async (hsUrl, _, ct) =>
        {
            if (hsUrl.Contains("slow.example.com", StringComparison.OrdinalIgnoreCase))
            {
                slowStartedTcs.TrySetResult();
                await releaseSlowTcs.Task.WaitAsync(ct);
            }

            return new StubMatrixGateway(hsUrl);
        });

        // Start two concurrent requests for the SAME slow key (should deduplicate into 1 creation call)
        var slowTask1 = gatewayFactory.GetAuthenticatedAsync("https://slow.example.com", "token_slow");
        var slowTask2 = gatewayFactory.GetAuthenticatedAsync("https://slow.example.com", "token_slow");

        await slowStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(slowTask1.IsCompleted);

        // While slow.example.com is blocked in-flight, fast.example.com must complete without waiting
        var fastGateway = await gatewayFactory
            .GetAuthenticatedAsync("https://fast.example.com", "token_fast")
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("https://fast.example.com", fastGateway.HomeserverUrl);

        // Release the slow creation and verify both callers receive the exact same deduplicated instance
        releaseSlowTcs.TrySetResult();
        var slowGateways = await Task.WhenAll(slowTask1, slowTask2);

        Assert.Same(slowGateways[0], slowGateways[1]);
        Assert.Equal(1, gatewayFactory.GetCreationCount("https://slow.example.com"));
        Assert.Equal(1, gatewayFactory.GetCreationCount("https://fast.example.com"));
    }

    private static MediaController CreateAuthenticatedMediaController(IMediaService mediaService, IMatrixSessionService sessionService)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "@admin:example.com"),
                new Claim("Homeserver", "https://example.com"),
                new Claim("AccessToken", "syt_token")
            ], "TestAuth"))
        };

        return new MediaController(mediaService, sessionService, NullLogger<MediaController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private sealed class FakeMediaService : IMediaService
    {
        public int StreamFetchCount { get; private set; }

        public Task<OperationResult<Stream>> GetMediaStreamAsync(string mxc, CancellationToken token = default)
        {
            StreamFetchCount++;
            return Task.FromResult(OperationResult<Stream>.Ok(new MemoryStream([1, 2, 3, 4])));
        }

        public Task<OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>> GetMediaMetadataAsync(string mxc, CancellationToken token = default) => throw new NotImplementedException();
        public Task<OperationResult> QuarantineMediaAsync(string mxc, CancellationToken token = default) => throw new NotImplementedException();
        public Task<OperationResult> UnquarantineMediaAsync(string mxc, CancellationToken token = default) => throw new NotImplementedException();
        public Task<OperationResult> DeleteMediaAsync(string mxc, CancellationToken token = default) => throw new NotImplementedException();
        public Task<OperationResult> ProtectMediaAsync(string mxc, CancellationToken token = default) => throw new NotImplementedException();
        public Task<OperationResult> UnprotectMediaAsync(string mxc, CancellationToken token = default) => throw new NotImplementedException();
        public Task<OperationResult<SynapseAdminPurgeMediaCacheResponse>> PurgeRemoteMediaCacheAsync(long beforeTs, CancellationToken token = default) => throw new NotImplementedException();
        public Task<OperationResult<SynapseAdminDeleteMediaResponse>> DeleteLocalMediaAsync(long beforeTs, long sizeGt, bool keepProfiles, CancellationToken token = default) => throw new NotImplementedException();
    }

    private sealed class FakeRestorableSessionService : IMatrixSessionService
    {
        public IMatrixGateway? Gateway => null;
        public bool IsLoggedIn => true;
        public Task<OperationResult> LoginAsync(string homeserver, string username, string password) => throw new NotImplementedException();
        public Task<OperationResult> LoginWithTokenAsync(string homeserver, string accessToken) => throw new NotImplementedException();
        public Task<OperationResult> RestoreSessionAsync(string homeserver, string accessToken, bool force = false) => Task.FromResult(OperationResult.Ok());
        public Task<string?> GetServerVersionAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public void Logout() { }
    }

    private sealed class TestableMatrixAuthGateway(
        IMemoryCache cache,
        Func<string, string, CancellationToken, Task<IMatrixGateway>> factory) : MatrixAuthGateway(null!, cache)
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _creationCounts = new(StringComparer.OrdinalIgnoreCase);

        public int GetCreationCount(string homeserverUrl) =>
            _creationCounts.TryGetValue(homeserverUrl, out var count) ? count : 0;

        protected override Task<IMatrixGateway> CreateAuthenticatedGatewayAsync(string homeserverUrl, string accessToken, CancellationToken cancellationToken)
        {
            _creationCounts.AddOrUpdate(homeserverUrl, 1, (_, current) => current + 1);
            return factory(homeserverUrl, accessToken, cancellationToken);
        }
    }

    private sealed class StubMatrixGateway(string homeserverUrl) : IMatrixGateway
    {
        public string UserId => "@admin:example.com";
        public string Username => "admin";
        public string HomeserverUrl => homeserverUrl;
        public string ServerName => "example.com";
        public string AccessToken => "token";
        public bool SupportsAdminApi => true;
        public string ServerBrand => "Synapse";
        public string ServerVersion => "1.120.0";

        public Task<SynapseAdminUserListResult?> GetUserListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> GetUserDetailsAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeactivateUserAsync(string userId, bool erase, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task QuarantineMediaByUserIdAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LoginResponse> LoginAsUserAsync(string userId, TimeSpan expireIn, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SendServerNoticeResponse?> SendServerNoticeAsync(string userId, object content, string? type = null, string? stateKey = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<UserMediaStatisticsResponse?> GetUserMediaStatisticsAsync(int limit = 10, string orderBy = "media_length", string dir = "b", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> UpdateUserAsync(string userId, object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminUserMembershipsResponse?> GetUserMembershipsAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminUserMediaResult?> GetUserMediaAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminExperimentalFeaturesResponse?> GetUserExperimentalFeaturesAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SetUserExperimentalFeaturesAsync(string userId, SynapseAdminExperimentalFeaturesRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminAccountValidityResponse?> RenewAccountValidityAsync(SynapseAdminAccountValidityRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminRoomListResult?> GetRoomListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom?> GetRoomDetailsAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminRoomMemberListResult?> GetRoomMembersAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminRoomStateResult?> GetRoomStateAsync(string roomId, string? type = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminRoomMediaListResult?> GetRoomMediaListAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteRoomAsync(string roomId, SynapseAdminRoomDeleteRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task QuarantineMediaByRoomIdAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task BlockRoomAsync(string roomId, bool block, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RoomStatisticsResponse?> GetLargestRoomsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminRoomMessagesResponse?> GetRoomMessagesAsync(string roomId, int? limit = null, string? from = null, string? dir = null, string? filter = null, string? to = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminPurgeHistoryResponse?> PurgeRoomHistoryAsync(string roomId, SynapseAdminPurgeHistoryRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminPurgeHistoryStatusResponse?> GetPurgeHistoryStatusAsync(string purgeId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<MatrixPublicRoomDirectoryResult?> GetPublicRoomsAsync(int limit, string? since = null, string? searchTerm = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string?> GetRoomDirectoryVisibilityAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SetRoomDirectoryVisibilityAsync(string roomId, string visibility, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task PutRoomAliasAsync(string roomAlias, string roomId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteRoomAliasAsync(string roomAlias, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<string>?> GetLocalRoomAliasesAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminDestinationListResult?> GetFederationDestinationListAsync(int offset, int limit, string direction, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ResetFederationConnectionTimeoutAsync(string destination, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminEventReportListResult?> GetEventReportListAsync(int offset, int limit, string direction, string? searchTerm = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteEventReportAsync(string reportId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminFetchEventResponse?> FetchEventAsync(string eventId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>> GetRegistrationTokensAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken?> CreateRegistrationTokenAsync(SynapseAdminRegistrationTokenCreateRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateRegistrationTokenAsync(string token, SynapseAdminRegistrationTokenUpdateRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteRegistrationTokenAsync(string token, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseVersionResponse?> GetSynapseVersionAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<byte[]?> DownloadMediaAsync(string mxcUrl, long maxBytes = 5242880, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Stream> GetMediaStreamAsync(string mxcUri, string? filename = null, int? timeout = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string mxcUri, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task QuarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task QuarantineMediaAsync(string mxcUri, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UnquarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UnquarantineMediaAsync(string mxcUri, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteMediaAsync(string mxcUri, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ProtectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ProtectMediaAsync(string mxcUri, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UnprotectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UnprotectMediaAsync(string mxcUri, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminPurgeMediaCacheResponse?> PurgeRemoteMediaCacheAsync(long beforeTs, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminDeleteMediaResponse?> DeleteLocalMediaAsync(long beforeTs, long sizeGt, bool keepProfiles, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminBackgroundUpdatesStatusResponse?> GetBackgroundUpdatesStatusAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminBackgroundUpdatesEnabledResponse?> SetBackgroundUpdatesEnabledAsync(bool enabled, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task StartBackgroundUpdatesJobAsync(string jobName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class BurstFailingAuthGateway : IMatrixAuthGateway
    {
        private int _gatewayCallCount;
        public int GatewayCallCount => Volatile.Read(ref _gatewayCallCount);

        public async Task ResolveHomeserverAsync(string homeserverUrl, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _gatewayCallCount);
            await Task.Delay(50, cancellationToken);
        }

        public Task<LoginResponse> LoginAsync(string homeserverUrl, string username, string password, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Invalid username or password", null, HttpStatusCode.Forbidden);

        public Task<IMatrixGateway> GetAuthenticatedAsync(string homeserverUrl, string accessToken, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class FakeAuthStringLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(name, arguments), resourceNotFound: false);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
