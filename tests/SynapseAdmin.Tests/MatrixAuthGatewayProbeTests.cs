using System.Collections.Concurrent;
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
    private const string WhoAmIPath = "/_matrix/client/v3/account/whoami";
    private const string FederationVersionPath = "/_matrix/federation/v1/version";
    private const string AdminVersionPath = "/_synapse/admin/v1/server_version";
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

    [Fact]
    public async Task GetAuthenticated_SynapseReportedViaFederation_ConfirmsAdminApiAndTakesItsVersion()
    {
        _homeserver.FederationVersion = () => Results.Json(new { server = new { name = "Synapse", version = "1.120.0" } });
        _homeserver.AdminServerVersion = () => Results.Json(new { server_version = "1.120.2" });

        var gateway = await CreateAuthGateway().GetAuthenticatedAsync(_homeserver.Url, AccessToken);

        Assert.IsType<SynapseCompatibleAdminGateway>(gateway);
        Assert.Equal(("Synapse", "1.120.2"), (gateway.ServerBrand, gateway.ServerVersion));
        Assert.Equal(new[] { WhoAmIPath, FederationVersionPath, AdminVersionPath }, _homeserver.RequestPaths);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetAuthenticated_AdminProbeNotFound_ReturnsGenericGateway(bool federationReportsSynapse)
    {
        _homeserver.FederationVersion = federationReportsSynapse
            ? () => Results.Json(new { server = new { name = "Synapse", version = "1.120.0" } })
            : () => Results.NotFound();
        _homeserver.AdminServerVersion = () => Results.Json(new { errcode = "M_UNRECOGNIZED", error = "Unrecognized request" }, statusCode: 404);

        var gateway = await CreateAuthGateway().GetAuthenticatedAsync(_homeserver.Url, AccessToken);

        Assert.IsType<GenericMatrixGateway>(gateway);
        Assert.False(gateway.SupportsAdminApi);
        Assert.Equal(new[] { WhoAmIPath, FederationVersionPath, AdminVersionPath }, _homeserver.RequestPaths);
    }

    /// <summary>
    /// Without a federation brand the admin probe decides alone. The brand stays "Generic". The version comes from the
    /// admin response even when that leaves it empty, and stays "Unknown" when the body is unreadable.
    /// </summary>
    [Theory]
    [InlineData("""{"server_version":"1.2.3"}""", "1.2.3")]
    [InlineData("{}", "")]
    [InlineData("OK", "Unknown")]
    public async Task GetAuthenticated_FederationUnavailable_FallsBackToTheAdminProbe(string adminBody, string expectedVersion)
    {
        _homeserver.FederationVersion = () => Results.NotFound();
        _homeserver.AdminServerVersion = () => Results.Text(adminBody, "application/json");

        var gateway = await CreateAuthGateway().GetAuthenticatedAsync(_homeserver.Url, AccessToken);

        Assert.IsType<SynapseCompatibleAdminGateway>(gateway);
        Assert.Equal(("Generic", expectedVersion), (gateway.ServerBrand, gateway.ServerVersion));
    }

    /// <summary>Tuwunel versions and whether each counts as admin-capable (>= 1.8.1).</summary>
    public static TheoryData<string, bool> TuwunelVersions => new()
    {
        { "1.8.1", true },
        { "1.8.0", false },
        { "1.9.0-dev", true },
        { "-1.0", true }, // dash at index 0: kept, so unparseable
        { "abc", true }, // unparseable: the active probe decides
        { "1.8", false },
    };

    /// <summary>A Tuwunel version that isn't admin-capable skips the admin probe, and so the admin API.</summary>
    [Theory]
    [MemberData(nameof(TuwunelVersions))]
    public async Task GetAuthenticated_TuwunelVersion_DecidesWhetherTheAdminApiIsProbed(string version, bool probed)
    {
        _homeserver.FederationVersion = () => Results.Json(new { server = new { name = "Tuwunel", version } });

        var gateway = await CreateAuthGateway().GetAuthenticatedAsync(_homeserver.Url, AccessToken);

        Assert.Equal(probed, gateway.SupportsAdminApi);
        Assert.Equal(probed, _homeserver.RequestPaths.Contains(AdminVersionPath));
    }

    [Theory]
    [MemberData(nameof(TuwunelVersions))]
    public void IsTuwunelAdminCapable_MatchesTheVersionTable(string version, bool expected) =>
        Assert.Equal(expected, MatrixAuthGateway.IsTuwunelAdminCapable(version));

    /// <summary>
    /// Discovery already identified Synapse: no federation probe, always the admin gateway, and the version falls back
    /// to "Unknown" when the admin probe fails or reports an empty version.
    /// </summary>
    [Theory]
    [InlineData(200, """{"server_version":"1.120.0"}""", "1.120.0")]
    [InlineData(200, """{"server_version":""}""", "Unknown")]
    [InlineData(404, """{"errcode":"M_UNRECOGNIZED","error":"Unrecognized request"}""", "Unknown")]
    public async Task GetAuthenticated_DiscoveredSynapse_SkipsTheFederationProbe(int adminStatus, string adminBody, string expectedVersion)
    {
        _homeserver.AdminServerVersion = () => Results.Text(adminBody, "application/json", statusCode: adminStatus);

        var gateway = await CreateAuthGateway("Synapse").GetAuthenticatedAsync(_homeserver.Url, AccessToken);

        Assert.IsType<SynapseCompatibleAdminGateway>(gateway);
        Assert.Equal(("Synapse", expectedVersion), (gateway.ServerBrand, gateway.ServerVersion));
        Assert.Equal(new[] { WhoAmIPath, AdminVersionPath }, _homeserver.RequestPaths);
    }

    private MatrixAuthGateway CreateAuthGateway(string? serverSoftwareName = null) =>
        new(new FixedDiscoveryClient(_homeserver.Url, serverSoftwareName), _cache);

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

    private sealed class FixedDiscoveryClient(string clientUrl, string? serverSoftwareName) : IHomeserverDiscoveryClient
    {
        public Task<HomeserverEndpoints> DiscoverAsync(string homeserverUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HomeserverEndpoints(clientUrl, null, serverSoftwareName, null));

        public Task<LoginResponse> LoginAsync(string clientBaseUrl, string username, string password, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// By default a Tuwunel-like homeserver that supports the Synapse Admin API; <see cref="FederationVersion"/> and
    /// <see cref="AdminServerVersion"/> change the probe answers. Records every request path in order. When
    /// <see cref="StallFirstFederationProbe"/> is set, the first federation version request waits until the client aborts it.
    /// </summary>
    private sealed class FakeHomeserver : IAsyncDisposable
    {
        private WebApplication? _app;
        private readonly TaskCompletionSource _federationProbeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _federationRequests;
        private readonly ConcurrentQueue<string> _requestPaths = new();

        public string Url { get; private set; } = null!;
        public IEnumerable<string> RequestPaths => _requestPaths;
        public Func<IResult> FederationVersion { get; set; } = () => Results.Json(new { server = new { name = "Tuwunel", version = "1.9.0" } });
        public Func<IResult> AdminServerVersion { get; set; } = () => Results.Json(new { server_version = "1.9.0" });
        public bool StallFirstFederationProbe { get; set; }
        public Task FederationProbeStarted => _federationProbeStarted.Task;

        public async Task StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            _app = builder.Build();

            _app.Use(async (context, next) =>
            {
                _requestPaths.Enqueue(context.Request.Path.Value!);
                await next(context);
            });
            _app.MapGet(WhoAmIPath, () => Results.Json(new { user_id = "@admin:example.com" }));
            // Typed as Func so MapGet uses the result-returning overload, not RequestDelegate (which drops the IResult).
            _app.MapGet(FederationVersionPath, (Func<HttpContext, Task<IResult>>)HandleFederationVersionAsync);
            _app.MapGet(AdminVersionPath, () => AdminServerVersion());

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

            return FederationVersion();
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
