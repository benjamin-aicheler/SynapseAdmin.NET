using System.Net.Http.Json;
using ArcaneLibs.Extensions;
using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using LibMatrix.StructuredData;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Extensions;

public static class AuthenticatedHomeserverSynapseExtensions
{
    public static async Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(this AuthenticatedHomeserverSynapse homeserver, string serverName, string mediaId)
    {
        var result = await homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminMediaMetadataResponse>(
            $"/_synapse/admin/v1/media/{serverName.UrlEncode()}/{mediaId.UrlEncode()}");
        return result?.Info;
    }

    public static async Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(this AuthenticatedHomeserverSynapse homeserver, MxcUri mxcUri)
    {
        return await homeserver.GetMediaMetadataAsync(mxcUri.ServerName, mxcUri.MediaId);
    }

    public static async Task<SynapseAdminRoomMediaListResult?> GetRoomMediaListAsync(this AuthenticatedHomeserverSynapse homeserver, string roomId, int? limit = null, string? from = null)
    {
        var url = $"/_synapse/admin/v1/room/{roomId.UrlEncode()}/media";
        // Parameters are included for future compatibility as requested
        if (limit.HasValue || !string.IsNullOrEmpty(from))
        {
            var query = new List<string>();
            if (limit.HasValue) query.Add($"limit={limit}");
            if (!string.IsNullOrEmpty(from)) query.Add($"from={from}");
            url += "?" + string.Join("&", query);
        }
        return await homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomMediaListResult>(url);
    }

    public static async Task<SendServerNoticeResponse?> SendServerNoticeAsync(
        this AuthenticatedHomeserverSynapse homeserver,
        string userId,
        object content,
        string? type = null,
        string? stateKey = null)
    {
        var req = new SendServerNoticeRequest
        {
            UserId = userId,
            Content = content,
            Type = type,
            StateKey = stateKey
        };

        var resp = await homeserver.ClientHttpClient.PostAsJsonAsync("/_synapse/admin/v1/send_server_notice", req);
        resp.EnsureSuccessStatusCode();
        
        return await resp.Content.ReadFromJsonAsync<SendServerNoticeResponse>();
    }

    public static async Task<RoomStatisticsResponse?> GetLargestRoomsAsync(this AuthenticatedHomeserverSynapse homeserver)
    {
        return await homeserver.ClientHttpClient.GetFromJsonAsync<RoomStatisticsResponse>("/_synapse/admin/v1/statistics/database/rooms");
    }

    public static async Task<UserMediaStatisticsResponse?> GetUserMediaStatisticsAsync(this AuthenticatedHomeserverSynapse homeserver, int limit = 10, string orderBy = "media_length", string dir = "b")
    {
        var url = $"/_synapse/admin/v1/statistics/users/media?limit={limit}&order_by={orderBy}&dir={dir}";
        return await homeserver.ClientHttpClient.GetFromJsonAsync<UserMediaStatisticsResponse>(url);
    }

    public static async Task<SynapseVersionResponse?> GetSynapseVersionAsync(this AuthenticatedHomeserverSynapse homeserver)
    {
        return await homeserver.ClientHttpClient.GetFromJsonAsync<SynapseVersionResponse>("/_synapse/admin/v1/server_version");
    }

    public static async Task<SynapseAdminUserListResult?> GetUserListAsync(this AuthenticatedHomeserverSynapse homeserver, int offset, int limit, string orderBy, string dir, CancellationToken token = default)
    {
        var url = $"/_synapse/admin/v3/users?from={offset}&limit={limit}&dir={dir.UrlEncode()}&order_by={orderBy.UrlEncode()}";
        return await homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserListResult>(url, cancellationToken: token);
    }

    public static async Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> GetUserDetailsAsync(this AuthenticatedHomeserverSynapse homeserver, string userId)
    {
        return await homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserListResult.SynapseAdminUserListResultUser>($"/_synapse/admin/v2/users/{userId.UrlEncode()}");
    }

