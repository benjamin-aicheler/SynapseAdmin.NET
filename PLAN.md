# Implementation Plan: Edit existing user accounts

| Field | Value |
|---|---|
| **Status** | In Progress |
| **Spec** | [SPEC.md](./SPEC.md) FR-4, AC-19 to AC-22, §10 (account actions on the signed-in admin, refused password changes, partial account edits), §13 |
| **Issue / Branch** | #584 / `ai/feature/issue-584-edit-user-accounts` (phase 0), then one branch per phase |
| **Author(s)** | Benjamin Aicheler, Claude |
| **Created** | 2026-11-25 |
| **Last updated** | 2026-11-26 |

## 1. Summary of Approach

The user page gets the routine account edits that the Synapse Admin API offers: status chips for suspension and shadow-ban, on/off actions (lock, suspend, admin, shadow-ban), password reset, reactivation and a profile edit. The work lands in four PRs, one per phase, in the order of the [plan on the issue](https://github.com/benjamin-aicheler/SynapseAdmin.NET/issues/584#issuecomment-7169386577). The gateway's v2 `PUT` takes a typed request instead of `object`. Every edit is a separate `UserService` method that sends only the fields it changes, and only for an account the homeserver knows. Dialogs collect input and return it to `UserDetails`, which re-checks the displayed user and then calls the service, so every action follows the stale-selection rule in one place. Third-party IDs, SSO IDs and `user_type` are out of scope (SPEC.md §3, decision of 2026-11-25).

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `Interfaces/Gateways/IMatrixGateway.cs:32`, `MatrixGatewayBase.cs:197`, `SynapseAdminGateway.cs:170` | `UpdateUserAsync(string userId, object request, …)` sends the v2 `PUT` with any body | Take `SynapseAdminUserUpsertRequest` instead of `object` (phase 2) |
| `Models/Requests/SynapseAdminUserUpsertRequest.cs` | Nullable fields, `null` omitted by `MatrixHttpClient`; only `CreateUserAsync` fills it | Reuse as is for edits: every field it needs (`admin`, `locked`, `deactivated`, `password`, `displayname`, `avatar_url`) is "omit when `null`". `UserType` stays unset |
| `Services/UserService.cs:219` `CreateUserAsync` | Reads the user first, sends the `PUT` only if it doesn't exist | Unchanged. New edit methods do the opposite: send only if the user exists |
| `Models/Responses/SynapseAdminUserListResult.cs:40,65` | Reads `shadow_banned` and `suspended` | — |
| `Extensions/Mapping/UserMappingExtensions.cs:29` `ToDetailViewModel` | Maps `ShadowBanned`, not `Suspended` | Map `Suspended` (`null` → `false`) |
| `Models/ViewModels/UserDetailViewModel.cs` | Has `ShadowBanned`; placeholder comment about threepids at line 27 | Add `Suspended`; remove the placeholder comment |
| `Components/Pages/UserDetails.razor:69–91, 117–121` | Status shows Deactivated, Locked, Active; actions: Renew, Deactivate (also on own account), Shadow login, Quarantine | Add chips and actions; own-account guard |
| `Components/Pages/UserDetails.razor.cs` | `GetDisplayedTarget()`, `IsStillDisplayed(target)`, `FormatTarget`, `DeactivateUser` pattern (confirm → re-check → call → reload) | Reuse for every new action; add a shared helper for "confirm, re-check, call, show, reload" |
| `Infrastructure/Gateways/GenericMatrixGateway.cs` | Refuses every admin method | Refuse the new methods (`GenericMatrixGatewayTests` checks all declared methods by reflection) |
| `Infrastructure/Gateways/TuwunelAdminGateway.cs` | Overrides user list and details | Refuse shadow-ban and lifting it |
| `tests/…/UserExperimentalFeaturesTests.cs:277`, `SecurityInfrastructureTests.cs:920` | Hand-written `IMatrixGateway` doubles | Add the new members and the changed signature |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: Dialogs return their input; `UserDetails` re-checks the target and calls the service | One place for the stale-selection check; dialogs stay simple to test; matches the confirmation actions | The page holds the password briefly in memory | ✅ chosen |
| B: Dialogs call the service themselves, as `UserCreateDialog` and `RenewAccountDialog` do | Follows an existing pattern | The dialog can't tell whether the page still shows the user, so each dialog needs the check passed in | ❌ rejected |
| C: One generic `ModifyUserAsync(userId, request)` service method for all v2 edits | Fewer methods | The page would build gateway DTOs, which breaks the N-tier rule; outcomes can't have their own localized message | ❌ rejected |
| D: One service method per operation (`SetUserLockedAsync`, `SetUserAdminAsync`, …) | Each body is fixed and tested; each outcome has its own message | More methods | ✅ chosen |
| E: Use `PUT /_synapse/admin/v1/users/{userId}/admin` for the admin toggle | Dedicated endpoint | Tuwunel doesn't serve it (🟨); disabled under MAS | ❌ rejected; v2 `PUT` with `admin` only |
| F: Use `LibMatrix`'s `ResetPasswordAsync` | Exists | Defaults `logout_devices` to `false`, no URL encoding, no `CancellationToken` | ❌ rejected; `ClientHttpClient` directly |

## 4. Technical Design

### Components affected

- **Gateway** (`IMatrixGateway`, `MatrixGatewayBase`, `SynapseAdminGateway`, `TuwunelAdminGateway`, `GenericMatrixGateway`):
  - `UpdateUserAsync(string userId, SynapseAdminUserUpsertRequest request, CancellationToken)`: signature change only.
  - `ResetPasswordAsync(string userId, SynapseAdminResetPasswordRequest request, CancellationToken)`: `POST /_synapse/admin/v1/reset_password/{userId}`, body `{ "new_password", "logout_devices" }` (both always sent). `TuwunelAdminGateway` overrides it and refuses, before sending the reset: when `userId` equals its own `UserId` (ordinal), because Tuwunel's handler removes all of the user's devices, including the requester's (`reset_password.rs` at v1.8.2 and v2.0.0); and when `GetUserDetailsAsync` reports the account as deactivated, because Tuwunel's `set_password` would reactivate it. This is a read before the write, as for registration tokens (#548).
  - `SetUserSuspendedAsync(string userId, bool suspend, CancellationToken)`: `PUT /_synapse/admin/v1/suspend/{userId}`, body `{ "suspend" }`.
  - `SetUserShadowBannedAsync(string userId, bool shadowBanned, CancellationToken)`: `POST` (body `{}`, via `PostEmptyAsync`) or `DELETE` (via `SendDeleteAsync`) `/_synapse/admin/v1/users/{userId}/shadow_ban`. Tuwunel refuses both.
  - User IDs in paths with `.UrlEncode()`, as the other user endpoints do.
- **Service** (`IUserService`, `UserService`), each through `RunGatewayCallAsync` with its own failure key and log template (never the password):
  - `SetUserLockedAsync(userId, locked)`, `SetUserAdminAsync(userId, admin)`, `ReactivateUserAsync(userId, password?)`, `UpdateUserProfileAsync(userId, UserProfileEditViewModel)`: each reads the user first and fails with `L["UserNotFound"]` if the homeserver doesn't know it, then sends a request with only its fields.
  - `SetUserSuspendedAsync(userId, suspend)`, `SetUserShadowBannedAsync(userId, shadowBanned)`, `ResetPasswordAsync(userId, password, logoutDevices)`: dedicated endpoints, no read first (they don't create users).
- **UI** (`UserDetails.razor(.cs)`, new `ResetPasswordDialog`, `ReactivateUserDialog`, `UserProfileDialog`):
  - Chips: Suspended (warning), Shadow-banned (error). Active unless Deactivated, Locked or Suspended applies; a shadow-banned account is still active, and its Shadow-banned chip appears next to Active.
  - Own account: `IsOwnAccount => string.Equals(_displayedUserId, MatrixSession.Gateway?.UserId, StringComparison.Ordinal)`. Disables Lock, Suspend, Remove admin, Shadow-ban and Deactivate, with a localized hint (`NotAvailableForOwnAccount`; a disabled `MudButton` fires no mouse events, so the tooltip wraps it in a `<span>`). Reset password stays available there; the dialog adds that this session stays signed in.
  - Actions follow the state shown; Reactivate replaces Deactivate for a deactivated user.
  - Each action: confirmation naming `FormatTarget(user, target)` → `IsStillDisplayed(target)` → service call → `Snackbar.ShowResult` → reload the user if still displayed.

### Data model changes

- `UserDetailViewModel.Suspended` (bool).
- `Models/Requests/SynapseAdminResetPasswordRequest` (`new_password`, `logout_devices`).
- `Models/Requests/SynapseAdminSuspendRequest` (`suspend`), or an anonymous object, as `BlockRoomAsync` sends `new { block }`; follow `BlockRoomAsync`.
- `Models/ViewModels/UserPasswordResetViewModel` (`Password`, `LogoutDevices`, same `MinLength(8)` as `UserCreateViewModel`), `UserReactivateViewModel` (`Password?`), `UserProfileEditViewModel` (`DisplayName`, `RemoveAvatar`).
- No storage or migration.

### API / interface changes

- `IMatrixGateway`: one changed signature, three new methods. Not public API; the two hand-written test doubles change with it.
- `IUserService`: seven new methods.

### Configuration / feature flags

- None.

## 5. Implementation Steps

Each phase is one PR against `main`. Each step leaves `dotnet build` and `dotnet test` green.

### Phase 0: Spec and plan (this PR)

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 0.1 | SPEC.md `[Planned]` entries for FR-4, AC-19 to AC-22, §8 dialogs, §10, §13 | `SPEC.md` | Review |
| 0.2 | This plan; TODO.md after approval | `PLAN.md`, `TODO.md` | Review |
| 0.3 ✅ | Check Tuwunel's handlers at v1.8.2 and v2.0.0 for: `locked` and `suspended` in `GET /v2/users/{id}` (else the chips would show a wrong state); `locked` and `admin` alone in a v2 `PUT` on an existing user; reactivation without a password; `displayname: ""` and `avatar_url: ""`; suspend (`reset_password` already checked: honors `logout_devices`, removes the requester's device too) | Notes in §10 Progress Notes; SPEC.md §10 for any accepted difference | Each request matched to a handler line, or a refusal planned |

### Phase 1: Account status display (AC-19)

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | Map `Suspended`; remove the placeholder comment | `UserDetailViewModel.cs`, `UserMappingExtensions.cs` | `MappingExtensionsTests` property and `null` cases |
| 1.2 | Suspended and Shadow-banned chips; Active rule | `UserDetails.razor`, `SharedResources*.resx` (`Suspended`, `ShadowBanned`) | `UserDetailsAccountStatusTests` (find chips via `.Closest(".mud-list-item")`, as `LockedUserTests` does) |

### Phase 2: Status actions (AC-20)

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | Type `UpdateUserAsync`'s request | Gateway interface, base, Synapse, Generic; two test doubles | Build; existing `UserServiceCharacterizationTests` "Create" row unchanged |
| 2.2 | Gateway: `SetUserSuspendedAsync`, `SetUserShadowBannedAsync`; Generic refuses; Tuwunel refuses shadow-ban | Gateways | `SynapseAdminGatewayCharacterizationTests` (method, path, encoding, exact body, cancellation), `GenericMatrixGatewayTests`, Tuwunel refusal sends nothing |
| 2.3 | Service: `SetUserLockedAsync`, `SetUserAdminAsync`, `SetUserSuspendedAsync`, `SetUserShadowBannedAsync` | `IUserService`, `UserService`, resx | `UserServiceCharacterizationTests` rows; a wire test that the lock edit's body is exactly `{"locked":true}` (no `admin` key); missing user sends no `PUT` |
| 2.4 | Actions card: toggles, own-account guard (also disables Deactivate) | `UserDetails.razor(.cs)`, resx | `UserDetailsAccountActionsTests`: labels follow state, confirmation names the user, navigation during the confirmation aborts, reload after success, own account disables the actions and shows the hint; `UserDetailsStaleSelectionTests` extended |

### Phase 3: Password reset and reactivation (AC-21)

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | Gateway `ResetPasswordAsync`; Generic refuses; Tuwunel refuses the signed-in admin's own user ID and a deactivated account | Gateways, `SynapseAdminResetPasswordRequest` | Wire test: exact body with both fields, `false` sent explicitly; Tuwunel: own ID refused with the switch on and off and nothing sent; a deactivated account refused after the details read and no reset sent; another active user's reset sent unchanged |
| 3.2 | Service `ResetPasswordAsync`, `ReactivateUserAsync` | `UserService`, resx | Characterization rows; reactivation without password omits `password`; logs contain no password (`RecordingLogger` arguments) |
| 3.3 | `ResetPasswordDialog` ("Log out all devices" off by default; own-account hint), `ReactivateUserDialog`; page wiring; Reset password disabled for a deactivated account | New dialogs, `UserDetails`, resx | `ResetPasswordDialogTests`, `ReactivateUserDialogTests` with the real `DialogService` (`DialogSnackbarTests` pattern); `.Input()` for `Immediate` fields |

### Phase 4: Profile edit (AC-22)

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | Service `UpdateUserProfileAsync`: only changed fields; no change sends nothing | `UserService`, `UserProfileEditViewModel` | Wire tests: name only, avatar removal only, both, emptied name sends `""` |
| 4.2 | `UserProfileDialog` and page wiring | New dialog, `UserDetails`, resx | `UserProfileDialogTests` |
| 4.3 | Finish: drop `[Planned]` tags, decision log rows, reset PLAN.md and TODO.md | `SPEC.md`, `PLAN.md`, `TODO.md`; `AGENTS.md` "What Tuwunel Refuses" (shadow-ban, own password reset) and any other pitfall that turned up | Review |

## 6. Testing Strategy

- **Unit & bUnit component tests:** wire tests in `SynapseAdminGatewayCharacterizationTests` pin method, path, URL encoding, exact JSON body and cancellation for every new request; `UserServiceCharacterizationTests` rows for success, failure, cancellation, not supported and the missing-user case; `MappingExtensionsTests` for `Suspended`; bUnit tests per phase (see steps), culture-independent, expected strings from `IStringLocalizer<SharedResources>`.
- **Integration tests:** none needed; no controller or middleware changes.
- **Manual / homeserver tests:** on a test Synapse: each action on a test account and on the signed-in account; reset with and without logout, checking the user's devices with `GET /_synapse/admin/v2/users/{userId}/devices` outside the app; reactivation with and without a password. On Tuwunel 1.8.2 and 2.0.0: the same, plus the shadow-ban refusal.
- **Test data:** made-up IDs such as `@alice:example.com`; passwords only as test literals.
- **Mapping to acceptance criteria:** AC-19 → phase 1 tests; AC-20 → 2.2–2.4; AC-21 → 3.1–3.3; AC-22 → 4.1–4.2.

## 7. Rollout & Rollback

- **Deployment approach:** four PRs, each usable on its own; released with the next version.
- **Migration steps:** none.
- **Rollback plan:** revert the phase's PR; no stored state.
- **Monitoring after release:** `Error` log entries of the new failure templates.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| An edit sends `admin: false` and demotes an admin, or `deactivated: false` and reactivates an account | Fixed bodies per method; exact-body wire tests; reading the user first |
| The admin ends their own session | Own-account guard; Synapse keeps the requesting session on a reset; Tuwunel refuses resetting one's own password |
| An admin logs out all of a user's sessions by accident | "Log out all devices" is off by default |
| Tuwunel reports `locked` or `suspended` differently from what it stores | Step 0.3 handler check before phase 1 |
| The password reaches a log | No `{Password}` argument in any template; `RecordingLogger` assertions |

## 9. Open Questions

- [x] Own-account guard: disable with a hint, not hide (2026-11-25). Deactivate is disabled on one's own page too.
- [x] "Log out all devices" is off by default on every account (2026-11-25). Resetting one's own password stays available: Synapse keeps the requesting session. On Tuwunel, which removes it too, resetting one's own password is refused (2026-11-25).
- [x] Shadow-banned doesn't count against Active; the Shadow-banned chip is shown next to it (2026-11-25).
- [x] Synapse v1.165.0 registers `PUT /_synapse/admin/v1/suspend/{userId}` unconditionally (`synapse/rest/admin/__init__.py:357`, outside the `auth_delegated` blocks). The handler requires a boolean `suspend`, answers `404` for an unknown user and `400` for a remote one (`users.py:819–846`).

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-25 | Scope narrowed: third-party IDs, SSO IDs and `user_type` left out (SPEC.md §3, #591). |
| 2026-11-25 | Review answers: disable own-account actions with a hint; shadow-banned is still Active. Suspension endpoint checked in Synapse v1.165.0. |
| 2026-11-25 | Step 0.3, Tuwunel handlers at v1.8.2 and v2.0.0 compared with Synapse v1.165.0 (`synapse/rest/admin/users.py`): **user details** (`users/mod.rs` `user_details`) read `locked` and `suspended` from storage; `shadow_banned` stays `false`, which is right, as Tuwunel has none. **v2 `PUT` on an existing user** (`create_or_modify.rs`): `admin`, `locked`, `displayname`, `avatar_url` change only when present, as in Synapse; `""` clears the name and the avatar; self-demotion is refused. **Reactivation**: without a password Tuwunel writes the sentinel `*` (active, no usable password), Synapse's `activate_account` leaves the account without a password, so the result is the same; with a password both set it and the account is active. Tuwunel refuses a password for an account whose origin is neither `password` nor `sso` (e.g. LDAP), Synapse when local passwords are off: both are the normal error path. **Suspend** (`suspend.rs`): same request and result; the response bodies differ (`{user_id, suspended}` vs `{"user_<id>_suspended": …}`), so the gateway doesn't read the response. **Reset password**: besides removing the requester's device, Tuwunel's `set_password` (not `set_password_if_active`) reactivates a deactivated account; Synapse leaves it deactivated. Planned: Reset password disabled for deactivated accounts on every server; `TuwunelAdminGateway` reads the account and refuses a reset for a deactivated one. |
| 2026-11-25 | Synapse's `set_password` keeps the requester's device and token when logging out devices; Tuwunel's `reset_password` removes all devices (v1.8.2 `all_device_ids` loop, v2.0.0 `remove_devices(…, None)`). Decision: switch off by default; own password reset refused on Tuwunel only. |
