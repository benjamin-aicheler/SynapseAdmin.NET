using System.Collections.Concurrent;
using System.Net;
using LibMatrix;
using LibMatrix.Homeservers;
using LibMatrix.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SynapseAdmin.Infrastructure.Gateways;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of the five <see cref="SynapseCompatibleAdminGateway"/> reads that deserialize with
/// <c>SynapseCompatibilityJsonOptions</c>, against a real HTTP server: the exact request target, numeric and string
/// <c>next_token</c>, and how non-success responses surface. LibMatrix's <c>MatrixHttpClient</c> throws before the
/// gateway's own <c>EnsureSuccessStatusCode</c>: <see cref="MatrixException"/> for a Matrix JSON error body,
/// <see cref="HttpRequestException"/> for any other body.
/// </summary>
public sealed class SynapseCompatibleGatewayCharacterizationTests : IAsyncLifetime
{
    private readonly FakeHomeserver _homeserver = new();
    private SynapseCompatibleAdminGateway _gateway = null!;

    public async Task InitializeAsync()
    {
        await _homeserver.StartAsync();
        _gateway = new SynapseCompatibleAdminGateway(new AuthenticatedHomeserverGeneric(
            "example.com", new HomeserverResolverService.WellKnownUris { Client = _homeserver.Url }, null, "syt_token"));
    }

    public async Task DisposeAsync() => await _homeserver.DisposeAsync();

    public static TheoryData<string> Methods => new()
    {
        nameof(SynapseCompatibleAdminGateway.GetUserListAsync),
        nameof(SynapseCompatibleAdminGateway.GetUserMediaStatisticsAsync),
        nameof(SynapseCompatibleAdminGateway.GetUserMediaAsync),
        nameof(SynapseCompatibleAdminGateway.GetFederationDestinationListAsync),
        nameof(SynapseCompatibleAdminGateway.GetEventReportListAsync),
    };

    // Each method with fixed arguments: the request target it sends, and a call that returns the parsed next_token.
    private static (string Target, Func<SynapseCompatibleAdminGateway, Task<string?>> Call) Method(string name) => name switch
    {
        nameof(SynapseCompatibleAdminGateway.GetUserListAsync) => (
            "/_synapse/admin/v3/users?from=20&limit=10&dir=f&order_by=name&user_id=%40alice%3aexample.com",
            async g => (await g.GetUserListAsync(20, 10, "name", "f", "@alice:example.com"))?.NextToken),
        nameof(SynapseCompatibleAdminGateway.GetUserMediaStatisticsAsync) => (
            "/_synapse/admin/v1/statistics/users/media?limit=10&order_by=media_length&dir=b&search_term=alice",
            async g => (await g.GetUserMediaStatisticsAsync(10, "media_length", "b", "alice"))?.NextToken),
        nameof(SynapseCompatibleAdminGateway.GetUserMediaAsync) => (
            "/_synapse/admin/v1/users/%40alice%3aexample.com/media?from=100&limit=25&order_by=created_ts&dir=b",
            async g => (await g.GetUserMediaAsync("@alice:example.com", 100, 25))?.NextToken),
        nameof(SynapseCompatibleAdminGateway.GetFederationDestinationListAsync) => (
            "/_synapse/admin/v1/federation/destinations?from=0&limit=50&dir=b",
            async g => (await g.GetFederationDestinationListAsync(0, 50, "b"))?.NextToken),
        nameof(SynapseCompatibleAdminGateway.GetEventReportListAsync) => (
            "/_synapse/admin/v1/event_reports?from=5&limit=10&dir=b&room_id=!room%3aexample.com",
            async g => (await g.GetEventReportListAsync(5, 10, "b", "!room:example.com"))?.NextToken),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Get_SendsTheSameRequest_AndReadsNumericAndStringNextTokens(string method)
    {
        var (target, call) = Method(method);

        _homeserver.Respond = () => Results.Json(new { next_token = 100 });
        var numeric = await call(_gateway);
        _homeserver.Respond = () => Results.Json(new { next_token = "100" });
        var text = await call(_gateway);

        Assert.Equal(("100", "100"), (numeric, text));
        Assert.Equal(new[] { target, target }, _homeserver.RequestTargets);
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Get_MatrixErrorResponse_ThrowsMatrixException(string method)
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_FORBIDDEN", error = "You are not a server admin" }, statusCode: 403);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => Method(method).Call(_gateway));

        Assert.Equal("M_FORBIDDEN", ex.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Get_NonJsonErrorResponse_ThrowsHttpRequestException(string method)
    {
        _homeserver.Respond = () => Results.Text("Internal Server Error", statusCode: 500);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Method(method).Call(_gateway));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    /// <summary>A Kestrel server that records every request target (path and query, as sent) and answers with <see cref="Respond"/>.</summary>
    private sealed class FakeHomeserver : IAsyncDisposable
    {
        private WebApplication? _app;

        public string Url { get; private set; } = null!;
        public ConcurrentQueue<string> RequestTargets { get; } = new();
        public Func<IResult> Respond { get; set; } = () => Results.NotFound();

        public async Task StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            _app = builder.Build();

            _app.MapGet("/{**path}", (Func<HttpContext, IResult>)(context =>
            {
                RequestTargets.Enqueue(context.Features.Get<IHttpRequestFeature>()!.RawTarget);
                return Respond();
            }));

            await _app.StartAsync();
            Url = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
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
