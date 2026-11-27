using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArcaneLibs.Extensions;
using LibMatrix.Homeservers;
using LibMatrix.RoomTypes;
using LibMatrix.StructuredData;
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
    public string? DeviceId => Homeserver.WhoAmI.DeviceId;

    public virtual bool SupportsAdminApi => false;
    public virtual string ServerBrand => GatewayConstants.GenericBrand;
    public virtual string ServerVersion => GatewayConstants.UnknownVersion;

    // --- Request helpers ---

    /// <summary>
    /// Raw DELETE with an optional body. LibMatrix's DeleteAsync/DeleteAsJsonAsync have no CancellationToken overload,
    /// so the request is built here and sent through ClientHttpClient.SendAsync.
    /// </summary>
    protected async Task SendDeleteAsync(string url, HttpContent? content, CancellationToken cancellationToken)
    {
        using var reqMessage = new HttpRequestMessage(HttpMethod.Delete, url) { Content = content };
        using var resp = await Homeserver.ClientHttpClient.SendAsync(reqMessage, cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Raw DELETE like <see cref="SendDeleteAsync(string, HttpContent?, CancellationToken)"/> that also reads the JSON
    /// response body. An empty body returns <c>null</c>; a body that isn't JSON throws <see cref="JsonException"/>.
    /// </summary>
    protected async Task<TResponse?> SendDeleteAsync<TResponse>(string url, HttpContent? content, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var reqMessage = new HttpRequestMessage(HttpMethod.Delete, url) { Content = content };
        using var resp = await Homeserver.ClientHttpClient.SendAsync(reqMessage, cancellationToken);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<TResponse>(body);
    }

    // --- Standard Matrix CS API Implementation ---

    /// <summary>
    /// Opens a media download, forwarding <paramref name="range"/> and <paramref name="cancellationToken"/> to the
    /// homeserver (#372). Only the response headers are read; the caller owns (and must dispose) the result.
    /// </summary>
    /// <remarks>
    /// LibMatrix's <c>GetMediaStreamAsync</c> is not used: it accepts neither a range nor a token, and on a fresh
    /// homeserver instance (one per HTTP request here) it first probes the media endpoint with an extra full
    /// <c>GET</c> whose response is never disposed. Instead the real request goes to the authenticated media endpoint
    /// directly and falls back to the legacy endpoint only when the server does not recognise the former.
    /// <c>SendUnhandledAsync</c> is used so that <c>206</c> and <c>416</c> reach us instead of being turned into
    /// exceptions.
    /// </remarks>
    public virtual async Task<MediaDownload> OpenMediaAsync(string mxcUri, MediaByteRange? range = null, CancellationToken cancellationToken = default)
    {
        var mxc = MxcUri.Parse(mxcUri.Trim());

        var response = await SendMediaRequestAsync(mxc.ToDownloadUri(), range, cancellationToken);
        try
        {
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.BadRequest &&
                await IsUnrecognisedEndpointAsync(response, cancellationToken))
            {
                response.Dispose();
                response = await SendMediaRequestAsync(mxc.ToLegacyDownloadUri(), range, cancellationToken);
            }

            return await ToMediaDownloadAsync(response, cancellationToken);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private Task<HttpResponseMessage> SendMediaRequestAsync(string relativeUri, MediaByteRange? range, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(relativeUri, UriKind.Relative));
        if (range is { } r) request.Headers.Range = new RangeHeaderValue(r.From, r.To);
        return Homeserver.ClientHttpClient.SendUnhandledAsync(request, cancellationToken);
    }

    /// <summary>
    /// Servers without authenticated media answer <c>M_UNRECOGNIZED</c> (or a bare 404/405 from a proxy). A missing
    /// object is <c>M_NOT_FOUND</c> and must not trigger a second request.
    /// </summary>
    private static async Task<bool> IsUnrecognisedEndpointAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var errorCode = await ReadErrorCodeAsync(response, cancellationToken);
        return errorCode == "M_UNRECOGNIZED" ||
               (errorCode == null && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }

    private static async Task<MediaDownload> ToMediaDownloadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                return new MediaDownload
                {
                    Status = MediaDownloadStatus.Full,
                    Content = await ResponseOwningStream.OpenAsync(response, cancellationToken),
                    ContentLength = response.Content.Headers.ContentLength
                };

            case HttpStatusCode.PartialContent:
                var contentRange = response.Content.Headers.ContentRange;
                if (contentRange is not { HasRange: true, From: not null, To: not null } || contentRange.Unit != "bytes")
                    throw new InvalidDataException("Homeserver returned 206 without a single byte range.");

                return new MediaDownload
                {
                    Status = MediaDownloadStatus.Partial,
                    Content = await ResponseOwningStream.OpenAsync(response, cancellationToken),
                    ContentLength = response.Content.Headers.ContentLength,
                    RangeFrom = contentRange.From,
                    RangeTo = contentRange.To,
                    CompleteLength = contentRange.Length
                };

            case HttpStatusCode.RequestedRangeNotSatisfiable:
                var completeLength = response.Content.Headers.ContentRange?.Length;
                response.Dispose();
                return new MediaDownload
                {
                    Status = MediaDownloadStatus.RangeNotSatisfiable,
                    Content = Stream.Null,
                    CompleteLength = completeLength
                };

            default:
                var status = response.StatusCode;
                var errorCode = await ReadErrorCodeAsync(response, cancellationToken);
                response.Dispose();
                throw new HttpRequestException($"Media download failed with {(int)status} ({errorCode ?? "no errcode"}).", null, status);
        }
    }

    /// <summary>Reads the Matrix <c>errcode</c> from a (small) error body, or <c>null</c> if there is none.</summary>
    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const int maxErrorBodyBytes = 16 * 1024;
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[maxErrorBodyBytes];
            var length = 0;
            int read;
            while (length < buffer.Length && (read = await body.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
                length += read;

            using var json = JsonDocument.Parse(buffer.AsMemory(0, length));
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                   json.RootElement.TryGetProperty("errcode", out var errcode) &&
                   errcode.ValueKind == JsonValueKind.String
                ? errcode.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // --- Abstract Admin Methods (Must be implemented by server-specific gateways) ---

    // User Management
    public abstract Task<SynapseAdminUserListResult?> GetUserListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> GetUserDetailsAsync(string userId, CancellationToken cancellationToken = default);
    public abstract Task DeactivateUserAsync(string userId, bool erase, CancellationToken cancellationToken = default);
    public abstract Task QuarantineMediaByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    public abstract Task<LoginResponse> LoginAsUserAsync(string userId, TimeSpan expireIn, CancellationToken cancellationToken = default);
    public abstract Task<SendServerNoticeResponse?> SendServerNoticeAsync(string userId, object content, string? type = null, string? stateKey = null, CancellationToken cancellationToken = default);
    public abstract Task<UserMediaStatisticsResponse?> GetUserMediaStatisticsAsync(int limit = 10, string orderBy = "media_length", string dir = "b", string? searchTerm = null, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> UpdateUserAsync(string userId, SynapseAdminUserUpsertRequest request, CancellationToken cancellationToken = default);
    public abstract Task SetUserSuspendedAsync(string userId, bool suspend, CancellationToken cancellationToken = default);
    public abstract Task SetUserShadowBannedAsync(string userId, bool shadowBanned, CancellationToken cancellationToken = default);
    public abstract Task ResetPasswordAsync(string userId, SynapseAdminResetPasswordRequest request, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminUserMembershipsResponse?> GetUserMembershipsAsync(string userId, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminUserMediaResult?> GetUserMediaAsync(string userId, int from, int limit, string orderBy = "created_ts", string dir = "b", CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminDeleteMediaResponse?> DeleteUserMediaAsync(string userId, int limit, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminExperimentalFeaturesResponse?> GetUserExperimentalFeaturesAsync(string userId, CancellationToken cancellationToken = default);
    public abstract Task SetUserExperimentalFeaturesAsync(string userId, SynapseAdminExperimentalFeaturesRequest request, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminAccountValidityResponse?> RenewAccountValidityAsync(SynapseAdminAccountValidityRequest request, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminUserDeviceListResult?> GetUserDevicesAsync(string userId, CancellationToken cancellationToken = default);
    public abstract Task RenameUserDeviceAsync(string userId, string deviceId, SynapseAdminDeviceUpdateRequest request, CancellationToken cancellationToken = default);
    public abstract Task DeleteUserDevicesAsync(string userId, SynapseAdminDeleteDevicesRequest request, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminWhoisResponse?> GetUserWhoisAsync(string userId, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminUserRedactResponse?> RedactUserEventsAsync(string userId, SynapseAdminUserRedactRequest request, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminUserRedactStatusResponse?> GetUserRedactionStatusAsync(string redactId, CancellationToken cancellationToken = default);

    // Room Management
    public abstract Task<SynapseAdminRoomListResult?> GetRoomListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom?> GetRoomDetailsAsync(string roomId, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminRoomMemberListResult?> GetRoomMembersAsync(string roomId, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminRoomStateResult?> GetRoomStateAsync(string roomId, string? type = null, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminRoomMediaListResult?> GetRoomMediaListAsync(string roomId, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminRoomDeleteResponse?> DeleteRoomAsync(string roomId, SynapseAdminRoomDeleteRequest request, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminRoomDeleteStatusResponse?> GetRoomDeleteStatusAsync(string deleteId, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminRoomDeleteStatusListResponse?> GetRoomDeleteStatusesAsync(string roomId, CancellationToken cancellationToken = default);
    public abstract Task QuarantineMediaByRoomIdAsync(string roomId, CancellationToken cancellationToken = default);
    public abstract Task BlockRoomAsync(string roomId, bool block, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminRoomBlockStatusResponse?> GetRoomBlockStatusAsync(string roomId, CancellationToken cancellationToken = default);
    public abstract Task<RoomStatisticsResponse?> GetLargestRoomsAsync(CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminRoomMessagesResponse?> GetRoomMessagesAsync(string roomId, int? limit = null, string? from = null, string? dir = null, string? filter = null, string? to = null, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminPurgeHistoryResponse?> PurgeRoomHistoryAsync(string roomId, SynapseAdminPurgeHistoryRequest request, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminPurgeHistoryStatusResponse?> GetPurgeHistoryStatusAsync(string purgeId, CancellationToken cancellationToken = default);

    // Federation
    public abstract Task<SynapseAdminDestinationListResult?> GetFederationDestinationListAsync(string? from, int limit, string direction, CancellationToken cancellationToken = default);
    public abstract Task ResetFederationConnectionTimeoutAsync(string destination, CancellationToken cancellationToken = default);

    // Event Reports
    public abstract Task<SynapseAdminEventReportListResult?> GetEventReportListAsync(int offset, int limit, string direction, string? searchTerm = null, CancellationToken cancellationToken = default);
    public abstract Task DeleteEventReportAsync(string reportId, CancellationToken cancellationToken = default);

    // Event Forensics
    public abstract Task<SynapseAdminFetchEventResponse?> FetchEventAsync(string eventId, CancellationToken cancellationToken = default);

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
        var mxc = MxcUri.Parse(mxcUri);
        await QuarantineMediaAsync(mxc.ServerName, mxc.MediaId, cancellationToken);
    }

    public abstract Task UnquarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);

    public virtual async Task UnquarantineMediaAsync(string mxcUri, CancellationToken cancellationToken = default)
    {
        var mxc = MxcUri.Parse(mxcUri);
        await UnquarantineMediaAsync(mxc.ServerName, mxc.MediaId, cancellationToken);
    }

    public abstract Task DeleteMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);

    public virtual async Task DeleteMediaAsync(string mxcUri, CancellationToken cancellationToken = default)
    {
        var mxc = MxcUri.Parse(mxcUri);
        await DeleteMediaAsync(mxc.ServerName, mxc.MediaId, cancellationToken);
    }

    public abstract Task ProtectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);
    public virtual async Task ProtectMediaAsync(string mxcUri, CancellationToken cancellationToken = default)
    {
        var mxc = MxcUri.Parse(mxcUri);
        await ProtectMediaAsync(mxc.ServerName, mxc.MediaId, cancellationToken);
    }

    public abstract Task UnprotectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default);
    public virtual async Task UnprotectMediaAsync(string mxcUri, CancellationToken cancellationToken = default)
    {
        var mxc = MxcUri.Parse(mxcUri);
        await UnprotectMediaAsync(mxc.ServerName, mxc.MediaId, cancellationToken);
    }

    public abstract Task<SynapseAdminPurgeMediaCacheResponse?> PurgeRemoteMediaCacheAsync(long beforeTs, CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminDeleteMediaResponse?> DeleteLocalMediaAsync(long beforeTs, long sizeGt, bool keepProfiles, CancellationToken cancellationToken = default);

    // --- Room Directory (Standard CS API) ---
    // LibMatrix's alias and directory methods aren't used (#580): they take no token, append the publicRooms since
    // token to the query unencoded and form-encode the alias in the path.

    /// <summary>
    /// Lists the published room directory: a <c>GET</c> with the paging token in the query, or a <c>POST</c> with the
    /// search term as a filter. The token is opaque and can contain '+' and '/' (Synapse's is standard base64), so it is
    /// always encoded.
    /// </summary>
    public virtual async Task<MatrixPublicRoomDirectoryResult?> GetPublicRoomsAsync(int limit, string? since = null, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        const string url = "/_matrix/client/v3/publicRooms";
        PublicRoomDirectoryResult result;
        if (string.IsNullOrEmpty(searchTerm))
        {
            var query = $"{url}?limit={limit}";
            if (!string.IsNullOrWhiteSpace(since)) query += $"&since={since.UrlEncode()}";
            result = await Homeserver.ClientHttpClient.GetFromJsonAsync<PublicRoomDirectoryResult>(query, cancellationToken: cancellationToken);
        }
        else
        {
            var request = new RoomDirectoryFilteredRequest
            {
                Limit = limit,
                Since = since,
                Filter = new RoomDirectoryFilter { GenericSearchTerm = searchTerm }
            };
            using var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, request, cancellationToken: cancellationToken);
            resp.EnsureSuccessStatusCode();
            result = await resp.Content.ReadFromJsonAsync<PublicRoomDirectoryResult>(cancellationToken) ?? throw new InvalidOperationException();
        }

        return new MatrixPublicRoomDirectoryResult
        {
            NextBatch = result.NextBatch,
            PrevBatch = result.PrevBatch,
            TotalRoomCountEstimate = result.TotalRoomCountEstimate,
            Chunk = result.Chunk.Select(c => new MatrixPublicRoomDirectoryResult.PublicRoomListItem
            {
                AvatarUrl = c.AvatarUrl,
                CanonicalAlias = c.CanonicalAlias,
                GuestCanJoin = c.GuestCanJoin,
                JoinRule = c.JoinRule,
                Name = c.Name,
                NumJoinedMembers = c.NumJoinedMembers,
                RoomId = c.RoomId,
                Topic = c.Topic,
                WorldReadable = c.WorldReadable
            }).ToList()
        };
    }

    public virtual async Task<string?> GetRoomDirectoryVisibilityAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var result = await Homeserver.ClientHttpClient.GetFromJsonAsync<RoomDirectoryVisibilityResponse>(
            $"/_matrix/client/v3/directory/list/room/{roomId.UrlEncode()}", cancellationToken: cancellationToken);
        return result.Visibility.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Publishes (<c>"public"</c>, in any case) or unpublishes (any other value) a room in the published room directory.
    /// </summary>
    /// <remarks>
    /// LibMatrix's <c>SetRoomDirectoryVisibilityAsync</c> is not used: it sends <c>POST</c>, takes no token and returns
    /// an undisposed response. The Client-Server spec defines only <c>GET</c> and <c>PUT</c> on this path (#517).
    /// The visibility is always sent explicitly, because the spec defaults a missing value to <c>public</c>.
    /// </remarks>
    public virtual async Task SetRoomDirectoryVisibilityAsync(string roomId, string visibility, CancellationToken cancellationToken = default)
    {
        var vis = visibility.ToLowerInvariant() == "public" ? "public" : "private";
        var url = $"/_matrix/client/v3/directory/list/room/{roomId.UrlEncode()}";
        using var resp = await Homeserver.ClientHttpClient.PutAsJsonAsync(url, new { visibility = vis }, cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public virtual async Task PutRoomAliasAsync(string roomAlias, string roomId, CancellationToken cancellationToken = default)
    {
        using var resp = await Homeserver.ClientHttpClient.PutAsJsonAsync(RoomAliasUrl(roomAlias), new RoomIdResponse { RoomId = roomId },
            cancellationToken: cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public virtual Task DeleteRoomAliasAsync(string roomAlias, CancellationToken cancellationToken = default) =>
        SendDeleteAsync(RoomAliasUrl(roomAlias), null, cancellationToken);

    public virtual async Task<List<string>?> GetLocalRoomAliasesAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var result = await Homeserver.ClientHttpClient.GetFromJsonAsync<AuthenticatedHomeserverGeneric.RoomAliasesResponse>(
            $"/_matrix/client/v3/rooms/{roomId.UrlEncode()}/aliases", cancellationToken: cancellationToken);
        return result.Aliases;
    }

    /// <summary>
    /// An alias may contain whitespace, which Synapse rejects. Form encoding (<c>.UrlEncode()</c>) would send a space
    /// as '+', which a path keeps, so the request would create or delete another alias instead (#580).
    /// </summary>
    private static string RoomAliasUrl(string roomAlias) =>
        $"/_matrix/client/v3/directory/room/{Uri.EscapeDataString(roomAlias)}";

    // --- Background Updates ---
    public abstract Task<SynapseAdminBackgroundUpdatesStatusResponse?> GetBackgroundUpdatesStatusAsync(CancellationToken cancellationToken = default);
    public abstract Task<SynapseAdminBackgroundUpdatesEnabledResponse?> SetBackgroundUpdatesEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    public abstract Task StartBackgroundUpdatesJobAsync(string jobName, CancellationToken cancellationToken = default);
}
