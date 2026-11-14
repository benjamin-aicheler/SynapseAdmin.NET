using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Controllers;
using SynapseAdmin.Extensions;
using SynapseAdmin.Infrastructure.DataProtection;
using SynapseAdmin.Infrastructure.Gateways;
using SynapseAdmin.Infrastructure.Security;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Resources;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using Xunit;

namespace SynapseAdmin.Tests;

public class SecurityInfrastructureTests
{
    [Fact]
    public void LoginRateLimiter_BlocksAfterFiveFailedAttempts_PerIpAndIdentifier_AndResets()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var limiter = new LoginRateLimiter(new MemoryLoginRateLimitStore(cache));

        const string ip = "192.0.2.10";
        const string identifier = "https://matrix.example.com|@Admin:example.com";

        for (var i = 0; i < 5; i++)
        {
            Assert.True(limiter.TryAcquire(ip, identifier));
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
    public async Task LoginRateLimiter_ParallelTryAcquire_AllowsAtMostFiveSlotsUnderConcurrency()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var limiter = new LoginRateLimiter(new MemoryLoginRateLimitStore(cache));

        const string ip = "192.0.2.55";
        const string identifier = "https://matrix.example.com|@Admin:example.com";
        const int parallelCallers = 50;

        var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, parallelCallers)
            .Select(i => Task.Run(async () =>
            {
                await startSignal.Task;
                var candidateId = i % 2 == 0 ? identifier : identifier.ToUpperInvariant();
                return limiter.TryAcquire(ip, candidateId);
            }))
            .ToArray();

        startSignal.SetResult();
        var outcomes = await Task.WhenAll(tasks);

        Assert.Equal(5, outcomes.Count(allowed => allowed));
        Assert.Equal(parallelCallers - 5, outcomes.Count(allowed => !allowed));
        Assert.False(limiter.IsAllowed(ip, identifier));

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
                    new ClientIdentityProvider(httpContextAccessor),
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

    private static MatrixSessionService CreateSessionService(
        IMatrixAuthGateway authGateway, ILoginRateLimiter limiter, IClientIdentityProvider clientIdentity) =>
        new(authGateway, limiter, clientIdentity, NullLogger<MatrixSessionService>.Instance, new FakeAuthStringLocalizer());

