using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of the five <see cref="SynapseAdminGateway"/> reads that deserialize with
/// <c>SynapseCompatibilityJsonOptions</c>, against a real HTTP server: the exact request target, numeric and string
/// <c>next_token</c>, and how non-success responses surface. LibMatrix's <c>MatrixHttpClient</c> throws before the
/// gateway's own <c>EnsureSuccessStatusCode</c>: <see cref="MatrixException"/> for a Matrix JSON error body,
/// <see cref="HttpRequestException"/> for any other body.
/// Also the four raw DELETEs: the exact request (method, target, Content-Type, body) and the same error behavior.
/// Also the registration token update and create bodies (explicit vs omitted nulls).
/// Also setting room directory visibility: the exact PUT, its cancellation token and the same error behavior.
/// Also blocking and unblocking a room and reading its block state (#586): the exact PUT and GET, their cancellation
/// token and the same error behavior.
/// Also the Client-Server alias and directory calls (#580): their results and the same error behavior.
/// Also the seven POSTs that send <c>{}</c> and read no response body: the exact request, that a server accepting only
/// the documented POST (as Synapse does) accepts them (#516), and the same error behavior.
/// Also room deletion (#518): the <c>delete_id</c> the DELETE returns, and the two status reads with the answers that
/// mean "no status".
/// Also the room list's sort direction per brand (#541).
/// Also the shadow login (#539): the token's expiry travels in the body, not the query string.
/// Also the federation destinations' <c>from</c>, the previous <c>next_token</c> passed back unchanged (#551).
/// Also suspending and shadow-banning a user and resetting a password (#584): the exact PUT, POST and DELETE, their
/// cancellation token and the same error behavior, and the password resets Tuwunel refuses.
/// Also a user's devices and recent connections (#598): the exact GET, PUT and POST, the encoding of user and device IDs,
/// the answers Synapse and Tuwunel send, their cancellation token and the same error behavior, and the session's device
/// ID from whoami.
/// Also deleting a user's media page by page (#599): the exact DELETE, always from the first page, its answer, its
/// cancellation token and the same error behavior.
/// Also redacting a user's events (#600): the exact POST with <c>rooms</c> always sent, the status read by
/// <c>redact_id</c> with "no status" for an unknown one, the cancellation token, the same error behavior, and Tuwunel's
/// refusal of both.
/// Also room recovery (#587): the exact make room admin and join POSTs, their cancellation token and the same error
/// behavior, Tuwunel's refusal of make room admin, and its join with Synapse's invite first.
/// </summary>
public sealed class SynapseAdminGatewayCharacterizationTests : IAsyncLifetime
{
    private readonly FakeHomeserver _homeserver = new();
    private SynapseAdminGateway _gateway = null!;

    public async Task InitializeAsync()
    {
        await _homeserver.StartAsync();
        _gateway = new SynapseAdminGateway(new AuthenticatedHomeserverGeneric(
            "example.com", new HomeserverResolverService.WellKnownUris { Client = _homeserver.Url }, null, "syt_token"));
    }

    public async Task DisposeAsync() => await _homeserver.DisposeAsync();

    public static TheoryData<string> Methods => new()
    {
        nameof(SynapseAdminGateway.GetUserListAsync),
        nameof(SynapseAdminGateway.GetUserMediaStatisticsAsync),
        nameof(SynapseAdminGateway.GetUserMediaAsync),
        nameof(SynapseAdminGateway.GetFederationDestinationListAsync),
        nameof(SynapseAdminGateway.GetEventReportListAsync),
    };

