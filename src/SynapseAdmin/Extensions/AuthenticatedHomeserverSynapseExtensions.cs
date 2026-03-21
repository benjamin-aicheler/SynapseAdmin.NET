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
}
