# Implementation Plan: Room Recovery — Make Room Admin, Join a Local User (#587)

| Field | Value |
|---|---|
| **Status** | Approved |
| **Spec** | [SPEC.md](./SPEC.md) FR-5, AC-26, §4, §8 (dialogs), §10 (making a room admin, joining a local user, room recovery on Tuwunel), §13, §15 |
| **Issue / Branch** | #587 / `ai/feature/issue-587-room-recovery` |
| **Author(s)** | Benjamin Aicheler, Claude |
| **Created** | 2026-11-27 |
| **Last updated** | 2026-11-27 |

## 1. Summary of Approach

Two gateway calls, our own (`LibMatrix` has neither): `POST /_synapse/admin/v1/rooms/{roomId}/make_room_admin` with an optional `user_id` (answer `{}`), and `POST /_synapse/admin/v1/join/{roomId}` with `user_id` (answer `{"room_id": …}`). `GenericMatrixGateway` refuses both. `TuwunelAdminGateway` refuses Make room admin and corrects the join: it reads the join rule and, unless it is `public` or missing, invites the user through the Client-Server API before the admin join, which is Synapse's order (SPEC §10). `RoomService` gets two `OperationResult` methods through `RunGatewayCallAsync` that check the user ID first. Join accepts only an exact local server name, through a new `MatrixIdHelper` method. The room page gets "Make room admin" and "Join user" in its Actions card. Both open one shared `RoomUserActionDialog`, which names the room and is the confirmation. The page then re-checks the room, sends the request and reloads the room after a success.

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `IMatrixGateway`, `MatrixGatewayBase` | No membership or power level writes. | Add `MakeRoomAdminAsync` and `JoinUserToRoomAsync` (abstract in the base). |
| `SynapseAdminGateway` | `PostEmptyAsync` (private, sends `{}`); `PostAsJsonAsync` drops `null` properties; room paths use `roomId.UrlEncode()`. | Both POSTs; a request DTO whose `null` `user_id` is dropped gives `{}` for the default target. |
| `TuwunelAdminGateway` | Overrides deviating methods; requests only it needs stay there (#548). | Refuse `MakeRoomAdminAsync`; override `JoinUserToRoomAsync` with the join rule read and the Client-Server invite (private helper). |
| `GenericMatrixGateway` | Refuses every admin method; `GenericMatrixGatewayTests` checks this by reflection. | Refuse both. |
| `GetRoomStateAsync` | `GET /_synapse/admin/v1/rooms/{roomId}/state[?type=…]`, served by Tuwunel. | Reused by the Tuwunel override; it filters by type and empty state key itself, so it doesn't depend on Tuwunel honouring `type`. |
| `MatrixIdHelper.IsOnServer` | Case-insensitive suffix check, no syntax check. | Unchanged (other callers rely on it); add `IsUserId` and `IsLocalUserId` (exact server name). |
| `RoomService`, `IRoomService` | Room actions through `RunGatewayCallAsync`; `BlockRoomAsync` as the pattern. | Add `MakeRoomAdminAsync`, `JoinUserToRoomAsync`. |
| `RoomDetails.razor(.cs)` | Actions card with `IsRoomOperationRunning`; `GetDisplayedTarget()` / `IsStillDisplayed(target)`; `ReloadRoomDetails()` reloads details, members and state events together; `room.Members` lists joined members (`/members`). | Two buttons, dialog, re-check, result, reload on success. |
| `DetailPageTestBase` | Stubs `IRoomService` and `IDialogService`. | No change expected; the new methods are on the existing stub. |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: One `RoomUserActionDialog` with an `Action` parameter (make admin / join) | One user ID field, one validation path, one test class | Texts switch on the action | ✅ chosen |
| B: Two dialogs | Each dialog simpler | Two near-identical forms and test classes | ❌ |
| C: Dialog, then a separate confirmation (as #600) | Matches the redaction flow | A second click with nothing new to read; neither operation deletes anything | ❌ (SPEC §15): the dialog names the room and is the confirmation, the page re-checks afterwards |
| D: Tuwunel: refuse the join too | Simplest | 1.8.2 fails in every non-public room only because it skips the invite, which the gateway can send in Synapse's order | ❌ |
| E: Tuwunel: send the join as is and record the difference | No extra requests | 1.8.2 fails where Synapse succeeds; 2.0.0 joins restricted rooms without the admin being there | ❌: not "does what Synapse does" |
| F: Tuwunel: refuse Make room admin only for a listed invite-only room (conditional, as #548) | Works in most rooms | Needs a directory read, the join rule and the target's membership before every call, and still differs in the granting user and in the order on failure | ❌ for now; possible follow-up |
| G: Make room admin: fill in the signed-in user ID instead of sending `{}` | One request shape | Synapse's documented default is the caller; `{}` is what the reference shows | ❌: send `{}` |
| H: Check "already joined" in the service | Covers every caller | The service doesn't have the member list; reading it costs a request | ❌: the dialog checks the page's members; the server still refuses a stale case |

## 4. Technical Design

### Components affected

- **DTOs:**
  - `Models/Requests/SynapseAdminMakeRoomAdminRequest`: `user_id` (`string?`, dropped when `null` by `PostAsJsonAsync`, so the body is `{}`).
  - `Models/Requests/SynapseAdminJoinRoomRequest`: `user_id` (`string`).
  - `Models/Responses/SynapseAdminJoinRoomResponse`: `room_id`.
  - Tuwunel invite body: `user_id`; a private record in `TuwunelAdminGateway`, or a small request DTO if the serializer needs it.
- **Gateway:**
  - `Task MakeRoomAdminAsync(string roomId, string? userId, CancellationToken)`: sends `POST /_synapse/admin/v1/rooms/{roomId.UrlEncode()}/make_room_admin` through `ClientHttpClient.PostAsJsonAsync` with the token, then `EnsureSuccessStatusCode`. The answer isn't read.
  - `Task<SynapseAdminJoinRoomResponse?> JoinUserToRoomAsync(string roomId, string userId, CancellationToken)`: sends `POST /_synapse/admin/v1/join/{roomId.UrlEncode()}`, then `EnsureSuccessStatusCode` and `ReadFromJsonAsync`.
  - The doc comments say that the path takes a room ID, and that an alias would need `Uri.EscapeDataString` (AGENTS.md, #580).
  - `GenericMatrixGateway`: both throw `OperationNotSupportedByHomeserverException`.
  - `TuwunelAdminGateway`:
    - `MakeRoomAdminAsync` throws `OperationNotSupportedByHomeserverException` before sending. The doc comment gives the reasons from SPEC §10.
    - `JoinUserToRoomAsync`:
      1. `GetRoomStateAsync(roomId, "m.room.join_rules")`, then takes the event with type `m.room.join_rules` and an empty state key.
      2. If it exists and its `join_rule` isn't `public` (ordinal), sends `POST /_matrix/client/v3/rooms/{roomId.UrlEncode()}/invite` with exactly `{"user_id": …}`.
      3. Then calls `base.JoinUserToRoomAsync`.
      - A failed read or invite propagates, so nothing more is sent. No retry and no fallback.
- **Helper (`MatrixIdHelper`):**
  - `IsUserId(string? id)`: starts with `@` and has a non-empty localpart, `:` and a non-empty server part. The split is at the first `:`, because a server name may carry a port.
  - `IsLocalUserId(string? id, string? serverName)`: `IsUserId` and the server part equals `serverName` ordinally. Returns false for an empty server name.
  - The doc comments say why `IsOnServer` isn't used here: Synapse's `is_mine` and Tuwunel's `user_is_local` compare exactly.
- **Service (`RoomService`, `IRoomService`):**
  - `Task<OperationResult> MakeRoomAdminAsync(string roomId, string? userId, CancellationToken)`:
    - A blank user ID becomes `null` (the signed-in admin); otherwise it is trimmed.
    - At the start of `call`, a non-null ID that isn't `IsUserId` fails with `InvalidUserId` (`Severity.Warning`) and sends nothing.
    - Success: `RoomAdminGranted` with the user (the gateway's `UserId` for `null`) and the room.
    - Failure key `ErrorMakingRoomAdmin`. An information log names the room and the target.
  - `Task<OperationResult> JoinUserToRoomAsync(string roomId, string userId, CancellationToken)`:
    - Trims the ID. At the start of `call`, an ID that isn't `IsUserId` fails with `InvalidUserId`, and one that isn't `IsLocalUserId(id, gateway.ServerName)` with `JoinUserNotLocal` (both warnings), sending nothing.
    - A `null` response is a failure with the failure key (operations, AGENTS.md "Null Gateway Results").
    - Success: `UserJoinedRoom` with the user and the room.
    - Failure key `ErrorJoiningUserToRoom`. An information log names the room and the user.
- **Dialog (`Components/Pages/RoomUserActionDialog.razor(.cs)`):**
  - **Parameters:** `Action` (`RoomUserAction.MakeAdmin` / `Join`, enum in `Models/ViewModels`), `RoomLabel`, `SignedInUserId`, `ServerName`, `JoinedMemberIds` (`IReadOnlyCollection<string>`).
  - **Texts:** title by action. The description names the room and uses the reference's wording (`MakeRoomAdminDescription`, `JoinUserDescription`). Make admin adds `MakeRoomAdminPrecondition`.
  - **User ID field:** `Immediate`, with a hint: `MakeRoomAdminUserHint` ("empty = you, {0}") or `JoinUserHint` ("a user on {0}").
  - **Validation:**
    - Make admin: empty, or `IsUserId`.
    - Join: `IsUserId`, then `IsLocalUserId` with `ServerName`, then not in `JoinedMemberIds` (ordinal).
    - The field shows `InvalidUserId`, `JoinUserNotLocal` or `UserAlreadyInRoom`.
  - **Submit:** the button (`MakeRoomAdmin` / `JoinUser`) is disabled while the input is invalid. It returns the trimmed ID (`""` for the signed-in admin) and sends nothing itself.
- **Page (`RoomDetails`):**
  - **Buttons:** "Make room admin" and "Join user" (`Color.Primary`, `Disabled="IsRoomOperationRunning"`) after Purge History.
  - **Flow (shared method):**
    1. `GetDisplayedTarget()` gives `target`.
    2. Open the dialog with `FormatTarget(room, target)`, `MatrixSession.Gateway?.UserId`, the gateway's `ServerName` and the joined member IDs, using `DialogPresets.Small()`.
    3. If the dialog is cancelled, stop. Otherwise check `IsStillDisplayed(target)`.
    4. Call the service with `_cts.Token`, then `Snackbar.ShowResult(result)`.
    5. On success, if `GetDisplayedTarget() == target`, `ReloadRoomDetails()`.
  - **Session data:** the page already injects `IMatrixSessionService` (`MatrixSession`); it reads the signed-in user ID and server name from `MatrixSession.Gateway` only for display and validation, and the service checks again.
- **Resources (EN/DE/FR):**
  - Buttons and titles: `MakeRoomAdmin`, `MakeRoomAdminTitle`, `JoinUser`, `JoinUserTitle`.
  - Dialog texts: `MakeRoomAdminDescription`, `MakeRoomAdminPrecondition`, `MakeRoomAdminUserHint`, `JoinUserDescription`, `JoinUserHint`.
  - Validation: `InvalidUserId`, `JoinUserNotLocal`, `UserAlreadyInRoom`.
  - Results: `RoomAdminGranted`, `UserJoinedRoom`, `ErrorMakingRoomAdmin`, `ErrorJoiningUserToRoom`.
  - Reuse the existing `UserId`, `Cancel` and `SelectionChangedActionAborted`.

### Data model changes

None.

### API / interface changes

`IMatrixGateway.MakeRoomAdminAsync`, `IMatrixGateway.JoinUserToRoomAsync`, `IRoomService.MakeRoomAdminAsync`, `IRoomService.JoinUserToRoomAsync`, and two `MatrixIdHelper` methods. No breaking change. Test fakes that implement `IMatrixGateway` or derive from `MatrixGatewayBase` get the two methods.

### Configuration / feature flags

- None.

## 5. Implementation Steps

### Phase 1: Gateway

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | DTOs; both methods on the interface, base, Synapse and Generic gateways; Tuwunel refusal of Make room admin; test fakes | `Models/`, `Infrastructure/Gateways/`, `Interfaces/Gateways/IMatrixGateway.cs`, fakes in tests | Wire tests in `SynapseAdminGatewayCharacterizationTests`: `POST /_synapse/admin/v1/rooms/%21room%3aexample.org/make_room_admin` with exactly `{}` and with exactly `{"user_id":"@bob:example.org"}`; `{}` answer accepted; join `POST /_synapse/admin/v1/join/%21room%3aexample.org` with exactly `{"user_id":…}`, `room_id` read; `MatrixException` and `HttpRequestException` propagate; cancellation reaches the request. Tuwunel Make room admin refused before sending. `GenericMatrixGatewayTests` covers both. |
| 1.2 | Tuwunel join correction | `TuwunelAdminGateway.cs` | Wire tests: `public` → no invite, only the admin join; `invite`, `knock`, `restricted` → state read, `POST /_matrix/client/v3/rooms/%21room%3aexample.org/invite` with exactly `{"user_id":…}`, then the join, in this order; no join rule event → no invite; state answer with other events only → no invite; failed read → nothing written; failed invite → no join. |

### Phase 2: Helper and service

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | `IsUserId`, `IsLocalUserId` | `Infrastructure/Helpers/MatrixIdHelper.cs` | `LocalMatrixIdTests`: well-formed, missing `@`, empty localpart or server, a port in the server name, exact vs mixed-case server, another server, empty server name |
| 2.2 | `MakeRoomAdminAsync`, `JoinUserToRoomAsync` | `Interfaces/IRoomService.cs`, `Services/RoomService.cs` | `RoomServiceCharacterizationTests` rows (no session, cancel, throw, refuse) for both. Specific tests: blank target → `null` sent, trimmed ID sent, malformed ID → warning and no call; join: malformed, other server, mixed-case server → warning and no call; `null` response → failure; success messages name user and room (`FormattingKeyLocalizer`); refusal → "not supported" warning |

### Phase 3: Dialog

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | Resources EN/DE/FR | `Resources/` | Build; keys present in all three files |
| 3.2 | `RoomUserAction` enum, `RoomUserActionDialog` | `Models/ViewModels/`, `Components/Pages/` | `RoomUserActionDialogTests` (real `DialogService`, `MudDialogProvider`): title and description per action, room named; make admin: empty submits `""`, malformed disables submit with `InvalidUserId`; join: empty disabled, other server / mixed case → `JoinUserNotLocal`, a joined member → `UserAlreadyInRoom`, valid → returns trimmed ID; cancel returns cancelled |

### Phase 4: Room page

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | Buttons, flow, reload | `Components/Pages/RoomDetails.razor(.cs)` | `RoomDetailsRecoveryTests`: buttons present and disabled while a purge or deletion runs; dialog parameters (room label, signed-in user, server name, joined members, `DialogPresets.Small()`); dialog cancelled → no call; room changed during the dialog → `SelectionChangedActionAborted`, no call; success → snackbar and room reloaded once; failure and "not supported" → snackbar, no reload; cancelled result → no snackbar; success after the room changed → no reload of the other room |

### Phase 5: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 5.1 | Remove `[Planned]` in SPEC.md; AGENTS.md Known Pitfalls (make room admin needs a joined local power holder and answers `{}`; the join's invite rule; Tuwunel's join correction and refusal in "What Tuwunel Refuses"; `IsLocalUserId` vs `IsOnServer` in Shared Helpers); README feature list; ARCHITECTURE.md only if a flow changed; reset PLAN.md and TODO.md | `SPEC.md`, `AGENTS.md`, `README.md`, `PLAN.md`, `TODO.md` | Review |

Each phase is one commit; the whole feature is one PR, unless the diff grows past a reviewable size, in which case Phases 1–2 and 3–5 become two stacked PRs.

## 6. Testing Strategy

- **Unit & bUnit component tests:** wire tests on a recording `HttpMessageHandler` (exact method, encoded path, exact body, request order for Tuwunel); service tests with `KeyLocalizer` / `FormattingKeyLocalizer`, `RecordingLogger<T>` and `ResultAssert`; bUnit tests on `DetailPageTestBase`, culture-independent, with expected strings from `IStringLocalizer<SharedResources>`; awaited handlers that open a dialog bounded with `.WaitAsync(TimeSpan.FromSeconds(10))`.
- **Integration tests:** none needed (no controller or middleware change).
- **Manual / homeserver tests:** on a Synapse test server: make room admin for self and for another user in an invite-only room where a local admin is still joined (power level event, invite); in a room without a joined local power holder (localized error); join a local user to a public and to an invite-only room; a remote user is refused in the dialog. On Tuwunel 1.8.2 and 2.0.0 if available: join to an invite-only room (invite, then join), Make room admin refused.
- **Test data:** made-up IDs on `example.org`; no real users or tokens.
- **Mapping to acceptance criteria:** AC-26 → Phase 1–4 tests above.

## 7. Rollout & Rollback

- **Deployment approach:** ships with the next release; no flag.
- **Migration steps:** none.
- **Rollback plan:** revert the PR; nothing is stored.
- **Monitoring after release:** information logs of each grant and join; error logs of failures.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| An admin expects Make room admin to rescue a room whose local admins all left. | Precondition text in the dialog (SPEC §13, §15); the localized error when Synapse refuses. |
| The Tuwunel invite succeeds and the admin join then fails, leaving an invite. | The same happens on Synapse (invite, then join); the error is shown and the invite is visible in the room's state. |
| A join rule read on Tuwunel returns stale state for a room the server has left. | The invite then fails as the admin isn't in the room, and nothing more is sent; the room page can't rejoin such rooms anyway (SPEC §10). |
| Tuwunel silently drops the invite when the target ignores the admin. | The join then fails with an error; no different success. |

## 9. Open Questions

- [ ] None blocking. Follow-ups, not in this task: a "Join to room" action on the user page; alias and `server_name` input for rooms this server has left; a conditional Tuwunel Make room admin (alternative F).

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-27 | Source check of Synapse v1.165.0 `rest/admin/rooms.py` and Tuwunel `service/admin/grant.rs`, `api/{client/,}admin/rooms/join.rs`, `service/membership/invite.rs`, `service/rooms/metadata/mod.rs` and `api/client/membership/invite.rs` at v1.8.2 and v2.0.0; findings in SPEC §10. |
