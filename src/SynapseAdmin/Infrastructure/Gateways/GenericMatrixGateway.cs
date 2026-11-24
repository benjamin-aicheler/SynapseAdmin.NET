using LibMatrix.Homeservers;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.Requests;

namespace SynapseAdmin.Infrastructure.Gateways;

/// <summary>
/// A generic implementation of the Matrix gateway for non-Synapse servers.
/// Currently only implements standard CS API methods; every admin method refuses with
/// <see cref="OperationNotSupportedByHomeserverException"/>.
/// </summary>
internal class GenericMatrixGateway(AuthenticatedHomeserverGeneric homeserver) : MatrixGatewayBase(homeserver)
{
    public override Task<SynapseAdminUserListResult?> GetUserListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> GetUserDetailsAsync(string userId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task DeactivateUserAsync(string userId, bool erase, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task QuarantineMediaByUserIdAsync(string userId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<LoginResponse> LoginAsUserAsync(string userId, TimeSpan expireIn, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SendServerNoticeResponse?> SendServerNoticeAsync(string userId, object content, string? type = null, string? stateKey = null, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<UserMediaStatisticsResponse?> GetUserMediaStatisticsAsync(int limit = 10, string orderBy = "media_length", string dir = "b", string? searchTerm = null, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> UpdateUserAsync(string userId, object request, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminUserMembershipsResponse?> GetUserMembershipsAsync(string userId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminUserMediaResult?> GetUserMediaAsync(string userId, int from, int limit, string orderBy = "created_ts", string dir = "b", CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminExperimentalFeaturesResponse?> GetUserExperimentalFeaturesAsync(string userId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task SetUserExperimentalFeaturesAsync(string userId, SynapseAdminExperimentalFeaturesRequest request, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminAccountValidityResponse?> RenewAccountValidityAsync(SynapseAdminAccountValidityRequest request, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminRoomListResult?> GetRoomListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom?> GetRoomDetailsAsync(string roomId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminRoomMemberListResult?> GetRoomMembersAsync(string roomId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminRoomStateResult?> GetRoomStateAsync(string roomId, string? type = null, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminRoomMediaListResult?> GetRoomMediaListAsync(string roomId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminRoomDeleteResponse?> DeleteRoomAsync(string roomId, SynapseAdminRoomDeleteRequest request, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminRoomDeleteStatusResponse?> GetRoomDeleteStatusAsync(string deleteId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminRoomDeleteStatusListResponse?> GetRoomDeleteStatusesAsync(string roomId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task QuarantineMediaByRoomIdAsync(string roomId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task BlockRoomAsync(string roomId, bool block, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<RoomStatisticsResponse?> GetLargestRoomsAsync(CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminRoomMessagesResponse?> GetRoomMessagesAsync(string roomId, int? limit = null, string? from = null, string? dir = null, string? filter = null, string? to = null, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminPurgeHistoryResponse?> PurgeRoomHistoryAsync(string roomId, SynapseAdminPurgeHistoryRequest request, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminPurgeHistoryStatusResponse?> GetPurgeHistoryStatusAsync(string purgeId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminDestinationListResult?> GetFederationDestinationListAsync(int offset, int limit, string direction, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task ResetFederationConnectionTimeoutAsync(string destination, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminEventReportListResult?> GetEventReportListAsync(int offset, int limit, string direction, string? searchTerm = null, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task DeleteEventReportAsync(string reportId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminFetchEventResponse?> FetchEventAsync(string eventId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<List<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>> GetRegistrationTokensAsync(CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken?> CreateRegistrationTokenAsync(SynapseAdminRegistrationTokenCreateRequest request, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task UpdateRegistrationTokenAsync(string token, SynapseAdminRegistrationTokenUpdateRequest request, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task DeleteRegistrationTokenAsync(string token, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseVersionResponse?> GetSynapseVersionAsync(CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string mxcUri, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task QuarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task UnquarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task DeleteMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task ProtectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task UnprotectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminPurgeMediaCacheResponse?> PurgeRemoteMediaCacheAsync(long beforeTs, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminDeleteMediaResponse?> DeleteLocalMediaAsync(long beforeTs, long sizeGt, bool keepProfiles, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminBackgroundUpdatesStatusResponse?> GetBackgroundUpdatesStatusAsync(CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task<SynapseAdminBackgroundUpdatesEnabledResponse?> SetBackgroundUpdatesEnabledAsync(bool enabled, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
    public override Task StartBackgroundUpdatesJobAsync(string jobName, CancellationToken cancellationToken = default) => throw new OperationNotSupportedByHomeserverException();
}
