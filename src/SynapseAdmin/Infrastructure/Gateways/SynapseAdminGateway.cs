using LibMatrix.Homeservers;
using LibMatrix.StructuredData;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.Requests;
using System.Text;
using System.Text.Json;
using ArcaneLibs.Extensions;
using SynapseAdmin.Infrastructure.Serialization;

namespace SynapseAdmin.Infrastructure.Gateways;

/// <summary>
/// The Synapse Admin API contract (ADR 0006): every request follows the Synapse Admin API reference. Serves Synapse and
/// any homeserver that answers the admin API without a gateway class of its own. A server that deviates gets a subclass
/// that overrides the deviating methods, such as <see cref="TuwunelAdminGateway"/>. <c>serverBrand</c> is
/// only reported, never used for a decision.
/// Does not depend on LibMatrix's SDK-level .Admin properties, so it also works on homeservers that return
/// AuthenticatedHomeserverGeneric instead of AuthenticatedHomeserverSynapse.
/// </summary>
public class SynapseAdminGateway(
    AuthenticatedHomeserverGeneric homeserver,
    string serverBrand = GatewayConstants.SynapseBrand,
    string serverVersion = GatewayConstants.UnknownVersion) : MatrixGatewayBase(homeserver)
{
    public override bool SupportsAdminApi => true;
    public override string ServerBrand => serverBrand;
    public override string ServerVersion => serverVersion;

    /// <summary>
    /// Cached options to handle Synapse's inconsistent next_token types (String vs Number).
    /// Passed only to JsonSerializer.DeserializeAsync (never to LibMatrix's mutating MatrixHttpClient).
    /// </summary>
    protected static readonly JsonSerializerOptions SynapseCompatibilityJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringConverter() }
    };

    /// <summary>
    /// GET and deserialize with <see cref="SynapseCompatibilityJsonOptions"/>. Never switch this to GetFromJsonAsync:
    /// LibMatrix's MatrixHttpClient mutates the options it is given.
    /// </summary>
    private async Task<T?> GetWithCompatibilityOptionsAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await Homeserver.ClientHttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(contentStream, SynapseCompatibilityJsonOptions, cancellationToken);
    }

    /// <summary>
    /// Raw DELETE with an optional body. LibMatrix's DeleteAsync/DeleteAsJsonAsync have no CancellationToken overload,
    /// so the request is built here and sent through ClientHttpClient.SendAsync.
    /// </summary>
    private async Task SendDeleteAsync(string url, HttpContent? content, CancellationToken cancellationToken)
    {
        using var reqMessage = new HttpRequestMessage(HttpMethod.Delete, url) { Content = content };
        using var resp = await Homeserver.ClientHttpClient.SendAsync(reqMessage, cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Raw DELETE like <see cref="SendDeleteAsync(string, HttpContent?, CancellationToken)"/> that also reads the JSON
    /// response body. An empty body returns <c>null</c>; a body that isn't JSON throws <see cref="JsonException"/>.
    /// </summary>
    private async Task<TResponse?> SendDeleteAsync<TResponse>(string url, HttpContent? content, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var reqMessage = new HttpRequestMessage(HttpMethod.Delete, url) { Content = content };
        using var resp = await Homeserver.ClientHttpClient.SendAsync(reqMessage, cancellationToken);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<TResponse>(body);
    }

    /// <summary>
    /// GET a status that may not exist: <c>null</c> for a 404 (<c>M_NOT_FOUND</c> or a bare 404 from a proxy) and for
    /// <c>M_UNRECOGNIZED</c> (a homeserver without the endpoint, as 404 or 405). Every other error propagates.
    /// </summary>
    private async Task<T?> GetOptionalStatusAsync<T>(string url, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await Homeserver.ClientHttpClient.GetFromJsonAsync<T>(url, cancellationToken: cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (LibMatrix.MatrixException ex) when (ex.ErrorCode is "M_NOT_FOUND" or "M_UNRECOGNIZED")
        {
            return null;
        }
    }

    /// <summary>POST an empty JSON object (<c>{}</c>) to an endpoint whose response body is not read.</summary>
    private async Task PostEmptyAsync(string url, CancellationToken cancellationToken)
    {
        using var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    // --- User Management ---

    public override async Task<SynapseAdminUserListResult?> GetUserListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        // locked defaults to false, which leaves locked users out of the rows and the total (#547). Without
        // deactivated, v3 includes deactivated users, so the list is every account.
        var url = $"/_synapse/admin/v3/users?from={offset}&limit={limit}&dir={direction.UrlEncode()}&order_by={orderBy.UrlEncode()}&locked=true";
        if (!string.IsNullOrEmpty(searchTerm))
        {
            if (searchTerm.StartsWith("@"))
            {
                url += $"&user_id={searchTerm.UrlEncode()}";
            }
            else
            {
                url += $"&name={searchTerm.UrlEncode()}";
            }
        }
        return await GetWithCompatibilityOptionsAsync<SynapseAdminUserListResult>(url, cancellationToken);
    }

    /// <summary>
    /// Returns <c>null</c> for an unknown local user (404 <c>M_NOT_FOUND</c>, from Synapse and Tuwunel). Every other
    /// error propagates, including <c>M_UNRECOGNIZED</c> and a bare 404, which don't say whether the user exists:
    /// <c>UserService.CreateUserAsync</c> relies on this before it sends a <c>PUT</c> that would modify an account.
    /// </summary>
    public override async Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> GetUserDetailsAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserListResult.SynapseAdminUserListResultUser>($"/_synapse/admin/v2/users/{userId.UrlEncode()}", cancellationToken: cancellationToken);
        }
        catch (LibMatrix.MatrixException ex) when (ex.ErrorCode == "M_NOT_FOUND")
        {
            return null;
        }
    }

    public override async Task DeactivateUserAsync(string userId, bool erase, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/deactivate/{userId.UrlEncode()}";
        using var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, new { erase }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task QuarantineMediaByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/user/{userId.UrlEncode()}/media/quarantine";
        await PostEmptyAsync(url, cancellationToken);
    }

    public override async Task<LoginResponse> LoginAsUserAsync(string userId, TimeSpan expireIn, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/users/{userId.UrlEncode()}/login";
        var request = new SynapseAdminLoginAsUserRequest
        {
            ValidUntilMs = DateTimeOffset.UtcNow.Add(expireIn).ToUnixTimeMilliseconds()
        };
        using var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, request, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        var loginResp = await resp.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"Failed to deserialize shadow login response for user {userId}.");
        loginResp.UserId = userId; // Synapse only returns the access token
        return loginResp;
    }

    public override async Task<SendServerNoticeResponse?> SendServerNoticeAsync(string userId, object content, string? type = null, string? stateKey = null, CancellationToken cancellationToken = default)
    {
        var req = new SendServerNoticeRequest
        {
            UserId = userId,
            Content = content,
            Type = type,
            StateKey = stateKey
        };
        using var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync("/_synapse/admin/v1/send_server_notice", req, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SendServerNoticeResponse>(cancellationToken: cancellationToken);
    }

    internal static string BuildUserMediaStatisticsUrl(int limit, string orderBy, string dir, string? searchTerm)
    {
        var url = $"/_synapse/admin/v1/statistics/users/media?limit={limit}&order_by={orderBy.UrlEncode()}&dir={dir.UrlEncode()}";
        if (!string.IsNullOrEmpty(searchTerm)) url += $"&search_term={searchTerm.UrlEncode()}";
        return url;
    }

    public override async Task<UserMediaStatisticsResponse?> GetUserMediaStatisticsAsync(int limit = 10, string orderBy = "media_length", string dir = "b", string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        var url = BuildUserMediaStatisticsUrl(limit, orderBy, dir, searchTerm);
        return await GetWithCompatibilityOptionsAsync<UserMediaStatisticsResponse>(url, cancellationToken);
    }

    public override async Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> UpdateUserAsync(string userId, object request, CancellationToken cancellationToken = default)
    {
        using var resp = await Homeserver.ClientHttpClient.PutAsJsonAsync($"/_synapse/admin/v2/users/{userId.UrlEncode()}", request, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminUserListResult.SynapseAdminUserListResultUser>(cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminUserMembershipsResponse?> GetUserMembershipsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserMembershipsResponse>($"/_synapse/admin/v1/users/{userId.UrlEncode()}/memberships", cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Synapse documents <c>from</c> as opaque, but it is an offset (<c>next_token</c> = <c>from</c> + items returned),
    /// the same as the users and rooms lists, which also page with computed offsets.
    /// </summary>
    internal static string BuildUserMediaUrl(string userId, int from, int limit, string orderBy, string dir) =>
        $"/_synapse/admin/v1/users/{userId.UrlEncode()}/media?from={from}&limit={limit}&order_by={orderBy.UrlEncode()}&dir={dir.UrlEncode()}";

    public override async Task<SynapseAdminUserMediaResult?> GetUserMediaAsync(string userId, int from, int limit, string orderBy = "created_ts", string dir = "b", CancellationToken cancellationToken = default)
    {
        var url = BuildUserMediaUrl(userId, from, limit, orderBy, dir);
        return await GetWithCompatibilityOptionsAsync<SynapseAdminUserMediaResult>(url, cancellationToken);
    }

    public override async Task<SynapseAdminExperimentalFeaturesResponse?> GetUserExperimentalFeaturesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/experimental_features/{userId.UrlEncode()}";
        return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminExperimentalFeaturesResponse>(url, cancellationToken: cancellationToken);
    }

    public override async Task SetUserExperimentalFeaturesAsync(string userId, SynapseAdminExperimentalFeaturesRequest request, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/experimental_features/{userId.UrlEncode()}";
        using var resp = await Homeserver.ClientHttpClient.PutAsJsonAsync(url, request, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task<SynapseAdminAccountValidityResponse?> RenewAccountValidityAsync(SynapseAdminAccountValidityRequest request, CancellationToken cancellationToken = default)
    {
        const string url = "/_synapse/admin/v1/account_validity/validity";
        using var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, request, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminAccountValidityResponse>(cancellationToken: cancellationToken);
    }

    // --- Room Management ---

    public override async Task<SynapseAdminRoomListResult?> GetRoomListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/rooms?from={offset}&limit={limit}&dir={direction.UrlEncode()}&order_by={orderBy.UrlEncode()}";
        if (!string.IsNullOrEmpty(searchTerm)) url += $"&search_term={searchTerm.UrlEncode()}";
        return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomListResult>(url, cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom?> GetRoomDetailsAsync(string roomId, CancellationToken cancellationToken = default)
    {
        return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom>($"/_synapse/admin/v1/rooms/{roomId.UrlEncode()}", cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminRoomMemberListResult?> GetRoomMembersAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/rooms/{roomId.UrlEncode()}/members";
        return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomMemberListResult>(url, cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminRoomStateResult?> GetRoomStateAsync(string roomId, string? type = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(type)
            ? $"/_synapse/admin/v1/rooms/{roomId.UrlEncode()}/state"
            : $"/_synapse/admin/v1/rooms/{roomId.UrlEncode()}/state?type={type.UrlEncode()}";
        return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomStateResult>(url, cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminRoomMediaListResult?> GetRoomMediaListAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/room/{roomId.UrlEncode()}/media";
        return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomMediaListResult>(url, cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminRoomDeleteResponse?> DeleteRoomAsync(string roomId, SynapseAdminRoomDeleteRequest request, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v2/rooms/{roomId.UrlEncode()}";
        return await SendDeleteAsync<SynapseAdminRoomDeleteResponse>(url,
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"), cancellationToken);
    }

    public override Task<SynapseAdminRoomDeleteStatusResponse?> GetRoomDeleteStatusAsync(string deleteId, CancellationToken cancellationToken = default) =>
        GetOptionalStatusAsync<SynapseAdminRoomDeleteStatusResponse>(
            $"/_synapse/admin/v2/rooms/delete_status/{deleteId.UrlEncode()}", cancellationToken);

    public override Task<SynapseAdminRoomDeleteStatusListResponse?> GetRoomDeleteStatusesAsync(string roomId, CancellationToken cancellationToken = default) =>
        GetOptionalStatusAsync<SynapseAdminRoomDeleteStatusListResponse>(
            $"/_synapse/admin/v2/rooms/{roomId.UrlEncode()}/delete_status", cancellationToken);

    public override async Task QuarantineMediaByRoomIdAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/room/{roomId.UrlEncode()}/media/quarantine";
        await PostEmptyAsync(url, cancellationToken);
    }

    public override async Task BlockRoomAsync(string roomId, bool block, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/rooms/{roomId.UrlEncode()}/block";
        using var resp = await Homeserver.ClientHttpClient.PutAsJsonAsync(url, new { block }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task<RoomStatisticsResponse?> GetLargestRoomsAsync(CancellationToken cancellationToken = default)
    {
        return await Homeserver.ClientHttpClient.GetFromJsonAsync<RoomStatisticsResponse>("/_synapse/admin/v1/statistics/database/rooms", cancellationToken: cancellationToken);
    }

    /// <summary>
    /// A null <paramref name="from"/> is left out on purpose for the first page (#549). The Admin API page calls
    /// <c>from</c> required, but Synapse (v1.165.0, which tests <c>?dir=b</c> without it) and Tuwunel (1.8.1) start
    /// backward history at the newest event when it is missing, as the Client-Server <c>/messages</c> does. Don't send
    /// a made-up token such as <c>END</c> instead: tokens are server-specific.
    /// </summary>
    public override async Task<SynapseAdminRoomMessagesResponse?> GetRoomMessagesAsync(string roomId, int? limit = null, string? from = null, string? dir = null, string? filter = null, string? to = null, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/rooms/{roomId.UrlEncode()}/messages";
        var query = new List<string>();
        if (limit.HasValue) query.Add($"limit={limit}");
        if (!string.IsNullOrEmpty(from)) query.Add($"from={from.UrlEncode()}");
        if (!string.IsNullOrEmpty(dir)) query.Add($"dir={dir.UrlEncode()}");
        if (!string.IsNullOrEmpty(filter)) query.Add($"filter={filter.UrlEncode()}");
        if (!string.IsNullOrEmpty(to)) query.Add($"to={to.UrlEncode()}");
        if (query.Count > 0) url += "?" + string.Join("&", query);

        return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomMessagesResponse>(url, cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminPurgeHistoryResponse?> PurgeRoomHistoryAsync(string roomId, SynapseAdminPurgeHistoryRequest request, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/purge_history/{roomId.UrlEncode()}";
        using var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, request, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminPurgeHistoryResponse>(cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminPurgeHistoryStatusResponse?> GetPurgeHistoryStatusAsync(string purgeId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/purge_history_status/{purgeId.UrlEncode()}";
        return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminPurgeHistoryStatusResponse>(url, cancellationToken: cancellationToken);
    }

    // --- Federation ---

    public override async Task<SynapseAdminDestinationListResult?> GetFederationDestinationListAsync(int offset, int limit, string direction, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/federation/destinations?from={offset}&limit={limit}&dir={direction.UrlEncode()}";
        return await GetWithCompatibilityOptionsAsync<SynapseAdminDestinationListResult>(url, cancellationToken);
    }

    public override async Task ResetFederationConnectionTimeoutAsync(string destination, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/federation/destinations/{destination.UrlEncode()}/reset_connection";
        await PostEmptyAsync(url, cancellationToken);
    }

    // --- Event Reports ---

    public override async Task<SynapseAdminEventReportListResult?> GetEventReportListAsync(int offset, int limit, string direction, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/event_reports?from={offset}&limit={limit}&dir={direction.UrlEncode()}";
        if (!string.IsNullOrEmpty(searchTerm))
        {
            if (searchTerm.StartsWith("!"))
            {
                url += $"&room_id={searchTerm.UrlEncode()}";
            }
            else
            {
                url += $"&user_id={searchTerm.UrlEncode()}";
            }
        }
        return await GetWithCompatibilityOptionsAsync<SynapseAdminEventReportListResult>(url, cancellationToken);
    }

    public override async Task DeleteEventReportAsync(string reportId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/event_reports/{reportId.UrlEncode()}";
        await SendDeleteAsync(url, null, cancellationToken);
    }

    // --- Event Forensics ---

    public override async Task<SynapseAdminFetchEventResponse?> FetchEventAsync(string eventId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/fetch_event/{eventId.UrlEncode()}";
        try
        {
            return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminFetchEventResponse>(url, cancellationToken: cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (LibMatrix.MatrixException ex) when (ex.ErrorCode == "M_NOT_FOUND")
        {
            return null;
        }
    }

    // --- Registration Tokens ---

    public override async Task<List<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>> GetRegistrationTokensAsync(CancellationToken cancellationToken = default)
    {
        var url = "/_synapse/admin/v1/registration_tokens";
        var resp = await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminRegistrationTokenListResult>(url, cancellationToken: cancellationToken);
        return resp?.RegistrationTokens ?? [];
    }

    public override async Task<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken?> CreateRegistrationTokenAsync(SynapseAdminRegistrationTokenCreateRequest request, CancellationToken cancellationToken = default)
    {
        var url = "/_synapse/admin/v1/registration_tokens/new";
        using var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, request, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>(cancellationToken: cancellationToken);
    }

    public override async Task UpdateRegistrationTokenAsync(string token, SynapseAdminRegistrationTokenUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/registration_tokens/{token.UrlEncode()}";
        using var resp = await Homeserver.ClientHttpClient.PutAsJsonAsync(url, request, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task DeleteRegistrationTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/registration_tokens/{token.UrlEncode()}";
        await SendDeleteAsync(url, null, cancellationToken);
    }

    // --- Server Admin ---

    public override async Task<SynapseVersionResponse?> GetSynapseVersionAsync(CancellationToken cancellationToken = default)
    {
        return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseVersionResponse>(GatewayConstants.ServerVersionPath, cancellationToken: cancellationToken);
    }

    // --- Media ---

    public override async Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/{serverName.UrlEncode()}/{mediaId.UrlEncode()}";
        var result = await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminMediaMetadataResponse>(url, cancellationToken: cancellationToken);
        return result?.Info;
    }

    public override async Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string mxcUri, CancellationToken cancellationToken = default)
    {
        var mxc = MxcUri.Parse(mxcUri);
        return await GetMediaMetadataAsync(mxc.ServerName, mxc.MediaId, cancellationToken);
    }

    public override async Task QuarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/quarantine/{serverName.UrlEncode()}/{mediaId.UrlEncode()}";
        await PostEmptyAsync(url, cancellationToken);
    }

    public override async Task UnquarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/unquarantine/{serverName.UrlEncode()}/{mediaId.UrlEncode()}";
        await PostEmptyAsync(url, cancellationToken);
    }

    public override async Task DeleteMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/{serverName.UrlEncode()}/{mediaId.UrlEncode()}";
        await SendDeleteAsync(url, null, cancellationToken);
    }

    public override async Task ProtectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/protect/{mediaId.UrlEncode()}";
        await PostEmptyAsync(url, cancellationToken);
    }

    public override async Task UnprotectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/unprotect/{mediaId.UrlEncode()}";
        await PostEmptyAsync(url, cancellationToken);
    }

    public override async Task<SynapseAdminPurgeMediaCacheResponse?> PurgeRemoteMediaCacheAsync(long beforeTs, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/purge_media_cache?before_ts={beforeTs}";
        using var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminPurgeMediaCacheResponse>(cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminDeleteMediaResponse?> DeleteLocalMediaAsync(long beforeTs, long sizeGt, bool keepProfiles, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/delete?before_ts={beforeTs}&size_gt={sizeGt}&keep_profiles={keepProfiles.ToString().ToLower()}";
        using var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminDeleteMediaResponse>(cancellationToken: cancellationToken);
    }

    // --- Background Updates ---

    public override async Task<SynapseAdminBackgroundUpdatesStatusResponse?> GetBackgroundUpdatesStatusAsync(CancellationToken cancellationToken = default)
    {
        return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminBackgroundUpdatesStatusResponse>("/_synapse/admin/v1/background_updates/status", cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminBackgroundUpdatesEnabledResponse?> SetBackgroundUpdatesEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        using var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync("/_synapse/admin/v1/background_updates/enabled", new { enabled }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminBackgroundUpdatesEnabledResponse>(cancellationToken: cancellationToken);
    }

    public override async Task StartBackgroundUpdatesJobAsync(string jobName, CancellationToken cancellationToken = default)
    {
        using var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync("/_synapse/admin/v1/background_updates/start_job", new { job_name = jobName }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }
}
