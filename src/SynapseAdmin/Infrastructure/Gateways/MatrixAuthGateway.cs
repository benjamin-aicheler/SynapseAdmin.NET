using LibMatrix.Homeservers;
using LibMatrix.Services;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.Requests;

namespace SynapseAdmin.Infrastructure.Gateways;

/// <summary>
/// Implementation of IMatrixAuthGateway using LibMatrix's HomeserverProviderService.
/// Centralizes unauthenticated "wire-level" entry points.
/// </summary>
public class MatrixAuthGateway(HomeserverProviderService hsProvider) : IMatrixAuthGateway
{
    public async Task ResolveHomeserverAsync(string homeserverUrl, CancellationToken cancellationToken = default)
    {
        // Matches MatrixSessionService logic: enableServer: true for federation support
        await hsProvider.GetRemoteHomeserver(homeserverUrl, enableServer: true);
    }

    public async Task<LoginResponse> LoginAsync(string homeserverUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var sdkResp = await hsProvider.Login(homeserverUrl, username, password);
        return new LoginResponse
        {
            AccessToken = sdkResp.AccessToken,
            DeviceId = sdkResp.DeviceId,
            Homeserver = sdkResp.Homeserver,
            UserId = sdkResp.UserId
        };
    }

    public async Task<IMatrixGateway> GetAuthenticatedAsync(string homeserverUrl, string accessToken, CancellationToken cancellationToken = default)
    {
        var authenticatedHomeserver = await hsProvider.GetAuthenticatedWithToken(homeserverUrl, accessToken);
        
        if (authenticatedHomeserver is AuthenticatedHomeserverSynapse synapse)
        {
            return new SynapseAdminGateway(synapse);
        }
        
        // For now we only support Synapse, but we could return a generic gateway here
        return new GenericMatrixGateway(authenticatedHomeserver);
    }
}

/// <summary>
/// A generic implementation of the Matrix gateway for non-Synapse servers.
/// Currently only implements standard CS API methods.
/// </summary>
internal class GenericMatrixGateway(AuthenticatedHomeserverGeneric homeserver) : MatrixGatewayBase(homeserver)
{
    public override Task<SynapseAdminUserListResult?> GetUserListAsync(int offset, int limit, string orderBy, string direction, CancellationToken cancellationToken = default) => throw new NotSupportedException("Admin APIs are only supported for Synapse.");
    public override Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> GetUserDetailsAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task DeactivateUserAsync(string userId, bool erase, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task QuarantineMediaByUserIdAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<LoginResponse> LoginAsUserAsync(string userId, TimeSpan expireIn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SendServerNoticeResponse?> SendServerNoticeAsync(string userId, object content, string? type = null, string? stateKey = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<UserMediaStatisticsResponse?> GetUserMediaStatisticsAsync(int limit = 10, string orderBy = "media_length", string dir = "b", CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> UpdateUserAsync(string userId, object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminUserMembershipsResponse?> GetUserMembershipsAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminUserMediaResult?> GetUserMediaAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminRoomListResult?> GetRoomListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom?> GetRoomDetailsAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminRoomMemberListResult?> GetRoomMembersAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminRoomStateResult?> GetRoomStateAsync(string roomId, string? type = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminRoomMediaListResult?> GetRoomMediaListAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task DeleteRoomAsync(string roomId, SynapseAdminRoomDeleteRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task QuarantineMediaByRoomIdAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task BlockRoomAsync(string roomId, bool block, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<RoomStatisticsResponse?> GetLargestRoomsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminRoomMessagesResponse?> GetRoomMessagesAsync(string roomId, int? limit = null, string? from = null, string? dir = null, string? filter = null, string? to = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminPurgeHistoryResponse?> PurgeRoomHistoryAsync(string roomId, SynapseAdminPurgeHistoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminPurgeHistoryStatusResponse?> GetPurgeHistoryStatusAsync(string purgeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminDestinationListResult?> GetFederationDestinationListAsync(int offset, int limit, string direction, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task ResetFederationConnectionTimeoutAsync(string destination, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminEventReportListResult?> GetEventReportListAsync(int offset, int limit, string direction, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task DeleteEventReportAsync(string reportId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<List<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>> GetRegistrationTokensAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken?> CreateRegistrationTokenAsync(SynapseAdminRegistrationTokenCreateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task UpdateRegistrationTokenAsync(string token, SynapseAdminRegistrationTokenUpdateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task DeleteRegistrationTokenAsync(string token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseVersionResponse?> GetSynapseVersionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string mxcUri, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task QuarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task UnquarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task DeleteMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task<SynapseAdminBackgroundUpdatesStatusResponse?> GetBackgroundUpdatesStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException("Background updates are only supported for Synapse.");
    public override Task<SynapseAdminBackgroundUpdatesEnabledResponse?> SetBackgroundUpdatesEnabledAsync(bool enabled, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override Task StartBackgroundUpdatesJobAsync(string jobName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
