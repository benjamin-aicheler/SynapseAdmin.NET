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
}
