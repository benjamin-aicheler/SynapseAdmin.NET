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
    private const string RoomListPath = "/_synapse/admin/v1/rooms";
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
    /// The Tuwunel brand, in any casing, gets the Tuwunel gateway (ADR 0006), which keeps the casing the server reports.
    /// </summary>
    [Theory]
    [InlineData("Tuwunel")]
    [InlineData("tuwunel")]
    [InlineData("TUWUNEL")]
    public async Task GetAuthenticated_TuwunelBrand_GetsTheTuwunelGateway(string brand)
    {
        _homeserver.FederationVersion = () => Results.Json(new { server = new { name = brand, version = "1.9.0" } });

        var gateway = await CreateAuthGateway().GetAuthenticatedAsync(_homeserver.Url, AccessToken);

        Assert.IsType<TuwunelAdminGateway>(gateway);
        Assert.Equal((brand, "1.9.0"), (gateway.ServerBrand, gateway.ServerVersion));
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
        _homeserver.FederationVersion = () => Results.Json(new { server = new { name = "Synapse", version = "1.165.0" } });
        _homeserver.AdminServerVersion = () => Results.Json(new { server_version = "1.165.1" });

        var gateway = await CreateAuthGateway().GetAuthenticatedAsync(_homeserver.Url, AccessToken);

        Assert.IsType<SynapseAdminGateway>(gateway);
        Assert.Equal(("Synapse", "1.165.1"), (gateway.ServerBrand, gateway.ServerVersion));
        Assert.Equal(new[] { WhoAmIPath, FederationVersionPath, AdminVersionPath }, _homeserver.RequestPaths);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetAuthenticated_AdminProbeNotFound_ReturnsGenericGateway(bool federationReportsSynapse)
    {
        _homeserver.FederationVersion = federationReportsSynapse
            ? () => Results.Json(new { server = new { name = "Synapse", version = "1.165.0" } })
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

        Assert.IsType<SynapseAdminGateway>(gateway);
        Assert.Equal(("Generic", expectedVersion), (gateway.ServerBrand, gateway.ServerVersion));
    }

    /// <summary>
    /// Tuwunel versions and whether each counts as admin-capable (>= 1.8.2). 1.8.1 doesn't serve seven endpoints the app
    /// calls, which 1.8.2 serves (SPEC.md §10, "Tuwunel versions").
    /// </summary>
    public static TheoryData<string, bool> TuwunelVersions => new()
    {
        { "1.8.2", true },
        { "1.8.1", false },
        { "1.8.0", false },
        { "1.9.0", true },
        { "2.0.0", true },
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
    /// Synapse versions and whether each counts as admin-capable (>= 1.145.0, which added the user memberships list;
    /// SPEC.md §10, "Synapse versions", #612). Only the release number at the start counts.
    /// </summary>
    public static TheoryData<string, bool> SynapseVersions => new()
    {
        { "1.145.0", true },
        { "1.144.0", false },
        { "1.140.0", false },
        { "1.103.0", false },
        { "1.165.0", true },
        { "2.0.0", true },
        { "1.145.0rc1", true },
        { "1.144.0rc3", false },
        { "1.165.0 (b=develop, 0adca7d)", true },
        { "1.120.0 (b=develop, 0adca7d)", false },
        { "1.145", true }, // missing patch: 0
        { "1.144", false },
        { "Unknown", true }, // probe failed: no version to check
        { "abc", true },
        { "v1.120.0", true }, // no release number at the start
        { "99999999999.0.0", true }, // too long for a version part: unparseable
    };

    [Theory]
    [MemberData(nameof(SynapseVersions))]
    [InlineData("", true)]
    public void IsSynapseAdminCapable_MatchesTheVersionTable(string version, bool expected) =>
        Assert.Equal(expected, MatrixAuthGateway.IsSynapseAdminCapable(version));

    /// <summary>
    /// A Synapse version from the federation probe that isn't admin-capable skips the admin probe, and so the admin
    /// API: the session gets the generic gateway, as on a Matrix server without the admin API (#612).
    /// </summary>
    [Theory]
    [MemberData(nameof(SynapseVersions))]
    public async Task GetAuthenticated_SynapseFederationVersion_DecidesWhetherTheAdminApiIsProbed(string version, bool probed)
    {
        _homeserver.FederationVersion = () => Results.Json(new { server = new { name = "Synapse", version } });

        var gateway = await CreateAuthGateway().GetAuthenticatedAsync(_homeserver.Url, AccessToken);

        Assert.Equal(probed, gateway.SupportsAdminApi);
        Assert.Equal(probed, _homeserver.RequestPaths.Contains(AdminVersionPath));
        Assert.Equal(probed ? typeof(SynapseAdminGateway) : typeof(GenericMatrixGateway), gateway.GetType());
    }

    /// <summary>
    /// Discovery already identified Synapse: the admin probe's version decides. A version below 1.145.0 gets no admin
    /// API, as on a generic Matrix server; a missing or unreadable version keeps it (#612).
    /// </summary>
    [Theory]
    [InlineData(200, """{"server_version":"1.144.0"}""", false)]
    [InlineData(200, """{"server_version":"1.103.0"}""", false)]
    [InlineData(200, """{"server_version":"1.145.0"}""", true)]
    [InlineData(200, """{"server_version":"1.145.0rc1"}""", true)]
    [InlineData(200, """{"server_version":""}""", true)]
    [InlineData(404, """{"errcode":"M_UNRECOGNIZED","error":"Unrecognized request"}""", true)]
    public async Task GetAuthenticated_DiscoveredSynapse_RequiresTheMinimumVersion(int adminStatus, string adminBody, bool supportsAdmin)
    {
        _homeserver.AdminServerVersion = () => Results.Text(adminBody, "application/json", statusCode: adminStatus);

        var gateway = await CreateAuthGateway("Synapse").GetAuthenticatedAsync(_homeserver.Url, AccessToken);

        Assert.Equal(supportsAdmin, gateway.SupportsAdminApi);
        Assert.Equal(supportsAdmin ? typeof(SynapseAdminGateway) : typeof(GenericMatrixGateway), gateway.GetType());
        Assert.Equal(new[] { WhoAmIPath, AdminVersionPath }, _homeserver.RequestPaths);
    }

    /// <summary>
    /// Discovery already identified Synapse: no federation probe, always the admin gateway, and the version falls back
    /// to "Unknown" when the admin probe fails or reports an empty version.
    /// </summary>
    [Theory]
    [InlineData(200, """{"server_version":"1.165.0"}""", "1.165.0")]
    [InlineData(200, """{"server_version":""}""", "Unknown")]
    [InlineData(404, """{"errcode":"M_UNRECOGNIZED","error":"Unrecognized request"}""", "Unknown")]
    public async Task GetAuthenticated_DiscoveredSynapse_SkipsTheFederationProbe(int adminStatus, string adminBody, string expectedVersion)
    {
        _homeserver.AdminServerVersion = () => Results.Text(adminBody, "application/json", statusCode: adminStatus);

        var gateway = await CreateAuthGateway("Synapse").GetAuthenticatedAsync(_homeserver.Url, AccessToken);

        Assert.IsType<SynapseAdminGateway>(gateway);
        Assert.Equal(("Synapse", expectedVersion), (gateway.ServerBrand, gateway.ServerVersion));
        Assert.Equal(new[] { WhoAmIPath, AdminVersionPath }, _homeserver.RequestPaths);
    }

    /// <summary>
    /// The room list's sort direction end to end, for each probe outcome (#541). Callers pass the dir of Synapse's
    /// documented order; only a Tuwunel server, whatever the case of its brand, gets the opposite dir, and only for the
    /// keys Synapse sorts largest first. The test doesn't depend on which gateway class the probe picks.
    /// </summary>
    [Theory]
    [InlineData(null, "Tuwunel", "joined_members", "b")]
    [InlineData(null, "tuwunel", "joined_members", "b")]
    [InlineData(null, "Tuwunel", "name", "f")]
    [InlineData(null, "Synapse", "joined_members", "f")]
    [InlineData(null, null, "joined_members", "f")] // federation unavailable: unknown brand
    [InlineData("Synapse", null, "joined_members", "f")] // discovery identified Synapse
    public async Task GetAuthenticated_RoomListDir_FollowsTheProbedServer(string? discoveredSoftware, string? federationBrand, string orderBy, string sentDir)
    {
        // A version both brands accept (Synapse >= 1.145.0, Tuwunel >= 1.8.2), so every case gets the admin API.
        _homeserver.FederationVersion = federationBrand == null
            ? () => Results.NotFound()
            : () => Results.Json(new { server = new { name = federationBrand, version = "1.165.0" } });
        _homeserver.AdminServerVersion = () => Results.Json(new { server_version = "1.165.0" });

        var gateway = await CreateAuthGateway(discoveredSoftware).GetAuthenticatedAsync(_homeserver.Url, AccessToken);
        await gateway.GetRoomListAsync(0, 10, orderBy, "f");

        Assert.Equal($"{RoomListPath}?from=0&limit=10&dir={sentDir}&order_by={orderBy}", Assert.Single(_homeserver.RoomListTargets));
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
    /// <see cref="AdminServerVersion"/> change the probe answers. Records every request path in order, and the full
    /// target of each room list request. When <see cref="StallFirstFederationProbe"/> is set, the first federation
    /// version request waits until the client aborts it.
    /// </summary>
    private sealed class FakeHomeserver : IAsyncDisposable
    {
        private WebApplication? _app;
        private readonly TaskCompletionSource _federationProbeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _federationRequests;
        private readonly ConcurrentQueue<string> _requestPaths = new();
        private readonly ConcurrentQueue<string> _roomListTargets = new();

        public string Url { get; private set; } = null!;
        public IEnumerable<string> RequestPaths => _requestPaths;
        public IEnumerable<string> RoomListTargets => _roomListTargets;
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
            _app.MapGet(RoomListPath, (Func<HttpContext, IResult>)(context =>
            {
                _roomListTargets.Enqueue(context.Request.Path.Value + context.Request.QueryString.Value);
                return Results.Json(new { offset = 0, total_rooms = 0, rooms = Array.Empty<object>() });
            }));

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
