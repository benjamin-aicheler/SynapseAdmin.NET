# Implementation Plan: Join a User to a Room from the User Page (#605)

| Field | Value |
|---|---|
| **Status** | Approved |
| **Spec** | [SPEC.md](./SPEC.md) FR-4, AC-27, §8 (dialogs), §10 (joining a local user, joining a user from the user page), §15 |
| **Issue / Branch** | #605 (follow-up to #587) / `ai/feature/issue-605-join-from-user-page` |
| **Author(s)** | Benjamin Aicheler, Claude |
| **Created** | 2026-11-27 |
| **Last updated** | 2026-11-27 |

## 1. Summary of Approach

The user page gets a "Join to room" action that reuses `IRoomService.JoinUserToRoomAsync` and the gateway from #587 unchanged, Tuwunel's invite-first correction included. A new `UserJoinRoomDialog` takes a room ID, typed or picked from the user's `leave`, `invite` and `knock` memberships. It refuses rooms the user is joined to or banned from, and it is the confirmation, as on the room page. Because the room ID is now typed, `RoomService.JoinUserToRoomAsync` also checks its form, through a new `MatrixIdHelper.IsRoomId`. The action is disabled for deactivated and suspended accounts (SPEC §10, §15).

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `IMatrixGateway.JoinUserToRoomAsync`, `TuwunelAdminGateway` | Admin join; on Tuwunel, the join rule read and the Client-Server invite first (#587). | None. |
| `RoomService.JoinUserToRoomAsync` | Checks the user ID (`IsUserId`, `IsLocalUserId`); the room ID comes from the room page and isn't checked. | Check the room ID first (`IsRoomId`); `InvalidRoomId` warning, nothing sent. |
| `MatrixIdHelper` | `IsOnServer`, `IsUserId`, `IsLocalUserId`. | Add `IsRoomId`: starts with `!`, has more after it, no whitespace. Both forms are valid: `!localpart:domain`, and `!` + unpadded URL-safe base64 for room version 12 (Synapse `RoomID.from_string`). |
| `UserDetails.razor(.cs)` | Actions card; `user.Memberships` (room ID and membership) loaded with the user; `GetDisplayedTarget()`, `IsStillDisplayed`, `LoadUserDetails(target, _loadCts.Token)` after an action. No `IRoomService`. | Inject `IRoomService`; "Join to room" button (disabled for deactivated or suspended accounts); dialog, re-check, result, reload. |
| `RoomUserActionDialog` (#587) | Asks for a user ID for a room. | Not reused: this dialog asks for a room for a user (§3). |
| `DetailPageTestBase` | Registers stubs for the user page's services, but no `IRoomService`. | Register a default `IRoomService` stub; room page tests keep registering their own, which wins. |
| `RoomServiceCharacterizationTests` | The `Join` row uses `RoomId` with a line break, to show that the logs are sanitized. | The row gets its own well-formed room ID, because `IsRoomId` now refuses whitespace; specific tests for the room ID check. |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: New `UserJoinRoomDialog` | Room-ID input and membership picks of its own; the room page dialog stays simple | One more dialog | ✅ chosen |
| B: Generalize `RoomUserActionDialog` to ask for either a user or a room | One dialog | Two different inputs, validations and texts in one component | ❌ |
| C: Picks as a `MudAutocomplete` over the memberships | One input | Free text plus suggestions is fiddly in MudBlazor and in bUnit | ❌: a text field plus pick chips that fill it |
| D: Offer the action for deactivated accounts | Synapse allows it | Nobody can use the account; Reset password is already disabled for it | ❌ (SPEC §15) |
| E: Leave suspended accounts to the homeserver's error | Less logic | A button whose action always fails with a generic error | ❌: disabled, as Synapse refuses it |
| F: Read the room's members before joining (the issue's first option) | Covers rooms the memberships don't list | An extra request; the memberships the page already has cover the `join` and `ban` cases for every room the server knows | ❌: use the loaded memberships; the homeserver still refuses a stale case |
| G: Check the room ID only in the dialog | Simpler service | The service is the place that guards what is sent; a typed alias would be form-encoded into the path (#580) | ❌: the service checks too |

## 4. Technical Design

### Components affected

- **Helper:** `MatrixIdHelper.IsRoomId(string? id)`: `!`, at least one more character, no whitespace. Its doc comment names both room ID forms and says that aliases (`#…`) are not room IDs (#606).
- **Service (`RoomService.JoinUserToRoomAsync`):** the room ID is trimmed. At the start of `call`, before the user checks, a room ID that isn't `IsRoomId` fails with `InvalidRoomId` (`Severity.Warning`) and sends nothing. The rest is unchanged. The interface doc comment adds the room ID rule.
- **Dialog (`Components/Pages/UserJoinRoomDialog.razor(.cs)`):**
  - **Parameters:** `UserLabel`, and `Memberships` (`IReadOnlyList<UserMembershipViewModel>`).
  - **Texts:** title `JoinToRoomTitle` with the user. The description `JoinToRoomDescription` uses the reference's conditions: only local users can be joined; the server administrator must be in the room and have permission to invite users.
  - **Room ID field:** `Immediate`, with the hint `JoinToRoomRoomIdHint`.
  - **Picks:** under `JoinToRoomPicks` ("Rooms the user left, was invited to or knocked on"), one small outlined chip per `leave`, `invite` or `knock` membership, showing the room ID and the membership. A click fills the field. Without such memberships, the section isn't shown.
  - **Validation:**
    - Blank shows no error, but the button stays disabled.
    - Not `IsRoomId`: `InvalidRoomId`.
    - A `join` membership for the ID (ordinal): `UserAlreadyInRoom` (from #587).
    - A `ban` membership: `UserBannedFromRoom`.
  - **Submit:** the button `JoinToRoom` returns the trimmed room ID. The dialog sends nothing itself.
- **Page (`UserDetails`):**
  - **Injection:** `[Inject] IRoomService RoomService`.
  - **Button:** "Join to room" (`Color.Primary`, `Disabled="@(user.Deactivated || user.Suspended)"`), after Edit profile.
  - **Flow (`OpenJoinRoomDialog`):**
    1. `GetDisplayedTarget()` gives `target`; stop if the user is deactivated or suspended.
    2. Open the dialog with `UserLabel` = `target` and `user.Memberships`, `DialogPresets.Small()`, title `L["JoinToRoomTitle", target]`.
    3. If the dialog is cancelled, stop. Otherwise check `IsStillDisplayed(target)`.
    4. Call `RoomService.JoinUserToRoomAsync(roomId, target, _cts.Token)`, then `Snackbar.ShowResult`.
    5. On success, if `GetDisplayedTarget() == target` and `_loadCts != null`, `LoadUserDetails(target, _loadCts.Token)`.
- **Resources (EN/DE/FR):** `JoinToRoom`, `JoinToRoomTitle`, `JoinToRoomDescription`, `JoinToRoomRoomIdHint`, `JoinToRoomPicks`, `InvalidRoomId`, `UserBannedFromRoom`. Reused: `RoomId`, `Cancel`, `UserAlreadyInRoom`, `UserJoinedRoom`, `ErrorJoiningUserToRoom`, `JoinUserNotLocal`, `SelectionChangedActionAborted`.

### Data model changes

None.

### API / interface changes

`MatrixIdHelper.IsRoomId`, and the room ID check documented on `IRoomService.JoinUserToRoomAsync`. No gateway change.

### Configuration / feature flags

- None.

## 5. Implementation Steps

### Phase 1: Helper and service

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | `IsRoomId`; room ID check in `JoinUserToRoomAsync` | `Infrastructure/Helpers/MatrixIdHelper.cs`, `Services/RoomService.cs`, `Interfaces/IRoomService.cs` | `LocalMatrixIdTests`: both room ID forms, an alias, a user ID, `!` alone, whitespace, empty, `null`. `RoomServiceCharacterizationTests`: the `Join` row on a well-formed room ID; an invalid room ID → `InvalidRoomId` warning, no request, no log, checked before the user ID; a padded room ID is sent trimmed |

### Phase 2: Dialog

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | Resources EN/DE/FR | `Resources/` | Build; keys in all three files; XML parses |
| 2.2 | `UserJoinRoomDialog` | `Components/Pages/` | `UserJoinRoomDialogTests` (real `DialogService`): title and description name the user; picks only for `leave` / `invite` / `knock`, none shown without them; a pick fills the field and submits its ID; typed ID returned trimmed; alias / user ID / whitespace → `InvalidRoomId`; joined → `UserAlreadyInRoom`; banned → `UserBannedFromRoom`; blank disabled without an error; cancel |

### Phase 3: User page

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | Inject `IRoomService`, button, flow, reload; default stub in the test base | `Components/Pages/UserDetails.razor(.cs)`, `tests/.../DetailPageTestBase.cs` | `UserDetailsJoinRoomTests`: button disabled for deactivated and for suspended accounts, enabled otherwise; dialog title, parameters and options; success → `JoinUserToRoomAsync(roomId, target, token)`, snackbar, user loaded again; failure / "not supported" / cancelled → no reload; cancelled dialog → nothing sent; user changed during the dialog → `SelectionChangedActionAborted`, nothing sent; user changed while the request runs → result shown, no reload. Existing `UserDetails*` tests stay green |

### Phase 4: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | Remove `[Planned]` in SPEC.md; AGENTS.md (Room Recovery pitfall: the user page entry point, the disabled states, `IsRoomId`; Shared Helpers); README user management line; reset PLAN.md and TODO.md | `SPEC.md`, `AGENTS.md`, `README.md`, `PLAN.md`, `TODO.md` | Review |

Each phase is one commit; one PR that resolves #605.

## 6. Testing Strategy

- **Unit & bUnit component tests:** as in the steps. Culture-independent, with expected strings from `IStringLocalizer<SharedResources>`. Awaited handlers that open a dialog are bounded with `.WaitAsync(TimeSpan.FromSeconds(10))`. Mutation checks on the new checks, as for #587.
- **Integration tests:** none needed.
- **Manual / homeserver tests:** on a Synapse test server: join a user back into a room they left (invite-only, with the admin joined), and into a public room; a suspended user's button is disabled; a typed alias is refused in the dialog.
- **Test data:** made-up IDs; no real users or tokens.
- **Mapping to acceptance criteria:** AC-27 → Phases 1–3.

## 7. Rollout & Rollback

- **Deployment approach:** next release; no flag.
- **Migration steps:** none.
- **Rollback plan:** revert the PR.
- **Monitoring after release:** the information and error logs of `JoinUserToRoomAsync` (unchanged).

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| The memberships are stale (the user joined or was banned elsewhere since the page loaded). | The homeserver refuses the invite; the operation's localized error is shown, and nothing else changes. |
| A room this server has left is picked from the memberships, and the join fails. | Synapse answers `404` and Tuwunel's join rule read fails before anything is written (SPEC §10); rejoining through remote servers is #606. |
| The added room ID check rejects a valid room ID. | `IsRoomId` checks only the sigil, a non-empty rest and whitespace, which no room ID form allows; tests cover both forms. |

## 9. Open Questions

- [ ] None blocking.

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-27 | Source check of Synapse v1.165.0: `http/servlet.py` `ResolveRoomIdMixin` (room ID or alias), `types` `RoomID.from_string` (both room ID forms), `handlers/room_member.py` (suspended target refused for a join not sent as the server; remote join without servers → `404`). |