    // Each method with fixed arguments: the request target it sends, and a call that returns the parsed next_token.
    private static (string Target, Func<SynapseAdminGateway, Task<string?>> Call) Method(string name) => name switch
    {
        nameof(SynapseAdminGateway.GetUserListAsync) => (
            "/_synapse/admin/v3/users?from=20&limit=10&dir=f&order_by=name&locked=true&user_id=%40alice%3aexample.com",
            async g => (await g.GetUserListAsync(20, 10, "name", "f", "@alice:example.com"))?.NextToken),
        nameof(SynapseAdminGateway.GetUserMediaStatisticsAsync) => (
            "/_synapse/admin/v1/statistics/users/media?limit=10&order_by=media_length&dir=b&search_term=alice",
            async g => (await g.GetUserMediaStatisticsAsync(10, "media_length", "b", "alice"))?.NextToken),
        nameof(SynapseAdminGateway.GetUserMediaAsync) => (
            "/_synapse/admin/v1/users/%40alice%3aexample.com/media?from=100&limit=25&order_by=created_ts&dir=b",
            async g => (await g.GetUserMediaAsync("@alice:example.com", 100, 25))?.NextToken),
        nameof(SynapseAdminGateway.GetFederationDestinationListAsync) => (
            "/_synapse/admin/v1/federation/destinations?from=100&limit=50&dir=b",
            async g => (await g.GetFederationDestinationListAsync("100", 50, "b"))?.NextToken),
        nameof(SynapseAdminGateway.GetEventReportListAsync) => (
            "/_synapse/admin/v1/event_reports?from=5&limit=10&dir=b&room_id=!room%3aexample.com",
            async g => (await g.GetEventReportListAsync(5, 10, "b", new EventReportListFilter(RoomId: "!room:example.com")))?.NextToken),
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

    /// <summary>
    /// The first page sends no <c>from</c>; later pages pass the previous <c>next_token</c> back unchanged, encoded, so a
    /// cursor that isn't a number also works (#551).
    /// </summary>
    [Theory]
    [InlineData(null, "/_synapse/admin/v1/federation/destinations?limit=10000&dir=f")]
    [InlineData("10000", "/_synapse/admin/v1/federation/destinations?from=10000&limit=10000&dir=f")]
    [InlineData("s1 a+b/c&d=e", "/_synapse/admin/v1/federation/destinations?from=s1+a%2bb%2fc%26d%3de&limit=10000&dir=f")]
    public async Task GetFederationDestinationList_PassesTheTokenBackUnchanged(string? from, string target)
    {
        _homeserver.Respond = () => Results.Json(new { destinations = Array.Empty<object>(), total = 0 });

        await _gateway.GetFederationDestinationListAsync(from, 10000, "f");

        Assert.Equal([target], _homeserver.RequestTargets);
    }

    /// <summary>
    /// <c>locked</c> defaults to <c>false</c>, which leaves locked users out of the rows and the total, so every list
    /// request includes them (#547). Without <c>deactivated</c>, v3 includes deactivated users: the list is every account.
    /// </summary>
    [Theory]
    [InlineData(null, "/_synapse/admin/v3/users?from=0&limit=10&dir=b&order_by=creation_ts&locked=true")]
    [InlineData("alice", "/_synapse/admin/v3/users?from=0&limit=10&dir=b&order_by=creation_ts&locked=true&name=alice")]
    [InlineData("@alice:example.com", "/_synapse/admin/v3/users?from=0&limit=10&dir=b&order_by=creation_ts&locked=true&user_id=%40alice%3aexample.com")]
    public async Task GetUserList_IncludesLockedUsers(string? searchTerm, string target)
    {
        _homeserver.Respond = () => Results.Json(new { users = Array.Empty<object>(), total = 0 });

        await _gateway.GetUserListAsync(0, 10, "creation_ts", "b", searchTerm);

        Assert.Equal([target], _homeserver.RequestTargets);
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
        nameof(SynapseAdminGateway.DeleteRoomAsync),
        nameof(SynapseAdminGateway.DeleteEventReportAsync),
        nameof(SynapseAdminGateway.DeleteRegistrationTokenAsync),
        nameof(SynapseAdminGateway.DeleteMediaAsync),
    };

    // Each raw DELETE with fixed arguments and the exact request it sends. The room body uses default (not web)
    // serializer options in declared property order, and leaves out the optional strings that are null (#543).
    private static (CapturedRequest Request, Func<SynapseAdminGateway, Task> Call) DeleteMethod(string name) => name switch
    {
        nameof(SynapseAdminGateway.DeleteRoomAsync) => (
            new("DELETE", "/_synapse/admin/v2/rooms/!room%3aexample.com", "application/json; charset=utf-8",
                """{"new_room_user_id":"@admin:example.com","block":true,"purge":false,"message":"Moved"}"""),
            g => g.DeleteRoomAsync("!room:example.com",
                new SynapseAdminRoomDeleteRequest { NewRoomUserId = "@admin:example.com", Block = true, Purge = false, Message = "Moved" })),
        nameof(SynapseAdminGateway.DeleteEventReportAsync) => (
            new("DELETE", "/_synapse/admin/v1/event_reports/42", null, ""),
            g => g.DeleteEventReportAsync("42")),
        nameof(SynapseAdminGateway.DeleteRegistrationTokenAsync) => (
            new("DELETE", "/_synapse/admin/v1/registration_tokens/invite%2b1", null, ""),
            g => g.DeleteRegistrationTokenAsync("invite+1")),
        nameof(SynapseAdminGateway.DeleteMediaAsync) => (
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
        nameof(SynapseAdminGateway.QuarantineMediaByUserIdAsync),
        nameof(SynapseAdminGateway.QuarantineMediaByRoomIdAsync),
        nameof(SynapseAdminGateway.ResetFederationConnectionTimeoutAsync),
        nameof(SynapseAdminGateway.QuarantineMediaAsync),
        nameof(SynapseAdminGateway.UnquarantineMediaAsync),
        nameof(SynapseAdminGateway.ProtectMediaAsync),
        nameof(SynapseAdminGateway.UnprotectMediaAsync),
    };

    // Each POST that sends an empty JSON object and reads no response body, with fixed arguments and the exact
    // request it sends. Protect/unprotect ignore serverName, so it doesn't appear in their targets.
    private static (CapturedRequest Request, Func<SynapseAdminGateway, Task> Call) EmptyBodyMethod(string name) => name switch
    {
        nameof(SynapseAdminGateway.QuarantineMediaByUserIdAsync) => (
            EmptyJsonObject("POST", "/_synapse/admin/v1/user/%40alice%3aexample.com/media/quarantine"),
            g => g.QuarantineMediaByUserIdAsync("@alice:example.com")),
        nameof(SynapseAdminGateway.QuarantineMediaByRoomIdAsync) => (
            EmptyJsonObject("POST", "/_synapse/admin/v1/room/!room%3aexample.com/media/quarantine"),
            g => g.QuarantineMediaByRoomIdAsync("!room:example.com")),
        nameof(SynapseAdminGateway.ResetFederationConnectionTimeoutAsync) => (
            EmptyJsonObject("POST", "/_synapse/admin/v1/federation/destinations/matrix.example.org%3a8448/reset_connection"),
            g => g.ResetFederationConnectionTimeoutAsync("matrix.example.org:8448")),
        nameof(SynapseAdminGateway.QuarantineMediaAsync) => (
            EmptyJsonObject("POST", "/_synapse/admin/v1/media/quarantine/example.com/media-a"),
            g => g.QuarantineMediaAsync("example.com", "media-a")),
        nameof(SynapseAdminGateway.UnquarantineMediaAsync) => (
            EmptyJsonObject("POST", "/_synapse/admin/v1/media/unquarantine/example.com/media-a"),
            g => g.UnquarantineMediaAsync("example.com", "media-a")),
        nameof(SynapseAdminGateway.ProtectMediaAsync) => (
            EmptyJsonObject("POST", "/_synapse/admin/v1/media/protect/media-a"),
            g => g.ProtectMediaAsync("remote.example", "media-a")),
        nameof(SynapseAdminGateway.UnprotectMediaAsync) => (
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

    // Blocking and unblocking a room (#586): one PUT on /_synapse/admin/v1/rooms/{roomId}/block with {"block": true | false}.
    private const string RoomBlockTarget = "/_synapse/admin/v1/rooms/!room%3aexample.com/block";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BlockRoom_SendsPutWithBlock(bool block)
    {
        _homeserver.AllowedMethod = "PUT";
        _homeserver.Respond = () => Results.Json(new { block });

        await _gateway.BlockRoomAsync("!room:example.com", block);

        Assert.Equal(
            new CapturedRequest("PUT", RoomBlockTarget, "application/json; charset=utf-8", $$"""{"block":{{(block ? "true" : "false")}}}"""),
            Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task BlockRoom_CancelledToken_SendsNoRequest()
    {
        _homeserver.Respond = () => Results.Json(new { block = true });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _gateway.BlockRoomAsync("!room:example.com", true, cts.Token));

        Assert.Empty(_homeserver.Requests);
    }

    [Fact]
    public async Task BlockRoom_MatrixErrorResponse_ThrowsMatrixException()
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_FORBIDDEN", error = "You are not a server admin" }, statusCode: 403);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => _gateway.BlockRoomAsync("!room:example.com", false));

        Assert.Equal("M_FORBIDDEN", ex.ErrorCode);
    }

    [Fact]
    public async Task BlockRoom_NonJsonErrorResponse_ThrowsHttpRequestException()
    {
        _homeserver.Respond = () => Results.Text("Internal Server Error", statusCode: 500);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _gateway.BlockRoomAsync("!room:example.com", true));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    // Reading a room's block state (#586): GET on the same path; user_id is only present for a blocked room, and Tuwunel
    // may leave it out even then.
    [Fact]
    public async Task GetRoomBlockStatus_Blocked_ReadsTheBlocker()
    {
        _homeserver.Respond = () => Results.Json(new { block = true, user_id = "@admin:example.com" });

        var status = await _gateway.GetRoomBlockStatusAsync("!room:example.com");

        Assert.Equal((true, "@admin:example.com"), (status!.Block, status.UserId));
        Assert.Equal(new CapturedRequest("GET", RoomBlockTarget, null, ""), Assert.Single(_homeserver.Requests));
    }

    [Theory]
    [InlineData("""{"block":false}""", false)]
    [InlineData("""{"block":true}""", true)]
    public async Task GetRoomBlockStatus_WithoutUserId_ReadsNoBlocker(string body, bool blocked)
    {
        _homeserver.Respond = () => Results.Text(body, "application/json");

        var status = await _gateway.GetRoomBlockStatusAsync("!room:example.com");

        Assert.Equal((blocked, (string?)null), (status!.Block, status.UserId));
    }

    [Fact]
    public async Task GetRoomBlockStatus_CancelledToken_SendsNoRequest()
    {
        _homeserver.Respond = () => Results.Json(new { block = false });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _gateway.GetRoomBlockStatusAsync("!room:example.com", cts.Token));

        Assert.Empty(_homeserver.Requests);
    }

    [Fact]
    public async Task GetRoomBlockStatus_MatrixErrorResponse_ThrowsMatrixException()
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_FORBIDDEN", error = "You are not a server admin" }, statusCode: 403);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => _gateway.GetRoomBlockStatusAsync("!room:example.com"));

        Assert.Equal("M_FORBIDDEN", ex.ErrorCode);
    }

    [Fact]
    public async Task GetRoomBlockStatus_NonJsonErrorResponse_ThrowsHttpRequestException()
    {
        _homeserver.Respond = () => Results.Text("Internal Server Error", statusCode: 500);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _gateway.GetRoomBlockStatusAsync("!room:example.com"));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    // The Client-Server alias and directory calls in MatrixGatewayBase (#580): what each returns, and how errors surface.
    public static TheoryData<string> ClientServerMethods => new()
    {
        nameof(SynapseAdminGateway.PutRoomAliasAsync),
        nameof(SynapseAdminGateway.DeleteRoomAliasAsync),
        nameof(SynapseAdminGateway.GetLocalRoomAliasesAsync),
        nameof(SynapseAdminGateway.GetRoomDirectoryVisibilityAsync),
        nameof(SynapseAdminGateway.GetPublicRoomsAsync) + ":get",
        nameof(SynapseAdminGateway.GetPublicRoomsAsync) + ":post",
    };

    // Each call with fixed arguments and a cancellation token. The public room directory sends a GET without a search
    // term and a POST with one.
    private static Func<SynapseAdminGateway, CancellationToken, Task> ClientServerMethod(string name) => name switch
    {
        nameof(SynapseAdminGateway.PutRoomAliasAsync) => (g, ct) => g.PutRoomAliasAsync("#lobby:example.com", "!room:example.com", ct),
        nameof(SynapseAdminGateway.DeleteRoomAliasAsync) => (g, ct) => g.DeleteRoomAliasAsync("#lobby:example.com", ct),
        nameof(SynapseAdminGateway.GetLocalRoomAliasesAsync) => (g, ct) => g.GetLocalRoomAliasesAsync("!room:example.com", ct),
        nameof(SynapseAdminGateway.GetRoomDirectoryVisibilityAsync) => (g, ct) => g.GetRoomDirectoryVisibilityAsync("!room:example.com", ct),
        nameof(SynapseAdminGateway.GetPublicRoomsAsync) + ":get" => (g, ct) => g.GetPublicRoomsAsync(50, "next", null, ct),
        nameof(SynapseAdminGateway.GetPublicRoomsAsync) + ":post" => (g, ct) => g.GetPublicRoomsAsync(50, "next", "lobby", ct),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };

    // The exact request each call sends. An alias is escaped with Uri.EscapeDataString, so whitespace can't turn into a
    // literal '+' in the path (#580); room IDs keep the form encoding every other path uses.
    private static CapturedRequest ClientServerRequest(string name) => name switch
    {
        nameof(SynapseAdminGateway.PutRoomAliasAsync) => new("PUT", "/_matrix/client/v3/directory/room/%23lobby%3Aexample.com",
            "application/json; charset=utf-8", """{"room_id":"!room:example.com"}"""),
        nameof(SynapseAdminGateway.DeleteRoomAliasAsync) => new("DELETE", "/_matrix/client/v3/directory/room/%23lobby%3Aexample.com", null, ""),
        nameof(SynapseAdminGateway.GetLocalRoomAliasesAsync) => new("GET", "/_matrix/client/v3/rooms/!room%3aexample.com/aliases", null, ""),
        nameof(SynapseAdminGateway.GetRoomDirectoryVisibilityAsync) => new("GET", DirectoryVisibilityTarget, null, ""),
        nameof(SynapseAdminGateway.GetPublicRoomsAsync) + ":get" => new("GET", "/_matrix/client/v3/publicRooms?limit=50&since=next", null, ""),
        nameof(SynapseAdminGateway.GetPublicRoomsAsync) + ":post" => new("POST", "/_matrix/client/v3/publicRooms",
            "application/json; charset=utf-8", """{"filter":{"generic_search_term":"lobby"},"limit":50,"since":"next"}"""),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };

    [Theory]
    [MemberData(nameof(ClientServerMethods))]
    public async Task ClientServer_SendsTheDocumentedRequest(string method)
    {
        _homeserver.Respond = () => Results.Json(new { aliases = Array.Empty<string>(), visibility = "public", chunk = Array.Empty<object>() });

        await ClientServerMethod(method)(_gateway, CancellationToken.None);

        Assert.Equal(ClientServerRequest(method), Assert.Single(_homeserver.Requests));
    }

    [Theory]
    [MemberData(nameof(ClientServerMethods))]
    public async Task ClientServer_CancelledToken_SendsNoRequest(string method)
    {
        _homeserver.Respond = () => Results.Json(new { aliases = Array.Empty<string>(), visibility = "public", chunk = Array.Empty<object>() });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClientServerMethod(method)(_gateway, cts.Token));

        Assert.Empty(_homeserver.Requests);
    }

    /// <summary>
    /// Synapse rejects whitespace in an alias, but only if it arrives as whitespace: form encoding would send a space as
    /// '+', which a path keeps, and create <c>#two+words</c> instead (#580).
    /// </summary>
    [Fact]
    public async Task PutAndDeleteRoomAlias_EscapeTheAliasAsAPathSegment()
    {
        const string target = "/_matrix/client/v3/directory/room/%23two%20words%2Bcaf%C3%A9%2F%3F%3Aexample.com";
        _homeserver.Respond = () => Results.Json(new { });

        await _gateway.PutRoomAliasAsync("#two words+café/?:example.com", "!room:example.com");
        await _gateway.DeleteRoomAliasAsync("#two words+café/?:example.com");

        Assert.Equal([target, target], _homeserver.RequestTargets);
    }

    /// <summary>
    /// Synapse's <c>next_batch</c> is standard base64 and can contain '+' and '/', which a query string must encode:
    /// an unencoded '+' reaches Synapse as a space and breaks the next page (#580). The POST carries it in JSON.
    /// </summary>
    [Fact]
    public async Task GetPublicRooms_EncodesTheSinceToken()
    {
        _homeserver.Respond = () => Results.Json(new { chunk = Array.Empty<object>() });

        await _gateway.GetPublicRoomsAsync(50, "a+b/c&d=e#f");

        Assert.Equal(["/_matrix/client/v3/publicRooms?limit=50&since=a%2bb%2fc%26d%3de%23f"], _homeserver.RequestTargets);
    }

    [Fact]
    public async Task PutAndDeleteRoomAlias_Succeed()
    {
        _homeserver.Respond = () => Results.Json(new { });

        await _gateway.PutRoomAliasAsync("#lobby:example.com", "!room:example.com");
        await _gateway.DeleteRoomAliasAsync("#lobby:example.com");

        Assert.Equal(["PUT", "DELETE"], _homeserver.Requests.Select(r => r.Method));
    }

    [Fact]
    public async Task GetLocalRoomAliases_ReturnsTheAliases()
    {
        _homeserver.Respond = () => Results.Json(new { aliases = new[] { "#lobby:example.com", "#hall:example.com" } });

        var aliases = await _gateway.GetLocalRoomAliasesAsync("!room:example.com");

        Assert.Equal(["#lobby:example.com", "#hall:example.com"], aliases);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    public async Task GetRoomDirectoryVisibility_ReturnsTheVisibility(string visibility)
    {
        _homeserver.Respond = () => Results.Json(new { visibility });

        Assert.Equal(visibility, await _gateway.GetRoomDirectoryVisibilityAsync("!room:example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("lobby")]
    public async Task GetPublicRooms_MapsTheResponse(string? searchTerm)
    {
        _homeserver.Respond = () => Results.Json(new
        {
            chunk = new[]
            {
                new
                {
                    avatar_url = "mxc://example.com/avatar", canonical_alias = "#lobby:example.com", guest_can_join = true,
                    join_rule = "public", name = "Lobby", num_joined_members = 3, room_id = "!room:example.com",
                    topic = "Welcome", world_readable = true
                }
            },
            next_batch = "n1",
            prev_batch = "p1",
            total_room_count_estimate = 7
        });

        var result = await _gateway.GetPublicRoomsAsync(50, "next", searchTerm);

        Assert.NotNull(result);
        Assert.Equal(("n1", "p1", (int?)7), (result.NextBatch, result.PrevBatch, result.TotalRoomCountEstimate));
        var room = Assert.Single(result.Chunk);
        Assert.Equal(
            ("mxc://example.com/avatar", "#lobby:example.com", true, "public", "Lobby", 3, "!room:example.com", "Welcome", true),
            (room.AvatarUrl, room.CanonicalAlias, room.GuestCanJoin, room.JoinRule, room.Name, room.NumJoinedMembers, room.RoomId,
                room.Topic, room.WorldReadable));
    }

    /// <summary>A missing estimate reads as 0, as LibMatrix's response type has always reported it.</summary>
    [Fact]
    public async Task GetPublicRooms_MissingFields_ReadAsDefaults()
    {
        _homeserver.Respond = () => Results.Json(new { chunk = Array.Empty<object>() });

        var result = await _gateway.GetPublicRoomsAsync(50);

        Assert.NotNull(result);
        Assert.Equal((null, null, (int?)0), (result.NextBatch, result.PrevBatch, result.TotalRoomCountEstimate));
        Assert.Empty(result.Chunk);
    }

    [Theory]
    [MemberData(nameof(ClientServerMethods))]
    public async Task ClientServer_MatrixErrorResponse_ThrowsMatrixException(string method)
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_NOT_FOUND", error = "Not found" }, statusCode: 404);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => ClientServerMethod(method)(_gateway, CancellationToken.None));

        Assert.Equal("M_NOT_FOUND", ex.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(ClientServerMethods))]
    public async Task ClientServer_NonJsonErrorResponse_ThrowsHttpRequestException(string method)
    {
        _homeserver.Respond = () => Results.Text("Not Found", statusCode: 404);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => ClientServerMethod(method)(_gateway, CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    // Registration token update (#519): Synapse leaves an omitted field unchanged and removes the limit for an explicit
    // null, so the update always sends both fields. Create keeps omitting null fields (Synapse's defaults apply).
    public static TheoryData<int?, long?, string> TokenUpdates => new()
    {
        { null, null, """{"uses_allowed":null,"expiry_time":null}""" },
        { null, 4781243146000, """{"uses_allowed":null,"expiry_time":4781243146000}""" },
        { 5, null, """{"uses_allowed":5,"expiry_time":null}""" },
        { 5, 4781243146000, """{"uses_allowed":5,"expiry_time":4781243146000}""" },
        // 0 is a cap that makes the token invalid without deleting it (#548), not "unlimited".
        { 0, null, """{"uses_allowed":0,"expiry_time":null}""" },
    };

    [Theory]
    [MemberData(nameof(TokenUpdates))]
    public async Task UpdateRegistrationToken_SendsBothFieldsWithExplicitNulls(int? usesAllowed, long? expiryTime, string body)
    {
        _homeserver.AllowedMethod = "PUT";
        _homeserver.Respond = () => Results.Json(new { token = "abcd" });

        await _gateway.UpdateRegistrationTokenAsync("abcd",
            new SynapseAdminRegistrationTokenUpdateRequest { UsesAllowed = usesAllowed, ExpiryTime = expiryTime });

        Assert.Equal(
            new CapturedRequest("PUT", "/_synapse/admin/v1/registration_tokens/abcd", "application/json; charset=utf-8", body),
            Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task CreateRegistrationToken_OmitsNullFields()
    {
        _homeserver.AllowedMethod = "POST";
        _homeserver.Respond = () => Results.Json(new { token = "abcd" });

        await _gateway.CreateRegistrationTokenAsync(new SynapseAdminRegistrationTokenCreateRequest { UsesAllowed = 1 });

        Assert.Equal(
            new CapturedRequest("POST", "/_synapse/admin/v1/registration_tokens/new", "application/json; charset=utf-8", """{"uses_allowed":1}"""),
            Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task CreateRegistrationToken_SendsAZeroCap()
    {
        _homeserver.AllowedMethod = "POST";
        _homeserver.Respond = () => Results.Json(new { token = "abcd" });

        await _gateway.CreateRegistrationTokenAsync(new SynapseAdminRegistrationTokenCreateRequest { UsesAllowed = 0 });

        Assert.Equal("""{"uses_allowed":0}""", Assert.Single(_homeserver.Requests).Body);
    }

    // ---- Registration tokens on Tuwunel (#548) -------------------------------------------------------

    // Tuwunel deletes a token as soon as it is invalid (its uses reached the cap, or its expiry passed), where Synapse
    // keeps it (a cap) or rejects the request (an expiry in the past). A request that would leave the token invalid at
    // once is refused, so the token isn't deleted (ADR 0006).
    private const long PastExpiry = 1_000;
    private const long FutureExpiry = 4_781_243_146_000;

    public static TheoryData<int?, long?> TuwunelInvalidNewTokens => new()
    {
        { 0, null },
        { null, PastExpiry },
        { 5, PastExpiry },
    };

    [Theory]
    [MemberData(nameof(TuwunelInvalidNewTokens))]
    public async Task Tuwunel_CreateRegistrationToken_InvalidAtOnce_RefusesWithoutARequest(int? usesAllowed, long? expiryTime)
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());

        var error = await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() => gateway.CreateRegistrationTokenAsync(
            new SynapseAdminRegistrationTokenCreateRequest { UsesAllowed = usesAllowed, ExpiryTime = expiryTime }));

        Assert.Equal(nameof(TuwunelAdminGateway.CreateRegistrationTokenAsync), error.Operation);
        Assert.Empty(_homeserver.Requests);
    }

    [Theory]
    [InlineData(null, null, "{}")]
    [InlineData(1, FutureExpiry, """{"uses_allowed":1,"expiry_time":4781243146000}""")]
    public async Task Tuwunel_CreateRegistrationToken_Valid_SendsTheSynapseRequest(int? usesAllowed, long? expiryTime, string body)
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.Respond = () => Results.Json(new { token = "abcd" });

        await gateway.CreateRegistrationTokenAsync(new SynapseAdminRegistrationTokenCreateRequest { UsesAllowed = usesAllowed, ExpiryTime = expiryTime });

        Assert.Equal(new CapturedRequest("POST", "/_synapse/admin/v1/registration_tokens/new", "application/json; charset=utf-8", body),
            Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task Tuwunel_UpdateRegistrationToken_PastExpiry_RefusesWithoutARequest()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());

        var error = await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() => gateway.UpdateRegistrationTokenAsync("abcd",
            new SynapseAdminRegistrationTokenUpdateRequest { UsesAllowed = null, ExpiryTime = PastExpiry }));

        Assert.Equal(nameof(TuwunelAdminGateway.UpdateRegistrationTokenAsync), error.Operation);
        Assert.Empty(_homeserver.Requests);
    }

    /// <summary>A cap reads the token's uses first and is refused at or below them; only the read is sent.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Tuwunel_UpdateRegistrationToken_CapAtOrBelowUses_RefusesAfterReadingTheToken(int usesAllowed)
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.Respond = () => Results.Json(new { token = "abcd", uses_allowed = 10, pending = 0, completed = 3 });

        await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() => gateway.UpdateRegistrationTokenAsync("abcd",
            new SynapseAdminRegistrationTokenUpdateRequest { UsesAllowed = usesAllowed, ExpiryTime = null }));

        Assert.Equal(new CapturedRequest("GET", "/_synapse/admin/v1/registration_tokens/abcd", null, ""), Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task Tuwunel_UpdateRegistrationToken_CapAboveUses_ReadsTheTokenThenSendsTheSynapseRequest()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.Respond = () => Results.Json(new { token = "abcd", uses_allowed = 10, pending = 0, completed = 3 });

        await gateway.UpdateRegistrationTokenAsync("abcd", new SynapseAdminRegistrationTokenUpdateRequest { UsesAllowed = 4, ExpiryTime = FutureExpiry });

        Assert.Equal(new[]
        {
            new CapturedRequest("GET", "/_synapse/admin/v1/registration_tokens/abcd", null, ""),
            new CapturedRequest("PUT", "/_synapse/admin/v1/registration_tokens/abcd", "application/json; charset=utf-8",
                """{"uses_allowed":4,"expiry_time":4781243146000}"""),
        }, _homeserver.Requests);
    }

    /// <summary>Without a cap nothing can run out, so the update is sent as it is, without reading the token.</summary>
    [Fact]
    public async Task Tuwunel_UpdateRegistrationToken_NoCap_SendsOnlyTheSynapseRequest()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.Respond = () => Results.Json(new { token = "abcd" });

        await gateway.UpdateRegistrationTokenAsync("abcd", new SynapseAdminRegistrationTokenUpdateRequest { UsesAllowed = null, ExpiryTime = null });

        Assert.Equal(
            new CapturedRequest("PUT", "/_synapse/admin/v1/registration_tokens/abcd", "application/json; charset=utf-8",
                """{"uses_allowed":null,"expiry_time":null}"""),
            Assert.Single(_homeserver.Requests));
    }

    // ---- Bulk local media deletion (#542) ------------------------------------------------------------

    // Synapse's documented request: before_ts, size_gt and keep_profiles as query parameters, an empty JSON body.
    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public async Task DeleteLocalMedia_SendsTheCutoffSizeAndAvatarOptionAsQueryParameters(bool keepProfiles, string sentKeepProfiles)
    {
        _homeserver.AllowedMethod = "POST";
        _homeserver.Respond = () => Results.Json(new { deleted_media = new[] { "a", "b" }, total = 2 });

        var response = await _gateway.DeleteLocalMediaAsync(1_700_000_000_000, 10_485_760, keepProfiles);

        Assert.Equal(
            new CapturedRequest("POST", $"/_synapse/admin/v1/media/delete?before_ts=1700000000000&size_gt=10485760&keep_profiles={sentKeepProfiles}",
                "application/json; charset=utf-8", "{}"),
            Assert.Single(_homeserver.Requests));
        Assert.Equal(2, response!.Total);
        Assert.Equal(["a", "b"], response.DeletedMedia);
    }

    // Tuwunel (1.8.1 to 2.0.0) selects by the file's modification time (about the upload) instead of last access, so its
    // result can't match Synapse's: the gateway refuses before sending anything (ADR 0006, AC-15).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tuwunel_DeleteLocalMedia_RefusesWithoutARequest(bool keepProfiles)
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());

        var error = await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(
            () => gateway.DeleteLocalMediaAsync(1_700_000_000_000, 0, keepProfiles));

        Assert.Equal(nameof(TuwunelAdminGateway.DeleteLocalMediaAsync), error.Operation);
        Assert.Empty(_homeserver.Requests);
    }

    // ---- Room list sort direction (#541) -------------------------------------------------------------

    // The dir a caller passes follows the Synapse docs, where these keys sort largest first. The Synapse gateway sends it
    // unchanged, whatever brand it reports; Tuwunel sorts every key ascending, so its gateway sends the opposite dir for
    // these keys. Which server gets which class is covered by MatrixAuthGatewayProbeTests.
    public static TheoryData<string, string> SynapseRoomListDirs => new()
    {
        { GatewayConstants.SynapseBrand, "f" },
        { GatewayConstants.SynapseBrand, "b" },
        { GatewayConstants.GenericBrand, "f" },
        { GatewayConstants.TuwunelBrand, "f" }, // the brand is only reported, never used for a decision
    };

    [Theory]
    [MemberData(nameof(SynapseRoomListDirs))]
    public async Task GetRoomList_SendsTheDirItIsGiven(string brand, string dir)
    {
        var gateway = new SynapseAdminGateway(CreateHomeserver(), brand);
        _homeserver.Respond = () => Results.Json(new { offset = 0, total_rooms = 0, rooms = Array.Empty<object>() });

        await gateway.GetRoomListAsync(0, 10, "joined_members", dir);

        Assert.Equal($"/_synapse/admin/v1/rooms?from=0&limit=10&dir={dir}&order_by=joined_members", Assert.Single(_homeserver.RequestTargets));
    }

    public static TheoryData<string, string, string> TuwunelRoomListDirs => new()
    {
        { "joined_members", "f", "b" },
        { "joined_members", "b", "f" },
        { "joined_local_members", "f", "b" },
        { "version", "f", "b" },
        { "state_events", "b", "f" },
        { "size", "f", "b" },
        { "name", "f", "f" },
        { "public", "b", "b" },
    };

    [Theory]
    [MemberData(nameof(TuwunelRoomListDirs))]
    public async Task Tuwunel_GetRoomList_SendsTheDirThatGivesTheDocumentedOrder(string orderBy, string dir, string sentDir)
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.Respond = () => Results.Json(new { offset = 0, total_rooms = 0, rooms = Array.Empty<object>() });

        await gateway.GetRoomListAsync(0, 10, orderBy, dir);

        Assert.Equal($"/_synapse/admin/v1/rooms?from=0&limit=10&dir={sentDir}&order_by={orderBy}", Assert.Single(_homeserver.RequestTargets));
    }

