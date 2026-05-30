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
/// Synapse-specific implementation of the Matrix gateway.
/// Encapsulates communication by following the service's lead: 
/// uses .Admin for SDK features and ClientHttpClient for project extensions.
/// </summary>
public class SynapseAdminGateway(AuthenticatedHomeserverSynapse synapse) : MatrixGatewayBase(synapse)
{
    private readonly AuthenticatedHomeserverSynapse _synapse = synapse;

    public override bool SupportsAdminApi => true;

    /// <summary>
    /// Creates fresh options to handle Synapse's inconsistent next_token types (String vs Number).
    /// We create a fresh instance every time because LibMatrix's MatrixHttpClient mutates the options,
    /// which causes an InvalidOperationException if the instance is reused and marked as read-only.
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
        var response = await _synapse.ClientHttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SynapseAdminUserListResult>(contentStream, GetSynapseCompatibilityJsonOptions(), cancellationToken);
    }

    public override async Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> GetUserDetailsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserListResult.SynapseAdminUserListResultUser>($"/_synapse/admin/v2/users/{userId.UrlEncode()}", cancellationToken: cancellationToken);
    }

    public override async Task DeactivateUserAsync(string userId, bool erase, CancellationToken cancellationToken = default)
    {
        // SDK doesn't support CancellationToken here
        await _synapse.Admin.DeactivateUserAsync(userId, erase);
    }

    public override async Task QuarantineMediaByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        // SDK doesn't support CancellationToken here
        await _synapse.Admin.QuarantineMediaByUserId(userId);
    }

    public override async Task<LoginResponse> LoginAsUserAsync(string userId, TimeSpan expireIn, CancellationToken cancellationToken = default)
    {
        // SDK returns LibMatrix.Responses.LoginResponse, we need to map to SynapseAdmin.Models.Responses.LoginResponse
        var sdkResp = await _synapse.Admin.LoginUserAsync(userId, expireIn);
        return new LoginResponse
        {
            AccessToken = sdkResp.AccessToken,
            DeviceId = sdkResp.DeviceId,
            Homeserver = sdkResp.Homeserver,
            UserId = sdkResp.UserId
        };
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
        var resp = await _synapse.ClientHttpClient.PostAsJsonAsync("/_synapse/admin/v1/send_server_notice", req, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SendServerNoticeResponse>(cancellationToken: cancellationToken);
    }

    public override async Task<UserMediaStatisticsResponse?> GetUserMediaStatisticsAsync(int limit = 10, string orderBy = "media_length", string dir = "b", CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/statistics/users/media?limit={limit}&order_by={orderBy.UrlEncode()}&dir={dir.UrlEncode()}";
        var response = await _synapse.ClientHttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<UserMediaStatisticsResponse>(contentStream, GetSynapseCompatibilityJsonOptions(), cancellationToken);
    }

    public override async Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> UpdateUserAsync(string userId, object request, CancellationToken cancellationToken = default)
    {
        var resp = await _synapse.ClientHttpClient.PutAsJsonAsync($"/_synapse/admin/v2/users/{userId.UrlEncode()}", request, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminUserListResult.SynapseAdminUserListResultUser>(cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminUserMembershipsResponse?> GetUserMembershipsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserMembershipsResponse>($"/_synapse/admin/v1/users/{userId.UrlEncode()}/memberships", cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminUserMediaResult?> GetUserMediaAsync(string userId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/users/{userId.UrlEncode()}/media";
        // We bypass GetFromJsonAsync to avoid LibMatrix's problematic option mutation
        var response = await _synapse.ClientHttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SynapseAdminUserMediaResult>(contentStream, GetSynapseCompatibilityJsonOptions(), cancellationToken);
    }


    // --- Room Management ---

    public override async Task<SynapseAdminRoomListResult?> GetRoomListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/rooms?from={offset}&limit={limit}&dir={direction.UrlEncode()}&order_by={orderBy.UrlEncode()}";
        if (!string.IsNullOrEmpty(searchTerm)) url += $"&search_term={searchTerm.UrlEncode()}";
        return await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomListResult>(url, cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom?> GetRoomDetailsAsync(string roomId, CancellationToken cancellationToken = default)
    {
        return await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom>($"/_synapse/admin/v1/rooms/{roomId.UrlEncode()}", cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminRoomMemberListResult?> GetRoomMembersAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var sdkResp = await _synapse.Admin.GetRoomMembersAsync(roomId);
        return new SynapseAdminRoomMemberListResult
        {
            Members = sdkResp.Members,
            Total = sdkResp.Total
        };
    }

    public override async Task<SynapseAdminRoomStateResult?> GetRoomStateAsync(string roomId, string? type = null, CancellationToken cancellationToken = default)
    {
        var sdkResp = await _synapse.Admin.GetRoomStateAsync(roomId, type);
        return new SynapseAdminRoomStateResult
        {
            Events = sdkResp.Events.Select(e => new MatrixEventResponse
            {
                Type = e.Type,
                StateKey = e.StateKey,
                RawContent = e.RawContent,
                OriginServerTs = e.OriginServerTs,
                RoomId = e.RoomId,
                Sender = e.Sender,
                Unsigned = e.Unsigned,
                EventId = e.EventId
            }).ToList()
        };
    }

    public override async Task<SynapseAdminRoomMediaListResult?> GetRoomMediaListAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/room/{roomId.UrlEncode()}/media";
        return await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomMediaListResult>(url, cancellationToken: cancellationToken);
    }

    public override async Task DeleteRoomAsync(string roomId, SynapseAdminRoomDeleteRequest request, CancellationToken cancellationToken = default)
    {
        var sdkReq = new LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Requests.SynapseAdminRoomDeleteRequest
        {
            Block = request.Block,
            Purge = request.Purge,
            Message = request.Message,
            NewRoomUserId = request.NewRoomUserId,
            RoomName = request.RoomName
        };
        await _synapse.Admin.DeleteRoom(roomId, sdkReq);
    }

    public override async Task QuarantineMediaByRoomIdAsync(string roomId, CancellationToken cancellationToken = default)
    {
        await _synapse.Admin.QuarantineMediaByRoomId(roomId);
    }

    public override async Task BlockRoomAsync(string roomId, bool block, CancellationToken cancellationToken = default)
    {
        await _synapse.Admin.BlockRoom(roomId, block);
    }

    public override async Task<RoomStatisticsResponse?> GetLargestRoomsAsync(CancellationToken cancellationToken = default)
    {
        return await _synapse.ClientHttpClient.GetFromJsonAsync<RoomStatisticsResponse>("/_synapse/admin/v1/statistics/database/rooms", cancellationToken: cancellationToken);
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
        
        return await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomMessagesResponse>(url, cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminPurgeHistoryResponse?> PurgeRoomHistoryAsync(string roomId, SynapseAdminPurgeHistoryRequest request, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/purge_history/{roomId.UrlEncode()}";
        var resp = await _synapse.ClientHttpClient.PostAsJsonAsync(url, request, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminPurgeHistoryResponse>(cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminPurgeHistoryStatusResponse?> GetPurgeHistoryStatusAsync(string purgeId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/purge_history_status/{purgeId.UrlEncode()}";
        return await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseAdminPurgeHistoryStatusResponse>(url, cancellationToken: cancellationToken);
    }

    // --- Federation ---

    public override async Task<SynapseAdminDestinationListResult?> GetFederationDestinationListAsync(int offset, int limit, string direction, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/federation/destinations?from={offset}&limit={limit}&dir={direction.UrlEncode()}";
        var response = await _synapse.ClientHttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SynapseAdminDestinationListResult>(contentStream, GetSynapseCompatibilityJsonOptions(), cancellationToken);
    }

    public override async Task ResetFederationConnectionTimeoutAsync(string destination, CancellationToken cancellationToken = default)
    {
        await _synapse.Admin.ResetFederationConnectionTimeoutAsync(destination);
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
        var response = await _synapse.ClientHttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SynapseAdminEventReportListResult>(contentStream, GetSynapseCompatibilityJsonOptions(), cancellationToken);
    }

    public override async Task DeleteEventReportAsync(string reportId, CancellationToken cancellationToken = default)
    {
        await _synapse.Admin.DeleteEventReportAsync(reportId);
    }

    // --- Registration Tokens ---

    public override async Task<List<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>> GetRegistrationTokensAsync(CancellationToken cancellationToken = default)
    {
        var sdkTokens = await _synapse.Admin.GetRegistrationTokensAsync();
        return sdkTokens.Select(t => new SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken
        {
            Token = t.Token,
            UsesAllowed = t.UsesAllowed,
            Pending = t.Pending,
            Completed = t.Completed,
            ExpiryTime = t.ExpiryTime
        }).ToList();
    }

    public override async Task<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken?> CreateRegistrationTokenAsync(SynapseAdminRegistrationTokenCreateRequest request, CancellationToken cancellationToken = default)
    {
        var sdkReq = new LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses.SynapseAdminRegistrationTokenCreateRequest
        {
            Token = request.Token,
            UsesAllowed = request.UsesAllowed,
            ExpiryTime = request.ExpiryTime,
            Length = request.Length
        };
        var url = "/_synapse/admin/v1/registration_tokens/new";
        var resp = await _synapse.ClientHttpClient.PostAsJsonAsync(url, sdkReq, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>(cancellationToken: cancellationToken);
    }

    public override async Task UpdateRegistrationTokenAsync(string token, SynapseAdminRegistrationTokenUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var sdkReq = new LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses.SynapseAdminRegistrationTokenUpdateRequest
        {
            UsesAllowed = request.UsesAllowed,
            ExpiryTime = request.ExpiryTime
        };
        await _synapse.Admin.UpdateRegistrationTokenAsync(token, sdkReq);
    }

    public override async Task DeleteRegistrationTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        await _synapse.Admin.DeleteRegistrationTokenAsync(token);
    }

    // --- Server Admin ---

    public override async Task<SynapseVersionResponse?> GetSynapseVersionAsync(CancellationToken cancellationToken = default)
    {
        return await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseVersionResponse>("/_synapse/admin/v1/server_version", cancellationToken: cancellationToken);
    }

    // Media

    public override async Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/{serverName.UrlEncode()}/{mediaId.UrlEncode()}";
        var result = await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseAdminMediaMetadataResponse>(url, cancellationToken: cancellationToken);
        return result?.Info;
    }

    public override async Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string mxcUri, CancellationToken cancellationToken = default)
    {
        var mxc = LibMatrix.StructuredData.MxcUri.Parse(mxcUri);
        return await GetMediaMetadataAsync(mxc.ServerName, mxc.MediaId, cancellationToken);
    }

    public override async Task QuarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        await _synapse.Admin.QuarantineMediaById(serverName, mediaId);
    }

    public override async Task UnquarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/unquarantine/{serverName.UrlEncode()}/{mediaId.UrlEncode()}";
        var resp = await _synapse.ClientHttpClient.PostAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task DeleteMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        await _synapse.Admin.DeleteMediaById(serverName, mediaId);
    }

    public override async Task ProtectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/protect/{mediaId.UrlEncode()}";
        var resp = await _synapse.ClientHttpClient.PostAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task UnprotectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/unprotect/{mediaId.UrlEncode()}";
        var resp = await _synapse.ClientHttpClient.PostAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public override async Task<SynapseAdminPurgeMediaCacheResponse?> PurgeRemoteMediaCacheAsync(long beforeTs, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/purge_media_cache?before_ts={beforeTs}";
        var resp = await _synapse.ClientHttpClient.PostAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminPurgeMediaCacheResponse>(cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminDeleteMediaResponse?> DeleteLocalMediaAsync(long beforeTs, long sizeGt, bool keepProfiles, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/delete?before_ts={beforeTs}&size_gt={sizeGt}&keep_profiles={keepProfiles.ToString().ToLower()}";
        var resp = await _synapse.ClientHttpClient.PostAsJsonAsync(url, new { }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminDeleteMediaResponse>(cancellationToken: cancellationToken);
    }

    // --- Background Updates ---

    public override async Task<SynapseAdminBackgroundUpdatesStatusResponse?> GetBackgroundUpdatesStatusAsync(CancellationToken cancellationToken = default)
    {
        return await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseAdminBackgroundUpdatesStatusResponse>("/_synapse/admin/v1/background_updates/status", cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminBackgroundUpdatesEnabledResponse?> SetBackgroundUpdatesEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var resp = await _synapse.ClientHttpClient.PostAsJsonAsync("/_synapse/admin/v1/background_updates/enabled", new { enabled }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminBackgroundUpdatesEnabledResponse>(cancellationToken: cancellationToken);
    }

    public override async Task StartBackgroundUpdatesJobAsync(string jobName, CancellationToken cancellationToken = default)
    {
        var resp = await _synapse.ClientHttpClient.PostAsJsonAsync("/_synapse/admin/v1/background_updates/start_job", new { job_name = jobName }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }
}
