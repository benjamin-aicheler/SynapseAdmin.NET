# Implementation Plan: Redact a User's Events (#600)

| Field | Value |
|---|---|
| **Status** | Approved |
| **Spec** | [SPEC.md](./SPEC.md) FR-4, AC-25, §8 (dialogs), §10 (redacting a user's events, no time range, redaction on Tuwunel), §13 |
| **Issue / Branch** | #600 (part 3 of #585) / `ai/feature/issue-600-redact-user-events` |
| **Author(s)** | Benjamin Aicheler, Claude |
| **Created** | 2026-11-27 |
| **Last updated** | 2026-11-27 |

## 1. Summary of Approach

Two gateway calls, our own rather than `LibMatrix`'s: `POST /_synapse/admin/v1/user/{userId}/redact` starts the background task and returns a `redact_id`, and `GET /_synapse/admin/v1/user/redact_status/{redactId}` reads its status, returning `null` for an unknown ID. `TuwunelAdminGateway` and `GenericMatrixGateway` refuse both. A new `IUserRedactionService` starts the redaction, keeps the `redact_id` in `IMemoryCache` per homeserver and user (as `RoomService` does for purges), and maps the status to a ViewModel with a failure count, logging the failure texts. The user page gets a "Redact events" action: `RedactUserEventsDialog` collects the options, the page confirms and re-checks the shown user, then shows a banner and follows the status with `StatusPoller`, as `RoomDetails` does for a history purge. The time range (`after_ts`, `before_ts`) is left out (SPEC §10).

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `IMatrixGateway`, `MatrixGatewayBase` | No redaction calls. `LibMatrix`'s `DeleteAllMessages` / `GetRedactStatus` leave out an empty `rooms` and wait for a status Synapse doesn't send. | Add `RedactUserEventsAsync` and `GetUserRedactionStatusAsync` (abstract in the base). |
| `SynapseAdminGateway` | `GetOptionalStatusAsync<T>` (null for `404`, `M_NOT_FOUND`, `M_UNRECOGNIZED`); purge start through `PostAsJsonAsync`. | POST with a typed body; status through `GetOptionalStatusAsync<T>`. |
| `TuwunelAdminGateway` | Refuses room deletion and its status reads (#559). | Refuse both new methods (SPEC §10 "Redaction on Tuwunel"). |
| `GenericMatrixGateway` | Refuses every admin method; `GenericMatrixGatewayTests` checks this by reflection. | Refuse both new methods. |
| `RoomService` | Tracks purge IDs: `active_purge:{homeserver}:{roomId}`, 24 h, a static lock, and compare-and-clear (#522). | Pattern reused in the new service; no change. |
| `UserDeviceService` (#598) | Devices and connections only. | No change: redaction gets its own service (§3). |
| `StatusPoller<TStatus>` | Follows a purge or a deletion on `RoomDetails`. | Reused unchanged. |
| `UserDetails.razor(.cs)` | Actions card; `user.Memberships` (room ID, membership) loaded with the user; `_displayedUserId`, `IsStillDisplayed`. | "Redact events" button, dialog, confirmation, banner, polling, stop on user change, resume from the cache. |
| `DetailPageTestBase` | Registers stubs for the page services. | Register an `IUserRedactionService` stub. |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: New `IUserRedactionService` | One purpose; holds its own cache and lock; `UserService` (557 lines) doesn't grow | One more service to register and stub | ✅ chosen |
| B: Add to `UserDeviceService`, as the issue's Location says | No new service | Devices and event redaction share nothing; the name would mislead | ❌ (the issue text gets updated in Phase 5) |
| C: Add to `UserService` | The user's other actions are there | `UserService` grows further and gains a cache and a lock it doesn't have yet | ❌ |
| D: The dialog sends the request, as `PurgeHistoryDialog` does | One fewer step | No separate confirmation naming the user; the re-check of the shown user would sit in the dialog | ❌: the dialog returns the options, and the page confirms and sends |
| E: Banner and polling in a child component | Smaller `UserDetails` | `RoomDetails` keeps purge polling in the page; a child needs the user-change and cache logic passed in | ❌: keep it in the page, as `RoomDetails` does |
| F: Offer the time range | Matches the reference | Synapse before 1.157 ignores it and would redact everything; no version checks (#562) | ❌ (SPEC §10) |
| G: Find a running redaction by user, as for room deletion | Would show redactions started elsewhere | Synapse has no status read by user, only by `redact_id` | ❌: in-memory tracking, as for purges |

## 4. Technical Design

### Components affected

- **DTOs (`Models/Requests`, `Models/Responses`):**
  - `SynapseAdminUserRedactRequest`:
    - `rooms` (`List<string>`, always sent),
    - `reason` (`string?`, left out when `null`: `PostAsJsonAsync` drops nulls),
    - `limit` (`int`),
    - `use_admin` (`bool`).
    - No `after_ts` / `before_ts` properties.
  - `SynapseAdminUserRedactResponse` (`redact_id`).
  - `SynapseAdminUserRedactStatusResponse`:
    - `status`,
    - `failed_redactions` (`Dictionary<string, string>?`),
    - `error`.
- **Gateway:**
  - `RedactUserEventsAsync(string userId, SynapseAdminUserRedactRequest request, CancellationToken)` → `SynapseAdminUserRedactResponse?`. It sends `POST /_synapse/admin/v1/user/{userId.UrlEncode()}/redact` through `ClientHttpClient.PostAsJsonAsync` with the token, then `EnsureSuccessStatusCode` and `ReadFromJsonAsync`.
  - `GetUserRedactionStatusAsync(string redactId, CancellationToken)` → `SynapseAdminUserRedactStatusResponse?`. It calls `GetOptionalStatusAsync<T>` on `/_synapse/admin/v1/user/redact_status/{redactId.UrlEncode()}`.
  - `TuwunelAdminGateway` and `GenericMatrixGateway` throw `OperationNotSupportedByHomeserverException` for both.
- **ViewModels:**
  - `UserRedactionRequestViewModel`: `AllRooms` (`bool`), `RoomIds` (`List<string>`), `Reason` (`string?`), `Limit` (`int`, default 1000), `UseAdmin` (`bool`).
  - `UserRedactionState` enum: `Unknown`, `Scheduled`, `Active`, `Complete`, `Failed`.
  - `UserRedactionStatusViewModel`: `RedactId`, `State`, `FailedRedactionCount`, `IsRunning`.
- **Mapper (`UserMappingExtensions`):** `SynapseAdminUserRedactStatusResponse.ToViewModel(string redactId)`.
  - The status is lower case; any other value is `Unknown`, as in `RoomMappingExtensions`.
  - `FailedRedactionCount` is `failed_redactions?.Count ?? 0`.
  - The error texts are not copied.
- **Service:** `IUserRedactionService` / `UserRedactionService(IMatrixSessionService, ILogger<UserRedactionService>, IStringLocalizer<SharedResources>, IMemoryCache)`, registered scoped in `Program.cs`. Its constants are `DefaultSearchLimit = 1000`, `MaxLoggedFailedRedactions = 20` and the 24 h cache lifetime.
  - `StartRedactionAsync(string userId, UserRedactionRequestViewModel request, CancellationToken)` → `OperationResult<string>` (the `redact_id`):
    - **Selection check:** `!AllRooms` with no room IDs fails with `UserRedactionNoRoomsSelected` (`Severity.Warning`) and sends nothing, because `[]` would mean the user's rooms. The check runs at the start of `call`, after the session check.
    - **Limit check:** a `Limit` below 1 fails with `UserRedactionInvalidLimit` (warning) and sends nothing; Synapse would treat `0` as 1000.
    - **Request:** `rooms` is `[]` for `AllRooms`, else the distinct selected IDs in order. A blank reason becomes `null`.
    - **Result:** a missing `redact_id` logs a warning and returns `Ok("")` with `UserRedactionStarted`, without tracking. Otherwise the service caches the ID and returns `Ok(redactId)`.
    - **Logging:** an information log names the user ID, the number of rooms (or "user's rooms"), the limit and `use_admin`, but not the reason.
    - **Errors:** failure key `ErrorRedactingUserEvents`.
  - `GetRedactionStatusAsync(string redactId, CancellationToken)` → `OperationResult<UserRedactionStatusViewModel>`:
    - **Refusal:** passes `logRefusal` (`Debug`), because only the page's poll runs it.
    - **Unknown ID:** a `null` response is `Ok` with `Unknown` (not a failure, so it doesn't count toward the poll's failure cap, as for room deletion).
    - **Failed:** logs a warning with the sanitized `error`.
    - **Complete with failures:** logs a warning with the count and the first `MaxLoggedFailedRedactions` event IDs and errors (sanitized).
    - **Errors:** failure key `ErrorFetchingUserRedactionStatus`.
  - **Tracking:**
    - `GetActiveRedactionId(string userId)` reads the cached ID.
    - `ClearActiveRedactionId(string userId, string expectedRedactId)` compares and removes under a static lock (#522).
    - The cache key is `active_redaction:{homeserver}:{userId}`, where `{homeserver}` is the lower-cased homeserver URL without a trailing slash.
- **Dialog (`Components/Pages/RedactUserEventsDialog.razor(.cs)`):**
  - **Parameters:** `UserLabel` and `Memberships` (`IReadOnlyList<UserMembershipViewModel>`).
  - **Rooms:** a radio group, "The user's rooms (n)" or "Selected rooms":
    - `n` counts the memberships `join` and `ban`.
    - With `n == 0`, a note says that the user's rooms redact nothing and that rooms the user left can be selected.
    - The selection is a multi-select list of every membership: room ID plus a membership chip.
  - **Reason:** a text field with the note that room members see it.
  - **Limit:** a numeric field, `Min = 1`, default 1000, with the reference's description.
  - **"Redact as admin":** a switch with the reference's note.
  - **Info alert:** only messages, encrypted messages and joins are redacted; in rooms the user left, only as admin.
  - **Submit:** "Continue" stays disabled while "Selected rooms" has no room or the limit is below 1. The dialog returns a `UserRedactionRequestViewModel` and sends nothing itself.
- **Page (`UserDetails`):**
  - **Button:** "Redact events" (`Color.Error`). It is disabled while a redaction banner is shown or no user is shown.
  - **Opening the dialog:** capture `target` with `GetDisplayedTarget()`, then open the dialog with the shown user's memberships.
  - **Confirmation:** `RedactUserEventsConfirmation` names the user and the scope: `RedactScopeUserRooms`, or `RedactScopeSelectedRooms` with the count. `RedactUserEventsHint` says redactions can't be undone. After the confirmation, `IsStillDisplayed(target)` runs, then `StartRedactionAsync` with the page token, then `Snackbar.ShowResult`.
  - **Tracking:** with a non-empty `redact_id`, and only if `target` is still shown, the page sets `_activeRedactId` / `_activeRedactionState = Scheduled` and starts the poll.
  - **Poll:** `StatusPoller<UserRedactionStatusViewModel>` with `IsCurrent = !token.IsCancellationRequested && _activeRedactId == redactId`.
    - `Scheduled` / `Active` update the shown state and keep checking.
    - `Complete` clears the tracking and shows `UserRedactionCompleted` (success) or `UserRedactionCompletedWithFailures` (warning, with the count).
    - `Failed` clears the tracking and shows `UserRedactionFailed` (error).
    - `Unknown` clears the tracking and shows `UserRedactionStatusUnknown` (warning).
    - `GiveUp` (5 failed checks in a row) clears the tracking and shows `ErrorFetchingUserRedactionStatus`.
    - Constants: `RedactionStatusPollIntervalMs = 5000`, `MaxConsecutiveRedactionStatusFailures = 5`.
  - **User change:** `OnParametersSetAsync` cancels the poll and clears `_activeRedactId` before the first `await`. After a user loads, the page reads `GetActiveRedactionId(userId)` and, if set, shows the banner and polls. `Dispose` cancels the poll.
  - **Banner:** at the top of the user content, like the purge banner, showing "Redacting the user's events" and the state chip.
- **Resources (EN/DE/FR):**
  - Actions and dialog: `RedactUserEvents`, `RedactUserEventsTitle`, `RedactRooms`, `RedactUserRooms`, `RedactSelectedRooms`, `RedactUserRoomsNone`, `RedactReason`, `RedactReasonHint`, `RedactSearchLimit`, `RedactSearchLimitHint`, `RedactAsAdmin`, `RedactAsAdminHint`, `RedactEventTypesHint`, `Continue` (new).
  - Confirmation: `RedactUserEventsConfirmation`, `RedactScopeUserRooms`, `RedactScopeSelectedRooms`, `RedactUserEventsHint`.
  - Results and banner: `UserRedactionStarted`, `UserRedactionInProgress`, `UserRedactionCompleted`, `UserRedactionCompletedWithFailures`, `UserRedactionFailed`, `UserRedactionStatusUnknown`, `UserRedactionNoRoomsSelected`, `UserRedactionInvalidLimit`, `ErrorRedactingUserEvents`, `ErrorFetchingUserRedactionStatus`.
  - Status chips: `UserRedactionStatusScheduled`, `UserRedactionStatusActive` (the existing `Active` is the account status label).

### Data model changes

None (in-memory cache entry only).

### API / interface changes

`IMatrixGateway.RedactUserEventsAsync`, `IMatrixGateway.GetUserRedactionStatusAsync`; new `IUserRedactionService`. No breaking change. Test fakes that implement `IMatrixGateway` or derive from `MatrixGatewayBase` get the two methods.

### Configuration / feature flags

- None. Default limit, poll interval, failure cap, cache lifetime and the log cap are named constants.

## 5. Implementation Steps

### Phase 1: Gateway

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | DTOs; both methods on the interface, base, Synapse, Tuwunel and Generic gateways; test fakes | `Models/`, `Infrastructure/Gateways/`, `Interfaces/Gateways/IMatrixGateway.cs`, fakes in tests | Wire tests in `SynapseAdminGatewayCharacterizationTests`: `POST /_synapse/admin/v1/user/%40alice%3aexample.com/redact` with exactly `{"rooms":[],"limit":1000,"use_admin":false}`, and with rooms and reason; no `after_ts` / `before_ts`; reads `redact_id`; status `GET …/redact_status/{id}` encoded; `404` / `M_NOT_FOUND` / `M_UNRECOGNIZED` → `null`, other errors propagate; cancellation reaches the request. Tuwunel refuses both before sending; `GenericMatrixGatewayTests` covers both. |

### Phase 2: Service

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | ViewModels, enum, mapper | `Models/ViewModels/`, `Extensions/Mapping/UserMappingExtensions.cs` | `MappingExtensionsTests`: each status, unknown and missing status → `Unknown`, `null` failures → 0, `RedactId` copied |
| 2.2 | `IUserRedactionService`, `UserRedactionService`, DI | `Interfaces/`, `Services/`, `Program.cs` | `UserRedactionServiceCharacterizationTests` (no session, cancel, throw, refuse for both methods); `UserRedactionServiceTests`: empty selection and limit below 1 refused without a request, request body mapping (distinct rooms, blank reason → `null`), cache per homeserver and user, missing `redact_id` not tracked, `null` status → `Unknown`, failed and failure logs (count, cap of 20, sanitized, never in the message), compare-and-clear, refusal logged at `Debug`, reason never logged |

### Phase 3: Dialog

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | Resources EN/DE/FR | `Resources/` | Build |
| 3.2 | `RedactUserEventsDialog` | `Components/Pages/` | `RedactUserEventsDialogTests` (real `DialogService`): default "user's rooms" with the join/ban count, the "none" note, selection lists all memberships, Continue disabled with an empty selection or limit < 1, returned ViewModel, Cancel returns nothing, hints shown |

### Phase 4: Page

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | Button, confirmation, start, banner, polling, user change, resume from cache | `UserDetails.razor(.cs)`, `DetailPageTestBase` | `UserDetailsRedactionTests`: confirmation names user and scope; cancel at dialog or confirmation sends nothing; changed user after confirmation aborts with `SelectionChangedActionAborted`; banner and button disabled while running; scheduled → active → complete (success), complete with failures (warning with count), failed, unknown, 5 failed checks; a cached ID shows the banner on open; a late answer after switching user changes nothing; not-supported warning shows and no banner |

### Phase 5: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 5.1 | Remove `[Planned]`; decision log (own service, dialog then confirmation, no time range, Tuwunel refusal, in-memory tracking) | `SPEC.md` | Review |
| 5.2 | README feature list; ARCHITECTURE in-memory cache list (`active_redaction:…`) and integrations; AGENTS.md Known Pitfalls (an empty `rooms` means joined and banned rooms; the selection must never be sent empty; what Synapse redacts) and the Tuwunel refusal list | `README.md`, `ARCHITECTURE.md`, `AGENTS.md` | Review |
| 5.3 | Update #600's text (service location), check the remaining box of #585, reset PLAN.md / TODO.md | GitHub, repo | |

## 6. Testing Strategy

- **Unit & bUnit component tests:** as listed per step, culture-independent (expected strings from `IStringLocalizer<SharedResources>`).
- **Integration tests:** none new.
- **Manual / homeserver tests:** on a Synapse test server, a spam account in two rooms:
  - redact in "the user's rooms" and see the banner end with success;
  - deactivate the account, then redact the left rooms as admin;
  - redact without "as admin" in a left room and see the failure count.
- **Test data:** made-up user, room and event IDs.
- **Mapping to acceptance criteria:** AC-25 → `SynapseAdminGatewayCharacterizationTests`, `GenericMatrixGatewayTests`, `MappingExtensionsTests`, `UserRedactionServiceCharacterizationTests`, `UserRedactionServiceTests`, `RedactUserEventsDialogTests`, `UserDetailsRedactionTests`.

## 7. Rollout & Rollback

- **Deployment approach:** regular release, no configuration.
- **Migration steps:** none.
- **Rollback plan:** revert the PR. A redaction already running on the homeserver finishes there.
- **Monitoring after release:** the information, warning and error logs of `UserRedactionService`.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| An empty selection is sent as `rooms: []` and redacts in all the user's rooms | Dialog can't submit it; the service refuses it before sending; a wire test pins the body |
| "The user's rooms" redacts nothing for a deactivated account | Room count and "none" note in the dialog; left rooms selectable (SPEC §13) |
| The reason exposes private details to room members | Hint at the field (SPEC §13) |
| The redaction is lost from view after an app restart or on another replica | Documented in SPEC §10; the redaction still runs on the homeserver |
| Failure texts or the reason end up in the UI or the log | Only a count in the UI; error texts sanitized in the log, capped at 20; the reason not logged |

## 9. Open Questions

- [x] The dialog says which events Synapse redacts (messages, encrypted messages, joins), which comes from Synapse's handler, not from the reference. Kept. Decided 2026-11-27.
- [x] Redaction gets its own `IUserRedactionService` instead of the #598 service that the issue names. Decided 2026-11-27.

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-27 | Checked Synapse v1.165.0. In `rest/admin/users.py` `RedactUser`: `rooms` is required, and an empty list becomes the rooms the user is joined to plus those they're banned from. `limit` `0` → 1000, negative → `400`. Synapse refuses while a redaction of the user is `active`, and an unknown `redact_id` → `404`. In `handlers/admin.py`: the redaction is sent as the user unless `use_admin` is set or the user isn't local, per-event failures go to `failed_redactions`, and the task ends `complete`. In `events_worker.py` `get_events_sent_by_user_in_room`: `sender = user`, types `m.room.member`/`message`/`encrypted`, `ORDER BY received_ts DESC`, the time range on `origin_server_ts`. The Tuwunel findings come from the triage comment on #585. |
| 2026-11-27 | Plan approved. |
