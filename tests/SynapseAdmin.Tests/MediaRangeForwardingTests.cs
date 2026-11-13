using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using LibMatrix.Homeservers;
using LibMatrix.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SynapseAdmin.Infrastructure.Gateways;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Issue #372: byte ranges and cancellation must reach the homeserver. These tests drive the real controller,
/// <c>MediaService</c> and gateway against a real HTTP server, so the media body is a non-seekable network
/// stream and ASP.NET Core cannot slice it locally.
/// </summary>
public class MediaRangeForwardingTests : IAsyncLifetime
{
    private static readonly byte[] MediaBytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    private readonly FakeHomeserver _homeserver = new();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _homeserver.StartAsync();

        var homeserverUrl = _homeserver.Url;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IMatrixSessionService>();
                services.AddScoped<IMatrixSessionService>(_ => new UpstreamSessionService(homeserverUrl));
            }));

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        await SignInAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _homeserver.DisposeAsync();
    }

    // --- Range forwarding ---

    [Fact]
    public async Task Download_ForwardsRange_AndRelaysPartialContent()
    {
        using var request = Get("Download", "ranged", "&filename=photo.png&mimeType=image/png");
        request.Headers.Range = new RangeHeaderValue(1, 2);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal([1, 2], await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(new ContentRangeHeaderValue(1, 2, 256), response.Content.Headers.ContentRange);
        Assert.Equal(2, response.Content.Headers.ContentLength);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("bytes", response.Headers.AcceptRanges);
        Assert.NotNull(response.Headers.ETag);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("photo.png", response.Content.Headers.ContentDisposition?.FileNameStar);

        var upstream = Assert.Single(_homeserver.Requests);
        Assert.Equal("bytes=1-2", upstream.Range);
    }

    [Fact]
    public async Task Preview_ForwardsSuffixRange_InlineWithoutDisposition()
    {
        using var request = Get("Preview", "ranged", "&mimeType=video/mp4");
        request.Headers.Range = new RangeHeaderValue(null, 3);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal([253, 254, 255], await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(new ContentRangeHeaderValue(253, 255, 256), response.Content.Headers.ContentRange);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Equal("bytes=-3", Assert.Single(_homeserver.Requests).Range);
    }

    [Fact]
    public async Task UpstreamIgnoringRange_ServesTheFullObject()
    {
        using var request = Get("Download", "noranges", "&mimeType=image/png");
        request.Headers.Range = new RangeHeaderValue(1, 2);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Null(response.Content.Headers.ContentRange);
        Assert.Equal("bytes=1-2", Assert.Single(_homeserver.Requests).Range);
    }

    [Fact]
    public async Task UnsatisfiableRange_Returns416_WithCompleteLength()
    {
        using var request = Get("Download", "ranged", "&mimeType=image/png");
        request.Headers.Range = new RangeHeaderValue(1000, null);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
        Assert.Equal(256, response.Content.Headers.ContentRange?.Length);
        Assert.False(response.Content.Headers.ContentRange?.HasRange);
    }

    [Fact]
    public async Task MismatchedIfRange_DoesNotForwardTheRange()
    {
        using var request = Get("Download", "ranged", "&mimeType=image/png");
        request.Headers.Range = new RangeHeaderValue(1, 2);
        request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue("\"stale\""));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Null(Assert.Single(_homeserver.Requests).Range);
    }

    [Fact]
    public async Task MatchingIfRange_ForwardsTheRange()
    {
        using var first = await _client.GetAsync(Path("Download", "ranged", "&mimeType=image/png"));
        var etag = first.Headers.ETag!;

        using var request = Get("Download", "ranged", "&mimeType=image/png");
        request.Headers.Range = new RangeHeaderValue(1, 2);
        request.Headers.IfRange = new RangeConditionHeaderValue(etag);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal([1, 2], await response.Content.ReadAsByteArrayAsync());
    }

    // --- Endpoint selection: one upstream request, no leaked probe ---

    [Fact]
    public async Task Download_MakesExactlyOneUpstreamRequest()
    {
        using var response = await _client.GetAsync(Path("Download", "ranged", "&mimeType=image/png"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaBytes, await response.Content.ReadAsByteArrayAsync());
        var upstream = Assert.Single(_homeserver.Requests);
        Assert.StartsWith("/_matrix/client/v1/media/download/", upstream.Path);
    }

    [Fact]
    public async Task ServerWithoutAuthenticatedMedia_FallsBackToLegacyEndpoint_WithTheRange()
    {
        using var request = Get("Download", "legacyonly", "&mimeType=image/png");
        request.Headers.Range = new RangeHeaderValue(4, 5);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal([4, 5], await response.Content.ReadAsByteArrayAsync());
        var requests = _homeserver.Requests.ToArray();
        Assert.Equal(2, requests.Length);
        Assert.StartsWith("/_matrix/client/v1/media/download/", requests[0].Path);
        Assert.StartsWith("/_matrix/media/v3/download/", requests[1].Path);
        Assert.All(requests, r => Assert.Equal("bytes=4-5", r.Range));
    }

    [Fact]
    public async Task MissingMedia_Returns404_WithoutLegacyRetry()
    {
        using var response = await _client.GetAsync(Path("Download", "missing", "&mimeType=image/png"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(_homeserver.Requests);
    }

    // --- Cancellation and disposal ---

    [Fact]
    public async Task ClientCancellation_BeforeUpstreamHeaders_CancelsTheUpstreamRequest()
    {
        using var cts = new CancellationTokenSource();
        var pending = _client.GetAsync(Path("Download", "stall", "&mimeType=image/png"), cts.Token);

        await _homeserver.Started("stall").WaitAsync(SignalTimeout);
        await cts.CancelAsync();

        await _homeserver.Aborted("stall").WaitAsync(SignalTimeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task ClientDisconnect_DuringBody_ReleasesTheUpstreamResponse()
    {
        var response = await _client.GetAsync(Path("Download", "hanging", "&mimeType=image/png"), HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStreamAsync();
        Assert.True(await body.ReadAsync(new byte[16]) > 0);

        response.Dispose();

        await _homeserver.Aborted("hanging").WaitAsync(SignalTimeout);
    }

    [Fact]
    public async Task NotModified_ReleasesTheUpstreamResponse_WithoutReadingTheBody()
    {
        using var request = Get("Download", "hanging", "&mimeType=image/png");
        request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        await _homeserver.Aborted("hanging").WaitAsync(SignalTimeout);
    }

    // --- Helpers ---

    private static string Path(string action, string mediaId, string query = "") =>
        $"/Media/{action}?mxc={Uri.EscapeDataString($"mxc://example.com/{mediaId}")}{query}";

    private static HttpRequestMessage Get(string action, string mediaId, string query = "") =>
        new(HttpMethod.Get, Path(action, mediaId, query));

    private async Task SignInAsync()
    {
        var services = _factory.Services;
        var antiforgery = services.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>>().Value;
        var tokens = antiforgery.GetAndStoreTokens(new DefaultHttpContext { RequestServices = services });

        var key = services.GetRequiredService<ISessionBridgeService>()
            .CreateBridge("https://example.com", "syt_token", "@admin:example.com", "admin", tokens.RequestToken!);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/Auth/SignIn")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { key })
        };
        request.Headers.Add("Cookie", $"{options.Cookie.Name}={tokens.CookieToken}");
        request.Headers.Add(tokens.HeaderName!, tokens.RequestToken);

        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>A session whose gateway talks to the fake homeserver through LibMatrix's real HTTP client.</summary>
    private sealed class UpstreamSessionService(string homeserverUrl) : IMatrixSessionService
    {
        public IMatrixGateway? Gateway { get; } = new GenericMatrixGateway(new AuthenticatedHomeserverGeneric(
            "example.com", new HomeserverResolverService.WellKnownUris { Client = homeserverUrl }, null, "syt_token"));

        public bool IsLoggedIn => true;
        public Task<OperationResult> RestoreSessionAsync(string homeserver, string accessToken, bool force = false) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> LoginAsync(string homeserver, string username, string password) => throw new NotImplementedException();
        public Task<OperationResult> LoginWithTokenAsync(string homeserver, string accessToken) => throw new NotImplementedException();
        public Task<string?> GetServerVersionAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public void Logout() { }
    }

    /// <summary>
    /// A Kestrel server implementing the media download endpoints. The media ID selects the behaviour:
    /// <list type="bullet">
    /// <item><c>ranged</c>: honours <c>Range</c> (206 / 416).</item>
    /// <item><c>noranges</c>: ignores <c>Range</c> and returns 200.</item>
    /// <item><c>legacyonly</c>: no authenticated media (<c>M_UNRECOGNIZED</c>); the legacy endpoint honours <c>Range</c>.</item>
    /// <item><c>missing</c>: <c>M_NOT_FOUND</c>.</item>
    /// <item><c>stall</c>: never sends headers.</item>
    /// <item><c>hanging</c>: sends headers and part of a large body, then waits.</item>
    /// </list>
    /// <c>stall</c> and <c>hanging</c> signal when the request starts and when the client aborts it.
    /// </summary>
    private sealed class FakeHomeserver : IAsyncDisposable
    {
        private WebApplication? _app;
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _started = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _aborted = new();

        public string Url { get; private set; } = null!;
        public ConcurrentQueue<(string Path, string? Range)> Requests { get; } = new();

        public Task Started(string mediaId) => Signal(_started, mediaId).Task;
        public Task Aborted(string mediaId) => Signal(_aborted, mediaId).Task;

        private static TaskCompletionSource Signal(ConcurrentDictionary<string, TaskCompletionSource> signals, string mediaId) =>
            signals.GetOrAdd(mediaId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        public async Task StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            _app = builder.Build();

            _app.MapGet("/_matrix/client/v1/media/download/{server}/{mediaId}",
                (HttpContext context, string mediaId) => HandleAsync(context, mediaId, legacy: false));
            _app.MapGet("/_matrix/media/v3/download/{server}/{mediaId}",
                (HttpContext context, string mediaId) => HandleAsync(context, mediaId, legacy: true));

            await _app.StartAsync();
            Url = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        }

        private async Task<IResult> HandleAsync(HttpContext context, string mediaId, bool legacy)
        {
            var range = context.Request.Headers.Range.ToString();
            Requests.Enqueue((context.Request.Path.Value!, string.IsNullOrEmpty(range) ? null : range));

            if (context.Request.Headers.Authorization.ToString() != "Bearer syt_token")
                return Results.Json(new { errcode = "M_MISSING_TOKEN" }, statusCode: 401);

            switch (mediaId)
            {
                case "ranged":
                case "legacyonly" when legacy:
                    return Results.Bytes(MediaBytes, "application/octet-stream", enableRangeProcessing: true);
                case "legacyonly":
                    return Results.Json(new { errcode = "M_UNRECOGNIZED", error = "Unrecognized request" }, statusCode: 404);
                case "noranges":
                    return Results.Bytes(MediaBytes, "application/octet-stream");
                case "stall":
                    Signal(_started, mediaId).TrySetResult();
                    await WaitForAbortAsync(context, mediaId);
                    return Results.Empty;
                case "hanging":
                    Signal(_started, mediaId).TrySetResult();
                    context.Response.ContentType = "application/octet-stream";
                    context.Response.ContentLength = 1024 * 1024;
                    await context.Response.Body.WriteAsync(new byte[4096]);
                    await context.Response.Body.FlushAsync();
                    await WaitForAbortAsync(context, mediaId);
                    return Results.Empty;
                default:
                    return Results.Json(new { errcode = "M_NOT_FOUND", error = "Not found" }, statusCode: 404);
            }
        }

        private async Task WaitForAbortAsync(HttpContext context, string mediaId)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                Signal(_aborted, mediaId).TrySetResult();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }
    }
}
