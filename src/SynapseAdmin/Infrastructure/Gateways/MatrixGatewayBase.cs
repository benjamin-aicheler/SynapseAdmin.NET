using LibMatrix.Homeservers;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.Requests;

namespace SynapseAdmin.Infrastructure.Gateways;

/// <summary>
/// Provides a base implementation for standard Matrix Client-Server API calls.
/// Server-specific gateways should inherit from this class.
/// </summary>
public abstract class MatrixGatewayBase(AuthenticatedHomeserverGeneric homeserver) : IMatrixGateway
{
    protected readonly AuthenticatedHomeserverGeneric Homeserver = homeserver;

    // --- Session Info Implementation ---
    public string UserId => Homeserver.UserId;
    public string Username => Homeserver.UserLocalpart;
    public string HomeserverUrl => Homeserver.BaseUrl;
    public string ServerName => Homeserver.ServerName;
    public string AccessToken => Homeserver.AccessToken;

    public virtual bool SupportsAdminApi => false;

    // --- Standard Matrix CS API Implementation ---
    
    public virtual async Task<byte[]?> DownloadMediaAsync(string mxcUrl, long maxBytes = 5 * 1024 * 1024, CancellationToken cancellationToken = default)
    {
        try
        {
            using var responseStream = await GetMediaStreamAsync(mxcUrl, cancellationToken: cancellationToken);
            using var ms = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            long totalRead = 0;

            while ((read = await responseStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                totalRead += read;
                if (totalRead > maxBytes) return null;
                await ms.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public virtual async Task<Stream> GetMediaStreamAsync(string mxcUri, string? filename = null, int? timeout = null, CancellationToken cancellationToken = default)
    {
        return await Homeserver.GetMediaStreamAsync(mxcUri, filename, timeout);
    }

    // --- Abstract Admin Methods (Must be implemented by server-specific gateways) ---

    // User Management
    public abstract Task<SynapseAdminUserListResult?> GetUserListAsync(int offset, int limit, string orderBy, string direction, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> GetUserDetailsAsync(string userId, CancellationToken cancellationToken = default);
    public abstract Task DeactivateUserAsync(string userId, bool erase, CancellationToken cancellationToken = default);
    public abstract Task QuarantineMediaByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    public abstract Task<LoginResponse> LoginAsUserAsync(string userId, TimeSpan expireIn, CancellationToken cancellationToken = default);
    public abstract Task<SendServerNoticeResponse?> SendServerNoticeAsync(string userId, object content, string? type = null, string? stateKey = null, CancellationToken cancellationToken = default);
    public abstract Task<UserMediaStatisticsResponse?> GetUserMediaStatisticsAsync(int limit = 10, string orderBy = "media_length", string dir = "b", CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> UpdateUserAsync(string userId, object request, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminUserMembershipsResponse?> GetUserMembershipsAsync(string userId, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminUserMediaResult?> GetUserMediaAsync(string userId, CancellationToken cancellationToken = default);

    // Room Management
    public abstract Task<SynapseAdminRoomListResult?> GetRoomListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom?> GetRoomDetailsAsync(string roomId, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminRoomMemberListResult?> GetRoomMembersAsync(string roomId, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminRoomStateResult?> GetRoomStateAsync(string roomId, string? type = null, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminRoomMediaListResult?> GetRoomMediaListAsync(string roomId, CancellationToken cancellationToken = default);
    public abstract Task DeleteRoomAsync(string roomId, SynapseAdminRoomDeleteRequest request, CancellationToken cancellationToken = default);
    public abstract Task QuarantineMediaByRoomIdAsync(string roomId, CancellationToken cancellationToken = default);
    public abstract Task BlockRoomAsync(string roomId, bool block, CancellationToken cancellationToken = default);
    public abstract Task<RoomStatisticsResponse?> GetLargestRoomsAsync(CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminRoomMessagesResponse?> GetRoomMessagesAsync(string roomId, int? limit = null, string? from = null, string? dir = null, string? filter = null, string? to = null, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminPurgeHistoryResponse?> PurgeRoomHistoryAsync(string roomId, SynapseAdminPurgeHistoryRequest request, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminPurgeHistoryStatusResponse?> GetPurgeHistoryStatusAsync(string purgeId, CancellationToken cancellationToken = default);

    // Federation
    public abstract Task<SynapseAdminDestinationListResult?> GetFederationDestinationListAsync(int offset, int limit, string direction, CancellationToken cancellationToken = default);
    public abstract Task ResetFederationConnectionTimeoutAsync(string destination, CancellationToken cancellationToken = default);

    // Event Reports
    public abstract Task<SynapseAdminEventReportListResult?> GetEventReportListAsync(int offset, int limit, string direction, CancellationToken cancellationToken = default);
    public abstract Task DeleteEventReportAsync(string reportId, CancellationToken cancellationToken = default);

    // Registration Tokens
    public abstract Task<List<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>> GetRegistrationTokensAsync(CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken?> CreateRegistrationTokenAsync(SynapseAdminRegistrationTokenCreateRequest request, CancellationToken cancellationToken = default);
    public abstract Task UpdateRegistrationTokenAsync(string token, SynapseAdminRegistrationTokenUpdateRequest request, CancellationToken cancellationToken = default);
    public abstract Task DeleteRegistrationTokenAsync(string token, CancellationToken cancellationToken = default);

    // --- Server Admin ---
    public abstract Task<SynapseVersionResponse?> GetSynapseVersionAsync(CancellationToken cancellationToken = default);

    // --- Media (Standard/Admin) ---
    public abstract Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string mxcUri, CancellationToken cancellationToken = default);
    public abstract Task QuarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);

    public virtual async Task QuarantineMediaAsync(string mxcUri, CancellationToken cancellationToken = default)
    {
        var mxc = LibMatrix.StructuredData.MxcUri.Parse(mxcUri);
        await QuarantineMediaAsync(mxc.ServerName, mxc.MediaId, cancellationToken);
    }

    public abstract Task UnquarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);

    public virtual async Task UnquarantineMediaAsync(string mxcUri, CancellationToken cancellationToken = default)
    {
        var mxc = LibMatrix.StructuredData.MxcUri.Parse(mxcUri);
        await UnquarantineMediaAsync(mxc.ServerName, mxc.MediaId, cancellationToken);
    }

    public abstract Task DeleteMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);

    public virtual async Task DeleteMediaAsync(string mxcUri, CancellationToken cancellationToken = default)
    {
        var mxc = LibMatrix.StructuredData.MxcUri.Parse(mxcUri);
        await DeleteMediaAsync(mxc.ServerName, mxc.MediaId, cancellationToken);
    }

    public abstract Task ProtectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);
    public virtual async Task ProtectMediaAsync(string mxcUri, CancellationToken cancellationToken = default)
    {
        var mxc = LibMatrix.StructuredData.MxcUri.Parse(mxcUri);
        await ProtectMediaAsync(mxc.ServerName, mxc.MediaId, cancellationToken);
    }

    public abstract Task UnprotectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);
    public virtual async Task UnprotectMediaAsync(string mxcUri, CancellationToken cancellationToken = default)
    {
        var mxc = LibMatrix.StructuredData.MxcUri.Parse(mxcUri);
        await UnprotectMediaAsync(mxc.ServerName, mxc.MediaId, cancellationToken);
    }

    public abstract Task<SynapseAdminPurgeMediaCacheResponse?> PurgeRemoteMediaCacheAsync(long beforeTs, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminDeleteMediaResponse?> DeleteLocalMediaAsync(long beforeTs, long sizeGt, bool keepProfiles, CancellationToken cancellationToken = default);

    // --- Background Updates ---
    public abstract Task<SynapseAdminBackgroundUpdatesStatusResponse?> GetBackgroundUpdatesStatusAsync(CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminBackgroundUpdatesEnabledResponse?> SetBackgroundUpdatesEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    public abstract Task StartBackgroundUpdatesJobAsync(string jobName, CancellationToken cancellationToken = default);
}
