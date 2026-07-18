using LibMatrix.Homeservers;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.Requests;
using System.Text.Json;
using ArcaneLibs.Extensions;
using SynapseAdmin.Infrastructure.Serialization;
using System.Net.Http.Json;

namespace SynapseAdmin.Infrastructure.Gateways;

/// <summary>
/// A Synapse-compatible implementation of the Matrix gateway.
/// Does not depend on LibMatrix's SDK-level .Admin properties, allowing support for homeservers (such as Tuwunel >= 1.8.1)
/// that implement the Synapse Admin API but return AuthenticatedHomeserverGeneric instead of AuthenticatedHomeserverSynapse.
/// </summary>
public class SynapseCompatibleAdminGateway(
    AuthenticatedHomeserverGeneric homeserver,
    string serverBrand = "Synapse",
    string serverVersion = "Unknown") : MatrixGatewayBase(homeserver)
{
    public override bool SupportsAdminApi => true;
    public override string ServerBrand => serverBrand;
    public override string ServerVersion => serverVersion;

    /// <summary>
    /// Creates fresh options to handle Synapse's inconsistent next_token types (String vs Number).
    /// </summary>
    private static JsonSerializerOptions GetSynapseCompatibilityJsonOptions() => new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringConverter() }
    };

    // --- User Management ---

    public override async Task<SynapseAdminUserListResult?> GetUserListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v3/users?from={offset}&limit={limit}&dir={direction.UrlEncode()}&order_by={orderBy.UrlEncode()}";
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
        var response = await Homeserver.ClientHttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SynapseAdminUserListResult>(contentStream, GetSynapseCompatibilityJsonOptions(), cancellationToken);
    }

    public override async Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> GetUserDetailsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserListResult.SynapseAdminUserListResultUser>($"/_synapse/admin/v2/users/{userId.UrlEncode()}", cancellationToken: cancellationToken);
    }

    public override async Task DeactivateUserAsync(string userId, bool erase, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/deactivate/{userId.UrlEncode()}";
        var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, new { erase }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task QuarantineMediaByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/user/{userId.UrlEncode()}/media/quarantine";
        var resp = await Homeserver.ClientHttpClient.PutAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task<LoginResponse> LoginAsUserAsync(string userId, TimeSpan expireIn, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/users/{userId.UrlEncode()}/login?valid_until_ms={DateTimeOffset.UtcNow.Add(expireIn).ToUnixTimeMilliseconds()}";
        var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        var loginResp = await resp.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);
        if (loginResp != null)
        {
            loginResp.UserId = userId; // Synapse only returns the access token
        }
        return loginResp!;
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
        var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync("/_synapse/admin/v1/send_server_notice", req, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SendServerNoticeResponse>(cancellationToken: cancellationToken);
    }

    public override async Task<UserMediaStatisticsResponse?> GetUserMediaStatisticsAsync(int limit = 10, string orderBy = "media_length", string dir = "b", CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/statistics/users/media?limit={limit}&order_by={orderBy.UrlEncode()}&dir={dir.UrlEncode()}";
        var response = await Homeserver.ClientHttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<UserMediaStatisticsResponse>(contentStream, GetSynapseCompatibilityJsonOptions(), cancellationToken);
    }

    public override async Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> UpdateUserAsync(string userId, object request, CancellationToken cancellationToken = default)
    {
        var resp = await Homeserver.ClientHttpClient.PutAsJsonAsync($"/_synapse/admin/v2/users/{userId.UrlEncode()}", request, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminUserListResult.SynapseAdminUserListResultUser>(cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminUserMembershipsResponse?> GetUserMembershipsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserMembershipsResponse>($"/_synapse/admin/v1/users/{userId.UrlEncode()}/memberships", cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminUserMediaResult?> GetUserMediaAsync(string userId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/users/{userId.UrlEncode()}/media";
        var response = await Homeserver.ClientHttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SynapseAdminUserMediaResult>(contentStream, GetSynapseCompatibilityJsonOptions(), cancellationToken);
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

    public override async Task DeleteRoomAsync(string roomId, SynapseAdminRoomDeleteRequest request, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v2/rooms/{roomId.UrlEncode()}";
        var reqMessage = new HttpRequestMessage(HttpMethod.Delete, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json")
        };
        var resp = await Homeserver.ClientHttpClient.SendAsync(reqMessage, cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task QuarantineMediaByRoomIdAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/room/{roomId.UrlEncode()}/media/quarantine";
        var resp = await Homeserver.ClientHttpClient.PutAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task BlockRoomAsync(string roomId, bool block, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/rooms/{roomId.UrlEncode()}/block";
        var resp = await Homeserver.ClientHttpClient.PutAsJsonAsync(url, new { block }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task<RoomStatisticsResponse?> GetLargestRoomsAsync(CancellationToken cancellationToken = default)
    {
        return await Homeserver.ClientHttpClient.GetFromJsonAsync<RoomStatisticsResponse>("/_synapse/admin/v1/statistics/database/rooms", cancellationToken: cancellationToken);
    }

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
        var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, request, cancellationToken: cancellationToken);
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
        var response = await Homeserver.ClientHttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SynapseAdminDestinationListResult>(contentStream, GetSynapseCompatibilityJsonOptions(), cancellationToken);
    }

    public override async Task ResetFederationConnectionTimeoutAsync(string destination, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/federation/destinations/{destination.UrlEncode()}/reset_connection";
        var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
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
        var response = await Homeserver.ClientHttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SynapseAdminEventReportListResult>(contentStream, GetSynapseCompatibilityJsonOptions(), cancellationToken);
    }

    public override async Task DeleteEventReportAsync(string reportId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/event_reports/{reportId.UrlEncode()}";
        var reqMessage = new HttpRequestMessage(HttpMethod.Delete, url);
        var resp = await Homeserver.ClientHttpClient.SendAsync(reqMessage, cancellationToken);
        resp.EnsureSuccessStatusCode();
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
        var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, request, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>(cancellationToken: cancellationToken);
    }

    public override async Task UpdateRegistrationTokenAsync(string token, SynapseAdminRegistrationTokenUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/registration_tokens/{token.UrlEncode()}";
        var resp = await Homeserver.ClientHttpClient.PutAsJsonAsync(url, request, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task DeleteRegistrationTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/registration_tokens/{token.UrlEncode()}";
        var reqMessage = new HttpRequestMessage(HttpMethod.Delete, url);
        var resp = await Homeserver.ClientHttpClient.SendAsync(reqMessage, cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    // --- Server Admin ---

    public override async Task<SynapseVersionResponse?> GetSynapseVersionAsync(CancellationToken cancellationToken = default)
    {
        return await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseVersionResponse>("/_synapse/admin/v1/server_version", cancellationToken: cancellationToken);
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
        var mxc = LibMatrix.StructuredData.MxcUri.Parse(mxcUri);
        return await GetMediaMetadataAsync(mxc.ServerName, mxc.MediaId, cancellationToken);
    }

    public override async Task QuarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/quarantine/{serverName.UrlEncode()}/{mediaId.UrlEncode()}";
        var resp = await Homeserver.ClientHttpClient.PutAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task UnquarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/unquarantine/{serverName.UrlEncode()}/{mediaId.UrlEncode()}";
        var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task DeleteMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/{serverName.UrlEncode()}/{mediaId.UrlEncode()}";
        var reqMessage = new HttpRequestMessage(HttpMethod.Delete, url);
        var resp = await Homeserver.ClientHttpClient.SendAsync(reqMessage, cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task ProtectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/protect/{mediaId.UrlEncode()}";
        var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task UnprotectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/unprotect/{mediaId.UrlEncode()}";
        var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task<SynapseAdminPurgeMediaCacheResponse?> PurgeRemoteMediaCacheAsync(long beforeTs, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/purge_media_cache?before_ts={beforeTs}";
        var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminPurgeMediaCacheResponse>(cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminDeleteMediaResponse?> DeleteLocalMediaAsync(long beforeTs, long sizeGt, bool keepProfiles, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/delete?before_ts={beforeTs}&size_gt={sizeGt}&keep_profiles={keepProfiles.ToString().ToLower()}";
        var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
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
        var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync("/_synapse/admin/v1/background_updates/enabled", new { enabled }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminBackgroundUpdatesEnabledResponse>(cancellationToken: cancellationToken);
    }

    public override async Task StartBackgroundUpdatesJobAsync(string jobName, CancellationToken cancellationToken = default)
    {
        var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync("/_synapse/admin/v1/background_updates/start_job", new { job_name = jobName }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }
}
