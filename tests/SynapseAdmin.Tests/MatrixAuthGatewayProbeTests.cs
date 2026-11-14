using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SynapseAdmin.Infrastructure.Gateways;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Drives the real <see cref="MatrixAuthGateway"/> gateway creation (capability probes included) against a
/// Kestrel fake homeserver. <c>SecurityInfrastructureTests</c> replaces the creation step, so the probes are
/// only covered here.
/// </summary>
public sealed class MatrixAuthGatewayProbeTests : IAsyncLifetime
{
    private const string AccessToken = "syt_probe_token";
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    private readonly FakeHomeserver _homeserver = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public Task InitializeAsync() => _homeserver.StartAsync();

    public async Task DisposeAsync()
    {
        _cache.Dispose();
        await _homeserver.DisposeAsync();
    }

    [Fact]
    public async Task GetAuthenticated_DetectsAdminApi_OnCompatibleServer()
    {
        var gateway = await CreateAuthGateway().GetAuthenticatedAsync(_homeserver.Url, AccessToken);

        Assert.True(gateway.SupportsAdminApi);
        Assert.Equal("Tuwunel", gateway.ServerBrand);
    }

    /// <summary>
    /// Issue #391: cancelling the request while a capability probe is in flight must not leave a gateway
    /// without admin API in the cache for that access token.
    /// </summary>
    [Fact]
    public async Task CancellationDuringProbe_DoesNotCacheGatewayWithoutAdminApi()
    {
        var authGateway = CreateAuthGateway();
        _homeserver.StallFirstFederationProbe = true;

        using var cts = new CancellationTokenSource();
        var cancelled = authGateway.GetAuthenticatedAsync(_homeserver.Url, AccessToken, cts.Token);
        await _homeserver.FederationProbeStarted.WaitAsync(SignalTimeout);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        var gateway = await GetAuthenticatedAfterInFlightCreationEndsAsync(authGateway);

        Assert.True(gateway.SupportsAdminApi);
    }

    private MatrixAuthGateway CreateAuthGateway() =>
        new(new FixedDiscoveryClient(_homeserver.Url), _cache);

    /// <summary>
    /// A caller that joins the cancelled in-flight creation observes its cancellation; retry until that creation
    /// has finished and the next call returns a gateway (from the cache, or freshly created).
    /// </summary>
    private async Task<IMatrixGateway> GetAuthenticatedAfterInFlightCreationEndsAsync(MatrixAuthGateway authGateway)
    {
        var deadline = DateTime.UtcNow + SignalTimeout;
        while (true)
        {
            try
            {
                return await authGateway.GetAuthenticatedAsync(_homeserver.Url, AccessToken);
            }
            catch (OperationCanceledException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
        }
    }

    private sealed class FixedDiscoveryClient(string clientUrl) : IHomeserverDiscoveryClient
    {
        public Task<HomeserverEndpoints> DiscoverAsync(string homeserverUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HomeserverEndpoints(clientUrl, null, null, null));

        public Task<LoginResponse> LoginAsync(string clientBaseUrl, string username, string password, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// A Tuwunel-like homeserver that supports the Synapse Admin API. When <see cref="StallFirstFederationProbe"/>
    /// is set, the first federation version request waits until the client aborts it.
    /// </summary>
    private sealed class FakeHomeserver : IAsyncDisposable
    {
        private WebApplication? _app;
        private readonly TaskCompletionSource _federationProbeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _federationRequests;

        public string Url { get; private set; } = null!;
        public bool StallFirstFederationProbe { get; set; }
        public Task FederationProbeStarted => _federationProbeStarted.Task;

        public async Task StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            _app = builder.Build();

            _app.MapGet("/_matrix/client/v3/account/whoami", () => Results.Json(new { user_id = "@admin:example.com" }));
            // Typed as Func so MapGet uses the result-returning overload, not RequestDelegate (which drops the IResult).
            _app.MapGet("/_matrix/federation/v1/version", (Func<HttpContext, Task<IResult>>)HandleFederationVersionAsync);
            _app.MapGet("/_synapse/admin/v1/server_version", () => Results.Json(new { server_version = "1.9.0" }));

            await _app.StartAsync();
            Url = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        }

        private async Task<IResult> HandleFederationVersionAsync(HttpContext context)
        {
            if (StallFirstFederationProbe && Interlocked.Increment(ref _federationRequests) == 1)
            {
                _federationProbeStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    // The client gave up on the request.
                }

                return Results.Empty;
            }

            return Results.Json(new { server = new { name = "Tuwunel", version = "1.9.0" } });
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
