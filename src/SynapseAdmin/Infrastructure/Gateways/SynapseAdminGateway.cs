using LibMatrix.Homeservers;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.Requests;
using System.Net.Http.Json;

namespace SynapseAdmin.Infrastructure.Gateways;

[System.Obsolete("Use SynapseCompatibleAdminGateway instead. This implementation is retained solely as an emergency fallback.")]
public class SynapseAdminGateway(AuthenticatedHomeserverSynapse synapse) : SynapseCompatibleAdminGateway(synapse, "Synapse", "Unknown")
{
    private readonly AuthenticatedHomeserverSynapse _synapse = synapse;

    // --- User Management ---

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

    // --- Room Management ---

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

    // --- Federation ---

    public override async Task ResetFederationConnectionTimeoutAsync(string destination, CancellationToken cancellationToken = default)
    {
        await _synapse.Admin.ResetFederationConnectionTimeoutAsync(destination);
    }

    // --- Event Reports ---

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
        using var resp = await _synapse.ClientHttpClient.PostAsJsonAsync(url, sdkReq, cancellationToken: cancellationToken);
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

    // --- Media ---

    public override async Task QuarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        await _synapse.Admin.QuarantineMediaById(serverName, mediaId);
    }

    public override async Task DeleteMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default)
    {
        await _synapse.Admin.DeleteMediaById(serverName, mediaId);
    }
}
