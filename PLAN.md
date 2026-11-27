# Implementation Plan: Join by Alias or Through Remote Servers (#606)

| Field | Value |
|---|---|
| **Status** | Approved |
| **Spec** | [SPEC.md](./SPEC.md) FR-4, FR-5, AC-28, §10 (joining a local user, joining by alias or through servers), §15 |
| **Issue / Branch** | #606 (follow-up to #587) / `ai/feature/issue-606-join-by-alias-or-servers` |
| **Author(s)** | Benjamin Aicheler, Claude |
| **Created** | 2026-11-27 |
| **Last updated** | 2026-11-27 |

## 1. Summary of Approach

The admin join gets two inputs that Synapse's handler reads: an alias in the path, on the user page only, and `server_name` query parameters, on both join dialogs. `IMatrixGateway.JoinUserToRoomAsync` takes a room ID or alias and an optional server list. It encodes an alias with `Uri.EscapeDataString` and each server as a query value. `RoomService` checks the forms with new `MatrixIdHelper.IsRoomAlias` and `IsServerName`, and refuses servers given with an alias, because both servers replace them with the alias's. `TuwunelAdminGateway`'s invite-first correction gets two steps before the join rule read. It resolves an alias first. Then, for a room without a joined local user or unknown to the server, it sends no invite: Synapse deletes the current state when this server leaves a room, while Tuwunel keeps it. The two dialogs get a "Servers to join through" field, and the user page's room field also takes an alias.

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `IMatrixGateway` / `MatrixGatewayBase` / `SynapseAdminGateway.JoinUserToRoomAsync(roomId, userId, ct)` | `POST /_synapse/admin/v1/join/{roomId.UrlEncode()}`, body `{"user_id"}`. | `(roomIdOrAlias, userId, serverNames, ct)`: alias → `Uri.EscapeDataString`, room ID → `.UrlEncode()` (unchanged wire); `?server_name=a&server_name=b`, each `.UrlEncode()`, only when given. |
| `GenericMatrixGateway` | Refuses. | Signature only. |
| `TuwunelAdminGateway.JoinUserToRoomAsync` | Reads the join rule (admin state), invites unless `public` or missing, then joins. | Resolve an alias (Client-Server directory) to a room ID. Read room details: `404` (`M_NOT_FOUND`) or `joined_local_members == 0` → no invite. Then the join rule read and invite as before. Then the join with the original alias or room ID and the servers. |
| `MatrixIdHelper` | `IsRoomId`, `IsUserId`, `IsLocalUserId`, `IsOnServer`. | Add `IsRoomAlias` (`#`, a non-empty localpart that may contain spaces, `:`, a server name) and `IsServerName` (DNS name, IPv4 or `[IPv6]`, optional port, Matrix spec grammar). |
| `IRoomService` / `RoomService.JoinUserToRoomAsync(roomId, userId, ct)` | Trims and checks the room ID (`IsRoomId`) and the user. | `(roomIdOrAlias, userId, serverNames = null, ct)`. Room: `IsRoomId` or `IsRoomAlias`, else `InvalidRoomId` (text extended to aliases). Servers trimmed and de-duplicated in order; each `IsServerName`, else `InvalidServerName`; with an alias → `JoinServersNeedRoomId`; all warnings, nothing sent. |
| `UserJoinRoomDialog` | Room ID field, picks, joined/banned checks; returns the room ID (`string`). | Room field takes an alias. New servers field, disabled for an alias. Joined/banned checks for room IDs only. Returns `RoomJoinRequestViewModel` (room, servers). |
| `RoomUserActionDialog` (Join mode) | User ID field; returns the user ID (`string`). | Servers field in Join mode; returns `RoomJoinRequestViewModel` there (user ID in the model). Make admin unchanged (returns `string`). |
| `RoomDetails` / `UserDetails` | Pass the dialog's string to the service. | Read the model and pass the servers. |
| Wire tests (#587, #605) | Tuwunel join: state read, invite, join. | Add the details read first; new tests for alias encoding, `server_name`, alias resolution, rooms without local members and `404`. |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: Send `server_name` although the reference doesn't document it | Rooms this server left can be rejoined, which the reference's undo-deletion guide needs; Synapse and Tuwunel both read it | Undocumented parameter | ✅ chosen, recorded in SPEC §15 |
| B: Leave remote joins out | Stays within the documented parameters | Closes #606 without the case it was opened for | ❌ |
| C: Servers on the user page only | Smaller | Synapse's room list keeps rooms this server left, and those are where servers are needed | ❌: both dialogs (SPEC §15) |
| D: Send servers with an alias and let the server ignore them | Simpler service | The admin's list would silently have no effect | ❌: refused; the field is disabled for an alias |
| E: Tuwunel: decide "server in room" from the state read (empty or not) | One read | Tuwunel keeps state after leaving, so a left room still has a join rule | ❌: `joined_local_members` from room details |
| F: Tuwunel: decide by the room details' `join_rules` | One read for both | Tuwunel reports `invite` for a room without a join rule, where Synapse doesn't invite | ❌: keep the state read for the rule |
| G: Tuwunel: pass the resolved room ID instead of the alias to the join | One resolution | The join's servers would then be the admin's (none), not the alias's | ❌: send the alias, Tuwunel resolves it again |
| H: Resolve aliases in the dialog to run the joined/banned checks | Checks for aliases too | One more request per keystroke or on submit; the homeserver refuses those cases anyway | ❌: checks for room IDs only |

## 4. Technical Design

### Components affected

- **Gateway:**
  - `Task<SynapseAdminJoinRoomResponse?> JoinUserToRoomAsync(string roomIdOrAlias, string userId, IReadOnlyList<string>? serverNames = null, CancellationToken cancellationToken = default)`.
  - Path: `roomIdOrAlias.StartsWith('#') ? Uri.EscapeDataString(roomIdOrAlias) : roomIdOrAlias.UrlEncode()`.
  - Query: for each server, `server_name={server.UrlEncode()}`, joined with `&`, only for a non-empty list.
  - The doc comment notes the undocumented `server_name` (SPEC §10) and that a server replaces the servers with the alias's.
- **Tuwunel (`JoinUserToRoomAsync` override):**
  1. Room ID: if the identifier is an alias, `GET /_matrix/client/v3/directory/room/{Uri.EscapeDataString(alias)}` gives `room_id`. A private helper in the Tuwunel gateway with a small response DTO. Errors propagate, so nothing is written for an unknown alias.
  2. `GetRoomDetailsAsync(roomId)`: a `MatrixException` with `M_NOT_FOUND`, or `JoinedLocalMembers == 0`, means no invite.
  3. Otherwise `NeedsInviteBeforeJoinAsync(roomId)` and the Client-Server invite, as today.
  4. `base.JoinUserToRoomAsync(roomIdOrAlias, userId, serverNames, ct)`.
- **Helpers (`MatrixIdHelper`):**
  - `IsRoomAlias(id)`: `#`, split at the first `:`, a non-empty localpart (whitespace allowed inside, but not at the ends after trimming), and an `IsServerName` server part.
  - `IsServerName(name)`: `^(\[[0-9A-Fa-f:.]+\]|[A-Za-z0-9.-]+)(:[0-9]{1,5})?$`, at most 255 characters (Matrix spec "Server name"). Kept in a `static readonly Regex` field.
- **Service (`RoomService.JoinUserToRoomAsync`):**
  - The checks run in this order: room (`IsRoomId || IsRoomAlias`, else `InvalidRoomId`), servers (`IsServerName` each, else `InvalidServerName` naming the first invalid one; an alias with servers → `JoinServersNeedRoomId`), then the user checks as now.
  - The servers are trimmed, empty ones dropped and duplicates removed in order (ordinal); `null` or empty means none.
  - The success message names the room ID from the answer (`room_id`) when it has one, else the given room or alias, so a join by alias shows the room it reached.
  - Log templates: the error and success logs gain `{ServerCount}`. The server names themselves aren't logged; they aren't secret, but the logs stay as they are otherwise.
- **ViewModel:** `RoomJoinRequestViewModel { string Target; IReadOnlyList<string> Servers }`, where `Target` is the room (user page) or the user (room page). The name stays neutral, because both dialogs return it. The dialogs split the servers field on commas and whitespace.
- **Dialogs:**
  - `UserJoinRoomDialog`:
    - **Room field:** label `RoomIdOrAlias`, hint updated.
    - **Servers field:** `JoinServers`, hint `JoinServersHint`. Disabled, with `JoinServersAliasNote`, while the room field holds an alias.
    - **Validation:** `InvalidServerName` with the first bad server. The joined and banned checks only when the room field is a room ID. The picks stay as they are.
    - **Result:** `RoomJoinRequestViewModel`.
  - `RoomUserActionDialog`: in Join mode, the same servers field (always enabled) and validation; the result is a `RoomJoinRequestViewModel` with `Target` = the user. Make admin keeps returning `string`.
- **Pages:**
  - `RoomDetails.OpenRoomUserActionAsync`: Join reads the model and calls `JoinUserToRoomAsync(target, model.Target, model.Servers, ct)`. Make admin is unchanged.
  - `UserDetails.OpenJoinRoomDialog`: reads the model and calls `JoinUserToRoomAsync(model.Target, target, model.Servers, ct)`.
- **Resources (EN/DE/FR):**
  - New: `RoomIdOrAlias`, `JoinServers`, `JoinServersHint`, `JoinServersAliasNote`, `InvalidServerName` (`{0}`), `JoinServersNeedRoomId`.
  - Changed: `InvalidRoomId` (mentions aliases), `JoinToRoomRoomIdHint` (room ID or alias), `JoinToRoomDescription` (keeps the reference's conditions).

### Data model changes

None.

### API / interface changes

`IMatrixGateway.JoinUserToRoomAsync` and `IRoomService.JoinUserToRoomAsync` gain the optional `serverNames` parameter; the first parameter is renamed `roomIdOrAlias`. New `RoomJoinRequestViewModel`. Both dialogs' Join result type changes from `string` to the model. Test fakes and the `RoomDetailsRecoveryTests` and `UserDetailsJoinRoomTests` dialog stubs follow.

### Configuration / feature flags

- None.

## 5. Implementation Steps

### Phase 1: Helpers and gateway

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | `IsRoomAlias`, `IsServerName` | `MatrixIdHelper.cs` | `LocalMatrixIdTests`: aliases (spaces inside, ports, missing parts); server names (DNS, IPv4, `[::1]:8448`, ports, `a/b`, `a b`, `a,b`, empty, over 255 characters) |
| 1.2 | Gateway signature, alias encoding, `server_name`; fakes | `IMatrixGateway`, `MatrixGatewayBase`, `SynapseAdminGateway`, `GenericMatrixGateway`, test fakes | Wire tests: an alias with a space → `/_synapse/admin/v1/join/%23my%20room%3Aexample.com`; a room ID path unchanged; `?server_name=a.org&server_name=b.org%3A8448`; no query without servers; same body |
| 1.3 | Tuwunel: alias resolution, local member check, then the existing invite logic | `TuwunelAdminGateway.cs` | Wire tests: the order (directory → details → state → invite → join); `joined_local_members` 0 → details, join (no state read, no invite); details `404` → join without invite; unknown alias → only the directory request; servers passed to the join; the #587 cases updated with the details read |

### Phase 2: Service

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | Signature, room/alias and server checks, de-duplication, log templates | `IRoomService.cs`, `RoomService.cs` | Characterization row updated (servers forwarded, log arguments); tests: alias accepted; alias with servers → `JoinServersNeedRoomId`; invalid server → `InvalidServerName` naming it; duplicates and blanks dropped in order; order of checks (room, servers, user); nothing sent on any refusal |

### Phase 3: Dialogs and pages

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | Resources EN/DE/FR | `Resources/` | Build; XML parses |
| 3.2 | `RoomJoinRequestViewModel`; `UserJoinRoomDialog` alias and servers; `RoomUserActionDialog` Join servers | `Models/ViewModels/`, `Components/Pages/` | Dialog tests: alias accepted (with a space); servers field disabled for an alias, with the note; servers parsed (commas, spaces, blanks); invalid server named; joined/banned checks skipped for an alias; results as models; Make admin result unchanged |
| 3.3 | Pages pass the servers | `RoomDetails.razor.cs`, `UserDetails.razor.cs` | Page tests updated: the service gets the servers; existing flows unchanged |

### Phase 4: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | Remove `[Planned]`; AGENTS.md (Room Recovery pitfall: `server_name`, alias encoding and resolution, the Tuwunel local member check; Shared Helpers: the two helpers); README; reset PLAN.md / TODO.md | docs | Review |

Each phase is one commit; one PR that resolves #606.

## 6. Testing Strategy

- **Unit & bUnit component tests:** as in the steps, culture-independent; mutation checks on the new checks and on the Tuwunel order.
- **Integration tests:** none needed.
- **Manual / homeserver tests:** on a Synapse test server: rejoin a user to a public room this server left, through a remote server; join by a local and by a remote alias; an invite-only room this server left fails with the homeserver's error.
- **Test data:** made-up IDs and server names.
- **Mapping to acceptance criteria:** AC-28 → Phases 1–3.

## 7. Rollout & Rollback

- **Deployment approach:** next release; no flag.
- **Migration steps:** none.
- **Rollback plan:** revert the PR.
- **Monitoring after release:** the join logs, now with the server count.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| A future Synapse drops or renames the undocumented `server_name`. | It is in the handler since the API's start and in `ruma`'s `synapse_admin_api`; SPEC §10 records the dependency; without it the join simply fails as today. |
| The Tuwunel check for local members races with a local user joining or leaving. | The same window exists on Synapse between its state read and the join; the worst case is the homeserver's error. |
| An alias resolves to a room the admin didn't mean. | The result names the room ID the homeserver joined (`UserJoinedRoom` with `room_id` from the answer). Phase 2 makes the message use the answer's `room_id` when present. |

## 9. Open Questions

- [x] Sending the undocumented `server_name` (alternative A): confirmed 2026-11-27.
- [x] The servers field on the room page too (alternative C): confirmed 2026-11-27.

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-27 | Source check: Synapse v1.165.0 `storage/databases/main/events.py` (current state deleted when the server leaves a room), `http/servlet.py` `ResolveRoomIdMixin`, `handlers/room_member.py` `lookup_room_alias` (alias servers replace `server_name`, alias server first), `types` `DomainSpecificString` (split at the first `:`); Tuwunel v1.8.2 and v2.0.0 `rooms/alias` `maybe_resolve_with_servers` (servers only for a room ID), admin `rooms/state.rs` (no membership check), `rooms/room_details.rs` and `rooms/mod.rs` (`404` for an unknown room, `joined_local_members`, `join_rules` defaulting to `invite`). |
