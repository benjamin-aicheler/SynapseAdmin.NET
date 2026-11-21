using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
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
using SynapseAdmin.Models.Requests;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of the five <see cref="SynapseCompatibleAdminGateway"/> reads that deserialize with
/// <c>SynapseCompatibilityJsonOptions</c>, against a real HTTP server: the exact request target, numeric and string
/// <c>next_token</c>, and how non-success responses surface. LibMatrix's <c>MatrixHttpClient</c> throws before the
/// gateway's own <c>EnsureSuccessStatusCode</c>: <see cref="MatrixException"/> for a Matrix JSON error body,
/// <see cref="HttpRequestException"/> for any other body.
/// Also the four raw DELETEs: the exact request (method, target, Content-Type, body) and the same error behavior.
/// Also setting room directory visibility: the exact PUT, its cancellation token and the same error behavior.
/// Also the seven POSTs that send <c>{}</c> and read no response body: the exact request, that a server accepting only
/// the documented POST (as Synapse does) accepts them (#516), and the same error behavior.
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

    public static TheoryData<string> DeleteMethods => new()
    {
        nameof(SynapseCompatibleAdminGateway.DeleteRoomAsync),
        nameof(SynapseCompatibleAdminGateway.DeleteEventReportAsync),
        nameof(SynapseCompatibleAdminGateway.DeleteRegistrationTokenAsync),
        nameof(SynapseCompatibleAdminGateway.DeleteMediaAsync),
    };

    // Each raw DELETE with fixed arguments and the exact request it sends. The room body uses default (not web)
    // serializer options: declared property order, nulls written.
    private static (CapturedRequest Request, Func<SynapseCompatibleAdminGateway, Task> Call) DeleteMethod(string name) => name switch
    {
        nameof(SynapseCompatibleAdminGateway.DeleteRoomAsync) => (
            new("DELETE", "/_synapse/admin/v2/rooms/!room%3aexample.com", "application/json; charset=utf-8",
                """{"new_room_user_id":"@admin:example.com","room_name":null,"block":true,"purge":false,"message":"Moved"}"""),
            g => g.DeleteRoomAsync("!room:example.com",
                new SynapseAdminRoomDeleteRequest { NewRoomUserId = "@admin:example.com", Block = true, Purge = false, Message = "Moved" })),
        nameof(SynapseCompatibleAdminGateway.DeleteEventReportAsync) => (
            new("DELETE", "/_synapse/admin/v1/event_reports/42", null, ""),
            g => g.DeleteEventReportAsync("42")),
        nameof(SynapseCompatibleAdminGateway.DeleteRegistrationTokenAsync) => (
            new("DELETE", "/_synapse/admin/v1/registration_tokens/invite%2b1", null, ""),
            g => g.DeleteRegistrationTokenAsync("invite+1")),
        nameof(SynapseCompatibleAdminGateway.DeleteMediaAsync) => (
            new("DELETE", "/_synapse/admin/v1/media/example.com/media-a", null, ""),
            g => g.DeleteMediaAsync("example.com", "media-a")),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };

    [Theory]
    [MemberData(nameof(DeleteMethods))]
    public async Task Delete_SendsTheSameRequest(string method)
    {
        var (request, call) = DeleteMethod(method);
        _homeserver.Respond = () => Results.Json(new { });

        await call(_gateway);

        Assert.Equal(request, Assert.Single(_homeserver.Requests));
    }

    [Theory]
    [MemberData(nameof(DeleteMethods))]
    public async Task Delete_MatrixErrorResponse_ThrowsMatrixException(string method)
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_NOT_FOUND", error = "Not found" }, statusCode: 404);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => DeleteMethod(method).Call(_gateway));

        Assert.Equal("M_NOT_FOUND", ex.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(DeleteMethods))]
    public async Task Delete_NonJsonErrorResponse_ThrowsHttpRequestException(string method)
    {
        _homeserver.Respond = () => Results.Text("Internal Server Error", statusCode: 500);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => DeleteMethod(method).Call(_gateway));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    public static TheoryData<string> EmptyBodyMethods => new()
    {
        nameof(SynapseCompatibleAdminGateway.QuarantineMediaByUserIdAsync),
        nameof(SynapseCompatibleAdminGateway.QuarantineMediaByRoomIdAsync),
        nameof(SynapseCompatibleAdminGateway.ResetFederationConnectionTimeoutAsync),
        nameof(SynapseCompatibleAdminGateway.QuarantineMediaAsync),
        nameof(SynapseCompatibleAdminGateway.UnquarantineMediaAsync),
        nameof(SynapseCompatibleAdminGateway.ProtectMediaAsync),
        nameof(SynapseCompatibleAdminGateway.UnprotectMediaAsync),
    };

    // Each POST that sends an empty JSON object and reads no response body, with fixed arguments and the exact
    // request it sends. Protect/unprotect ignore serverName, so it doesn't appear in their targets.
    private static (CapturedRequest Request, Func<SynapseCompatibleAdminGateway, Task> Call) EmptyBodyMethod(string name) => name switch
    {
        nameof(SynapseCompatibleAdminGateway.QuarantineMediaByUserIdAsync) => (
            EmptyJsonObject("POST", "/_synapse/admin/v1/user/%40alice%3aexample.com/media/quarantine"),
            g => g.QuarantineMediaByUserIdAsync("@alice:example.com")),
        nameof(SynapseCompatibleAdminGateway.QuarantineMediaByRoomIdAsync) => (
            EmptyJsonObject("POST", "/_synapse/admin/v1/room/!room%3aexample.com/media/quarantine"),
            g => g.QuarantineMediaByRoomIdAsync("!room:example.com")),
        nameof(SynapseCompatibleAdminGateway.ResetFederationConnectionTimeoutAsync) => (
            EmptyJsonObject("POST", "/_synapse/admin/v1/federation/destinations/matrix.example.org%3a8448/reset_connection"),
            g => g.ResetFederationConnectionTimeoutAsync("matrix.example.org:8448")),
        nameof(SynapseCompatibleAdminGateway.QuarantineMediaAsync) => (
            EmptyJsonObject("POST", "/_synapse/admin/v1/media/quarantine/example.com/media-a"),
            g => g.QuarantineMediaAsync("example.com", "media-a")),
        nameof(SynapseCompatibleAdminGateway.UnquarantineMediaAsync) => (
            EmptyJsonObject("POST", "/_synapse/admin/v1/media/unquarantine/example.com/media-a"),
            g => g.UnquarantineMediaAsync("example.com", "media-a")),
        nameof(SynapseCompatibleAdminGateway.ProtectMediaAsync) => (
            EmptyJsonObject("POST", "/_synapse/admin/v1/media/protect/media-a"),
            g => g.ProtectMediaAsync("remote.example", "media-a")),
        nameof(SynapseCompatibleAdminGateway.UnprotectMediaAsync) => (
            EmptyJsonObject("POST", "/_synapse/admin/v1/media/unprotect/media-a"),
            g => g.UnprotectMediaAsync("remote.example", "media-a")),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };

    private static CapturedRequest EmptyJsonObject(string method, string target) =>
        new(method, target, "application/json; charset=utf-8", "{}");

    [Theory]
    [MemberData(nameof(EmptyBodyMethods))]
    public async Task EmptyBody_SendsTheSameRequest(string method)
    {
        var (request, call) = EmptyBodyMethod(method);
        _homeserver.Respond = () => Results.Json(new { });

        await call(_gateway);

        Assert.Equal(request, Assert.Single(_homeserver.Requests));
    }

    // Synapse registers only POST for all seven routes and answers any other method with 405 M_UNRECOGNIZED (#516).
    [Theory]
    [MemberData(nameof(EmptyBodyMethods))]
    public async Task EmptyBody_ServerAcceptingOnlyPost_Succeeds(string method)
    {
        _homeserver.AllowedMethod = "POST";
        _homeserver.Respond = () => Results.Json(new { });

        await EmptyBodyMethod(method).Call(_gateway);

        Assert.Equal("POST", Assert.Single(_homeserver.Requests).Method);
    }

    [Fact]
    public async Task FakeHomeserver_WithAllowedMethod_RejectsOtherMethods()
    {
        _homeserver.AllowedMethod = "POST";
        _homeserver.Respond = () => Results.Json(new { });
        using var client = new HttpClient { BaseAddress = new Uri(_homeserver.Url) };

        using var response = await client.PutAsJsonAsync("/_synapse/admin/v1/media/quarantine/example.com/media-a", new { });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Contains("M_UNRECOGNIZED", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [MemberData(nameof(EmptyBodyMethods))]
    public async Task EmptyBody_MatrixErrorResponse_ThrowsMatrixException(string method)
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_FORBIDDEN", error = "You are not a server admin" }, statusCode: 403);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => EmptyBodyMethod(method).Call(_gateway));

        Assert.Equal("M_FORBIDDEN", ex.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(EmptyBodyMethods))]
    public async Task EmptyBody_NonJsonErrorResponse_ThrowsHttpRequestException(string method)
    {
        _homeserver.Respond = () => Results.Text("Internal Server Error", statusCode: 500);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => EmptyBodyMethod(method).Call(_gateway));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    // Room directory visibility (MatrixGatewayBase, inherited): the Client-Server spec defines only GET and PUT on
    // /_matrix/client/v3/directory/list/room/{roomId}, with {"visibility": "public" | "private"} (#517).
    // "public" in any case publishes; any other value unpublishes, as before.
    public static TheoryData<string, string> DirectoryVisibilities => new()
    {
        { "public", "public" },
        { "PUBLIC", "public" },
        { "private", "private" },
    };

    private const string DirectoryVisibilityTarget = "/_matrix/client/v3/directory/list/room/!room%3aexample.com";

    [Theory]
    [MemberData(nameof(DirectoryVisibilities))]
    public async Task SetRoomDirectoryVisibility_SendsPutWithVisibility(string visibility, string expected)
    {
        _homeserver.AllowedMethod = "PUT";
        _homeserver.Respond = () => Results.Json(new { });

        await _gateway.SetRoomDirectoryVisibilityAsync("!room:example.com", visibility);

        Assert.Equal(
            new CapturedRequest("PUT", DirectoryVisibilityTarget, "application/json; charset=utf-8", $$"""{"visibility":"{{expected}}"}"""),
            Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task SetRoomDirectoryVisibility_CancelledToken_SendsNoRequest()
    {
        _homeserver.Respond = () => Results.Json(new { });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _gateway.SetRoomDirectoryVisibilityAsync("!room:example.com", "public", cts.Token));

        Assert.Empty(_homeserver.Requests);
    }

    [Fact]
    public async Task SetRoomDirectoryVisibility_MatrixErrorResponse_ThrowsMatrixException()
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_NOT_FOUND", error = "Room not found" }, statusCode: 404);

        var ex = await Assert.ThrowsAsync<MatrixException>(
            () => _gateway.SetRoomDirectoryVisibilityAsync("!room:example.com", "public"));

        Assert.Equal("M_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task SetRoomDirectoryVisibility_NonJsonErrorResponse_ThrowsHttpRequestException()
    {
        _homeserver.Respond = () => Results.Text("Internal Server Error", statusCode: 500);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => _gateway.SetRoomDirectoryVisibilityAsync("!room:example.com", "private"));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    /// <summary>One request as the server received it. <see cref="Target"/> is the path and query exactly as sent.</summary>
    private sealed record CapturedRequest(string Method, string Target, string? ContentType, string Body);

    /// <summary>
    /// A Kestrel server that records every request (any HTTP method) and answers with <see cref="Respond"/>. When
    /// <see cref="AllowedMethod"/> is set, any other method gets Synapse's 405 <c>M_UNRECOGNIZED</c> instead.
    /// </summary>
    private sealed class FakeHomeserver : IAsyncDisposable
    {
        private WebApplication? _app;

        public string Url { get; private set; } = null!;
        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();
        public IEnumerable<string> RequestTargets => Requests.Select(r => r.Target);
        public Func<IResult> Respond { get; set; } = () => Results.NotFound();
        public string? AllowedMethod { get; set; }

        public async Task StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            _app = builder.Build();

            _app.Map("/{**path}", (Func<HttpContext, Task<IResult>>)(async context =>
            {
                using var reader = new StreamReader(context.Request.Body);
                Requests.Enqueue(new CapturedRequest(context.Request.Method, context.Features.Get<IHttpRequestFeature>()!.RawTarget,
                    context.Request.ContentType, await reader.ReadToEndAsync()));
                if (AllowedMethod != null && !HttpMethods.Equals(context.Request.Method, AllowedMethod))
                {
                    return Results.Json(new { errcode = "M_UNRECOGNIZED", error = "Unrecognized request" }, statusCode: 405);
                }

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