    // The room list filters (#588): a flag goes out only when set, as Synapse's lowercase boolean literal; a missing
    // flag means "both" on Synapse and Tuwunel.
    public static TheoryData<bool?, bool?, string> RoomListFilters => new()
    {
        { null, null, "" },
        { true, null, "&public_rooms=true" },
        { false, null, "&public_rooms=false" },
        { null, true, "&empty_rooms=true" },
        { null, false, "&empty_rooms=false" },
        { true, false, "&public_rooms=true&empty_rooms=false" },
    };

    [Theory]
    [MemberData(nameof(RoomListFilters))]
    public async Task GetRoomList_SendsTheFiltersThatAreSet(bool? publicRooms, bool? emptyRooms, string expectedFlags)
    {
        var gateway = new SynapseAdminGateway(CreateHomeserver());
        _homeserver.Respond = () => Results.Json(new { offset = 0, total_rooms = 0, rooms = Array.Empty<object>() });

        await gateway.GetRoomListAsync(20, 10, "name", "b", "my room", publicRooms, emptyRooms);

        Assert.Equal($"/_synapse/admin/v1/rooms?from=20&limit=10&dir=b&order_by=name&search_term=my+room{expectedFlags}", Assert.Single(_homeserver.RequestTargets));
    }

    // Tuwunel serves both flags and selects the same rooms as Synapse (SPEC.md §10), so they go out unchanged, next to
    // the dir correction for the keys Synapse sorts largest first (#541).
    [Theory]
    [MemberData(nameof(RoomListFilters))]
    public async Task Tuwunel_GetRoomList_SendsTheFiltersUnchanged(bool? publicRooms, bool? emptyRooms, string expectedFlags)
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.Respond = () => Results.Json(new { offset = 0, total_rooms = 0, rooms = Array.Empty<object>() });

        await gateway.GetRoomListAsync(0, 10, "joined_members", "f", publicRooms: publicRooms, emptyRooms: emptyRooms);