    private static IClientIdentityProvider ClientAt(string ip)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return new ClientIdentityProvider(new FakeHttpContextAccessor { HttpContext = context });
    }

    /// <summary>
    /// Simulates a Blazor circuit: the IP is captured by the circuit handler while the SignalR connection's
    /// HttpContext is present, after which (as in interactive event handlers) no ambient HttpContext exists.
    /// </summary>
    private static async Task<ClientIdentityProvider> OpenCircuitAt(string ip)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        var accessor = new FakeHttpContextAccessor { HttpContext = context };
        var provider = new ClientIdentityProvider(accessor);
        var handler = new ClientIdentityCircuitHandler(provider, NullLogger<ClientIdentityCircuitHandler>.Instance);

        await handler.OnCircuitOpenedAsync(null!, CancellationToken.None);
        accessor.HttpContext = null;
        return provider;
    }

    [Fact]
    public async Task TokenLogin_FailuresFromClientA_DoNotBlockClientB_OnSameHomeserver()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var limiter = new LoginRateLimiter(new MemoryLoginRateLimitStore(cache));
        var authGateway = new TokenAuthGateway(validToken: "syt_valid");
        const string homeserver = "https://matrix.example.com";

        var clientA = CreateSessionService(authGateway, limiter, ClientAt("192.0.2.1"));
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal("LoginFailed", (await clientA.LoginWithTokenAsync(homeserver, $"syt_wrong_{i}")).Message);
        }

        // Burst protection for client A is preserved: the 6th attempt never reaches the homeserver.
        Assert.Equal("TooManyLoginAttempts", (await clientA.LoginWithTokenAsync(homeserver, "syt_wrong_6")).Message);
        Assert.Equal(5, authGateway.AuthenticatedCalls.Count);

        // Client B (different IP, same homeserver) is unaffected and actually authenticates.
        var clientB = CreateSessionService(authGateway, limiter, ClientAt("192.0.2.2"));
        var result = await clientB.LoginWithTokenAsync(homeserver.ToUpperInvariant(), "syt_valid");

        Assert.True(result.Success);
        Assert.Equal("syt_valid", authGateway.AuthenticatedCalls.Last());
        Assert.True(clientB.IsLoggedIn);

        // B's successful login does not reset A's bucket.
        Assert.Equal("TooManyLoginAttempts", (await clientA.LoginWithTokenAsync(homeserver, "syt_valid")).Message);
    }

    /// <summary>#395 (H-10): a failed token login (e.g. while switching accounts) keeps the current session.</summary>
    [Fact]
    public async Task FailedTokenLogin_KeepsExistingSession()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var limiter = new LoginRateLimiter(new MemoryLoginRateLimitStore(cache));
        var authGateway = new TokenAuthGateway(validToken: "syt_valid");
        const string homeserver = "https://matrix.example.com";
        var session = CreateSessionService(authGateway, limiter, ClientAt("192.0.2.3"));
        Assert.True((await session.LoginWithTokenAsync(homeserver, "syt_valid")).Success);
        var existing = session.Gateway;

        var result = await session.LoginWithTokenAsync(homeserver, "syt_wrong");

        Assert.Equal("LoginFailed", result.Message);
        Assert.True(session.IsLoggedIn);
        Assert.Same(existing, session.Gateway);
    }

    /// <summary>Restoring a session replaces it, so a failed restore still clears the gateway.</summary>
    [Fact]
    public async Task FailedSessionRestore_StillClearsGateway()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var limiter = new LoginRateLimiter(new MemoryLoginRateLimitStore(cache));
        var authGateway = new TokenAuthGateway(validToken: "syt_valid");
        const string homeserver = "https://matrix.example.com";
        var session = CreateSessionService(authGateway, limiter, ClientAt("192.0.2.4"));
        Assert.True((await session.LoginWithTokenAsync(homeserver, "syt_valid")).Success);

        var result = await session.RestoreSessionAsync(homeserver, "syt_revoked", force: true);

        Assert.False(result.Success);
        Assert.False(session.IsLoggedIn);
    }

    [Fact]
    public async Task InteractiveLogin_ClientsWithoutHttpContext_UseTheirCircuitIp_AndDoNotShareBucket()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var limiter = new LoginRateLimiter(new MemoryLoginRateLimitStore(cache));
        var authGateway = new TokenAuthGateway(validToken: "syt_valid");
        const string homeserver = "https://matrix.example.com";

        var circuitX = await OpenCircuitAt("198.51.100.10");
        var circuitY = await OpenCircuitAt("198.51.100.20");
        Assert.Equal("198.51.100.10", circuitX.GetClientIp());
        Assert.Equal("198.51.100.20", circuitY.GetClientIp());

        var x = CreateSessionService(authGateway, limiter, circuitX);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal("LoginFailed", (await x.LoginWithTokenAsync(homeserver, "syt_wrong")).Message);
        }
        Assert.Equal("TooManyLoginAttempts", (await x.LoginWithTokenAsync(homeserver, "syt_wrong")).Message);

        // Without the circuit-captured IP both would land in the shared "unknown" bucket and Y would be blocked.
        var y = CreateSessionService(authGateway, limiter, circuitY);
        Assert.True((await y.LoginWithTokenAsync(homeserver, "syt_valid")).Success);
    }

    [Fact]
    public async Task InteractivePasswordLogin_ClientsWithoutHttpContext_AreThrottledPerCircuitIp()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var limiter = new LoginRateLimiter(new MemoryLoginRateLimitStore(cache));
        var authGateway = new BurstFailingAuthGateway();
        const string homeserver = "https://matrix.example.com";

        var x = CreateSessionService(authGateway, limiter, await OpenCircuitAt("198.51.100.30"));
        var y = CreateSessionService(authGateway, limiter, await OpenCircuitAt("198.51.100.40"));

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal("LoginFailed", (await x.LoginAsync(homeserver, $"user{i}", "wrong")).Message);
        }
        Assert.Equal("TooManyLoginAttempts", (await x.LoginAsync(homeserver, "user9", "wrong")).Message);

        // A different client trying a different account still reaches the homeserver.
        Assert.Equal("LoginFailed", (await y.LoginAsync(homeserver, "bob", "wrong")).Message);
        Assert.Equal(6, authGateway.GatewayCallCount);
    }

    [Fact]
    public async Task ClientIdentityProvider_PrefersCircuitIp_UpdatesOnReconnect_AndFallsBackToHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.1");
        var accessor = new FakeHttpContextAccessor { HttpContext = context };
        var provider = new ClientIdentityProvider(accessor);
        var handler = new ClientIdentityCircuitHandler(provider, NullLogger<ClientIdentityCircuitHandler>.Instance);

        // Not captured yet (e.g. controller request): ambient HttpContext is used.
        Assert.Equal("203.0.113.1", provider.GetClientIp());

        await handler.OnCircuitOpenedAsync(null!, CancellationToken.None);
        accessor.HttpContext = null;
        Assert.Equal("203.0.113.1", provider.GetClientIp());

        // Reconnect from a new address updates the identity.
        var reconnect = new DefaultHttpContext();
        reconnect.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.2");
        accessor.HttpContext = reconnect;
        await handler.OnConnectionUpAsync(null!, CancellationToken.None);
        accessor.HttpContext = null;
        Assert.Equal("203.0.113.2", provider.GetClientIp());

        // A reconnect without a usable HttpContext keeps the last known IP.
        await handler.OnConnectionUpAsync(null!, CancellationToken.None);
        Assert.Equal("203.0.113.2", provider.GetClientIp());

        Assert.Null(new ClientIdentityProvider(new FakeHttpContextAccessor()).GetClientIp());
    }

    [Fact]
    public async Task ClientIdentity_IsWiredPerCircuitScope_AndSharedWithSessionService()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider()));

        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;

        var handler = Assert.Single(provider.GetServices<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler>()
            .OfType<ClientIdentityCircuitHandler>());
        Assert.NotNull(handler);

        // The handler writes into the same scoped instance that MatrixSessionService reads from.
        var concrete = provider.GetRequiredService<ClientIdentityProvider>();
        Assert.Same(concrete, provider.GetRequiredService<IClientIdentityProvider>());
        Assert.IsType<MatrixSessionService>(provider.GetRequiredService<IMatrixSessionService>());

        // Different circuits (scopes) get different identities.
        using var otherScope = factory.Services.CreateScope();
        Assert.NotSame(concrete, otherScope.ServiceProvider.GetRequiredService<ClientIdentityProvider>());
    }

    private const string BrowserBinding = "antiforgery-request-token-of-browser-a";

    [Fact]
    public void SessionBridgeService_ConsumesTokenOnlyOnce()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var bridge = new SessionBridgeService(cache, NullLogger<SessionBridgeService>.Instance);

        var key = bridge.CreateBridge("https://matrix.example.com", "syt_secret_token", "@admin:example.com", "admin", BrowserBinding);

        Assert.False(string.IsNullOrWhiteSpace(key));
        Assert.True(bridge.TryConsumeBridge(key, BrowserBinding, out var data));
        Assert.Equal("https://matrix.example.com", data.Homeserver);
        Assert.Equal("syt_secret_token", data.AccessToken);
        Assert.Equal("@admin:example.com", data.UserId);
        Assert.Equal("admin", data.Username);

        // Replay must fail
        Assert.False(bridge.TryConsumeBridge(key, BrowserBinding, out var replayData));
        Assert.Equal(string.Empty, replayData.AccessToken);
    }

    [Fact]
    public async Task SessionBridgeService_ParallelRedemption_SucceedsExactlyOnce()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var bridge = new SessionBridgeService(cache, NullLogger<SessionBridgeService>.Instance);

        for (var round = 0; round < 20; round++)
        {
            var key = bridge.CreateBridge("https://matrix.example.com", "syt_token", "@admin:example.com", "admin", BrowserBinding);
            const int parallelCallers = 32;
            using var barrier = new Barrier(parallelCallers);

            var outcomes = await Task.WhenAll(Enumerable.Range(0, parallelCallers).Select(_ => Task.Factory.StartNew(() =>
            {
                barrier.SignalAndWait();
                return bridge.TryConsumeBridge(key, BrowserBinding, out var data) && data.AccessToken == "syt_token";
            }, TaskCreationOptions.LongRunning)));

            Assert.Equal(1, outcomes.Count(ok => ok));
        }
    }

    [Fact]
    public void SessionBridgeService_RejectsExpiredKeys()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-11-13T12:00:00Z"));
        var bridge = new SessionBridgeService(cache, NullLogger<SessionBridgeService>.Instance, clock);

        var fresh = bridge.CreateBridge("https://matrix.example.com", "syt_token", "@admin:example.com", "admin", BrowserBinding);
        var stale = bridge.CreateBridge("https://matrix.example.com", "syt_token", "@admin:example.com", "admin", BrowserBinding);

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.True(bridge.TryConsumeBridge(fresh, BrowserBinding, out _));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(bridge.TryConsumeBridge(stale, BrowserBinding, out var data));
        Assert.Equal(string.Empty, data.AccessToken);
    }

    [Fact]
    public void SessionBridgeService_RequiresMatchingBinding_WithoutBurningTheKey()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var bridge = new SessionBridgeService(cache, NullLogger<SessionBridgeService>.Instance);

        Assert.ThrowsAny<ArgumentException>(() => bridge.CreateBridge("https://matrix.example.com", "syt_token", "@a:example.com", "a", ""));

        var key = bridge.CreateBridge("https://matrix.example.com", "syt_token", "@admin:example.com", "admin", BrowserBinding);

        Assert.False(bridge.TryConsumeBridge(key, null, out _));
        Assert.False(bridge.TryConsumeBridge(key, "", out _));
        Assert.False(bridge.TryConsumeBridge(key, "antiforgery-request-token-of-browser-b", out _));
        Assert.False(bridge.TryConsumeBridge(null, BrowserBinding, out _));
        Assert.False(bridge.TryConsumeBridge("not-a-key", BrowserBinding, out _));

        // Failed attempts from another browser must not consume the key for the initiating browser.
        Assert.True(bridge.TryConsumeBridge(key, BrowserBinding, out var data));
        Assert.Equal("syt_token", data.AccessToken);
    }

    /// <summary>Antiforgery cookie + request token pair for one simulated browser, issued by the app under test.</summary>
    private sealed record BrowserTokens(string CookieName, string CookieToken, string HeaderName, string RequestToken);

    private static BrowserTokens IssueBrowserTokens(IServiceProvider services)
    {
        var antiforgery = services.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>>().Value;
        var tokens = antiforgery.GetAndStoreTokens(new DefaultHttpContext { RequestServices = services });
        return new BrowserTokens(options.Cookie.Name!, tokens.CookieToken!, tokens.HeaderName!, tokens.RequestToken!);
    }

    private static HttpRequestMessage SignInRequest(string? key, BrowserTokens? cookieOf, BrowserTokens? headerOf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Auth/SignIn")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { key })
        };
        if (cookieOf != null) request.Headers.Add("Cookie", $"{cookieOf.CookieName}={cookieOf.CookieToken}");
        if (headerOf != null) request.Headers.Add(headerOf.HeaderName, headerOf.RequestToken);
        return request;
    }

    private static WebApplicationFactory<Program> CreateAppFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider()));

    private static HttpClient CreateBrowserClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

    [Fact]
    public async Task AuthSignIn_BoundPost_IssuesCookie_AndRejectsGetReplayAndQueryString()
    {
        await using var factory = CreateAppFactory();
        var bridge = factory.Services.GetRequiredService<ISessionBridgeService>();
        var browserA = IssueBrowserTokens(factory.Services);
        using var client = CreateBrowserClient(factory);

        var key = bridge.CreateBridge("https://example.com", "syt_token", "@admin:example.com", "admin", browserA.RequestToken);

        // The old GET-with-query-string form no longer exists.
        using var getResponse = await client.GetAsync($"/Auth/SignIn?key={Uri.EscapeDataString(key)}");
        Assert.True(getResponse.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound,
            $"GET /Auth/SignIn returned {getResponse.StatusCode}");

        using var ok = await client.SendAsync(SignInRequest(key, browserA, browserA));
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Contains(ok.Headers.GetValues("Set-Cookie"), c =>
            c.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal) &&
            c.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
            c.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("no-store", ok.Headers.CacheControl?.ToString() ?? string.Empty);

        using var replay = await client.SendAsync(SignInRequest(key, browserA, browserA));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.False(replay.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task AuthSignIn_RejectsMissingOrForeignBinding_WithoutConsumingTheKey()
    {
        await using var factory = CreateAppFactory();
        var bridge = factory.Services.GetRequiredService<ISessionBridgeService>();
        var browserA = IssueBrowserTokens(factory.Services);
        var browserB = IssueBrowserTokens(factory.Services);
        using var client = CreateBrowserClient(factory);

        var key = bridge.CreateBridge("https://example.com", "syt_token", "@admin:example.com", "admin", browserA.RequestToken);

        // No antiforgery at all (e.g. cross-site form/link).
        using var none = await client.SendAsync(SignInRequest(key, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);

        // A's header without A's cookie (stolen token replayed from elsewhere).
        using var headerOnly = await client.SendAsync(SignInRequest(key, browserB, browserA));
        Assert.Equal(HttpStatusCode.BadRequest, headerOnly.StatusCode);

        // Another browser with its own valid antiforgery pair, but the key is bound to A.
        using var foreign = await client.SendAsync(SignInRequest(key, browserB, browserB));
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.False(foreign.Headers.Contains("Set-Cookie"));

        // None of the above burned the key: the initiating browser can still redeem it.
        using var ok = await client.SendAsync(SignInRequest(key, browserA, browserA));
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task AuthSignIn_ParallelBoundRequests_IssueExactlyOneCookie()
    {
        await using var factory = CreateAppFactory();
        var bridge = factory.Services.GetRequiredService<ISessionBridgeService>();
        var browserA = IssueBrowserTokens(factory.Services);
        using var client = CreateBrowserClient(factory);

        var key = bridge.CreateBridge("https://example.com", "syt_token", "@admin:example.com", "admin", browserA.RequestToken);

        // Stays within the "auth" rate-limit policy (10 requests / minute per IP).
        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => client.SendAsync(SignInRequest(key, browserA, browserA))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest));
        foreach (var response in responses) response.Dispose();
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
        Assert.Equal("private, no-cache", downloadController.Response.Headers.CacheControl.ToString());
        Assert.Equal("nosniff", downloadController.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.NotNull(downloadFile.EntityTag);
        Assert.Equal(1, mediaService.StreamFetchCount);

        // Conditional request on Download with matching If-None-Match returns 304 only after confirming the media
        // still exists (the upstream stream is opened, then released without being sent; see #370)
        var conditionalDownloadController = CreateAuthenticatedMediaController(mediaService, sessionService);
        conditionalDownloadController.Request.Headers.IfNoneMatch = downloadFile.EntityTag.Tag.ToString();
        var notModifiedResult = await conditionalDownloadController.Download(mxc, "photo.png", "image/png");

        var statusCodeResult = Assert.IsType<StatusCodeResult>(notModifiedResult);
        Assert.Equal(StatusCodes.Status304NotModified, statusCodeResult.StatusCode);
        Assert.Equal("private, no-cache", conditionalDownloadController.Response.Headers.CacheControl.ToString());
        Assert.Equal(downloadFile.EntityTag.Tag.ToString(), conditionalDownloadController.Response.Headers.ETag.ToString());
        Assert.Equal(2, mediaService.StreamFetchCount);

        // Preview sets identical Cache-Control and ETag
        var previewController = CreateAuthenticatedMediaController(mediaService, sessionService);
        var previewResult = await previewController.Preview(mxc, "image/png");

        var previewFile = Assert.IsType<FileStreamResult>(previewResult);
        Assert.Equal("private, no-cache", previewController.Response.Headers.CacheControl.ToString());
        Assert.Equal(downloadFile.EntityTag, previewFile.EntityTag);
    }

    [Fact]
    public async Task MediaController_WebApplicationFactory_CoversAuthGate_ETag304_RangeProcessing_AndCacheHeaders()
    {
        const string mxc = "mxc://example.com/media123";
        var mediaService = new FakeMediaService();
        var sessionService = new FakeRestorableSessionService();

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IMediaService>();
                services.RemoveAll<IMatrixSessionService>();
                services.AddScoped<IMediaService>(_ => mediaService);
                services.AddScoped<IMatrixSessionService>(_ => sessionService);
            });
        });

        using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        // 1. Unauthenticated requests to /Media/* must return 401 Unauthorized (not 302 redirect)
        using var unauthDownload = await anonymousClient.GetAsync($"/Media/Download?mxc={Uri.EscapeDataString(mxc)}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthDownload.StatusCode);

        using var unauthPreview = await anonymousClient.GetAsync($"/Media/Preview?mxc={Uri.EscapeDataString(mxc)}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthPreview.StatusCode);
        Assert.Equal(0, mediaService.StreamFetchCount);

        // 2. Authenticate via the Cookie Bridge (/Auth/SignIn)
        using var authenticatedClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var bridgeService = factory.Services.GetRequiredService<ISessionBridgeService>();
        var browser = IssueBrowserTokens(factory.Services);
        var bridgeKey = bridgeService.CreateBridge("https://example.com", "syt_token", "@admin:example.com", "admin", browser.RequestToken);
        using var signInResponse = await authenticatedClient.SendAsync(SignInRequest(bridgeKey, browser, browser));
        Assert.Equal(HttpStatusCode.NoContent, signInResponse.StatusCode);

        // 3. Authenticated Download returns 200 OK, Cache-Control, ETag, and Accept-Ranges
        using var downloadResponse = await authenticatedClient.GetAsync(
            $"/Media/Download?mxc={Uri.EscapeDataString(mxc)}&filename=photo.png&mimeType=image/png");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.True(downloadResponse.Headers.CacheControl?.Private);
        Assert.True(downloadResponse.Headers.CacheControl?.NoCache);
        Assert.Null(downloadResponse.Headers.CacheControl?.MaxAge);
        Assert.Equal("nosniff", downloadResponse.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("bytes", downloadResponse.Headers.AcceptRanges);
        Assert.NotNull(downloadResponse.Headers.ETag);
        Assert.Equal([1, 2, 3, 4], await downloadResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, mediaService.StreamFetchCount);

        var etag = downloadResponse.Headers.ETag!;

        // 4. Conditional GET with matching If-None-Match returns 304 Not Modified (after an existence check, #370)
        using var conditionalRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/Media/Download?mxc={Uri.EscapeDataString(mxc)}&filename=photo.png&mimeType=image/png");
        conditionalRequest.Headers.IfNoneMatch.Add(etag);

        using var notModifiedResponse = await authenticatedClient.SendAsync(conditionalRequest);
        Assert.Equal(HttpStatusCode.NotModified, notModifiedResponse.StatusCode);
        Assert.True(notModifiedResponse.Headers.CacheControl?.Private);
        Assert.True(notModifiedResponse.Headers.CacheControl?.NoCache);
        Assert.Equal(etag.Tag, notModifiedResponse.Headers.ETag?.Tag);
        Assert.Empty(await notModifiedResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal(2, mediaService.StreamFetchCount);

        // 5. Range request returns 206 Partial Content with the requested byte slice
        using var rangeRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/Media/Download?mxc={Uri.EscapeDataString(mxc)}&filename=photo.png&mimeType=image/png");
        rangeRequest.Headers.Range = new RangeHeaderValue(1, 2);

        using var rangeResponse = await authenticatedClient.SendAsync(rangeRequest);
        Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
        Assert.Equal([2, 3], await rangeResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal(new ContentRangeHeaderValue(1, 2, 4), rangeResponse.Content.Headers.ContentRange);

        // 6. Preview sets Cache-Control, ETag, and sandbox CSP for SVG content
        using var svgPreviewResponse = await authenticatedClient.GetAsync(
            $"/Media/Preview?mxc={Uri.EscapeDataString(mxc)}&mimeType={Uri.EscapeDataString("image/svg+xml")}");
        Assert.Equal(HttpStatusCode.OK, svgPreviewResponse.StatusCode);
        Assert.True(svgPreviewResponse.Headers.CacheControl?.Private);
        Assert.True(svgPreviewResponse.Headers.CacheControl?.NoCache);
        Assert.Equal(etag.Tag, svgPreviewResponse.Headers.ETag?.Tag);
        Assert.Equal(
            "default-src 'none'; style-src 'unsafe-inline'; sandbox",
            svgPreviewResponse.Headers.GetValues("Content-Security-Policy").Single());
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

        public Task<OperationResult<MediaDownload>> OpenMediaAsync(string mxc, MediaByteRange? range = null, CancellationToken token = default)
        {
            StreamFetchCount++;
            return Task.FromResult(OperationResult<MediaDownload>.Ok(new MediaDownload
            {
                Status = MediaDownloadStatus.Full,
                Content = new MemoryStream([1, 2, 3, 4])
            }));
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
        public Task<UserMediaStatisticsResponse?> GetUserMediaStatisticsAsync(int limit = 10, string orderBy = "media_length", string dir = "b", string? searchTerm = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> UpdateUserAsync(string userId, object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminUserMembershipsResponse?> GetUserMembershipsAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminUserMediaResult?> GetUserMediaAsync(string userId, int from, int limit, string orderBy = "created_ts", string dir = "b", CancellationToken cancellationToken = default) => throw new NotImplementedException();
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
        public Task<MediaDownload> OpenMediaAsync(string mxcUri, MediaByteRange? range = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
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

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    /// <summary>
    /// Per-instance accessor. The real <see cref="HttpContextAccessor"/> stores its context in a static AsyncLocal,
    /// so several "clients" created in one test flow would overwrite each other's context.
    /// </summary>
    private sealed class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class TokenAuthGateway(string validToken) : IMatrixAuthGateway
    {
        private readonly List<string> _authenticatedCalls = [];

        public IReadOnlyList<string> AuthenticatedCalls
        {
            get { lock (_authenticatedCalls) return _authenticatedCalls.ToList(); }
        }

        public Task ResolveHomeserverAsync(string homeserverUrl, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<LoginResponse> LoginAsync(string homeserverUrl, string username, string password, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IMatrixGateway> GetAuthenticatedAsync(string homeserverUrl, string accessToken, CancellationToken cancellationToken = default)
        {
            lock (_authenticatedCalls) _authenticatedCalls.Add(accessToken);
            if (accessToken != validToken)
            {
                throw new HttpRequestException("Unknown token", null, HttpStatusCode.Unauthorized);
            }

            return Task.FromResult(Stub.Create<IMatrixGateway>(s => s.On("get_UserId", _ => "@admin:example.com")));
        }
    }

    private sealed class FakeAuthStringLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(name, arguments), resourceNotFound: false);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
