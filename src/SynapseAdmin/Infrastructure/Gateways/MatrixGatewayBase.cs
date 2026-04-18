using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Requests;
using LibMatrix.EventTypes.Spec;
using LibMatrix.Responses;
using LibMatrix.StructuredData;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using System.Net.Http;
using System.IO;

namespace SynapseAdmin.Infrastructure.Gateways;

/// <summary>
/// Provides a base implementation for standard Matrix Client-Server API calls.
/// Server-specific gateways should inherit from this class.
/// </summary>
public abstract class MatrixGatewayBase(AuthenticatedHomeserverGeneric homeserver) : IMatrixGateway
{
    protected readonly AuthenticatedHomeserverGeneric Homeserver = homeserver;

    // --- Standard Matrix CS API Implementation ---
    
    public virtual async Task<byte[]?> DownloadMediaAsync(string mxcUrl, long maxBytes = 3 * 1024 * 1024, CancellationToken cancellationToken = default)
    {
        var downloadUrl = await Homeserver.GetMediaUrlAsync(mxcUrl);
        // Optimization: Use standard GetAsync as MatrixHttpClient doesn't support ResponseHeadersRead overload
        using var response = await Homeserver.ClientHttpClient.GetAsync(downloadUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is null || contentLength.Value <= maxBytes)
        {
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        return null;
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
    public abstract Task<HttpResponseMessage> UpdateUserAsync(string userId, object request, CancellationToken cancellationToken = default);
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

    // Server Admin
    public abstract Task<SynapseVersionResponse?> GetSynapseVersionAsync(CancellationToken cancellationToken = default);

    // Media
    public abstract Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(MxcUri mxc, CancellationToken cancellationToken = default);
}
