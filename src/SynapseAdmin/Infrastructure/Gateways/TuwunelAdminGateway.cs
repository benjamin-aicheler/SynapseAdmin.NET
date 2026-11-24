using ArcaneLibs.Extensions;
using LibMatrix.Homeservers;
using SynapseAdmin.Infrastructure.Helpers;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Infrastructure.Gateways;

/// <summary>
/// The gateway for Tuwunel (>= 1.8.2), which serves the Synapse Admin API with known deviations (ADR 0006). It inherits
/// the Synapse contract and overrides only the methods where Tuwunel deviates, so that each returns Synapse's result.
/// <c>MatrixAuthGateway</c> picks it when the federation reports the Tuwunel brand, in any casing.
/// </summary>
internal sealed class TuwunelAdminGateway(
    AuthenticatedHomeserverGeneric homeserver,
    string serverBrand = GatewayConstants.TuwunelBrand,
    string serverVersion = GatewayConstants.UnknownVersion) : SynapseAdminGateway(homeserver, serverBrand, serverVersion)
{
    /// <summary>
    /// Callers pass the dir of Synapse's documented order. Tuwunel sorts the keys Synapse sorts largest first
    /// ascending instead, so it needs the opposite dir for the same order (#541).
    /// </summary>
    public override Task<SynapseAdminRoomListResult?> GetRoomListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        if (RoomSortKeys.IsLargestFirst(orderBy))
        {
            direction = RoomSortKeys.Reverse(direction);
        }

        return base.GetRoomListAsync(offset, limit, orderBy, direction, searchTerm, cancellationToken);
    }

    /// <summary>
    /// Refused (#542). Tuwunel 1.8.1 selects media for <c>POST /_synapse/admin/v1/media/delete</c> by upload time
    /// instead of last access and ignores <c>keep_profiles</c>, so it would delete recently used media and avatars that
    /// Synapse keeps. No correction in the gateway can make the result match, so nothing is sent.
    /// </summary>
    public override Task<SynapseAdminDeleteMediaResponse?> DeleteLocalMediaAsync(long beforeTs, long sizeGt, bool keepProfiles, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    // Room deletion is refused with both of its status reads (#559). Tuwunel (1.8.1, 2.0.0) serves the three routes with
    // Synapse's status names, but it purges a room even when it couldn't evict local users, where Synapse refuses to,
    // and logs some failed purge steps without reporting them. Its "complete" can therefore report a deletion Synapse
    // would have failed, and its status has no error text. The status reads are refused too, so that a deletion started
    // in another tool isn't reported as done either.

    /// <summary>Refused (#559): Tuwunel's deletion can purge after failed evictions and hide failed purge steps.</summary>
    public override Task<SynapseAdminRoomDeleteResponse?> DeleteRoomAsync(string roomId, SynapseAdminRoomDeleteRequest request, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused (#559): Tuwunel's <c>complete</c> doesn't mean what Synapse's does.</summary>
    public override Task<SynapseAdminRoomDeleteStatusResponse?> GetRoomDeleteStatusAsync(string deleteId, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused (#559): Tuwunel's <c>complete</c> doesn't mean what Synapse's does.</summary>
    public override Task<SynapseAdminRoomDeleteStatusListResponse?> GetRoomDeleteStatusesAsync(string roomId, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    // Tuwunel 1.8.1 deletes a registration token as soon as it is invalid, the next time tokens are listed or checked:
    // when its uses reach the cap or its expiry passes. Synapse keeps such a token (a cap of 0 is its documented way to
    // invalidate one without deleting it) and rejects an expiry in the past. A create or update that would leave the
    // token invalid at once is therefore refused, so Tuwunel doesn't delete it (#548).

    /// <summary>Refuses a cap of 0 or an expiry in the past, which Tuwunel would answer by deleting the token (#548).</summary>
    public override Task<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken?> CreateRegistrationTokenAsync(SynapseAdminRegistrationTokenCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (request.UsesAllowed == 0 || IsInThePast(request.ExpiryTime))
        {
            throw new OperationNotSupportedByHomeserverException();
        }

        return base.CreateRegistrationTokenAsync(request, cancellationToken);
    }

    /// <summary>
    /// Refuses an expiry in the past, and a cap at or below the token's uses so far, which it reads first (Tuwunel
    /// consumes a use in one step, so <c>completed</c> is every use). Only that read is sent before a refusal (#548).
    /// </summary>
    public override async Task UpdateRegistrationTokenAsync(string token, SynapseAdminRegistrationTokenUpdateRequest request, CancellationToken cancellationToken = default)
    {
        if (IsInThePast(request.ExpiryTime))
        {
            throw new OperationNotSupportedByHomeserverException();
        }

        if (request.UsesAllowed is { } cap)
        {
            var current = await Homeserver.ClientHttpClient.GetFromJsonAsync<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>(
                $"/_synapse/admin/v1/registration_tokens/{token.UrlEncode()}", cancellationToken: cancellationToken);
            if (cap <= current.Completed)
            {
                throw new OperationNotSupportedByHomeserverException();
            }
        }

        await base.UpdateRegistrationTokenAsync(token, request, cancellationToken);
    }

    // ---- User list and details --------------------------------------------------------------------------

    /// <summary>
    /// Tuwunel ignores <c>order_by</c> and always sorts users by user ID, so any other key would return a different
    /// order than requested: only <c>name</c> is sent. Its <c>creation_ts</c> of 0 is reported as unknown.
    /// </summary>
    public override async Task<SynapseAdminUserListResult?> GetUserListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(orderBy, "name", StringComparison.Ordinal))
        {
            throw new OperationNotSupportedByHomeserverException();
        }

        var result = await base.GetUserListAsync(offset, limit, orderBy, direction, searchTerm, cancellationToken);
        foreach (var user in result?.Users ?? [])
        {
            ClearUnknownCreationTs(user);
        }

        return result;
    }

    public override async Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> GetUserDetailsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await base.GetUserDetailsAsync(userId, cancellationToken);
        if (user != null)
        {
            ClearUnknownCreationTs(user);
        }

        return user;
    }

    /// <summary>Tuwunel stores no creation time and sends 0 for it, which would show as 1970.</summary>
    private static void ClearUnknownCreationTs(SynapseAdminUserListResult.SynapseAdminUserListResultUser user)
    {
        if (user.CreationTs == 0)
        {
            user.CreationTs = null;
        }
    }

    // ---- Operations Tuwunel doesn't serve --------------------------------------------------------------
    // Tuwunel's status page (tuwunel-admin-api-2026-11-24.md) marks these endpoints as not implemented or not
    // applicable, and Tuwunel answers them with 404 M_UNRECOGNIZED. They are refused before anything is sent, so the
    // admin sees the "not supported" warning instead of an error.

    /// <summary>Refused: Tuwunel has no media quarantine.</summary>
    public override Task QuarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused: Tuwunel has no media quarantine.</summary>
    public override Task UnquarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused: Tuwunel has no media quarantine.</summary>
    public override Task QuarantineMediaByRoomIdAsync(string roomId, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused: Tuwunel has no media quarantine.</summary>
    public override Task QuarantineMediaByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused: Tuwunel has no protection from quarantine.</summary>
    public override Task ProtectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused: Tuwunel has no protection from quarantine.</summary>
    public override Task UnprotectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused: Tuwunel has no event report store.</summary>
    public override Task<SynapseAdminEventReportListResult?> GetEventReportListAsync(int offset, int limit, string direction, string? searchTerm = null, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused: Tuwunel has no event report store.</summary>
    public override Task DeleteEventReportAsync(string reportId, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused: Tuwunel has no account validity.</summary>
    public override Task<SynapseAdminAccountValidityResponse?> RenewAccountValidityAsync(SynapseAdminAccountValidityRequest request, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused: Tuwunel's feature flags are server-wide, not per user.</summary>
    public override Task<SynapseAdminExperimentalFeaturesResponse?> GetUserExperimentalFeaturesAsync(string userId, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused: Tuwunel's feature flags are server-wide, not per user.</summary>
    public override Task SetUserExperimentalFeaturesAsync(string userId, SynapseAdminExperimentalFeaturesRequest request, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused: Tuwunel's database (RocksDB) has no background updates.</summary>
    public override Task<SynapseAdminBackgroundUpdatesStatusResponse?> GetBackgroundUpdatesStatusAsync(CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused: Tuwunel's database (RocksDB) has no background updates.</summary>
    public override Task<SynapseAdminBackgroundUpdatesEnabledResponse?> SetBackgroundUpdatesEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused: Tuwunel's database (RocksDB) has no background updates.</summary>
    public override Task StartBackgroundUpdatesJobAsync(string jobName, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused: the room size estimate is PostgreSQL-only, even in Synapse.</summary>
    public override Task<RoomStatisticsResponse?> GetLargestRoomsAsync(CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>
    /// Refused. Tuwunel selects cached remote media for <c>POST /_synapse/admin/v1/purge_media_cache</c> by when the file
    /// was stored instead of when it was last accessed, so it would also remove recently used media that Synapse keeps.
    /// </summary>
    public override Task<SynapseAdminPurgeMediaCacheResponse?> PurgeRemoteMediaCacheAsync(long beforeTs, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    private static bool IsInThePast(long? unixMs) =>
        unixMs is { } ms && ms < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
