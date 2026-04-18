using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Requests;
using LibMatrix.EventTypes.Spec;
using LibMatrix.Responses;
using LibMatrix.StructuredData;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.Requests;
using System.Net.Http;
using System.Net.Http.Json;
using ArcaneLibs.Extensions;

namespace SynapseAdmin.Infrastructure.Gateways;

/// <summary>
/// Synapse-specific implementation of the Matrix gateway.
/// Encapsulates communication by following the service's lead: 
/// uses .Admin for SDK features and ClientHttpClient for project extensions.
/// </summary>
public class SynapseAdminGateway(AuthenticatedHomeserverSynapse synapse) : MatrixGatewayBase(synapse)
{
    private readonly AuthenticatedHomeserverSynapse _synapse = synapse;

    // --- User Management ---

    public override async Task<SynapseAdminUserListResult?> GetUserListAsync(int offset, int limit, string orderBy, string direction, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v3/users?from={offset}&limit={limit}&dir={direction.UrlEncode()}&order_by={orderBy.UrlEncode()}";
        return await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserListResult>(url, cancellationToken: cancellationToken);
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
        // SDK doesn't support CancellationToken here
        return await _synapse.Admin.LoginUserAsync(userId, expireIn);
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
        return await _synapse.ClientHttpClient.GetFromJsonAsync<UserMediaStatisticsResponse>(url, cancellationToken: cancellationToken);
    }

    public override async Task<HttpResponseMessage> UpdateUserAsync(string userId, object request, CancellationToken cancellationToken = default)
    {
        return await _synapse.ClientHttpClient.PutAsJsonAsync($"/_synapse/admin/v2/users/{userId.UrlEncode()}", request, cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminUserMembershipsResponse?> GetUserMembershipsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserMembershipsResponse>($"/_synapse/admin/v1/users/{userId.UrlEncode()}/memberships", cancellationToken: cancellationToken);
    }

    public override async Task<SynapseAdminUserMediaResult?> GetUserMediaAsync(string userId, CancellationToken cancellationToken = default)
    {
        // SDK doesn't support CancellationToken here
        return await _synapse.Admin.GetUserMediaAsync(userId);
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
        // SDK doesn't support CancellationToken here
        return await _synapse.Admin.GetRoomMembersAsync(roomId);
    }

    public override async Task<SynapseAdminRoomStateResult?> GetRoomStateAsync(string roomId, string? type = null, CancellationToken cancellationToken = default)
    {
        // SDK doesn't support CancellationToken here
        return await _synapse.Admin.GetRoomStateAsync(roomId, type);
    }

    public override async Task<SynapseAdminRoomMediaListResult?> GetRoomMediaListAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/room/{roomId.UrlEncode()}/media";
        return await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomMediaListResult>(url, cancellationToken: cancellationToken);
    }

    public override async Task DeleteRoomAsync(string roomId, SynapseAdminRoomDeleteRequest request, CancellationToken cancellationToken = default)
    {
        // SDK doesn't support CancellationToken here
        await _synapse.Admin.DeleteRoom(roomId, request);
    }

    public override async Task QuarantineMediaByRoomIdAsync(string roomId, CancellationToken cancellationToken = default)
    {
        // SDK doesn't support CancellationToken here
        await _synapse.Admin.QuarantineMediaByRoomId(roomId);
    }

    public override async Task BlockRoomAsync(string roomId, bool block, CancellationToken cancellationToken = default)
    {
        // SDK doesn't support CancellationToken here
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

    // --- Federation ---

    public override async Task<SynapseAdminDestinationListResult?> GetFederationDestinationListAsync(int offset, int limit, string direction, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/federation/destinations?from={offset}&limit={limit}&dir={direction.UrlEncode()}";
        return await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseAdminDestinationListResult>(url, cancellationToken: cancellationToken);
    }

    public override async Task ResetFederationConnectionTimeoutAsync(string destination, CancellationToken cancellationToken = default)
    {
        // SDK doesn't support CancellationToken here
        await _synapse.Admin.ResetFederationConnectionTimeoutAsync(destination);
    }

    // --- Event Reports ---

    public override async Task<SynapseAdminEventReportListResult?> GetEventReportListAsync(int offset, int limit, string direction, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/event_reports?from={offset}&limit={limit}&dir={direction.UrlEncode()}";
        return await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseAdminEventReportListResult>(url, cancellationToken: cancellationToken);
    }

    public override async Task DeleteEventReportAsync(string reportId, CancellationToken cancellationToken = default)
    {
        // SDK doesn't support CancellationToken here
        await _synapse.Admin.DeleteEventReportAsync(reportId);
    }

    // --- Registration Tokens ---

    public override async Task<List<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>> GetRegistrationTokensAsync(CancellationToken cancellationToken = default)
    {
        // SDK doesn't support CancellationToken here
        return await _synapse.Admin.GetRegistrationTokensAsync();
    }

    public override async Task<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken?> CreateRegistrationTokenAsync(SynapseAdminRegistrationTokenCreateRequest request, CancellationToken cancellationToken = default)
    {
        var url = "/_synapse/admin/v1/registration_tokens/new";
        var resp = await _synapse.ClientHttpClient.PostAsJsonAsync(url, request, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>(cancellationToken: cancellationToken);
    }

    public override async Task UpdateRegistrationTokenAsync(string token, SynapseAdminRegistrationTokenUpdateRequest request, CancellationToken cancellationToken = default)
    {
        // SDK doesn't support CancellationToken here
        await _synapse.Admin.UpdateRegistrationTokenAsync(token, request);
    }

    public override async Task DeleteRegistrationTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        // SDK doesn't support CancellationToken here
        await _synapse.Admin.DeleteRegistrationTokenAsync(token);
    }

    // --- Server Admin ---

    public override async Task<SynapseVersionResponse?> GetSynapseVersionAsync(CancellationToken cancellationToken = default)
    {
        return await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseVersionResponse>("/_synapse/admin/v1/server_version", cancellationToken: cancellationToken);
    }

    // --- Media ---

    public override async Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        var url = $"/_synapse/admin/v1/media/{serverName.UrlEncode()}/{mediaId.UrlEncode()}";
        var result = await _synapse.ClientHttpClient.GetFromJsonAsync<SynapseAdminMediaMetadataResponse>(url, cancellationToken: cancellationToken);
        return result?.Info;
    }

    public override async Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(MxcUri mxc, CancellationToken cancellationToken = default)
    {
        return await GetMediaMetadataAsync(mxc.ServerName, mxc.MediaId, cancellationToken);
    }
}
