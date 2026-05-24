using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.Requests;

namespace SynapseAdmin.Interfaces.Gateways;

/// <summary>
/// Defines the core contract for interacting with a Matrix server.
/// Methods are designed to match the current usage in business services.
/// </summary>
public interface IMatrixGateway
{
    // --- Session Info ---
    string UserId { get; }
    string Username { get; }
    string HomeserverUrl { get; }
    string ServerName { get; }
    string AccessToken { get; }

    bool SupportsAdminApi { get; }

    // --- User Management (Admin/Synapse) ---
    Task<SynapseAdminUserListResult?> GetUserListAsync(int offset, int limit, string orderBy, string direction, CancellationToken cancellationToken = default);
    Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> GetUserDetailsAsync(string userId, CancellationToken cancellationToken = default);
    Task DeactivateUserAsync(string userId, bool erase, CancellationToken cancellationToken = default);
    Task QuarantineMediaByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<LoginResponse> LoginAsUserAsync(string userId, TimeSpan expireIn, CancellationToken cancellationToken = default);
    Task<SendServerNoticeResponse?> SendServerNoticeAsync(string userId, object content, string? type = null, string? stateKey = null, CancellationToken cancellationToken = default);
    Task<UserMediaStatisticsResponse?> GetUserMediaStatisticsAsync(int limit = 10, string orderBy = "media_length", string dir = "b", CancellationToken cancellationToken = default);
    Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> UpdateUserAsync(string userId, object request, CancellationToken cancellationToken = default);
    Task<SynapseAdminUserMembershipsResponse?> GetUserMembershipsAsync(string userId, CancellationToken cancellationToken = default);
    Task<SynapseAdminUserMediaResult?> GetUserMediaAsync(string userId, CancellationToken cancellationToken = default);

    // --- Room Management (Admin/Synapse) ---
    Task<SynapseAdminRoomListResult?> GetRoomListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default);
    Task<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom?> GetRoomDetailsAsync(string roomId, CancellationToken cancellationToken = default);
    Task<SynapseAdminRoomMemberListResult?> GetRoomMembersAsync(string roomId, CancellationToken cancellationToken = default);
    Task<SynapseAdminRoomStateResult?> GetRoomStateAsync(string roomId, string? type = null, CancellationToken cancellationToken = default);
    Task<SynapseAdminRoomMediaListResult?> GetRoomMediaListAsync(string roomId, CancellationToken cancellationToken = default);
    Task DeleteRoomAsync(string roomId, SynapseAdminRoomDeleteRequest request, CancellationToken cancellationToken = default);
    Task QuarantineMediaByRoomIdAsync(string roomId, CancellationToken cancellationToken = default);
    Task BlockRoomAsync(string roomId, bool block, CancellationToken cancellationToken = default);
    Task<RoomStatisticsResponse?> GetLargestRoomsAsync(CancellationToken cancellationToken = default);
    Task<SynapseAdminRoomMessagesResponse?> GetRoomMessagesAsync(string roomId, int? limit = null, string? from = null, string? dir = null, string? filter = null, string? to = null, CancellationToken cancellationToken = default);
    Task<SynapseAdminPurgeHistoryResponse?> PurgeRoomHistoryAsync(string roomId, SynapseAdminPurgeHistoryRequest request, CancellationToken cancellationToken = default);
    Task<SynapseAdminPurgeHistoryStatusResponse?> GetPurgeHistoryStatusAsync(string purgeId, CancellationToken cancellationToken = default);

    // --- Federation (Admin/Synapse) ---
    Task<SynapseAdminDestinationListResult?> GetFederationDestinationListAsync(int offset, int limit, string direction, CancellationToken cancellationToken = default);
    Task ResetFederationConnectionTimeoutAsync(string destination, CancellationToken cancellationToken = default);

    // --- Event Reports (Admin/Synapse) ---
    Task<SynapseAdminEventReportListResult?> GetEventReportListAsync(int offset, int limit, string direction, CancellationToken cancellationToken = default);
    Task DeleteEventReportAsync(string reportId, CancellationToken cancellationToken = default);

    // --- Registration Tokens (Admin/Synapse) ---
    Task<List<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>> GetRegistrationTokensAsync(CancellationToken cancellationToken = default);
    Task<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken?> CreateRegistrationTokenAsync(SynapseAdminRegistrationTokenCreateRequest request, CancellationToken cancellationToken = default);
    Task UpdateRegistrationTokenAsync(string token, SynapseAdminRegistrationTokenUpdateRequest request, CancellationToken cancellationToken = default);
    Task DeleteRegistrationTokenAsync(string token, CancellationToken cancellationToken = default);

    // --- Server Admin ---
    Task<SynapseVersionResponse?> GetSynapseVersionAsync(CancellationToken cancellationToken = default);

    // --- Media (Standard/Admin) ---
    Task<byte[]?> DownloadMediaAsync(string mxcUrl, long maxBytes = 5 * 1024 * 1024, CancellationToken cancellationToken = default);
    Task<Stream> GetMediaStreamAsync(string mxcUri, string? filename = null, int? timeout = null, CancellationToken cancellationToken = default);
    Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);
    Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string mxcUri, CancellationToken cancellationToken = default);
    Task QuarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);
    Task QuarantineMediaAsync(string mxcUri, CancellationToken cancellationToken = default);
    Task UnquarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);
    Task UnquarantineMediaAsync(string mxcUri, CancellationToken cancellationToken = default);
    Task DeleteMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);
    Task DeleteMediaAsync(string mxcUri, CancellationToken cancellationToken = default);
    Task ProtectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);
    Task ProtectMediaAsync(string mxcUri, CancellationToken cancellationToken = default);
    Task UnprotectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);
    Task UnprotectMediaAsync(string mxcUri, CancellationToken cancellationToken = default);
    Task<SynapseAdminPurgeMediaCacheResponse?> PurgeRemoteMediaCacheAsync(long beforeTs, CancellationToken cancellationToken = default);
    Task<SynapseAdminDeleteMediaResponse?> DeleteLocalMediaAsync(long beforeTs, long sizeGt, bool keepProfiles, CancellationToken cancellationToken = default);

    // --- Background Updates ---
    Task<SynapseAdminBackgroundUpdatesStatusResponse?> GetBackgroundUpdatesStatusAsync(CancellationToken cancellationToken = default);
    Task<SynapseAdminBackgroundUpdatesEnabledResponse?> SetBackgroundUpdatesEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    Task StartBackgroundUpdatesJobAsync(string jobName, CancellationToken cancellationToken = default);
}