        Assert.Equal($"/_synapse/admin/v1/rooms?from=0&limit=10&dir=b&order_by=joined_members{expectedFlags}", Assert.Single(_homeserver.RequestTargets));
    }

    // ---- Event report filters and details (#588) ------------------------------------------------------

    // Each set field goes out under its documented name, form-encoded; a null or empty field is left out.
    public static TheoryData<string?, string?, string?, string> EventReportFilters => new()
    {
        { null, null, null, "" },
        { "", "", "", "" },
        { "!room:example.com", null, null, "&room_id=!room%3aexample.com" },
        { null, "@alice:example.com", null, "&user_id=%40alice%3aexample.com" },
        { null, null, "@bob+x:example.com", "&event_sender_user_id=%40bob%2bx%3aexample.com" },
        { "!room:example.com", "alice", "@bob:example.com", "&room_id=!room%3aexample.com&user_id=alice&event_sender_user_id=%40bob%3aexample.com" },
    };

    [Theory]
    [MemberData(nameof(EventReportFilters))]
    public async Task GetEventReportList_SendsTheFiltersThatAreSet(string? roomId, string? reporter, string? sender, string expectedFilters)
    {
        _homeserver.Respond = () => Results.Json(new { total = 0, event_reports = Array.Empty<object>() });

        await _gateway.GetEventReportListAsync(0, 10, "b", new EventReportListFilter(roomId, reporter, sender));

        Assert.Equal($"/_synapse/admin/v1/event_reports?from=0&limit=10&dir=b{expectedFilters}", Assert.Single(_homeserver.RequestTargets));
    }

    [Fact]
    public async Task GetEventReportList_WithoutAFilter_SendsNone()
    {
        _homeserver.Respond = () => Results.Json(new { total = 0, event_reports = Array.Empty<object>() });

        await _gateway.GetEventReportListAsync(0, 10, "b");

        Assert.Equal("/_synapse/admin/v1/event_reports?from=0&limit=10&dir=b", Assert.Single(_homeserver.RequestTargets));
    }

    [Fact]
    public async Task GetEventReportDetails_ReadsTheReportAndTheEventJson()
    {
        _homeserver.Respond = () => Results.Text("""
            {
                "id": 42,
                "received_ts": 1570897107409,
                "room_id": "!room:example.com",
                "name": "Matrix HQ",
                "canonical_alias": "#hq:example.com",
                "event_id": "$event",
                "user_id": "@alice:example.com",
                "reason": "spam",
                "score": -100,
                "sender": "@bob:example.com",
                "event_json": { "type": "m.room.message", "content": { "body": "hi" } }
            }
            """, "application/json");

        var report = await _gateway.GetEventReportDetailsAsync("42");

        Assert.Equal("/_synapse/admin/v1/event_reports/42", Assert.Single(_homeserver.RequestTargets));
        Assert.NotNull(report);
        Assert.Equal(("42", 1570897107409L, "!room:example.com", "Matrix HQ", "#hq:example.com", "$event"),
            (report.Id, report.ReceivedTs, report.RoomId, report.Name, report.CanonicalAlias, report.EventId));
        Assert.Equal(("@alice:example.com", "spam", -100, "@bob:example.com"), (report.UserId, report.Reason, report.Score, report.Sender));
        Assert.Equal("hi", report.EventJson?["content"]?["body"]?.GetValue<string>());
    }

    /// <summary>
    /// Synapse answers 404 <c>M_NOT_FOUND</c> for an unknown report and for one whose event was purged or whose room
    /// was deleted (its query joins the stored event and the room state); a bare 404 means the same, as for events.
    /// </summary>
    [Theory]
    [InlineData("M_NOT_FOUND")]
    [InlineData(null)]
    public async Task GetEventReportDetails_NotFound_ReturnsNull(string? errcode)
    {
        _homeserver.Respond = () => errcode == null
            ? Results.Text("Not Found", statusCode: 404)
            : Results.Json(new { errcode, error = "Event report not found" }, statusCode: 404);

        Assert.Null(await _gateway.GetEventReportDetailsAsync("42"));
    }

    [Theory]
    [InlineData(403, "M_FORBIDDEN")]
    [InlineData(404, "M_UNRECOGNIZED")]
    [InlineData(400, "M_INVALID_PARAM")]
    public async Task GetEventReportDetails_OtherMatrixError_Throws(int statusCode, string errcode)
    {
        _homeserver.Respond = () => Results.Json(new { errcode, error = "Error" }, statusCode: statusCode);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => _gateway.GetEventReportDetailsAsync("42"));

        Assert.Equal(errcode, ex.ErrorCode);
    }

    [Fact]
    public async Task GetEventReportDetails_EncodesTheReportId()
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_NOT_FOUND", error = "Not found" }, statusCode: 404);

        await _gateway.GetEventReportDetailsAsync("4/2 x");

        Assert.Equal("/_synapse/admin/v1/event_reports/4%2f2+x", Assert.Single(_homeserver.RequestTargets));
    }

    // ---- Event context (#588) ---------------------------------------------------------------------------

    [Fact]
    public async Task GetEventContext_SendsTheRoomTheEventAndTheLimit_AndReadsTheAnswer()
    {
        _homeserver.Respond = () => Results.Text("""
            {
                "start": "t27-54_2_0_2",
                "end": "t29-57_2_0_2",
                "events_before": [ { "event_id": "$b2", "type": "m.room.message" }, { "event_id": "$b1", "type": "m.room.message" } ],
                "event": { "event_id": "$e", "type": "m.room.message", "content": { "body": "hi" } },
                "events_after": [ { "event_id": "$a1", "type": "m.room.member", "state_key": "@u:hs" } ],
                "state": [ { "event_id": "$s", "type": "m.room.create", "state_key": "" } ]
            }
            """, "application/json");

        var context = await _gateway.GetEventContextAsync("!room:example.com", "$e+v/1", 20);

        Assert.Equal("/_synapse/admin/v1/rooms/!room%3aexample.com/context/%24e%2bv%2f1?limit=20", Assert.Single(_homeserver.RequestTargets));
        Assert.NotNull(context);
        Assert.Equal(("t27-54_2_0_2", "t29-57_2_0_2"), (context.Start, context.End));
        Assert.Equal(["$b2", "$b1"], context.EventsBefore.Select(e => e["event_id"]!.GetValue<string>()));
        Assert.Equal("hi", context.Event?["content"]?["body"]?.GetValue<string>());
        Assert.Equal("$a1", Assert.Single(context.EventsAfter)["event_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Tuwunel_GetEventContext_SendsTheSameRequest()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.Respond = () => Results.Json(new { start = "", end = "", events_before = Array.Empty<object>(), events_after = Array.Empty<object>() });

        await gateway.GetEventContextAsync("!room:example.com", "$e", 20);

        Assert.Equal("/_synapse/admin/v1/rooms/!room%3aexample.com/context/%24e?limit=20", Assert.Single(_homeserver.RequestTargets));
    }

    /// <summary>Synapse and Tuwunel answer 404 <c>M_NOT_FOUND</c> for an unknown event or one in another room.</summary>
    [Theory]
    [InlineData("M_NOT_FOUND")]
    [InlineData(null)]
    public async Task GetEventContext_NotFound_ReturnsNull(string? errcode)
    {
        _homeserver.Respond = () => errcode == null
            ? Results.Text("Not Found", statusCode: 404)
            : Results.Json(new { errcode, error = "Event not found." }, statusCode: 404);

        Assert.Null(await _gateway.GetEventContextAsync("!room:example.com", "$e", 20));
    }

    /// <summary>Tuwunel answers 403 for a room it doesn't know; that and every other error stays an error.</summary>
    [Theory]
    [InlineData(403, "M_FORBIDDEN")]
    [InlineData(404, "M_UNRECOGNIZED")]
    public async Task GetEventContext_OtherMatrixError_Throws(int statusCode, string errcode)
    {
        _homeserver.Respond = () => Results.Json(new { errcode, error = "Error" }, statusCode: statusCode);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => _gateway.GetEventContextAsync("!room:example.com", "$e", 20));

        Assert.Equal(errcode, ex.ErrorCode);
    }

    // ---- Jump to date (#588) ----------------------------------------------------------------------------

    [Theory]
    [InlineData("b")]
    [InlineData("f")]
    public async Task GetEventForTimestamp_SendsTheRoomTheTimeAndTheDirection_AndReadsTheEvent(string dir)
    {
        _homeserver.Respond = () => Results.Json(new { event_id = "$e", origin_server_ts = 1_700_000_000_123L });

        var found = await _gateway.GetEventForTimestampAsync("!room:example.com", 1_700_000_000_000, dir);

        Assert.Equal($"/_synapse/admin/v1/rooms/!room%3aexample.com/timestamp_to_event?ts=1700000000000&dir={dir}", Assert.Single(_homeserver.RequestTargets));
        Assert.Equal(("$e", 1_700_000_000_123L), (found?.EventId, found?.OriginServerTs));
    }

    [Fact]
    public async Task Tuwunel_GetEventForTimestamp_SendsTheSameRequest()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.Respond = () => Results.Json(new { event_id = "$e", origin_server_ts = 1L });

        await gateway.GetEventForTimestampAsync("!room:example.com", 5, "b");

        Assert.Equal("/_synapse/admin/v1/rooms/!room%3aexample.com/timestamp_to_event?ts=5&dir=b", Assert.Single(_homeserver.RequestTargets));
    }

    /// <summary>Synapse and Tuwunel answer 404 <c>M_NOT_FOUND</c> when no event lies in that direction.</summary>
    [Theory]
    [InlineData("M_NOT_FOUND")]
    [InlineData(null)]
    public async Task GetEventForTimestamp_NotFound_ReturnsNull(string? errcode)
    {
        _homeserver.Respond = () => errcode == null
            ? Results.Text("Not Found", statusCode: 404)
            : Results.Json(new { errcode, error = "Unable to find event" }, statusCode: 404);

        Assert.Null(await _gateway.GetEventForTimestampAsync("!room:example.com", 5, "b"));
    }

    [Theory]
    [InlineData(403, "M_FORBIDDEN")]
    [InlineData(404, "M_UNRECOGNIZED")]
    public async Task GetEventForTimestamp_OtherMatrixError_Throws(int statusCode, string errcode)
    {
        _homeserver.Respond = () => Results.Json(new { errcode, error = "Error" }, statusCode: statusCode);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => _gateway.GetEventForTimestampAsync("!room:example.com", 5, "b"));

        Assert.Equal(errcode, ex.ErrorCode);
    }

    [Fact]
    public async Task GetRoomMessageContext_SendsTheContextRequest_AndReadsTypedEvents()
    {
        _homeserver.Respond = () => Results.Text("""
            {
                "start": "s1", "end": "e1",
                "events_before": [ { "event_id": "$b1", "type": "m.room.message", "sender": "@u:hs", "origin_server_ts": 1, "content": { "body": "old" } } ],
                "event": { "event_id": "$e", "type": "m.room.message", "sender": "@u:hs", "origin_server_ts": 2, "content": { "body": "hit" } },
                "events_after": [ { "event_id": "$a1", "type": "m.room.member", "sender": "@u:hs", "origin_server_ts": 3, "state_key": "@u:hs" } ]
            }
            """, "application/json");

        var context = await _gateway.GetRoomMessageContextAsync("!room:example.com", "$e+v", 100);

        Assert.Equal("/_synapse/admin/v1/rooms/!room%3aexample.com/context/%24e%2bv?limit=100", Assert.Single(_homeserver.RequestTargets));
        Assert.NotNull(context);
        Assert.Equal(("s1", "e1"), (context.Start, context.End));
        Assert.Equal(("$b1", "old"), (Assert.Single(context.EventsBefore).EventId, context.EventsBefore[0].Content?.GetProperty("body").GetString()));
        Assert.Equal(("$e", 2L), (context.Event?.EventId, context.Event?.OriginServerTs));
        Assert.Equal(("$a1", "@u:hs"), (Assert.Single(context.EventsAfter).EventId, context.EventsAfter[0].StateKey));
    }

    [Theory]
    [InlineData("M_NOT_FOUND")]
    [InlineData(null)]
    public async Task GetRoomMessageContext_NotFound_ReturnsNull(string? errcode)
    {
        _homeserver.Respond = () => errcode == null
            ? Results.Text("Not Found", statusCode: 404)
            : Results.Json(new { errcode, error = "Event not found." }, statusCode: 404);

        Assert.Null(await _gateway.GetRoomMessageContextAsync("!room:example.com", "$e", 100));
    }

    private AuthenticatedHomeserverGeneric CreateHomeserver() => new(
        "example.com", new HomeserverResolverService.WellKnownUris { Client = _homeserver.Url }, null, "syt_token");

    // ---- Shadow login (#539) -------------------------------------------------------------------------

    /// <summary>
    /// Synapse reads <c>valid_until_ms</c> only from the JSON body, as an integer, and issues a token that never
    /// expires without it. A query parameter is ignored, so the target carries none.
    /// </summary>
    [Fact]
    public async Task LoginAsUser_SendsTheExpiryAsAnIntegerInTheBody()
    {
        _homeserver.AllowedMethod = "POST";
        _homeserver.Respond = () => Results.Json(new { access_token = "syt_shadow" });
        var expireIn = TimeSpan.FromHours(1);

        var before = DateTimeOffset.UtcNow.Add(expireIn).ToUnixTimeMilliseconds();
        var response = await _gateway.LoginAsUserAsync("@alice:example.com", expireIn);
        var after = DateTimeOffset.UtcNow.Add(expireIn).ToUnixTimeMilliseconds();

        var request = Assert.Single(_homeserver.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/_synapse/admin/v1/users/%40alice%3aexample.com/login", request.Target);
        Assert.Equal("application/json; charset=utf-8", request.ContentType);
        using var body = JsonDocument.Parse(request.Body);
        var property = Assert.Single(body.RootElement.EnumerateObject());
        Assert.Equal("valid_until_ms", property.Name);
        Assert.Equal(JsonValueKind.Number, property.Value.ValueKind);
        Assert.InRange(property.Value.GetInt64(), before, after);
        Assert.Equal("syt_shadow", response.AccessToken);
        Assert.Equal("@alice:example.com", response.UserId);
    }

    // ---- User details ----------------------------------------------------------------------------------

    [Fact]
    public async Task GetUserDetails_SendsAGetForTheUser_AndReadsIt()
    {
        _homeserver.Respond = () => Results.Json(new { name = "@alice:example.com", admin = true });

        var user = await _gateway.GetUserDetailsAsync("@alice:example.com");

        Assert.Equal(("@alice:example.com", true), (user?.Name, user?.Admin));
        Assert.Equal(new CapturedRequest("GET", "/_synapse/admin/v2/users/%40alice%3aexample.com", null, ""),
            Assert.Single(_homeserver.Requests));
    }

    /// <summary>
    /// Synapse and Tuwunel answer an unknown local user with 404 <c>M_NOT_FOUND</c>: the user doesn't exist. Creating a
    /// user relies on this answer before it sends the <c>PUT</c> that would modify an existing account.
    /// </summary>
    [Fact]
    public async Task GetUserDetails_UnknownUser_ReturnsNull()
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_NOT_FOUND", error = "User not found" }, statusCode: 404);

        Assert.Null(await _gateway.GetUserDetailsAsync("@alice:example.com"));
    }

    /// <summary>
    /// A homeserver without the endpoint (<c>M_UNRECOGNIZED</c>) or a bare 404 from a proxy doesn't say whether the user
    /// exists, so they stay errors: creating a user must not take them for "no such user".
    /// </summary>
    [Theory]
    [InlineData(404, "M_UNRECOGNIZED")]
    [InlineData(405, "M_UNRECOGNIZED")]
    [InlineData(404, null)]
    public async Task GetUserDetails_MissingEndpoint_Throws(int statusCode, string? errcode)
    {
        _homeserver.Respond = () => errcode == null
            ? Results.Text("Not Found", statusCode: statusCode)
            : Results.Json(new { errcode, error = "Unrecognized request" }, statusCode: statusCode);

        var ex = await Record.ExceptionAsync(() => _gateway.GetUserDetailsAsync("@alice:example.com"));

        Assert.True(ex is MatrixException { ErrorCode: "M_UNRECOGNIZED" } or HttpRequestException { StatusCode: HttpStatusCode.NotFound },
            $"Unexpected exception: {ex}");
    }

    // ---- Room messages (#549) ------------------------------------------------------------------------

    /// <summary>
    /// The first page leaves <c>from</c> out. The Admin API page calls it required, but Synapse (whose own admin test
    /// requests <c>?dir=b</c> without it) and Tuwunel start backward history at the newest event when it is missing.
    /// A later page sends the returned <c>end</c> token as it came, URL-encoded.
    /// </summary>
    [Fact]
    public async Task GetRoomMessages_FirstPageOmitsFrom_ThenSendsTheEndTokenBack()
    {
        _homeserver.Respond = () => Results.Json(new { chunk = Array.Empty<object>(), start = "s1", end = "t+1/2=" });

        var first = await _gateway.GetRoomMessagesAsync("!room:example.com", limit: 50, dir: "b");
        await _gateway.GetRoomMessagesAsync("!room:example.com", limit: 50, from: first!.End, dir: "b");

        Assert.Equal(new[]
        {
            "/_synapse/admin/v1/rooms/!room%3aexample.com/messages?limit=50&dir=b",
            "/_synapse/admin/v1/rooms/!room%3aexample.com/messages?limit=50&from=t%2b1%2f2%3d&dir=b",
        }, _homeserver.RequestTargets);
    }

    // ---- Room deletion (#518) ------------------------------------------------------------------------

    private static Task<SynapseAdminRoomDeleteResponse?> DeleteRoom(SynapseAdminGateway gateway) =>
        gateway.DeleteRoomAsync("!room:example.com", new SynapseAdminRoomDeleteRequest { Block = true, Purge = true });

    [Fact]
    public async Task DeleteRoom_ReturnsTheDeleteId()
    {
        _homeserver.Respond = () => Results.Json(new { delete_id = "delete-1" });

        var response = await DeleteRoom(_gateway);

        Assert.Equal("delete-1", response?.DeleteId);
    }

    /// <summary>
    /// The Admin API documents <c>new_room_user_id</c>, <c>room_name</c> and <c>message</c> as optional strings, so a
    /// null one is left out instead of sent as <c>null</c>. <c>block</c> and <c>purge</c> are always sent, which also
    /// keeps the body from being empty (#543).
    /// </summary>
    [Theory]
    [MemberData(nameof(RoomDeleteBodies))]
    public async Task DeleteRoom_SendsOnlyTheSetOptionalStrings(SynapseAdminRoomDeleteRequest request, string expectedBody)
    {
        _homeserver.Respond = () => Results.Json(new { delete_id = "delete-1" });

        await _gateway.DeleteRoomAsync("!room:example.com", request);

        Assert.Equal(expectedBody, Assert.Single(_homeserver.Requests).Body);
    }

    public static TheoryData<SynapseAdminRoomDeleteRequest, string> RoomDeleteBodies => new()
    {
        // What RoomService sends.
        { new SynapseAdminRoomDeleteRequest { Block = false, Purge = true }, """{"block":false,"purge":true}""" },
        {
            new SynapseAdminRoomDeleteRequest
            {
                NewRoomUserId = "@admin:example.com", RoomName = "Notice", Block = true, Purge = false, Message = "Moved"
            },
            """{"new_room_user_id":"@admin:example.com","room_name":"Notice","block":true,"purge":false,"message":"Moved"}"""
        },
        // An empty string is a value, not a missing one.
        {
            new SynapseAdminRoomDeleteRequest { RoomName = "", Block = true, Purge = true },
            """{"room_name":"","block":true,"purge":true}"""
        },
    };

    /// <summary>The body used to be ignored, so an accepted deletion without one must not become an error.</summary>
    [Fact]
    public async Task DeleteRoom_EmptyBody_ReturnsNull()
    {
        _homeserver.Respond = () => Results.Ok();

        Assert.Null(await DeleteRoom(_gateway));
    }

    /// <summary>The gateway can't log, so it leaves a body that isn't JSON to the service (<c>RoomService</c>).</summary>
    [Fact]
    public async Task DeleteRoom_NonJsonBody_ThrowsJsonException()
    {
        _homeserver.Respond = () => Results.Text("Accepted", statusCode: 200);

        await Assert.ThrowsAnyAsync<JsonException>(() => DeleteRoom(_gateway));
    }

    [Fact]
    public async Task GetRoomDeleteStatus_SendsAGetForTheDeleteId_AndReadsTheStatus()
    {
        _homeserver.Respond = () => Results.Json(new
        {
            delete_id = "delete+1",
            room_id = "!room:example.com",
            status = "failed",
            error = "Purge failed",
            shutdown_room = new { kicked_users = new[] { "@bob:example.com" }, failed_to_kick_users = Array.Empty<string>(), local_aliases = Array.Empty<string>(), new_room_id = (string?)null }
        });

        var status = await _gateway.GetRoomDeleteStatusAsync("delete+1");

        Assert.Equal(new CapturedRequest("GET", "/_synapse/admin/v2/rooms/delete_status/delete%2b1", null, ""), Assert.Single(_homeserver.Requests));
        Assert.NotNull(status);
        Assert.Equal(("delete+1", "!room:example.com", "failed", "Purge failed"), (status.DeleteId, status.RoomId, status.Status, status.Error));
    }

    [Fact]
    public async Task GetRoomDeleteStatuses_SendsAGetForTheRoom_AndReadsEveryResult()
    {
        _homeserver.Respond = () => Results.Json(new
        {
            results = new object[]
            {
                new { delete_id = "delete-1", room_id = "!room:example.com", status = "complete" },
                new { delete_id = "delete-2", room_id = "!room:example.com", status = "active" },
            }
        });

        var statuses = await _gateway.GetRoomDeleteStatusesAsync("!room:example.com");

        Assert.Equal(new CapturedRequest("GET", "/_synapse/admin/v2/rooms/!room%3aexample.com/delete_status", null, ""), Assert.Single(_homeserver.Requests));
        Assert.NotNull(statuses);
        Assert.Equal(new (string?, string)[] { ("delete-1", "complete"), ("delete-2", "active") }, statuses.Results.Select(r => (r.DeleteId, r.Status)));
    }

    private static readonly string[] DeleteStatusMethodNames =
    [
        nameof(SynapseAdminGateway.GetRoomDeleteStatusAsync),
        nameof(SynapseAdminGateway.GetRoomDeleteStatusesAsync),
    ];

    public static TheoryData<string> DeleteStatusMethods => new(DeleteStatusMethodNames);

    private static Func<SynapseAdminGateway, Task<object?>> DeleteStatusMethod(string name) => name switch
    {
        nameof(SynapseAdminGateway.GetRoomDeleteStatusAsync) => async g => await g.GetRoomDeleteStatusAsync("delete-1"),
        nameof(SynapseAdminGateway.GetRoomDeleteStatusesAsync) => async g => await g.GetRoomDeleteStatusesAsync("!room:example.com"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };

    /// <summary>
    /// A deletion Synapse no longer knows (404 <c>M_NOT_FOUND</c>), a homeserver without the endpoint
    /// (<c>M_UNRECOGNIZED</c>, as 404 or 405) and a bare 404 from a proxy all mean "no status".
    /// </summary>
    public static TheoryData<string, int, string?> MissingDeleteStatuses
    {
        get
        {
            var data = new TheoryData<string, int, string?>();
            foreach (var method in DeleteStatusMethodNames)
            {
                data.Add(method, 404, "M_NOT_FOUND");
                data.Add(method, 404, "M_UNRECOGNIZED");
                data.Add(method, 405, "M_UNRECOGNIZED");
                data.Add(method, 404, null);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(MissingDeleteStatuses))]
    public async Task GetDeleteStatus_UnknownDeletionOrMissingEndpoint_ReturnsNull(string method, int statusCode, string? errcode)
    {
        _homeserver.Respond = () => errcode == null
            ? Results.Text("Not Found", statusCode: statusCode)
            : Results.Json(new { errcode, error = "Not found" }, statusCode: statusCode);

        Assert.Null(await DeleteStatusMethod(method)(_gateway));
    }

    [Theory]
    [MemberData(nameof(DeleteStatusMethods))]
    public async Task GetDeleteStatus_MatrixErrorResponse_ThrowsMatrixException(string method)
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_FORBIDDEN", error = "You are not a server admin" }, statusCode: 403);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => DeleteStatusMethod(method)(_gateway));

        Assert.Equal("M_FORBIDDEN", ex.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(DeleteStatusMethods))]
    public async Task GetDeleteStatus_NonJsonErrorResponse_ThrowsHttpRequestException(string method)
    {
        _homeserver.Respond = () => Results.Text("Internal Server Error", statusCode: 500);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => DeleteStatusMethod(method)(_gateway));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    // Tuwunel purges a room even when it couldn't evict local users, where Synapse refuses to, and doesn't report some
    // failed purge steps, so its "complete" can mean less than Synapse's. The gateway refuses the deletion and both status
    // reads before sending anything (ADR 0006, #559).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tuwunel_DeleteRoom_RefusesWithoutARequest(bool block)
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());

        var error = await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() =>
            gateway.DeleteRoomAsync("!room:example.com", new SynapseAdminRoomDeleteRequest { Block = block, Purge = true }));

        Assert.Equal(nameof(TuwunelAdminGateway.DeleteRoomAsync), error.Operation);
        Assert.Empty(_homeserver.Requests);
    }

    [Theory]
    [MemberData(nameof(DeleteStatusMethods))]
    public async Task Tuwunel_GetDeleteStatus_RefusesWithoutARequest(string method)
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());

        var error = await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() => DeleteStatusMethod(method)(gateway));

        Assert.Equal(method, error.Operation);
        Assert.Empty(_homeserver.Requests);
    }

    // ---- User create or modify, user details (#546) ------------------------------------------------------

    /// <summary>The body parameters <c>PUT /_synapse/admin/v2/users/{userId}</c> documents.</summary>
    private static readonly string[] UserUpsertParameters =
        ["password", "logout_devices", "displayname", "avatar_url", "threepids", "external_ids", "admin", "deactivated", "locked", "user_type"];

    /// <summary>
    /// The request declares only parameters the endpoint accepts. <c>shadow_banned</c> isn't one of them: shadow-banning
    /// has its own endpoint, <c>/_synapse/admin/v1/users/{userId}/shadow_ban</c>.
    /// </summary>
    [Fact]
    public void UserUpsertRequest_DeclaresOnlyDocumentedParameters()
    {
        var names = typeof(SynapseAdminUserUpsertRequest).GetProperties()
            .Select(p => p.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false)
                .Cast<System.Text.Json.Serialization.JsonPropertyNameAttribute>().Single().Name);

        Assert.All(names, name => Assert.Contains(name, UserUpsertParameters));
    }

    /// <summary>
    /// A field left <c>null</c> is omitted (<c>MatrixHttpClient</c> drops nulls), which the endpoint reads as "unchanged".
    /// For <c>user_type</c> that means this request can't clear the type, which takes an explicit <c>null</c>.
    /// </summary>
    [Fact]
    public async Task UpdateUser_CreateShapedRequest_SendsOnlyTheSetFields()
    {
        _homeserver.Respond = () => Results.Json(new { name = "@alice:example.com" }, statusCode: 201);

        await _gateway.UpdateUserAsync("@alice:example.com",
            new SynapseAdminUserUpsertRequest { Password = "pw", DisplayName = "Al", Admin = true, Deactivated = false });

        var request = Assert.Single(_homeserver.Requests);
        Assert.Equal(("PUT", "/_synapse/admin/v2/users/%40alice%3aexample.com"), (request.Method, request.Target));
        Assert.Equal("""{"password":"pw","displayname":"Al","admin":true,"deactivated":false}""", request.Body);
    }

    /// <summary>
    /// An empty display name or avatar URL removes it (#584). <c>MatrixHttpClient</c> drops only <c>null</c>, so the
    /// empty strings reach the homeserver, and nothing else is sent.
    /// </summary>
    [Fact]
    public async Task UpdateUser_EmptyProfileFields_AreSent()
    {
        _homeserver.Respond = () => Results.Json(new { name = "@alice:example.com" });

        await _gateway.UpdateUserAsync("@alice:example.com", new SynapseAdminUserUpsertRequest { DisplayName = "", AvatarUrl = "" });

        Assert.Equal("""{"displayname":"","avatar_url":""}""", Assert.Single(_homeserver.Requests).Body);
    }

    /// <summary>
    /// Synapse's user details have no <c>expiration_ts</c> (only renewing account validity returns one), so the
    /// expiration is unknown, not "never", after a standard load.
    /// </summary>
    [Fact]
    public async Task GetUserDetails_DocumentedResponse_HasNoExpiration()
    {
        _homeserver.Respond = () => Results.Text("""
            {"name":"@user:example.com","displayname":"User","threepids":[],"avatar_url":null,"is_guest":false,
             "admin":false,"deactivated":false,"erased":false,"shadow_banned":false,"creation_ts":1560432506,
             "last_seen_ts":1732919539393,"appservice_id":null,"consent_server_notice_sent":null,"consent_version":null,
             "consent_ts":null,"external_ids":[],"user_type":null,"locked":false,"suspended":false}
            """, "application/json");

        var user = await _gateway.GetUserDetailsAsync("@user:example.com");

        Assert.Equal("@user:example.com", user?.Name);
        Assert.Null(user!.ExpirationTs);
    }

    // ---- Suspension and shadow-ban (#584) ---------------------------------------------------------------

    // Suspending: PUT /_synapse/admin/v1/suspend/{userId} with {"suspend": true | false}. Synapse answers
    // {"user_<id>_suspended": …} and Tuwunel {"user_id": …, "suspended": …}, so the response isn't read.
    private const string SuspendTarget = "/_synapse/admin/v1/suspend/%40alice%3aexample.com";

    // Shadow-banning: POST (with {}) and DELETE (without a body) on /_synapse/admin/v1/users/{userId}/shadow_ban.
    private const string ShadowBanTarget = "/_synapse/admin/v1/users/%40alice%3aexample.com/shadow_ban";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetUserSuspended_SendsPutWithSuspend(bool suspend)
    {
        _homeserver.AllowedMethod = "PUT";
        _homeserver.Respond = () => Results.Json(new Dictionary<string, bool> { ["user_@alice:example.com_suspended"] = suspend });

        await _gateway.SetUserSuspendedAsync("@alice:example.com", suspend);

        Assert.Equal(
            new CapturedRequest("PUT", SuspendTarget, "application/json; charset=utf-8", $$"""{"suspend":{{(suspend ? "true" : "false")}}}"""),
            Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task SetUserShadowBanned_True_SendsPostWithAnEmptyObject()
    {
        _homeserver.AllowedMethod = "POST";
        _homeserver.Respond = () => Results.Json(new { });

        await _gateway.SetUserShadowBannedAsync("@alice:example.com", true);

        Assert.Equal(EmptyJsonObject("POST", ShadowBanTarget), Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task SetUserShadowBanned_False_SendsDeleteWithoutABody()
    {
        _homeserver.AllowedMethod = "DELETE";
        _homeserver.Respond = () => Results.Json(new { });

        await _gateway.SetUserShadowBannedAsync("@alice:example.com", false);

        Assert.Equal(new CapturedRequest("DELETE", ShadowBanTarget, null, ""), Assert.Single(_homeserver.Requests));
    }

    private static readonly Dictionary<string, Func<SynapseAdminGateway, CancellationToken, Task>> UserStateMethods = new()
    {
        ["Suspend"] = (g, ct) => g.SetUserSuspendedAsync("@alice:example.com", true, ct),
        ["Unsuspend"] = (g, ct) => g.SetUserSuspendedAsync("@alice:example.com", false, ct),
        ["ShadowBan"] = (g, ct) => g.SetUserShadowBannedAsync("@alice:example.com", true, ct),
        ["LiftShadowBan"] = (g, ct) => g.SetUserShadowBannedAsync("@alice:example.com", false, ct),
        ["ResetPassword"] = (g, ct) => g.ResetPasswordAsync("@alice:example.com",
            new SynapseAdminResetPasswordRequest { NewPassword = "new-secret", LogoutDevices = false }, ct),
    };

    public static TheoryData<string> UserStateMethodNames => new(UserStateMethods.Keys);

    [Theory]
    [MemberData(nameof(UserStateMethodNames))]
    public async Task UserState_CancelledToken_SendsNoRequest(string method)
    {
        _homeserver.Respond = () => Results.Json(new { });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UserStateMethods[method](_gateway, cts.Token));

        Assert.Empty(_homeserver.Requests);
    }

    [Theory]
    [MemberData(nameof(UserStateMethodNames))]
    public async Task UserState_MatrixErrorResponse_ThrowsMatrixException(string method)
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_NOT_FOUND", error = "User not found" }, statusCode: 404);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => UserStateMethods[method](_gateway, CancellationToken.None));

        Assert.Equal("M_NOT_FOUND", ex.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(UserStateMethodNames))]
    public async Task UserState_NonJsonErrorResponse_ThrowsHttpRequestException(string method)
    {
        _homeserver.Respond = () => Results.Text("Internal Server Error", statusCode: 500);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => UserStateMethods[method](_gateway, CancellationToken.None));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    // Resetting a password: POST /_synapse/admin/v1/reset_password/{userId} with both fields, logout_devices always
    // sent, since Synapse defaults it to true and the page defaults it to false (#584).
    private const string ResetPasswordTarget = "/_synapse/admin/v1/reset_password/%40alice%3aexample.com";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResetPassword_SendsPostWithBothFields(bool logoutDevices)
    {
        _homeserver.AllowedMethod = "POST";
        _homeserver.Respond = () => Results.Json(new { });

        await _gateway.ResetPasswordAsync("@alice:example.com",
            new SynapseAdminResetPasswordRequest { NewPassword = "new-secret", LogoutDevices = logoutDevices });

        Assert.Equal(
            new CapturedRequest("POST", ResetPasswordTarget, "application/json; charset=utf-8",
                $$"""{"new_password":"new-secret","logout_devices":{{(logoutDevices ? "true" : "false")}}}"""),
            Assert.Single(_homeserver.Requests));
    }

    /// <summary>A Tuwunel gateway signed in as <paramref name="userId"/>, with the whoami request it sent removed.</summary>
    private async Task<TuwunelAdminGateway> CreateTuwunelGatewaySignedInAsAsync(string userId)
    {
        var homeserver = CreateHomeserver();
        _homeserver.Respond = () => Results.Json(new { user_id = userId });
        await homeserver.Initialise();
        _homeserver.Requests.Clear();
        return new TuwunelAdminGateway(homeserver);
    }

    /// <summary>
    /// Tuwunel removes every device of the user when it logs devices out, the requester's included, so resetting the
    /// signed-in admin's own password is refused, whatever the switch says (#584).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tuwunel_ResetOwnPassword_RefusesWithoutARequest(bool logoutDevices)
    {
        var gateway = await CreateTuwunelGatewaySignedInAsAsync("@alice:example.com");

        var error = await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() => gateway.ResetPasswordAsync(
            "@alice:example.com", new SynapseAdminResetPasswordRequest { NewPassword = "new-secret", LogoutDevices = logoutDevices }));

        Assert.Equal(nameof(TuwunelAdminGateway.ResetPasswordAsync), error.Operation);
        Assert.Empty(_homeserver.Requests);
    }

    /// <summary>Tuwunel's reset writes a password, which reactivates a deactivated account: refused after the read.</summary>
    [Fact]
    public async Task Tuwunel_ResetPasswordOfADeactivatedAccount_RefusesAfterReadingTheAccount()
    {
        var gateway = await CreateTuwunelGatewaySignedInAsAsync("@admin:example.com");
        _homeserver.Respond = () => Results.Json(new { name = "@alice:example.com", deactivated = true });

        var error = await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() => gateway.ResetPasswordAsync(
            "@alice:example.com", new SynapseAdminResetPasswordRequest { NewPassword = "new-secret", LogoutDevices = true }));

        Assert.Equal(nameof(TuwunelAdminGateway.ResetPasswordAsync), error.Operation);
        Assert.Equal([("GET", "/_synapse/admin/v2/users/%40alice%3aexample.com")], _homeserver.Requests.Select(r => (r.Method, r.Target)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tuwunel_ResetPasswordOfAnotherActiveUser_ReadsTheAccountThenSendsTheSynapseRequest(bool logoutDevices)
    {
        var gateway = await CreateTuwunelGatewaySignedInAsAsync("@admin:example.com");
        _homeserver.Respond = () => Results.Json(new { name = "@alice:example.com", deactivated = false });

        await gateway.ResetPasswordAsync("@alice:example.com",
            new SynapseAdminResetPasswordRequest { NewPassword = "new-secret", LogoutDevices = logoutDevices });

        Assert.Equal(
            [
                new CapturedRequest("GET", "/_synapse/admin/v2/users/%40alice%3aexample.com", null, ""),
                new CapturedRequest("POST", ResetPasswordTarget, "application/json; charset=utf-8",
                    $$"""{"new_password":"new-secret","logout_devices":{{(logoutDevices ? "true" : "false")}}}"""),
            ],
            _homeserver.Requests);
    }

    /// <summary>Tuwunel has no shadow-ban (its status page marks the endpoint not implemented): lifting one is refused too.</summary>
    [Fact]
    public async Task Tuwunel_LiftShadowBan_RefusesWithoutARequest()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());

        var error = await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(
            () => gateway.SetUserShadowBannedAsync("@alice:example.com", false));

        Assert.Equal(nameof(TuwunelAdminGateway.SetUserShadowBannedAsync), error.Operation);
        Assert.Empty(_homeserver.Requests);
    }

    /// <summary>Tuwunel serves suspension with the same request and result (PLAN.md §10), so the Synapse request is sent.</summary>
    [Fact]
    public async Task Tuwunel_SetUserSuspended_SendsTheSynapseRequest()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.Respond = () => Results.Json(new { user_id = "@alice:example.com", suspended = true });

        await gateway.SetUserSuspendedAsync("@alice:example.com", true);

        Assert.Equal(
            new CapturedRequest("PUT", SuspendTarget, "application/json; charset=utf-8", """{"suspend":true}"""),
            Assert.Single(_homeserver.Requests));
    }

    // ---- Tuwunel admin API coverage ---------------------------------------------------------------------

    // Operations Tuwunel doesn't serve (its status page marks them not implemented or not applicable) and the remote
    // media cache purge, which it selects by storage time instead of last access: the gateway refuses each before
    // sending anything (ADR 0006).
    private static readonly Dictionary<string, Func<SynapseAdminGateway, Task>> TuwunelRefusedOperations = new()
    {
        [nameof(SynapseAdminGateway.QuarantineMediaAsync)] = g => g.QuarantineMediaAsync("example.com", "media1"),
        [nameof(SynapseAdminGateway.UnquarantineMediaAsync)] = g => g.UnquarantineMediaAsync("example.com", "media1"),
        [nameof(SynapseAdminGateway.ProtectMediaAsync)] = g => g.ProtectMediaAsync("example.com", "media1"),
        [nameof(SynapseAdminGateway.UnprotectMediaAsync)] = g => g.UnprotectMediaAsync("example.com", "media1"),
        [nameof(SynapseAdminGateway.QuarantineMediaByRoomIdAsync)] = g => g.QuarantineMediaByRoomIdAsync("!room:example.com"),
        [nameof(SynapseAdminGateway.QuarantineMediaByUserIdAsync)] = g => g.QuarantineMediaByUserIdAsync("@alice:example.com"),
        [nameof(SynapseAdminGateway.GetEventReportListAsync)] = g => g.GetEventReportListAsync(0, 10, "b"),
        [nameof(SynapseAdminGateway.DeleteEventReportAsync)] = g => g.DeleteEventReportAsync("1"),
        [nameof(SynapseAdminGateway.GetEventReportDetailsAsync)] = g => g.GetEventReportDetailsAsync("1"),
        [nameof(SynapseAdminGateway.RenewAccountValidityAsync)] = g => g.RenewAccountValidityAsync(
            new SynapseAdminAccountValidityRequest { UserId = "@alice:example.com" }),
        [nameof(SynapseAdminGateway.GetUserExperimentalFeaturesAsync)] = g => g.GetUserExperimentalFeaturesAsync("@alice:example.com"),
        [nameof(SynapseAdminGateway.SetUserExperimentalFeaturesAsync)] = g => g.SetUserExperimentalFeaturesAsync("@alice:example.com",
            new SynapseAdminExperimentalFeaturesRequest()),
        [nameof(SynapseAdminGateway.GetBackgroundUpdatesStatusAsync)] = g => g.GetBackgroundUpdatesStatusAsync(),
        [nameof(SynapseAdminGateway.SetBackgroundUpdatesEnabledAsync)] = g => g.SetBackgroundUpdatesEnabledAsync(false),
        [nameof(SynapseAdminGateway.StartBackgroundUpdatesJobAsync)] = g => g.StartBackgroundUpdatesJobAsync("populate_stats_process_rooms"),
        [nameof(SynapseAdminGateway.GetLargestRoomsAsync)] = g => g.GetLargestRoomsAsync(),
        [nameof(SynapseAdminGateway.PurgeRemoteMediaCacheAsync)] = g => g.PurgeRemoteMediaCacheAsync(1_700_000_000_000),
        [nameof(SynapseAdminGateway.SetUserShadowBannedAsync)] = g => g.SetUserShadowBannedAsync("@alice:example.com", true),
    };

    public static TheoryData<string> TuwunelRefusedOperationNames => new(TuwunelRefusedOperations.Keys);

    [Theory]
    [MemberData(nameof(TuwunelRefusedOperationNames))]
    public async Task Tuwunel_UnservedOrDeviatingOperation_RefusesWithoutARequest(string operation)
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());

        var error = await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() => TuwunelRefusedOperations[operation](gateway));

        Assert.Equal(operation, error.Operation);
        Assert.Empty(_homeserver.Requests);
    }

    /// <summary>The media overloads that take an MXC URI reach the same refusal.</summary>
    [Fact]
    public async Task Tuwunel_MxcMediaOverloads_RefuseWithoutARequest()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        const string mxc = "mxc://example.com/media1";

        await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() => gateway.QuarantineMediaAsync(mxc));
        await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() => gateway.UnquarantineMediaAsync(mxc));
        await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() => gateway.ProtectMediaAsync(mxc));
        await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() => gateway.UnprotectMediaAsync(mxc));
        Assert.Empty(_homeserver.Requests);
    }

    /// <summary>Tuwunel ignores <c>order_by</c> on the user list and always sorts by user ID, so only <c>name</c> passes.</summary>
    [Theory]
    [InlineData("displayname")]
    [InlineData("admin")]
    [InlineData("is_guest")]
    [InlineData("deactivated")]
    [InlineData("creation_ts")]
    [InlineData("Name")] // keys are compared ordinally, as Synapse matches them
    public async Task Tuwunel_GetUserList_OtherKeyThanName_RefusesWithoutARequest(string orderBy)
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());

        var error = await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() => gateway.GetUserListAsync(0, 10, orderBy, "f"));

        Assert.Equal(nameof(TuwunelAdminGateway.GetUserListAsync), error.Operation);
        Assert.Empty(_homeserver.Requests);
    }

    [Fact]
    public async Task Tuwunel_GetUserList_ByName_SendsTheSynapseRequest()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.Respond = () => Results.Json(new { users = Array.Empty<object>(), total = 0 });

        await gateway.GetUserListAsync(20, 10, "name", "b", "alice");

        Assert.Equal(["/_synapse/admin/v3/users?from=20&limit=10&dir=b&order_by=name&locked=true&name=alice"], _homeserver.RequestTargets);
    }

    /// <summary>
    /// Tuwunel stores no creation time and sends <c>creation_ts: 0</c> (1.8.1 leaves it out): the gateway reports it as
    /// unknown (<c>null</c>) instead of 1970. A real value passes unchanged, on Tuwunel and on Synapse.
    /// </summary>
    [Theory]
    [InlineData("""{"name":"@a:example.com","creation_ts":0}""", null)]
    [InlineData("""{"name":"@a:example.com"}""", null)]
    [InlineData("""{"name":"@a:example.com","creation_ts":1560432506}""", 1560432506L)]
    public async Task Tuwunel_GetUserDetails_ZeroOrMissingCreationTs_IsUnknown(string body, long? expected)
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.Respond = () => Results.Text(body, "application/json");

        var user = await gateway.GetUserDetailsAsync("@a:example.com");

        Assert.Equal(expected, user!.CreationTs);
    }

    [Fact]
    public async Task Tuwunel_GetUserList_ZeroCreationTs_IsUnknown()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.Respond = () => Results.Text(
            """{"users":[{"name":"@a:example.com","creation_ts":0},{"name":"@b:example.com","creation_ts":1560432506000}],"total":2}""",
            "application/json");

        var list = await gateway.GetUserListAsync(0, 10, "name", "f");

        Assert.Equal(new long?[] { null, 1560432506000 }, list!.Users.Select(u => u.CreationTs));
    }

    [Fact]
    public async Task Synapse_GetUserDetails_CreationTs_IsReadAsSent()
    {
        _homeserver.Respond = () => Results.Text("""{"name":"@a:example.com","creation_ts":1560432506}""", "application/json");

        var user = await _gateway.GetUserDetailsAsync("@a:example.com");

        Assert.Equal(1560432506L, user!.CreationTs);
    }

    /// <summary>One request as the server received it. <see cref="Target"/> is the path and query exactly as sent.</summary>
    // ---- User devices and recent connections (#598) -------------------------------------------------------

    private const string UserDevicesTarget = "/_synapse/admin/v2/users/%40alice%3aexample.com/devices";

    [Fact]
    public async Task GetUserDevices_ReadsSynapsesAnswer()
    {
        _homeserver.Respond = () => Results.Text("""
            {"devices":[{"device_id":"QBUAZIFURK","display_name":"android","last_seen_ip":"192.0.2.1",
            "last_seen_user_agent":"Mozilla/5.0","last_seen_ts":1474491775024,"user_id":"@alice:example.com",
            "dehydrated":false}],"total":1}
            """, "application/json");

        var result = await _gateway.GetUserDevicesAsync("@alice:example.com");

        var device = Assert.Single(result!.Devices);
        Assert.Equal(
            ("QBUAZIFURK", "android", "192.0.2.1", "Mozilla/5.0", 1474491775024L, "@alice:example.com", (bool?)false),
            (device.DeviceId, device.DisplayName, device.LastSeenIp, device.LastSeenUserAgent, device.LastSeenTs, device.UserId, device.Dehydrated));
        Assert.Equal(1, result.Total);
        Assert.Equal(new CapturedRequest("GET", UserDevicesTarget, null, ""), Assert.Single(_homeserver.Requests));
    }

    /// <summary>
    /// Synapse leaves out the name of an unnamed device; Tuwunel never sends a user agent and sends <c>dehydrated</c>
    /// only for a user with a dehydrated device. A device that was never seen has no IP and no time.
    /// </summary>
    [Fact]
    public async Task GetUserDevices_MissingFields_ReadAsNull()
    {
        _homeserver.Respond = () => Results.Text("""{"devices":[{"device_id":"NEW","user_id":"@alice:example.com"}],"total":1}""", "application/json");

        var device = Assert.Single((await _gateway.GetUserDevicesAsync("@alice:example.com"))!.Devices);

        Assert.Equal(("NEW", (string?)null, (string?)null, (string?)null, (long?)null, (bool?)null),
            (device.DeviceId, device.DisplayName, device.LastSeenIp, device.LastSeenUserAgent, device.LastSeenTs, device.Dehydrated));
    }

    /// <summary>
    /// The device ID is percent-encoded with <c>%20</c> for a space: clients choose device IDs, and a path keeps the
    /// <c>+</c> that form encoding would send, which would name another device.
    /// </summary>
    [Theory]
    [InlineData("QBUAZIFURK", "QBUAZIFURK")]
    [InlineData("my phone/1+2", "my%20phone%2F1%2B2")]
    public async Task RenameUserDevice_SendsPutWithTheName(string deviceId, string encodedDeviceId)
    {
        _homeserver.AllowedMethod = "PUT";
        _homeserver.Respond = () => Results.Json(new { });

        await _gateway.RenameUserDeviceAsync("@alice:example.com", deviceId, new SynapseAdminDeviceUpdateRequest { DisplayName = "Work phone" });

        Assert.Equal(
            new CapturedRequest("PUT", $"{UserDevicesTarget}/{encodedDeviceId}", "application/json; charset=utf-8",
                """{"display_name":"Work phone"}"""),
            Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task DeleteUserDevices_SendsPostWithTheDeviceIds()
    {
        _homeserver.AllowedMethod = "POST";
        _homeserver.Respond = () => Results.Json(new { });

        await _gateway.DeleteUserDevicesAsync("@alice:example.com", new SynapseAdminDeleteDevicesRequest { Devices = ["QBUAZIFURK", "my phone"] });

        Assert.Equal(
            new CapturedRequest("POST", "/_synapse/admin/v2/users/%40alice%3aexample.com/delete_devices", "application/json; charset=utf-8",
                """{"devices":["QBUAZIFURK","my phone"]}"""),
            Assert.Single(_homeserver.Requests));
    }

    /// <summary>Synapse's answer: connections under the empty device key, grouped in sessions.</summary>
    [Fact]
    public async Task GetUserWhois_ReadsTheConnections()
    {
        _homeserver.Respond = () => Results.Text("""
            {"user_id":"@alice:example.com","devices":{"":{"sessions":[{"connections":[
            {"ip":"192.0.2.1","last_seen":1417222374433,"user_agent":"Mozilla/5.0"},
            {"ip":"192.0.2.10","last_seen":1417222374500}]}]}}}
            """, "application/json");

        var result = await _gateway.GetUserWhoisAsync("@alice:example.com");

        var connections = Assert.Single(Assert.Single(result!.Devices!).Value.Sessions!).Connections!;
        Assert.Equal(2, connections.Count);
        Assert.Equal(("192.0.2.1", (long?)1417222374433, (string?)"Mozilla/5.0"), (connections[0].Ip, connections[0].LastSeen, connections[0].UserAgent));
        Assert.Equal(("192.0.2.10", (long?)1417222374500, (string?)null), (connections[1].Ip, connections[1].LastSeen, connections[1].UserAgent));
        Assert.Equal(new CapturedRequest("GET", "/_synapse/admin/v1/whois/%40alice%3aexample.com", null, ""), Assert.Single(_homeserver.Requests));
    }

    private static readonly Dictionary<string, Func<SynapseAdminGateway, CancellationToken, Task>> DeviceMethods = new()
    {
        [nameof(SynapseAdminGateway.GetUserDevicesAsync)] = (g, ct) => g.GetUserDevicesAsync("@alice:example.com", ct),
        [nameof(SynapseAdminGateway.RenameUserDeviceAsync)] = (g, ct) => g.RenameUserDeviceAsync("@alice:example.com", "QBUAZIFURK",
            new SynapseAdminDeviceUpdateRequest { DisplayName = "Work phone" }, ct),
        [nameof(SynapseAdminGateway.DeleteUserDevicesAsync)] = (g, ct) => g.DeleteUserDevicesAsync("@alice:example.com",
            new SynapseAdminDeleteDevicesRequest { Devices = ["QBUAZIFURK"] }, ct),
        [nameof(SynapseAdminGateway.GetUserWhoisAsync)] = (g, ct) => g.GetUserWhoisAsync("@alice:example.com", ct),
    };

    public static TheoryData<string> DeviceMethodNames => new(DeviceMethods.Keys);

    [Theory]
    [MemberData(nameof(DeviceMethodNames))]
    public async Task Devices_CancelledToken_SendsNoRequest(string method)
    {
        _homeserver.Respond = () => Results.Json(new { });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DeviceMethods[method](_gateway, cts.Token));

        Assert.Empty(_homeserver.Requests);
    }

    [Theory]
    [MemberData(nameof(DeviceMethodNames))]
    public async Task Devices_MatrixErrorResponse_ThrowsMatrixException(string method)
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_NOT_FOUND", error = "No device found" }, statusCode: 404);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => DeviceMethods[method](_gateway, CancellationToken.None));

        Assert.Equal("M_NOT_FOUND", ex.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(DeviceMethodNames))]
    public async Task Devices_NonJsonErrorResponse_ThrowsHttpRequestException(string method)
    {
        _homeserver.Respond = () => Results.Text("Internal Server Error", statusCode: 500);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => DeviceMethods[method](_gateway, CancellationToken.None));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    /// <summary>Tuwunel (1.8.2, 2.0.0) serves the device calls as Synapse does, so its gateway sends the same requests.</summary>
    [Theory]
    [MemberData(nameof(DeviceMethodNames))]
    public async Task Tuwunel_Devices_SendTheSynapseRequests(string method)
    {
        _homeserver.Respond = () => Results.Json(new { });
        await DeviceMethods[method](_gateway, CancellationToken.None);
        var synapseRequest = Assert.Single(_homeserver.Requests);
        _homeserver.Requests.Clear();

        await DeviceMethods[method](new TuwunelAdminGateway(CreateHomeserver()), CancellationToken.None);

        Assert.Equal(synapseRequest, Assert.Single(_homeserver.Requests));
    }

    /// <summary>The session's device comes from the whoami answer that every sign-in already fetches.</summary>
    [Theory]
    [InlineData("""{"user_id":"@admin:example.com","device_id":"APPSESSION"}""", "APPSESSION")]
    [InlineData("""{"user_id":"@admin:example.com"}""", null)]
    public async Task DeviceId_ComesFromWhoami(string whoami, string? expected)
    {
        var homeserver = CreateHomeserver();
        _homeserver.Respond = () => Results.Text(whoami, "application/json");
        await homeserver.Initialise();

        Assert.Equal(expected, new SynapseAdminGateway(homeserver).DeviceId);
    }

    // ---- Deleting a user's media (#599) ---------------------------------------------------------------

    private const string UserMediaDeleteTarget = "/_synapse/admin/v1/users/%40alice%3aexample.com/media?from=0&limit=100&order_by=created_ts&dir=b";

    /// <summary>Always from the first page: the remaining media are reordered after each deletion.</summary>
    [Fact]
    public async Task DeleteUserMedia_SendsDeleteForTheFirstPage_AndReadsTheCount()
    {
        _homeserver.AllowedMethod = "DELETE";
        _homeserver.Respond = () => Results.Json(new { deleted_media = new[] { "abc", "def" }, total = 2 });

        var result = await _gateway.DeleteUserMediaAsync("@alice:example.com", 100);

        Assert.Equal(["abc", "def"], result!.DeletedMedia);
        Assert.Equal(2, result.Total);
        Assert.Equal(new CapturedRequest("DELETE", UserMediaDeleteTarget, null, ""), Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task DeleteUserMedia_EmptyBody_IsNull()
    {
        _homeserver.Respond = () => Results.Text("", "application/json");

        Assert.Null(await _gateway.DeleteUserMediaAsync("@alice:example.com", 100));
    }

    [Fact]
    public async Task DeleteUserMedia_CancelledToken_SendsNoRequest()
    {
        _homeserver.Respond = () => Results.Json(new { deleted_media = Array.Empty<string>(), total = 0 });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _gateway.DeleteUserMediaAsync("@alice:example.com", 100, cts.Token));

        Assert.Empty(_homeserver.Requests);
    }

    [Fact]
    public async Task DeleteUserMedia_MatrixErrorResponse_ThrowsMatrixException()
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_NOT_FOUND", error = "Unknown user" }, statusCode: 404);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => _gateway.DeleteUserMediaAsync("@alice:example.com", 100));

        Assert.Equal("M_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task DeleteUserMedia_NonJsonErrorResponse_ThrowsHttpRequestException()
    {
        _homeserver.Respond = () => Results.Text("Internal Server Error", statusCode: 500);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _gateway.DeleteUserMediaAsync("@alice:example.com", 100));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    /// <summary>Tuwunel (1.8.2, 2.0.0) serves the endpoint as Synapse does, so its gateway sends the same request.</summary>
    [Fact]
    public async Task Tuwunel_DeleteUserMedia_SendsTheSynapseRequest()
    {
        _homeserver.Respond = () => Results.Json(new { deleted_media = Array.Empty<string>(), total = 0 });

        await new TuwunelAdminGateway(CreateHomeserver()).DeleteUserMediaAsync("@alice:example.com", 100);

        Assert.Equal(new CapturedRequest("DELETE", UserMediaDeleteTarget, null, ""), Assert.Single(_homeserver.Requests));
    }

    // ---- Redacting a user's events (#600) --------------------------------------------------------------

    private const string RedactTarget = "/_synapse/admin/v1/user/%40alice%3aexample.com/redact";

    private static SynapseAdminUserRedactRequest RedactInUserRooms() =>
        new() { Rooms = [], Limit = 1000, UseAdmin = false };

    /// <summary>
    /// <c>rooms</c> is required and sent also when empty (the user's rooms), which <c>LibMatrix</c>'s call leaves out.
    /// Without a reason, the body has no <c>reason</c>, and it never has a time range.
    /// </summary>
    [Fact]
    public async Task RedactUserEvents_UserRooms_SendsPostWithAnEmptyRoomList_AndReadsTheRedactId()
    {
        _homeserver.AllowedMethod = "POST";
        _homeserver.Respond = () => Results.Json(new { redact_id = "redact-1" });

        var result = await _gateway.RedactUserEventsAsync("@alice:example.com", RedactInUserRooms());

        Assert.Equal("redact-1", result!.RedactId);
        Assert.Equal(
            new CapturedRequest("POST", RedactTarget, "application/json; charset=utf-8", """{"rooms":[],"limit":1000,"use_admin":false}"""),
            Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task RedactUserEvents_SelectedRoomsWithReason_SendsThemInTheBody()
    {
        _homeserver.AllowedMethod = "POST";
        _homeserver.Respond = () => Results.Json(new { redact_id = "redact-2" });

        await _gateway.RedactUserEventsAsync("@alice:example.com", new SynapseAdminUserRedactRequest
        {
            Rooms = ["!a:example.com", "!b:example.com"],
            Reason = "spam",
            Limit = 50,
            UseAdmin = true
        });

        Assert.Equal(
            new CapturedRequest("POST", RedactTarget, "application/json; charset=utf-8",
                """{"rooms":["!a:example.com","!b:example.com"],"reason":"spam","limit":50,"use_admin":true}"""),
            Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task RedactUserEvents_AnswerWithoutRedactId_ReadsNull()
    {
        _homeserver.Respond = () => Results.Json(new { });

        var result = await _gateway.RedactUserEventsAsync("@alice:example.com", RedactInUserRooms());

        Assert.NotNull(result);
        Assert.Null(result.RedactId);
    }

    [Fact]
    public async Task RedactUserEvents_CancelledToken_SendsNoRequest()
    {
        _homeserver.Respond = () => Results.Json(new { redact_id = "redact-1" });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _gateway.RedactUserEventsAsync("@alice:example.com", RedactInUserRooms(), cts.Token));

        Assert.Empty(_homeserver.Requests);
    }

    /// <summary>Synapse refuses a second redaction while one of the same user is active, with a 400 and no errcode.</summary>
    [Fact]
    public async Task RedactUserEvents_MatrixErrorResponse_ThrowsMatrixException()
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_UNKNOWN", error = "Redact already in progress for user @alice:example.com" }, statusCode: 400);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => _gateway.RedactUserEventsAsync("@alice:example.com", RedactInUserRooms()));

        Assert.Equal("M_UNKNOWN", ex.ErrorCode);
    }

    [Fact]
    public async Task RedactUserEvents_NonJsonErrorResponse_ThrowsHttpRequestException()
    {
        _homeserver.Respond = () => Results.Text("Internal Server Error", statusCode: 500);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _gateway.RedactUserEventsAsync("@alice:example.com", RedactInUserRooms()));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    [Fact]
    public async Task GetUserRedactionStatus_SendsAGetForTheRedactId_AndReadsTheFailures()
    {
        _homeserver.AllowedMethod = "GET";
        _homeserver.Respond = () => Results.Json(new
        {
            status = "complete",
            failed_redactions = new Dictionary<string, string> { ["$event1"] = "Not allowed", ["$event2"] = "Timeout" }
        });

        var status = await _gateway.GetUserRedactionStatusAsync("redact+1");

        Assert.Equal(new CapturedRequest("GET", "/_synapse/admin/v1/user/redact_status/redact%2b1", null, ""), Assert.Single(_homeserver.Requests));
        Assert.NotNull(status);
        Assert.Equal("complete", status.Status);
        Assert.Equal(new Dictionary<string, string> { ["$event1"] = "Not allowed", ["$event2"] = "Timeout" }, status.FailedRedactions);
        Assert.Null(status.Error);
    }

    [Fact]
    public async Task GetUserRedactionStatus_Failed_ReadsTheError()
    {
        _homeserver.Respond = () => Results.Json(new { status = "failed", error = "Unknown error, please check the logs for more information." });

        var status = await _gateway.GetUserRedactionStatusAsync("redact-1");

        Assert.Equal(("failed", "Unknown error, please check the logs for more information."), (status!.Status, status.Error));
        Assert.Null(status.FailedRedactions);
    }

    /// <summary>
    /// A redaction Synapse no longer knows (404 <c>M_NOT_FOUND</c>), a homeserver without the endpoint
    /// (<c>M_UNRECOGNIZED</c>, as 404 or 405) and a bare 404 from a proxy all mean "no status".
    /// </summary>
    [Theory]
    [InlineData(404, "M_NOT_FOUND")]
    [InlineData(404, "M_UNRECOGNIZED")]
    [InlineData(405, "M_UNRECOGNIZED")]
    [InlineData(404, null)]
    public async Task GetUserRedactionStatus_UnknownRedactionOrMissingEndpoint_ReturnsNull(int statusCode, string? errcode)
    {
        _homeserver.Respond = () => errcode == null
            ? Results.Text("Not Found", statusCode: statusCode)
            : Results.Json(new { errcode, error = "Not found" }, statusCode: statusCode);

        Assert.Null(await _gateway.GetUserRedactionStatusAsync("redact-1"));
    }

    [Fact]
    public async Task GetUserRedactionStatus_CancelledToken_SendsNoRequest()
    {
        _homeserver.Respond = () => Results.Json(new { status = "active" });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _gateway.GetUserRedactionStatusAsync("redact-1", cts.Token));

        Assert.Empty(_homeserver.Requests);
    }

    [Fact]
    public async Task GetUserRedactionStatus_MatrixErrorResponse_ThrowsMatrixException()
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_FORBIDDEN", error = "You are not a server admin" }, statusCode: 403);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => _gateway.GetUserRedactionStatusAsync("redact-1"));

        Assert.Equal("M_FORBIDDEN", ex.ErrorCode);
    }

    [Fact]
    public async Task GetUserRedactionStatus_NonJsonErrorResponse_ThrowsHttpRequestException()
    {
        _homeserver.Respond = () => Results.Text("Internal Server Error", statusCode: 500);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _gateway.GetUserRedactionStatusAsync("redact-1"));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    // Tuwunel 1.8.2 and 2.0.0 take the limit window in reverse timeline order instead of newest received first, and
    // redact through a different permission path, so the same request can redact other events. The gateway refuses the
    // redaction and its status read before sending anything (ADR 0006).
    [Fact]
    public async Task Tuwunel_RedactUserEvents_RefusesWithoutARequest()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());

        var error = await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() =>
            gateway.RedactUserEventsAsync("@alice:example.com", RedactInUserRooms()));

        Assert.Equal(nameof(TuwunelAdminGateway.RedactUserEventsAsync), error.Operation);
        Assert.Empty(_homeserver.Requests);
    }

    [Fact]
    public async Task Tuwunel_GetUserRedactionStatus_RefusesWithoutARequest()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());

        var error = await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() => gateway.GetUserRedactionStatusAsync("redact-1"));

        Assert.Equal(nameof(TuwunelAdminGateway.GetUserRedactionStatusAsync), error.Operation);
        Assert.Empty(_homeserver.Requests);
    }

    // ---- Room recovery (#587) ------------------------------------------------------------------------

    private const string MakeRoomAdminTarget = "/_synapse/admin/v1/rooms/!room%3aexample.com/make_room_admin";
    private const string JoinTarget = "/_synapse/admin/v1/join/!room%3aexample.com";
    private const string JoinRulesStateTarget = "/_synapse/admin/v1/rooms/!room%3aexample.com/state?type=m.room.join_rules";
    private const string RoomDetailsTarget = "/_synapse/admin/v1/rooms/!room%3aexample.com";
    private const string AliasJoinTarget = "/_synapse/admin/v1/join/%23my%20room%3Aexample.com";
    private const string AliasDirectoryTarget = "/_matrix/client/v3/directory/room/%23my%20room%3Aexample.com";
    private const string InviteTarget = "/_matrix/client/v3/rooms/!room%3aexample.com/invite";
    private const string JsonContentType = "application/json; charset=utf-8";

    /// <summary>Without a user, the body is <c>{}</c>, so Synapse grants the power to the calling admin.</summary>
    [Fact]
    public async Task MakeRoomAdmin_SignedInAdmin_SendsPostWithAnEmptyObject()
    {
        _homeserver.AllowedMethod = "POST";
        _homeserver.Respond = () => Results.Json(new { });

        await _gateway.MakeRoomAdminAsync("!room:example.com", null);

        Assert.Equal(new CapturedRequest("POST", MakeRoomAdminTarget, JsonContentType, "{}"), Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task MakeRoomAdmin_OtherUser_SendsTheUserId()
    {
        _homeserver.AllowedMethod = "POST";
        _homeserver.Respond = () => Results.Json(new { });

        await _gateway.MakeRoomAdminAsync("!room:example.com", "@bob:other.org");

        Assert.Equal(
            new CapturedRequest("POST", MakeRoomAdminTarget, JsonContentType, """{"user_id":"@bob:other.org"}"""),
            Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task MakeRoomAdmin_CancelledToken_SendsNoRequest()
    {
        _homeserver.Respond = () => Results.Json(new { });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _gateway.MakeRoomAdminAsync("!room:example.com", null, cts.Token));

        Assert.Empty(_homeserver.Requests);
    }

    /// <summary>Synapse refuses with a 400 when no joined local user holds the power to change power levels.</summary>
    [Fact]
    public async Task MakeRoomAdmin_MatrixErrorResponse_ThrowsMatrixException()
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_UNKNOWN", error = "No local admin user in room" }, statusCode: 400);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => _gateway.MakeRoomAdminAsync("!room:example.com", null));

        Assert.Equal("M_UNKNOWN", ex.ErrorCode);
    }

    [Fact]
    public async Task MakeRoomAdmin_NonJsonErrorResponse_ThrowsHttpRequestException()
    {
        _homeserver.Respond = () => Results.Text("Internal Server Error", statusCode: 500);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _gateway.MakeRoomAdminAsync("!room:example.com", null));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    [Fact]
    public async Task JoinUserToRoom_SendsPostWithTheUserId_AndReadsTheRoomId()
    {
        _homeserver.AllowedMethod = "POST";
        _homeserver.Respond = () => Results.Json(new { room_id = "!room:example.com" });

        var result = await _gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com");

        Assert.Equal("!room:example.com", result!.RoomId);
        Assert.Equal(
            new CapturedRequest("POST", JoinTarget, JsonContentType, """{"user_id":"@bob:example.com"}"""),
            Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task JoinUserToRoom_CancelledToken_SendsNoRequest()
    {
        _homeserver.Respond = () => Results.Json(new { room_id = "!room:example.com" });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com", cancellationToken: cts.Token));

        Assert.Empty(_homeserver.Requests);
    }

    /// <summary>Synapse refuses a user on another server with a 400, and an invite the admin may not send with a 403.</summary>
    [Fact]
    public async Task JoinUserToRoom_MatrixErrorResponse_ThrowsMatrixException()
    {
        _homeserver.Respond = () => Results.Json(new { errcode = "M_FORBIDDEN", error = "You don't have permission to invite users" }, statusCode: 403);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => _gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com"));

        Assert.Equal("M_FORBIDDEN", ex.ErrorCode);
    }

    [Fact]
    public async Task JoinUserToRoom_NonJsonErrorResponse_ThrowsHttpRequestException()
    {
        _homeserver.Respond = () => Results.Text("Internal Server Error", statusCode: 500);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com"));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    // Tuwunel 1.8.2 and 2.0.0 invite before they change the power levels and skip the invite for a room listed in the
    // public room directory, even an invite-only one. The gateway can't send Synapse's invite as the local user the power
    // comes from, so it refuses make room admin before sending anything (ADR 0006).
    [Fact]
    public async Task Tuwunel_MakeRoomAdmin_RefusesWithoutARequest()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());

        var error = await Assert.ThrowsAsync<OperationNotSupportedByHomeserverException>(() => gateway.MakeRoomAdminAsync("!room:example.com", null));

        Assert.Equal(nameof(TuwunelAdminGateway.MakeRoomAdminAsync), error.Operation);
        Assert.Empty(_homeserver.Requests);
    }

    private static object StateWith(params object[] events) => new { state = events };

    private static object JoinRulesEvent(object content, string stateKey = "") =>
        new { type = "m.room.join_rules", state_key = stateKey, content, sender = "@admin:example.com" };

    /// <summary>
    /// Answers the room details with <paramref name="localMembers"/> joined local users (#606), the state read with
    /// <paramref name="state"/>, the invite with <paramref name="invite"/>, the alias lookup with the room, and any join
    /// with the room ID.
    /// </summary>
    private void RespondToTuwunelJoin(object state, IResult? invite = null, int localMembers = 1)
    {
        _homeserver.RespondTo = request => request.Target.Split('?')[0] switch
        {
            RoomDetailsTarget => Results.Json(new { room_id = "!room:example.com", joined_local_members = localMembers }),
            AliasDirectoryTarget => Results.Json(new { room_id = "!room:example.com", servers = new[] { "example.com" } }),
            AliasJoinTarget => Results.Json(new { room_id = "!room:example.com" }),
            "/_synapse/admin/v1/rooms/!room%3aexample.com/state" => Results.Json(state),
            InviteTarget => invite ?? Results.Json(new { }),
            JoinTarget => Results.Json(new { room_id = "!room:example.com" }),
            _ => Results.NotFound()
        };
    }

    private static readonly CapturedRequest DetailsRead = new("GET", RoomDetailsTarget, null, "");
    private static readonly CapturedRequest JoinRulesRead = new("GET", JoinRulesStateTarget, null, "");
    private static readonly CapturedRequest InviteBob = new("POST", InviteTarget, JsonContentType, """{"user_id":"@bob:example.com"}""");
    private static readonly CapturedRequest JoinBob = new("POST", JoinTarget, JsonContentType, """{"user_id":"@bob:example.com"}""");

    /// <summary>
    /// Synapse invites as the requesting admin before the join unless the join rule is <c>public</c>, restricted rooms
    /// included. Tuwunel 1.8.2 never invites and 2.0.0 only sometimes, so the gateway sends that invite itself.
    /// </summary>
    [Theory]
    [InlineData("invite")]
    [InlineData("knock")]
    [InlineData("restricted")]
    [InlineData("knock_restricted")]
    [InlineData("private")]
    public async Task Tuwunel_JoinUserToRoom_RoomNotPublic_InvitesAsTheAdminBeforeTheJoin(string joinRule)
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        RespondToTuwunelJoin(StateWith(JoinRulesEvent(new { join_rule = joinRule })));

        var result = await gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com");

        Assert.Equal("!room:example.com", result!.RoomId);
        Assert.Equal(new[] { DetailsRead, JoinRulesRead, InviteBob, JoinBob }, _homeserver.Requests);
    }

    /// <summary>A join rules event without a <c>join_rule</c> isn't <c>public</c> for Synapse either.</summary>
    [Fact]
    public async Task Tuwunel_JoinUserToRoom_JoinRulesWithoutARule_Invites()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        RespondToTuwunelJoin(StateWith(JoinRulesEvent(new { })));

        await gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com");

        Assert.Equal(new[] { DetailsRead, JoinRulesRead, InviteBob, JoinBob }, _homeserver.Requests);
    }

    [Fact]
    public async Task Tuwunel_JoinUserToRoom_PublicRoom_JoinsWithoutAnInvite()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        RespondToTuwunelJoin(StateWith(JoinRulesEvent(new { join_rule = "public" })));

        await gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com");

        Assert.Equal(new[] { DetailsRead, JoinRulesRead, JoinBob }, _homeserver.Requests);
    }

    /// <summary>
    /// Without a join rules event Synapse doesn't invite. Other events, and a join rules event with a state key (not the
    /// room's join rule), are ignored, so a server that ignores <c>type</c> gets the same result.
    /// </summary>
    [Fact]
    public async Task Tuwunel_JoinUserToRoom_NoJoinRulesEvent_JoinsWithoutAnInvite()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        RespondToTuwunelJoin(StateWith(
            new { type = "m.room.create", state_key = "", content = new { room_version = "11" } },
            JoinRulesEvent(new { join_rule = "invite" }, stateKey: "x")));

        await gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com");

        Assert.Equal(new[] { DetailsRead, JoinRulesRead, JoinBob }, _homeserver.Requests);
    }

    /// <summary>The first matching event decides, as other events in the answer don't change the join rule.</summary>
    [Fact]
    public async Task Tuwunel_JoinUserToRoom_StateWithOtherEvents_UsesTheJoinRulesEvent()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        RespondToTuwunelJoin(StateWith(
            new { type = "m.room.create", state_key = "", content = new { room_version = "11" } },
            JoinRulesEvent(new { join_rule = "invite" })));

        await gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com");

        Assert.Equal(new[] { DetailsRead, JoinRulesRead, InviteBob, JoinBob }, _homeserver.Requests);
    }

    [Fact]
    public async Task Tuwunel_JoinUserToRoom_FailedStateRead_SendsNothingMore()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.RespondTo = request => request.Target == RoomDetailsTarget
            ? Results.Json(new { room_id = "!room:example.com", joined_local_members = 1 })
            : Results.Json(new { errcode = "M_FORBIDDEN", error = "Not allowed" }, statusCode: 403);

        await Assert.ThrowsAsync<MatrixException>(() => gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com"));

        Assert.Equal(new[] { DetailsRead, JoinRulesRead }, _homeserver.Requests);
    }

    /// <summary>A room details error other than "not found" propagates, so nothing is written (#606).</summary>
    [Fact]
    public async Task Tuwunel_JoinUserToRoom_FailedDetailsRead_SendsNothingMore()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.RespondTo = _ => Results.Json(new { errcode = "M_FORBIDDEN", error = "Not allowed" }, statusCode: 403);

        await Assert.ThrowsAsync<MatrixException>(() => gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com"));

        Assert.Equal(new[] { DetailsRead }, _homeserver.Requests);
    }

    /// <summary>An admin who isn't in the room may not invite; the join is then not sent, as on Synapse.</summary>
    [Fact]
    public async Task Tuwunel_JoinUserToRoom_FailedInvite_SendsNoJoin()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        RespondToTuwunelJoin(StateWith(JoinRulesEvent(new { join_rule = "invite" })),
            invite: Results.Json(new { errcode = "M_FORBIDDEN", error = "You are not in the room" }, statusCode: 403));

        var ex = await Assert.ThrowsAsync<MatrixException>(() => gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com"));

        Assert.Equal("M_FORBIDDEN", ex.ErrorCode);
        Assert.Equal(new[] { DetailsRead, JoinRulesRead, InviteBob }, _homeserver.Requests);
    }

    [Fact]
    public async Task Tuwunel_JoinUserToRoom_CancelledToken_SendsNoRequest()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        RespondToTuwunelJoin(StateWith(JoinRulesEvent(new { join_rule = "invite" })));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com", cancellationToken: cts.Token));

        Assert.Empty(_homeserver.Requests);
    }

    // ---- Join by alias or through servers (#606) ---------------------------------------------------------

    /// <summary>
    /// An alias may contain spaces; form encoding would send '+', which a path keeps, so it takes
    /// <c>Uri.EscapeDataString</c> (#580). The body is the same.
    /// </summary>
    [Fact]
    public async Task JoinUserToRoom_Alias_IsEscapedInThePath()
    {
        _homeserver.AllowedMethod = "POST";
        _homeserver.Respond = () => Results.Json(new { room_id = "!room:example.com" });

        var result = await _gateway.JoinUserToRoomAsync("#my room:example.com", "@bob:example.com");

        Assert.Equal("!room:example.com", result!.RoomId);
        Assert.Equal(
            new CapturedRequest("POST", AliasJoinTarget, JsonContentType, """{"user_id":"@bob:example.com"}"""),
            Assert.Single(_homeserver.Requests));
    }

    /// <summary>Synapse reads every <c>server_name</c> query parameter as a server to join through.</summary>
    [Fact]
    public async Task JoinUserToRoom_Servers_AreSentAsServerNameParameters()
    {
        _homeserver.Respond = () => Results.Json(new { room_id = "!room:example.com" });

        await _gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com", ["a.org", "b.org:8448", "[::1]"]);

        Assert.Equal(
            new CapturedRequest("POST", JoinTarget + "?server_name=a.org&server_name=b.org%3a8448&server_name=%5b%3a%3a1%5d",
                JsonContentType, """{"user_id":"@bob:example.com"}"""),
            Assert.Single(_homeserver.Requests));
    }

    [Fact]
    public async Task JoinUserToRoom_NoServers_SendsNoQuery()
    {
        _homeserver.Respond = () => Results.Json(new { room_id = "!room:example.com" });

        await _gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com", []);

        Assert.Equal(JoinTarget, Assert.Single(_homeserver.RequestTargets));
    }

    /// <summary>
    /// Synapse deletes a room's current state when this server leaves it, so it finds no join rule and doesn't invite.
    /// Tuwunel keeps the state, so without a joined local user the gateway skips the state read and the invite (#606).
    /// </summary>
    [Fact]
    public async Task Tuwunel_JoinUserToRoom_NoJoinedLocalUser_JoinsWithoutReadingTheRuleOrInviting()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        RespondToTuwunelJoin(StateWith(JoinRulesEvent(new { join_rule = "invite" })), localMembers: 0);

        await gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com");

        Assert.Equal(new[] { DetailsRead, JoinBob }, _homeserver.Requests);
    }

    /// <summary>A room Tuwunel doesn't know (404 <c>M_NOT_FOUND</c>) has no state on Synapse either: no invite.</summary>
    [Fact]
    public async Task Tuwunel_JoinUserToRoom_UnknownRoom_JoinsWithoutInviting()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.RespondTo = request => request.Target == RoomDetailsTarget
            ? Results.Json(new { errcode = "M_NOT_FOUND", error = "Room not found" }, statusCode: 404)
            : Results.Json(new { room_id = "!room:example.com" });

        await gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com", ["remote.org"]);

        Assert.Equal(new[] { DetailsRead, JoinBob with { Target = JoinTarget + "?server_name=remote.org" } }, _homeserver.Requests);
    }

    [Fact]
    public async Task Tuwunel_JoinUserToRoom_Servers_ArePassedToTheJoin()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        RespondToTuwunelJoin(StateWith(JoinRulesEvent(new { join_rule = "public" })));

        await gateway.JoinUserToRoomAsync("!room:example.com", "@bob:example.com", ["a.org", "b.org"]);

        Assert.Equal(new[] { DetailsRead, JoinRulesRead, JoinBob with { Target = JoinTarget + "?server_name=a.org&server_name=b.org" } },
            _homeserver.Requests);
    }

    /// <summary>
    /// An alias is resolved first, as Synapse resolves it, and the checks run on the room it names. The join carries the
    /// alias, so Tuwunel joins through the alias's servers, as Synapse does.
    /// </summary>
    [Fact]
    public async Task Tuwunel_JoinUserToRoom_Alias_ResolvesItFirst_AndJoinsWithTheAlias()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        RespondToTuwunelJoin(StateWith(JoinRulesEvent(new { join_rule = "invite" })));

        var result = await gateway.JoinUserToRoomAsync("#my room:example.com", "@bob:example.com");

        Assert.Equal("!room:example.com", result!.RoomId);
        Assert.Equal(new[]
        {
            new CapturedRequest("GET", AliasDirectoryTarget, null, ""),
            DetailsRead, JoinRulesRead, InviteBob,
            JoinBob with { Target = AliasJoinTarget }
        }, _homeserver.Requests);
    }

    [Fact]
    public async Task Tuwunel_JoinUserToRoom_UnknownAlias_SendsNothingMore()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.RespondTo = _ => Results.Json(new { errcode = "M_NOT_FOUND", error = "Room alias not found" }, statusCode: 404);

        var ex = await Assert.ThrowsAsync<MatrixException>(() => gateway.JoinUserToRoomAsync("#my room:example.com", "@bob:example.com"));

        Assert.Equal("M_NOT_FOUND", ex.ErrorCode);
        Assert.Equal(new[] { new CapturedRequest("GET", AliasDirectoryTarget, null, "") }, _homeserver.Requests);
    }

    [Fact]
    public async Task Tuwunel_JoinUserToRoom_AliasAnswerWithoutARoom_SendsNothingMore()
    {
        var gateway = new TuwunelAdminGateway(CreateHomeserver());
        _homeserver.RespondTo = _ => Results.Json(new { servers = new[] { "example.com" } });

        await Assert.ThrowsAsync<InvalidDataException>(() => gateway.JoinUserToRoomAsync("#my room:example.com", "@bob:example.com"));

        Assert.Single(_homeserver.Requests);
    }

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
        /// <summary>When set, answers each request by what it is, instead of <see cref="Respond"/>.</summary>
        public Func<CapturedRequest, IResult>? RespondTo { get; set; }
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
                var request = new CapturedRequest(context.Request.Method, context.Features.Get<IHttpRequestFeature>()!.RawTarget,
                    context.Request.ContentType, await reader.ReadToEndAsync());
                Requests.Enqueue(request);
                if (AllowedMethod != null && !HttpMethods.Equals(context.Request.Method, AllowedMethod))
                {
                    return Results.Json(new { errcode = "M_UNRECOGNIZED", error = "Unrecognized request" }, statusCode: 405);
                }

                return RespondTo?.Invoke(request) ?? Respond();
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
