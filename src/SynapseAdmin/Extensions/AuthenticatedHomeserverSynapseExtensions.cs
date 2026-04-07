using System.Net.Http.Json;
using LibMatrix.Homeservers;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Extensions;

public static class AuthenticatedHomeserverSynapseExtensions
{
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
