using System.Text.Json.Nodes;
using ArcaneLibs.Extensions;
using LibMatrix.Homeservers;
using LibMatrix.RoomTypes;
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
    /// <para>
    /// <paramref name="publicRooms"/> and <paramref name="emptyRooms"/> go out unchanged (#588). Tuwunel (1.8.2,
    /// 2.0.0) tests <c>empty_rooms</c> against the local joined members where Synapse tests all joined members, but
    /// Synapse sets <c>joined_members</c> to <c>0</c> when its last local user leaves a room, so both select the same
    /// rooms. Both filter by directory visibility for <c>public_rooms</c>, and both count and page after filtering.
    /// </para>
    /// </summary>
    public override Task<SynapseAdminRoomListResult?> GetRoomListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, bool? publicRooms = null, bool? emptyRooms = null, CancellationToken cancellationToken = default)
    {
        if (RoomSortKeys.IsLargestFirst(orderBy))
        {
            direction = RoomSortKeys.Reverse(direction);
        }

        return base.GetRoomListAsync(offset, limit, orderBy, direction, searchTerm, publicRooms, emptyRooms, cancellationToken);
    }

    /// <summary>
    /// Refused (#542). Tuwunel (1.8.1 to 2.0.0) selects media for <c>POST /_synapse/admin/v1/media/delete</c> by the
    /// file's modification time (about the upload) instead of last access, so it would delete recently used media that
    /// Synapse keeps. It honors <c>keep_profiles</c>, but no correction in the gateway can make the selection match, so
    /// nothing is sent.
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

    // Tuwunel (1.8.1 to 2.0.0) deletes a registration token as soon as it is invalid, the next time tokens are listed or
    // checked: when its uses reach the cap or its expiry passes. Synapse keeps such a token (a cap of 0 is its documented
    // way to invalidate one without deleting it) and rejects an expiry in the past. A create or update that would leave
    // the token invalid at once is therefore refused, so Tuwunel doesn't delete it (#548).

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

    /// <summary>
    /// Tuwunel (1.8.2, 2.0.0) differs from Synapse twice here (#584). When it logs devices out, it removes every device
    /// of the user, the requester's included, where Synapse keeps the session that sent the reset: resetting the
    /// signed-in admin's own password is refused, whatever <c>logout_devices</c> says. And its reset writes a password,
    /// which is how Tuwunel marks an account as active, so it reactivates a deactivated account that Synapse leaves
    /// deactivated: the account is read first, and the reset refused if it is deactivated.
    /// </summary>
    public override async Task ResetPasswordAsync(string userId, SynapseAdminResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (string.Equals(userId, UserId, StringComparison.Ordinal))
        {
            throw new OperationNotSupportedByHomeserverException();
        }

        if (await GetUserDetailsAsync(userId, cancellationToken) is { Deactivated: true })
        {
            throw new OperationNotSupportedByHomeserverException();
        }

        await base.ResetPasswordAsync(userId, request, cancellationToken);
    }

    /// <summary>Tuwunel stores no creation time and sends 0 for it, which would show as 1970.</summary>
    private static void ClearUnknownCreationTs(SynapseAdminUserListResult.SynapseAdminUserListResultUser user)
    {
        if (user.CreationTs == 0)
        {
            user.CreationTs = null;
        }
    }

    // Tuwunel 1.8.2 and 2.0.0 serve the redaction of a user's events with Synapse's inputs and status names, but take
    // the limit window in reverse timeline order (Synapse: newest received first) and redact through a different
    // permission path, so the same request can redact other events. Both the redaction and its status read are refused
    // (ADR 0006, #600).

    /// <summary>Refused (#600): Tuwunel can select other events than Synapse for the same request.</summary>
    public override Task<SynapseAdminUserRedactResponse?> RedactUserEventsAsync(string userId, SynapseAdminUserRedactRequest request, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>Refused (#600), with the redaction: its <c>complete</c> doesn't describe Synapse's selection.</summary>
    public override Task<SynapseAdminUserRedactStatusResponse?> GetUserRedactionStatusAsync(string redactId, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    // Tuwunel 1.8.2 and 2.0.0 serve both room recovery endpoints, with differences (#587, SPEC.md §10). Make room
    // admin invites before it changes the power levels and skips the invite for a room listed in the public room
    // directory, even an invite-only one; Synapse invites, as the local user it grants the power through, unless the join
    // rule is public. The gateway can't send that invite, so the operation is refused. The admin join is corrected:
    // Tuwunel 1.8.2 never invites first and 2.0.0 only when the user couldn't join otherwise, so the gateway sends
    // Synapse's invite itself before the join. Synapse deletes a room's current state when this server leaves it, so it
    // finds no join rule and doesn't invite there (#606); Tuwunel keeps the state, so the gateway first checks for a joined
    // local user.

    private const string JoinRulesEventType = "m.room.join_rules";
    private const string PublicJoinRule = "public";

    /// <summary>
    /// Refused (#587): in an invite-only room listed in the public room directory, Tuwunel grants the power without the
    /// invite Synapse sends, and the gateway can't send it as the local user Synapse uses.
    /// </summary>
    public override Task MakeRoomAdminAsync(string roomId, string? userId, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

    /// <summary>
    /// Does what Synapse does before the join (#587, #606):
    /// <list type="number">
    /// <item>An alias is resolved through the Client-Server directory, as Synapse resolves it.</item>
    /// <item>Only in a room where this server has a joined local user does Synapse still have a join rule: for any other
    /// room, or one this server doesn't know (<c>404</c>), no invite is sent.</item>
    /// <item>Unless the room's join rule is <c>public</c> or the room has none, the user is invited as the signed-in admin
    /// through the Client-Server API.</item>
    /// <item>The admin join is sent with the alias or room ID and the servers as given; Tuwunel 2.0.0 sees the invite and
    /// doesn't invite again.</item>
    /// </list>
    /// A failed read or invite propagates, so nothing more is sent.
    /// </summary>
    public override async Task<SynapseAdminJoinRoomResponse?> JoinUserToRoomAsync(string roomIdOrAlias, string userId, IReadOnlyList<string>? serverNames = null, CancellationToken cancellationToken = default)
    {
        var roomId = roomIdOrAlias.StartsWith('#') ? await ResolveAliasAsync(roomIdOrAlias, cancellationToken) : roomIdOrAlias;
        if (await HasJoinedLocalMemberAsync(roomId, cancellationToken) && await NeedsInviteBeforeJoinAsync(roomId, cancellationToken))
        {
            var url = $"/_matrix/client/v3/rooms/{roomId.UrlEncode()}/invite";
            using var resp = await Homeserver.ClientHttpClient.PostAsJsonAsync(url, new MatrixRoomInviteRequest { UserId = userId }, cancellationToken: cancellationToken);
            resp.EnsureSuccessStatusCode();
        }

        return await base.JoinUserToRoomAsync(roomIdOrAlias, userId, serverNames, cancellationToken);
    }

    /// <summary>The room an alias names, from <c>GET /_matrix/client/v3/directory/room/{alias}</c> (#606).</summary>
    private async Task<string> ResolveAliasAsync(string alias, CancellationToken cancellationToken)
    {
        var resolved = await Homeserver.ClientHttpClient.GetFromJsonAsync<RoomIdResponse>(RoomAliasUrl(alias), cancellationToken: cancellationToken);
        return string.IsNullOrEmpty(resolved?.RoomId)
            ? throw new InvalidDataException("The room directory answered without a room ID.")
            : resolved.RoomId;
    }

    /// <summary>
    /// Whether this server has a joined local user in the room, as Synapse keeps a room's current state only then (#606).
    /// A room Tuwunel doesn't know answers <c>404 M_NOT_FOUND</c>, which means no.
    /// </summary>
    private async Task<bool> HasJoinedLocalMemberAsync(string roomId, CancellationToken cancellationToken)
    {
        try
        {
            return await GetRoomDetailsAsync(roomId, cancellationToken) is { JoinedLocalMembers: > 0 };
        }
        catch (LibMatrix.MatrixException ex) when (ex.ErrorCode == "M_NOT_FOUND")
        {
            return false;
        }
    }

    /// <summary>
    /// Synapse's rule: invite unless the current <c>m.room.join_rules</c> says <c>public</c>; no such event, no invite.
    /// The events are filtered here, so the result doesn't depend on Tuwunel honouring the <c>type</c> parameter.
    /// </summary>
    private async Task<bool> NeedsInviteBeforeJoinAsync(string roomId, CancellationToken cancellationToken)
    {
        var state = await GetRoomStateAsync(roomId, JoinRulesEventType, cancellationToken);
        var joinRules = state?.Events.FirstOrDefault(e =>
            e.Type == JoinRulesEventType && string.IsNullOrEmpty(e.StateKey));
        if (joinRules == null) return false;

        var joinRule = joinRules.RawContent?["join_rule"] is JsonValue value && value.TryGetValue<string>(out var rule) ? rule : null;
        return !string.Equals(joinRule, PublicJoinRule, StringComparison.Ordinal);
    }

    // ---- Operations Tuwunel doesn't serve --------------------------------------------------------------
    // Tuwunel's status page (tuwunel-admin-api-2026-11-24.md) marks these endpoints as not implemented or not
    // applicable, and Tuwunel answers them with 404 M_UNRECOGNIZED. They are refused before anything is sent, so the
    // admin sees the "not supported" warning instead of an error.

    /// <summary>Refused: Tuwunel has no shadow-ban, so there is none to set or lift (#584).</summary>
    public override Task SetUserShadowBannedAsync(string userId, bool shadowBanned, CancellationToken cancellationToken = default) =>
        throw new OperationNotSupportedByHomeserverException();

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
