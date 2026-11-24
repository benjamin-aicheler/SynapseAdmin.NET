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
/// Also the seven POSTs that send <c>{}</c> and read no response body: the exact request, that a server accepting only
/// the documented POST (as Synapse does) accepts them (#516), and the same error behavior.
/// Also room deletion (#518): the <c>delete_id</c> the DELETE returns, and the two status reads with the answers that
/// mean "no status".
/// Also the room list's sort direction per brand (#541).
/// Also the shadow login (#539): the token's expiry travels in the body, not the query string.
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
            "/_synapse/admin/v3/users?from=20&limit=10&dir=f&order_by=name&user_id=%40alice%3aexample.com",
            async g => (await g.GetUserListAsync(20, 10, "name", "f", "@alice:example.com"))?.NextToken),
        nameof(SynapseAdminGateway.GetUserMediaStatisticsAsync) => (
            "/_synapse/admin/v1/statistics/users/media?limit=10&order_by=media_length&dir=b&search_term=alice",
            async g => (await g.GetUserMediaStatisticsAsync(10, "media_length", "b", "alice"))?.NextToken),
        nameof(SynapseAdminGateway.GetUserMediaAsync) => (
            "/_synapse/admin/v1/users/%40alice%3aexample.com/media?from=100&limit=25&order_by=created_ts&dir=b",
            async g => (await g.GetUserMediaAsync("@alice:example.com", 100, 25))?.NextToken),
        nameof(SynapseAdminGateway.GetFederationDestinationListAsync) => (
            "/_synapse/admin/v1/federation/destinations?from=0&limit=50&dir=b",
            async g => (await g.GetFederationDestinationListAsync(0, 50, "b"))?.NextToken),
        nameof(SynapseAdminGateway.GetEventReportListAsync) => (
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
        nameof(SynapseAdminGateway.DeleteRoomAsync),
        nameof(SynapseAdminGateway.DeleteEventReportAsync),
        nameof(SynapseAdminGateway.DeleteRegistrationTokenAsync),
        nameof(SynapseAdminGateway.DeleteMediaAsync),
    };

    // Each raw DELETE with fixed arguments and the exact request it sends. The room body uses default (not web)
    // serializer options: declared property order, nulls written.
    private static (CapturedRequest Request, Func<SynapseAdminGateway, Task> Call) DeleteMethod(string name) => name switch
    {
        nameof(SynapseAdminGateway.DeleteRoomAsync) => (
            new("DELETE", "/_synapse/admin/v2/rooms/!room%3aexample.com", "application/json; charset=utf-8",
                """{"new_room_user_id":"@admin:example.com","room_name":null,"block":true,"purge":false,"message":"Moved"}"""),
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

    // Registration token update (#519): Synapse leaves an omitted field unchanged and removes the limit for an explicit
    // null, so the update always sends both fields. Create keeps omitting null fields (Synapse's defaults apply).
    public static TheoryData<int?, long?, string> TokenUpdates => new()
    {
        { null, null, """{"uses_allowed":null,"expiry_time":null}""" },
        { null, 4781243146000, """{"uses_allowed":null,"expiry_time":4781243146000}""" },
        { 5, null, """{"uses_allowed":5,"expiry_time":null}""" },
        { 5, 4781243146000, """{"uses_allowed":5,"expiry_time":4781243146000}""" },
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
