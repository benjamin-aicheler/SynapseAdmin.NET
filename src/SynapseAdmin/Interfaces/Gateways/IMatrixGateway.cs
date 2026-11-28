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
    /// <summary>
    /// The device of this session's access token, as <c>/_matrix/client/v3/account/whoami</c> reported it at sign-in, or
    /// <c>null</c> if the homeserver reported none (#598).
    /// </summary>
    string? DeviceId { get; }

    bool SupportsAdminApi { get; }
    string ServerBrand { get; }
    string ServerVersion { get; }

    // --- User Management (Admin/Synapse) ---
    Task<SynapseAdminUserListResult?> GetUserListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default);
    Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> GetUserDetailsAsync(string userId, CancellationToken cancellationToken = default);
    Task DeactivateUserAsync(string userId, bool erase, CancellationToken cancellationToken = default);
    Task QuarantineMediaByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<LoginResponse> LoginAsUserAsync(string userId, TimeSpan expireIn, CancellationToken cancellationToken = default);
    Task<SendServerNoticeResponse?> SendServerNoticeAsync(string userId, object content, string? type = null, string? stateKey = null, CancellationToken cancellationToken = default);
    /// <param name="searchTerm">Optional substring filter on user ID or display name (Synapse <c>search_term</c>).</param>
    Task<UserMediaStatisticsResponse?> GetUserMediaStatisticsAsync(int limit = 10, string orderBy = "media_length", string dir = "b", string? searchTerm = null, CancellationToken cancellationToken = default);
    Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> UpdateUserAsync(string userId, SynapseAdminUserUpsertRequest request, CancellationToken cancellationToken = default);
    /// <summary>Suspends or unsuspends a local account (<c>PUT /_synapse/admin/v1/suspend/{userId}</c>).</summary>
    Task SetUserSuspendedAsync(string userId, bool suspend, CancellationToken cancellationToken = default);
    /// <summary>Shadow-bans a local account or lifts the ban (<c>POST</c> or <c>DELETE /_synapse/admin/v1/users/{userId}/shadow_ban</c>).</summary>
    Task SetUserShadowBannedAsync(string userId, bool shadowBanned, CancellationToken cancellationToken = default);
    /// <summary>Sets a new password for a local account (<c>POST /_synapse/admin/v1/reset_password/{userId}</c>).</summary>
    Task ResetPasswordAsync(string userId, SynapseAdminResetPasswordRequest request, CancellationToken cancellationToken = default);
    Task<SynapseAdminUserMembershipsResponse?> GetUserMembershipsAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>One page of media uploaded by a local user. <paramref name="from"/> is the item offset.</summary>
    Task<SynapseAdminUserMediaResult?> GetUserMediaAsync(string userId, int from, int limit, string orderBy = "created_ts", string dir = "b", CancellationToken cancellationToken = default);
    /// <summary>
    /// Deletes the first <paramref name="limit"/> of a local user's uploads, newest first, protected and quarantined
    /// media and avatars included (<c>DELETE /_synapse/admin/v1/users/{userId}/media</c>, #599). <c>total</c> is the
    /// number this call deleted.
    /// </summary>
    Task<SynapseAdminDeleteMediaResponse?> DeleteUserMediaAsync(string userId, int limit, CancellationToken cancellationToken = default);
    Task<SynapseAdminExperimentalFeaturesResponse?> GetUserExperimentalFeaturesAsync(string userId, CancellationToken cancellationToken = default);
    Task SetUserExperimentalFeaturesAsync(string userId, SynapseAdminExperimentalFeaturesRequest request, CancellationToken cancellationToken = default);
    Task<SynapseAdminAccountValidityResponse?> RenewAccountValidityAsync(SynapseAdminAccountValidityRequest request, CancellationToken cancellationToken = default);
    /// <summary>A local user's devices (<c>GET /_synapse/admin/v2/users/{userId}/devices</c>, #598).</summary>
    Task<SynapseAdminUserDeviceListResult?> GetUserDevicesAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>Renames one of a local user's devices (<c>PUT /_synapse/admin/v2/users/{userId}/devices/{deviceId}</c>, #598).</summary>
    Task RenameUserDeviceAsync(string userId, string deviceId, SynapseAdminDeviceUpdateRequest request, CancellationToken cancellationToken = default);
    /// <summary>
    /// Deletes some of a local user's devices and invalidates their access tokens
    /// (<c>POST /_synapse/admin/v2/users/{userId}/delete_devices</c>, #598).
    /// </summary>
    Task DeleteUserDevicesAsync(string userId, SynapseAdminDeleteDevicesRequest request, CancellationToken cancellationToken = default);
    /// <summary>A local user's recent connections (<c>GET /_synapse/admin/v1/whois/{userId}</c>, #598).</summary>
    Task<SynapseAdminWhoisResponse?> GetUserWhoisAsync(string userId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Starts redacting a user's events as a background task (<c>POST /_synapse/admin/v1/user/{userId}/redact</c>, #600)
    /// and returns its <c>redact_id</c>.
    /// </summary>
    Task<SynapseAdminUserRedactResponse?> RedactUserEventsAsync(string userId, SynapseAdminUserRedactRequest request, CancellationToken cancellationToken = default);
    /// <summary>
    /// The status of a redaction (<c>GET /_synapse/admin/v1/user/redact_status/{redactId}</c>, #600); <c>null</c> for a
    /// redaction the homeserver doesn't know (any more).
    /// </summary>
    Task<SynapseAdminUserRedactStatusResponse?> GetUserRedactionStatusAsync(string redactId, CancellationToken cancellationToken = default);

    // --- Room Management (Admin/Synapse) ---
    Task<SynapseAdminRoomListResult?> GetRoomListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, bool? publicRooms = null, bool? emptyRooms = null, CancellationToken cancellationToken = default);
    Task<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom?> GetRoomDetailsAsync(string roomId, CancellationToken cancellationToken = default);
    Task<SynapseAdminRoomMemberListResult?> GetRoomMembersAsync(string roomId, CancellationToken cancellationToken = default);
    Task<SynapseAdminRoomStateResult?> GetRoomStateAsync(string roomId, string? type = null, CancellationToken cancellationToken = default);
    Task<SynapseAdminRoomMediaListResult?> GetRoomMediaListAsync(string roomId, CancellationToken cancellationToken = default);
    Task<SynapseAdminRoomDeleteResponse?> DeleteRoomAsync(string roomId, SynapseAdminRoomDeleteRequest request, CancellationToken cancellationToken = default);
    /// <summary>The status of one room deletion, or <c>null</c> if the homeserver doesn't know it (anymore) or has no such endpoint.</summary>
    Task<SynapseAdminRoomDeleteStatusResponse?> GetRoomDeleteStatusAsync(string deleteId, CancellationToken cancellationToken = default);
    /// <summary>The room's active and recent deletions, or <c>null</c> if there are none or the homeserver has no such endpoint.</summary>
    Task<SynapseAdminRoomDeleteStatusListResponse?> GetRoomDeleteStatusesAsync(string roomId, CancellationToken cancellationToken = default);
    Task QuarantineMediaByRoomIdAsync(string roomId, CancellationToken cancellationToken = default);
    Task BlockRoomAsync(string roomId, bool block, CancellationToken cancellationToken = default);
    Task<SynapseAdminRoomBlockStatusResponse?> GetRoomBlockStatusAsync(string roomId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Grants a user the highest power level of a local user who is joined to the room, and invites the user if they
    /// aren't in the room and it isn't public (<c>POST /_synapse/admin/v1/rooms/{roomId}/make_room_admin</c>, #587).
    /// <paramref name="userId"/> <c>null</c> means the signed-in admin. The path takes a room ID; an alias would need
    /// <c>Uri.EscapeDataString</c> (#580).
    /// </summary>
    Task MakeRoomAdminAsync(string roomId, string? userId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Joins a local user to a room, inviting the user first as the signed-in admin unless the room is public
    /// (<c>POST /_synapse/admin/v1/join/{roomIdOrAlias}</c>, #587). An alias goes into the path with
    /// <c>Uri.EscapeDataString</c> (#580), a room ID form-encoded. <paramref name="serverNames"/> are the servers to join
    /// through when this server isn't in the room, sent as <c>server_name</c> query parameters (#606): the reference
    /// doesn't document them, but Synapse and Tuwunel read them for a room ID and replace them with the alias's servers for
    /// an alias.
    /// </summary>
    Task<SynapseAdminJoinRoomResponse?> JoinUserToRoomAsync(string roomIdOrAlias, string userId, IReadOnlyList<string>? serverNames = null, CancellationToken cancellationToken = default);
    Task<RoomStatisticsResponse?> GetLargestRoomsAsync(CancellationToken cancellationToken = default);
    Task<SynapseAdminRoomMessagesResponse?> GetRoomMessagesAsync(string roomId, int? limit = null, string? from = null, string? dir = null, string? filter = null, string? to = null, CancellationToken cancellationToken = default);
    Task<SynapseAdminPurgeHistoryResponse?> PurgeRoomHistoryAsync(string roomId, SynapseAdminPurgeHistoryRequest request, CancellationToken cancellationToken = default);
    Task<SynapseAdminPurgeHistoryStatusResponse?> GetPurgeHistoryStatusAsync(string purgeId, CancellationToken cancellationToken = default);

    // --- Room Directory (Standard CS API) ---
    Task<MatrixPublicRoomDirectoryResult?> GetPublicRoomsAsync(int limit, string? since = null, string? searchTerm = null, CancellationToken cancellationToken = default);
    Task<string?> GetRoomDirectoryVisibilityAsync(string roomId, CancellationToken cancellationToken = default);
    Task SetRoomDirectoryVisibilityAsync(string roomId, string visibility, CancellationToken cancellationToken = default);
    Task PutRoomAliasAsync(string roomAlias, string roomId, CancellationToken cancellationToken = default);
    Task DeleteRoomAliasAsync(string roomAlias, CancellationToken cancellationToken = default);
    Task<List<string>?> GetLocalRoomAliasesAsync(string roomId, CancellationToken cancellationToken = default);

    // --- Federation (Admin/Synapse) ---
    Task<SynapseAdminDestinationListResult?> GetFederationDestinationListAsync(string? from, int limit, string direction, CancellationToken cancellationToken = default);
    Task ResetFederationConnectionTimeoutAsync(string destination, CancellationToken cancellationToken = default);

    // --- Event Reports (Admin/Synapse) ---
    Task<SynapseAdminEventReportListResult?> GetEventReportListAsync(int offset, int limit, string direction, string? searchTerm = null, CancellationToken cancellationToken = default);
    Task DeleteEventReportAsync(string reportId, CancellationToken cancellationToken = default);

    // --- Event Forensics (Admin/Synapse) ---
    Task<SynapseAdminFetchEventResponse?> FetchEventAsync(string eventId, CancellationToken cancellationToken = default);

    // --- Registration Tokens (Admin/Synapse) ---
    Task<List<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>> GetRegistrationTokensAsync(CancellationToken cancellationToken = default);
    Task<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken?> CreateRegistrationTokenAsync(SynapseAdminRegistrationTokenCreateRequest request, CancellationToken cancellationToken = default);
    Task UpdateRegistrationTokenAsync(string token, SynapseAdminRegistrationTokenUpdateRequest request, CancellationToken cancellationToken = default);
    Task DeleteRegistrationTokenAsync(string token, CancellationToken cancellationToken = default);

    // --- Server Admin ---
    Task<SynapseVersionResponse?> GetSynapseVersionAsync(CancellationToken cancellationToken = default);

    // --- Media (Standard/Admin) ---
    /// <summary>Opens a media download, forwarding an optional byte range. The caller must dispose the result.</summary>
    Task<MediaDownload> OpenMediaAsync(string mxcUri, MediaByteRange? range = null, CancellationToken cancellationToken = default);
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