    public static async Task<SynapseAdminUserMembershipsResponse?> GetUserMembershipsAsync(this AuthenticatedHomeserverSynapse homeserver, string userId)
    {
        return await homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserMembershipsResponse>($"/_synapse/admin/v1/users/{userId.UrlEncode()}/memberships");
    }

    public static async Task<SynapseAdminRoomListResult?> GetRoomListAsync(this AuthenticatedHomeserverSynapse homeserver, int offset, int limit, string orderBy, string dir, string? searchTerm = null, CancellationToken token = default)
    {
        var url = $"/_synapse/admin/v1/rooms?from={offset}&limit={limit}&dir={dir.UrlEncode()}&order_by={orderBy.UrlEncode()}";
        if (!string.IsNullOrEmpty(searchTerm)) url += $"&search_term={searchTerm.UrlEncode()}";
        return await homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomListResult>(url, cancellationToken: token);
    }

    public static async Task<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom?> GetRoomDetailsAsync(this AuthenticatedHomeserverSynapse homeserver, string roomId)
    {
        return await homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom>($"/_synapse/admin/v1/rooms/{roomId.UrlEncode()}");
    }

    public static async Task<SynapseAdminRoomMessagesResponse?> GetRoomMessagesAsync(this AuthenticatedHomeserverSynapse homeserver, string roomId, int? limit = null, string? from = null, string? dir = null, string? filter = null, string? to = null)
    {
        var url = $"/_synapse/admin/v1/rooms/{roomId.UrlEncode()}/messages";
        var query = new List<string>();
        if (limit.HasValue) query.Add($"limit={limit}");
        if (!string.IsNullOrEmpty(from)) query.Add($"from={from.UrlEncode()}");
        if (!string.IsNullOrEmpty(dir)) query.Add($"dir={dir.UrlEncode()}");
        if (!string.IsNullOrEmpty(filter)) query.Add($"filter={filter.UrlEncode()}");
        if (!string.IsNullOrEmpty(to)) query.Add($"to={to.UrlEncode()}");
        if (query.Count > 0) url += "?" + string.Join("&", query);
        
        return await homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomMessagesResponse>(url);
    }

    public static async Task<SynapseAdminEventReportListResult?> GetEventReportListAsync(this AuthenticatedHomeserverSynapse homeserver, int offset, int limit, string dir, CancellationToken token = default)
    {
        var url = $"/_synapse/admin/v1/event_reports?from={offset}&limit={limit}&dir={dir.UrlEncode()}";
        return await homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminEventReportListResult>(url, cancellationToken: token);
    }

    public static async Task<SynapseAdminDestinationListResult?> GetFederationDestinationListAsync(this AuthenticatedHomeserverSynapse homeserver, int offset, int limit, string dir, CancellationToken token = default)
    {
        var url = $"/_synapse/admin/v1/federation/destinations?from={offset}&limit={limit}&dir={dir.UrlEncode()}";
        return await homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminDestinationListResult>(url, cancellationToken: token);
    }

    public static async Task<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken?> CreateRegistrationTokenAsync(this AuthenticatedHomeserverSynapse homeserver, SynapseAdminRegistrationTokenCreateRequest request)
    {
        var url = "/_synapse/admin/v1/registration_tokens/new";
        var resp = await homeserver.ClientHttpClient.PostAsJsonAsync(url, request);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>();
    }

    public static async Task<HttpResponseMessage> UpdateUserAsync(this AuthenticatedHomeserverSynapse homeserver, string userId, object request)
    {
        return await homeserver.ClientHttpClient.PutAsJsonAsync($"/_synapse/admin/v2/users/{userId.UrlEncode()}", request);
    }

    public static async Task<byte[]?> DownloadMediaAsync(this AuthenticatedHomeserverGeneric homeserver, string mxcUri, long maxBytes = 3 * 1024 * 1024)
    {
        var downloadUrl = await homeserver.GetMediaUrlAsync(mxcUri);
        using var response = await homeserver.ClientHttpClient.GetAsync(downloadUrl);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is null || contentLength.Value <= maxBytes)
        {
            return await response.Content.ReadAsByteArrayAsync();
        }
        return null;
    }
}
