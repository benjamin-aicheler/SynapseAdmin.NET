# Implementation Plan: Distinguish a requested room deletion from a completed one (#518)

| Field | Value |
|---|---|
| **Status** | Approved |
| **Spec** | [SPEC.md](./SPEC.md): FR-5, AC-11, edge case "Room deletion status", §13 risk (all `[Planned]`) |
| **Issue / Branch** | #518 / `ai/bugfix/issue-518-room-deletion-status` |
| **Author(s)** | Benjamin Aicheler (AI-assisted) |
| **Created** | 2026-11-23 |
| **Last updated** | 2026-11-23 |

## 1. Summary of Approach

`DELETE /_synapse/admin/v2/rooms/{roomId}` only starts a background job and returns a `delete_id`, but the app reports a finished deletion and goes back to the room list. The gateway will return the `delete_id`, the service will report "room deletion requested", and the room page will stay open, as it does after a purge. The room page shows a banner at the top while the deletion is scheduled or running, like the tombstone banner, and checks the job every 5 seconds by `delete_id`, the same way the purge status is checked. On load, the page asks Synapse for the room's deletions by room ID (`/_synapse/admin/v2/rooms/{roomId}/delete_status`). That way the banner also appears when the room is opened again, after an app restart, or for a deletion started elsewhere, and no `delete_id` has to be kept in `IMemoryCache`. While a deletion or a purge runs, all five room actions are disabled. A failed deletion and a failed purge both show only a localized message, and the homeserver's error text goes to the log, which changes the purge failure message as well.

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `SynapseCompatibleAdminGateway.DeleteRoomAsync` (l. 220) | Sends the `DELETE` through `SendDeleteAsync`, which never reads the response. Returns `Task`. | Read the response body and return `SynapseAdminRoomDeleteResponse?` (`delete_id`). |
| `SynapseCompatibleAdminGateway` | No deletion status endpoints. `FetchEventAsync` (l. 313) maps a `404` (`HttpRequestException` / `MatrixException` `M_NOT_FOUND`) to `null`. | Add a status read by `delete_id` and one by room ID, with the same `404` → `null` mapping, plus `M_UNRECOGNIZED` for servers without the endpoint. |
| `IMatrixGateway`, `MatrixGatewayBase`, `GenericMatrixGateway` | `Task DeleteRoomAsync(...)`. | New return type and two new members. `GenericMatrixGateway` throws `NotSupportedException`, like its other admin members. |
| `RoomService.DeleteRoomAsync` (l. 214) | Logs "Successfully deleted room…" and returns `OperationResult.Ok(L["RoomDeletedSuccessfully"])`. Removes the room name cache entry and the tracked purge. | Return `OperationResult<string>` (`delete_id`, or empty) with `RoomDeletionRequested`. Log the request with its `delete_id`, and log a warning when the ID is missing, like `PurgeRoomHistoryAsync` (l. 381). Keep removing the cache entry and the purge. |
| `RoomService` | Purge status: `GetPurgeHistoryStatusAsync` returns the raw DTO to the page. | Add `GetRoomDeletionStatusAsync(deleteId)` and `GetActiveRoomDeletionAsync(roomId)`. Both return a ViewModel built in `Extensions/Mapping/`, not the DTO. |
| `RoomDetails.razor.cs` `ConfirmAndDeleteRoomAsync` (l. 223) | Shows the result and goes to `/rooms` on success. | Show the result and stay on the page. On success with a `delete_id`, if the room is still the one shown, show the banner and start checking. |
| `RoomDetails.razor.cs` purge polling (l. 365–508) | `StartPolling`, `PollPurgeStatusAsync`, `ApplyPurgeStatus`, `IsCurrentPoll` (#522 guard), cap of 5 failures in a row. | Add a deletion poll built the same way (§3 E). Leave the purge code alone. |
| `RoomDetails.razor` | Tombstone banner (l. 30), purge banner (l. 46). The five Actions buttons (l. 125–129) are never disabled. | Deletion banner after the tombstone banner. Disable all five Actions buttons while a deletion or a purge runs. |
| `RoomDetails.razor.cs` `ApplyPurgeStatus` (l. 479) | A failed purge shows `PurgeFailed` ("History purge failed: {0}") with Synapse's `error`, or `UnknownError`. | Show `PurgeFailed` without the error text. `RoomService.GetPurgeHistoryStatusAsync` logs the error. `UnknownError` loses its only use and is removed. |
| Tests | `RoomServiceCharacterizationTests` "Delete" row pins `RoomDeletedSuccessfully` and the log line. `RoomDetailsActionTests.DeleteHandler_ShowsItsTexts_AndReturnsToTheRoomListOnlyOnSuccess` pins the navigation. `RoomDetailsSnackbarTests` and `RoomDetailsStaleSelectionTests` stub `DeleteRoomAsync` as `OperationResult`. Hand-written `IMatrixGateway` doubles in `UserExperimentalFeaturesTests.cs:283` and `SecurityInfrastructureTests.cs:931`. | These are intended behavior changes, not a refactor: update the tests, and keep the handler names `"DeleteRoom"` and `"DeleteAndBlockRoom"` (tests call them by name). |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: Track the `delete_id` like purge (`active_delete:{scope}:{roomId}` in `IMemoryCache`) | Same as purge, no extra request on page load | Lost on app restart; misses deletions from other sessions or tools; more shared state that needs the #522 lock | ❌ rejected |
| B: Find deletions by room ID on page load, check the status by `delete_id` | The server is the source of truth, like the tombstone; survives app restarts; no cache | One more admin `GET` per room page load; the endpoint may be missing on Tuwunel (no banner then) | ✅ chosen |
| C: Check the status by room ID only | One endpoint | Handles a list every 5 s; with several deletions it's unclear which one is shown | ❌ rejected |
| D: Go back to the room list and track the job for the whole circuit | The admin can leave the page | Needs a circuit-wide service; you decided the page behaves like purge (stays) | ❌ rejected |
| E: Extract one poller for purge and deletion now | No second polling loop | Purge polling is pinned by many tests on private names (`activePurgeId`, `ApplyPurgeStatus`, …); a behavior-preserving refactor belongs in its own PR (REFACTOR_RULES.md) | ❌ deferred: follow-up issue after this PR |

## 4. Technical Design

### Components affected

- **Gateway:** `IMatrixGateway`, `MatrixGatewayBase`, `GenericMatrixGateway`, `SynapseCompatibleAdminGateway`.
- **Models:** `Models/Responses/` (3 DTOs), `Models/ViewModels/` (status ViewModel and state enum).
- **Mapping:** `Extensions/Mapping/RoomMappingExtensions`.
- **Service:** `IRoomService`, `RoomService`.
- **UI:** `RoomDetails.razor` and `RoomDetails.razor.cs`.
- **Resources:** `SharedResources.resx`, `.de.resx`, `.fr.resx`.

### Data model changes

No storage. New types:

- `SynapseAdminRoomDeleteResponse`: `delete_id`.
- `SynapseAdminRoomDeleteStatusResponse`: `delete_id`, `room_id`, `status`, `error`. `shutdown_room` isn't read.
- `SynapseAdminRoomDeleteStatusListResponse`: `results`.
- `RoomDeletionState` enum: `Scheduled`, `Active`, `Complete`, `Failed`, `Unknown`.
- `RoomDeletionStatusViewModel`: `DeleteId`, `RoomId`, `State`. No `Error`: Synapse's error text goes only to the log.

The mapper compares the status ordinally (Synapse sends lower case). Any other value maps to `Unknown`.

### API / interface changes

All changes are internal, with no HTTP endpoint changes.

- **`IMatrixGateway`:**
  - `Task<SynapseAdminRoomDeleteResponse?> DeleteRoomAsync(roomId, request, ct)`
  - `Task<SynapseAdminRoomDeleteStatusResponse?> GetRoomDeleteStatusAsync(deleteId, ct)` calls `GET /_synapse/admin/v2/rooms/delete_status/{deleteId}`.
  - `Task<SynapseAdminRoomDeleteStatusListResponse?> GetRoomDeleteStatusesAsync(roomId, ct)` calls `GET /_synapse/admin/v2/rooms/{roomId}/delete_status`.
  - Path segments are URL-encoded. `404` and `M_UNRECOGNIZED` return `null`, and every other error propagates.
- **`SynapseCompatibleAdminGateway`:** a `SendDeleteAsync<TResponse>` overload next to the existing helper reads the JSON body. It returns `null` for an empty body and throws `JsonException` for a body that isn't JSON. The gateway has no logger, so `RoomService.DeleteRoomAsync` catches that `JsonException`, logs it at `Warning` and still reports "requested" without an ID: the homeserver accepted the deletion, so the admin must not see an error. The existing `SendDeleteAsync` stays for the other `DELETE`s.
- **`IRoomService`:**
  - `Task<OperationResult<string>> DeleteRoomAsync(...)`: same parameters, and the data is the `delete_id` (empty if missing).
  - `Task<OperationResult<RoomDeletionStatusViewModel>> GetRoomDeletionStatusAsync(deleteId, ct)`: a gateway `null` returns `Ok` with `State = Unknown`, not `Failure`. This is a deliberate exception to the null-result rule, because "unknown" is an answer, while a failure counts toward the cap of 5. A `failed` status logs Synapse's `error`, sanitized, at `Warning`.
  - `Task<OperationResult<RoomDeletionStatusViewModel?>> GetActiveRoomDeletionAsync(roomId, ct)`: returns the first active entry, else the first scheduled one, skipping entries without an ID, or `null`. A gateway `null` returns `Ok(null)`, like a collection read. A failure is logged at `Error`, like every other read, and the page ignores it.
- **`RoomDetails`:**
  - Fields `activeDeletion` and `_deletionPollingCts`.
  - Constants `RoomDeletionPollIntervalMs = 5000` and `MaxConsecutiveRoomDeletionStatusFailures = 5`.
  - Methods `StartDeletionPolling`, `StopDeletionPolling`, `PollRoomDeletionStatusAsync`, `ApplyRoomDeletionStatus` and `IsCurrentDeletionPoll`, the last one checking token and `delete_id` like #522.
  - `OnParametersSetAsync` clears the deletion state before the first `await`. After `LoadRoomDetails` succeeds, it calls `GetActiveRoomDeletionAsync` and discards a late answer (token / `_loadedRoomId`). `Dispose` cancels the poll.
  - What each status does:
    - `Scheduled` / `Active`: the banner shows that state.
    - `Complete`: snackbar `RoomDeletedSuccessfully`; stop both polls; `room = null`, `_displayedRoomId = null`, `errorMessage = L["RoomDeleted"]`, so the page shows its existing "room not found" alert with this text and the back button.
    - `Failed`: snackbar `RoomDeletionFailed` (`Error`).
    - `Unknown`: snackbar `RoomDeletionStatusUnknown` (`Warning`).
    - 5 failures in a row, or an unexpected exception: `ErrorFetchingRoomDeletionStatus`.
  - `IsRoomOperationRunning`, true when a deletion is scheduled or active, or a purge is tracked (`activePurgeId`), sets `Disabled` on all five Actions buttons.
- **Purge failure:**
  - `ApplyPurgeStatus` shows `L["PurgeFailed"]` without formatting and drops its `error` parameter. No test calls it by name; the fields `activePurgeId` and `activePurgeStatus`, which tests read, stay.
  - `RoomService.GetPurgeHistoryStatusAsync` logs a `failed` status's `error`, sanitized, at `Warning`, as the deletion status does. The DTO and the interface stay as they are.
- **New resource keys (EN/DE/FR):**
  - `RoomDeletionRequested`
  - `RoomDeletionInProgress`, `RoomDeletionScheduledDescription` and `RoomDeletionActiveDescription` (banner)
  - `RoomDeletionFailed`, `RoomDeletionStatusUnknown` and `ErrorFetchingRoomDeletionStatus`
  - `RoomDeleted` (page text)
  - `RoomDeletedSuccessfully` stays, as the completion snackbar.
- **Changed resource keys:** `PurgeFailed` loses its `{0}` in EN/DE/FR ("History purge failed.", "Bereinigung des Verlaufs fehlgeschlagen.", "La purge de l'historique a échoué."). `UnknownError` is removed.

### Configuration / feature flags

- None. The poll interval and the failure cap are named constants (AGENTS.md "Tuning Values").

## 5. Implementation Steps

One PR, with one commit per phase. Each phase builds and passes `dotnet test`.

### Phase 1: Gateway and DTOs

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | Add the three response DTOs. | `Models/Responses/` | Build |
| 1.2 | Change `DeleteRoomAsync` and add the two status reads to the interface, base and generic gateway, and to the two hand-written test doubles. `RoomService` ignores the return value for now. | `Interfaces/Gateways/IMatrixGateway.cs`, `MatrixGatewayBase.cs`, `GenericMatrixGateway.cs`, `UserExperimentalFeaturesTests.cs`, `SecurityInfrastructureTests.cs` | Build, existing tests green |
| 1.3 | Implement them in `SynapseCompatibleAdminGateway`: the `SendDeleteAsync<TResponse>` overload, the status reads, and the `404` / `M_UNRECOGNIZED` mapping. | `SynapseCompatibleAdminGateway.cs` | 1.4 |
| 1.4 | Wire tests: method, URL with encoded IDs, the body sent unchanged, `delete_id` parsed, empty body → `null`, status by ID (with and without `error`), status list, `404` / `M_NOT_FOUND` / `M_UNRECOGNIZED` → `null`, `500` propagates. Give the existing `DeleteRoomAsync` characterization row a response body. | `SynapseCompatibleGatewayCharacterizationTests.cs` (it owns the fake homeserver) | `dotnet test --filter SynapseCompatibleGatewayCharacterization` |

### Phase 2: Service, ViewModel, strings

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | `RoomDeletionState`, `RoomDeletionStatusViewModel`, and `RoomMappingExtensions.ToViewModel` for the status DTO, with a property-by-property test covering every status value, an unknown value and `null` fields. | `Models/ViewModels/`, `Extensions/Mapping/RoomMappingExtensions.cs`, `MappingExtensionsTests.cs` | Mapping tests |
| 2.2 | `DeleteRoomAsync` → `OperationResult<string>` with `RoomDeletionRequested`, new log lines, the missing-ID warning, and a caught and logged `JsonException` for a non-JSON body. | `IRoomService.cs`, `RoomService.cs` | 2.5 |
| 2.3 | `GetRoomDeletionStatusAsync` and `GetActiveRoomDeletionAsync` through `RunGatewayCallAsync`. | `IRoomService.cs`, `RoomService.cs` | 2.5 |
| 2.4 | EN/DE/FR strings. | `Resources/SharedResources*.resx` | Build; key parity check as in earlier PRs |
| 2.5 | Service tests. Update the "Delete" row and add rows for the two new methods to `RoomServiceCharacterizationTests`, with ID, without ID, each status, `null` → `Unknown`, picking the active entry, the list `null` → `Ok(null)`, and `failed` logging the error. Change the `RoomDetails*Tests` stubs to `OperationResult<string>`. The navigation assertion stays until Phase 3. | `RoomServiceCharacterizationTests.cs`, `Components/RoomDetails*Tests.cs` | `dotnet test` |

### Phase 3: Room page

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | Delete handler: no navigation; start the banner and checks when a `delete_id` came back and the room is still shown. | `RoomDetails.razor.cs` | 3.4 |
| 3.2 | Look up deletions on load, the poll loop and `ApplyRoomDeletionStatus`, with cleanup in `Dispose` and on route change. | `RoomDetails.razor.cs` | 3.4 |
| 3.3 | Banner (`Severity.Warning`, `Variant.Filled`, like the tombstone, with a spinner while active). | `RoomDetails.razor` | 3.4 |
| 3.4 | bUnit tests for: requested with and without an ID; no navigation; `scheduled` → `active` → `complete` (snackbar and "room deleted" state); `failed` (localized, no Synapse text in the markup); `Unknown`; 5 failures; an unexpected exception; banner on load from a found deletion; a failed lookup shows no banner and no snackbar; a late answer for room A after switching to B, and for an older `delete_id`. Rewrite `DeleteHandler_ShowsItsTexts_AndReturnsToTheRoomListOnlyOnSuccess` so it stays on the page. | new `Components/RoomDetailsRoomDeletionTests.cs`, `RoomDetailsActionTests.cs`, `RoomDetailsStaleSelectionTests.cs`, `RoomDetailsSnackbarTests.cs` | `dotnet test` |

### Phase 4: Room actions and purge failure

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | `IsRoomOperationRunning` and `Disabled` on all five Actions buttons. | `RoomDetails.razor`, `RoomDetails.razor.cs` | 4.4 |
| 4.2 | Purge failure: `ApplyPurgeStatus` without the error text, `PurgeFailed` without `{0}` in EN/DE/FR, `UnknownError` removed. | `RoomDetails.razor.cs`, `Resources/SharedResources*.resx` | 4.4 |
| 4.3 | `GetPurgeHistoryStatusAsync` logs a failed purge's error. | `RoomService.cs` | 4.4 |
| 4.4 | Tests: the five buttons are enabled with no operation, disabled while a deletion is scheduled or active and while a purge is tracked, and enabled again after each way an operation ends. The exception is a completed deletion, after which the page shows the room as deleted and has no buttons. Rewrite `FailedPurgeStatus_ClearsThePurge_AndReportsTheError` to check the localized message and that the error text is not in the snackbar or the markup. A service test checks that a failed purge logs the error template and its argument (`RecordingLogger`). | `Components/RoomDetailsRoomDeletionTests.cs`, `RoomDetailsStaleSelectionTests.cs`, `RoomServiceCharacterizationTests.cs` | `dotnet test` |

### Phase 5: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 5.1 | Remove the `[Planned]` tags in SPEC.md, and add a decision log row for B and the error-text choice. | `SPEC.md` | Review |
| 5.2 | ARCHITECTURE.md: room deletion and its status in the Synapse Admin API integration row (§6, l. 218). A "Room deletion status" pitfall in AGENTS.md: by room ID to find, by `delete_id` to check, `404` means unknown, status kept 24 h or until restart, `null` → `Unknown` instead of `Failure`. | `ARCHITECTURE.md`, `AGENTS.md` | Review |
| 5.3 | Reset PLAN.md and TODO.md to the templates, and open the follow-up issue for the shared poller (§3 E). | `PLAN.md`, `TODO.md` | Review |

## 6. Testing Strategy

- **Unit & bUnit component tests:** gateway wire tests (1.4), mapper (2.1), service (2.5), room page (3.4). Expected texts come from `IStringLocalizer<SharedResources>`, so the tests don't depend on the culture. Polling tests record state in the stub or await a `TaskCompletionSource` (AGENTS.md bUnit pitfalls).
- **Integration tests:** none, since no HTTP endpoint of the app changes.
- **Manual / homeserver tests:**
  - **Synapse:** delete a room with many events and watch the banner go from scheduled to active to complete; reopen the room in a second browser while the deletion runs; restart Synapse during a deletion (expect "status unknown"); Delete & Block.
  - **Tuwunel >= 1.8.1:** check the `DELETE` response and both status endpoints. If they're missing, expect only "requested".
- **Test data:** synthetic room and user IDs only.
- **Mapping to acceptance criteria:** AC-11 → `SynapseCompatibleGatewayCharacterizationTests`, `RoomServiceCharacterizationTests`, `MappingExtensionsTests`, `RoomDetailsRoomDeletionTests`; AC-5 (stale selection) → `RoomDetailsStaleSelectionTests`.

## 7. Rollout & Rollback

- **Deployment approach:** ships with the next release, with no flag.
- **Migration steps:** none, because nothing is persisted.
- **Rollback plan:** revert the PR. No state is left behind.
- **Monitoring after release:** the new log lines "Requested deletion of room…", missing `delete_id`, and failed deletion with Synapse's error.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| One more admin `GET` per room page load. | A small read. A `404` / `M_UNRECOGNIZED` is mapped to "none" without an exception reaching the service, so the log stays quiet. |
| The status API is experimental and may change. | Unknown values → `Unknown` → checks stop with a message. The deletion request doesn't depend on it (SPEC §13). |
| An empty or non-JSON `DELETE` response from a compatible server. Today the body is never read, so reading it must not turn an accepted deletion into an error. Both → "requested" without a banner: an empty body as `null` from the gateway, a non-JSON body as a `JsonException` that the service catches and logs at `Warning`. Wire and service tests cover both, and the manual Tuwunel test checks the real response. |
| `RoomDetails.razor.cs` grows by about 120 lines, with a second polling loop. | It's built the same way as the purge loop. The shared poller is a follow-up issue (§3 E). |
| A second deletion from another session while one runs. | Buttons are disabled on this page. Synapse's rejection shows `ErrorDeletingRoom` (SPEC edge case). |
| The room actions stay disabled when a status check never finishes. This is new for purge, whose actions were never disabled. | The buttons depend only on the tracked operation, which `complete`, `failed`, `Unknown`, the cap of 5 failures and an unexpected exception all clear. Tests cover the buttons being enabled again after each of these. |

## 9. Open Questions

- [x] **On `complete`:** stay on the page and show "This room has been deleted." with the back button. (Decided 2026-11-23.)
- [x] **On `failed`:** a localized message only, with Synapse's error text in the log. Purge changes the same way (Phase 4). (Decided 2026-11-23.)
- [x] **Disabled during an operation:** all five Actions buttons, while a deletion or a purge runs. The per-item buttons in the media tables stay enabled. (Decided 2026-11-23.)
- [x] **Tracking:** option B, which finds deletions by room ID. (Decided 2026-11-23.)
- [x] **Banner colour:** `Warning`, like the tombstone. (Decided 2026-11-23.)

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-23 | Draft. Verified on `main` @ `0dc486b`: the code matches the triage on #518. Upstream: Synapse `admin_api/rooms.html` documents the status by room ID as well as by `delete_id`. A purge status exists only by `purge_id`. |
| 2026-11-23 | Review: all five open questions decided as in §9. Added Phase 4, which disables the actions during a deletion or a purge and applies the deletion's failure message rule to purge. |
| 2026-11-23 | Plan approved. TODO.md filled. |
| 2026-11-23 | Phase 2 done. The lookup by room logs a failure at `Error` instead of `Warning`: the table-driven failure test expects `Error` for every read, and the page shows nothing either way. When both exist it picks the active deletion before a scheduled one. New bUnit helper `DetailPageTestBase.WithData<T>(result)`, because a dispatch-proxy stub can't return one shared `OperationResult` for a method that now returns `OperationResult<string>`. Mutation checks on the active-first choice, the skipping of entries without an ID and the caught `JsonException` each fail their test. |
| 2026-11-23 | Phase 1 done. The gateway has no logger, so a non-JSON `DELETE` body throws `JsonException`, which the service catches and logs (2.2), instead of the gateway returning `null`. The wire tests went into `SynapseCompatibleGatewayCharacterizationTests`, which owns the fake homeserver, instead of a new class. Mutation checks: removing the `M_UNRECOGNIZED`, bare-404 or empty-body handling each fails its tests. |
