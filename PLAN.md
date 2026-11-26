# Implementation Plan: Delete All Media of a User (#599)

| Field | Value |
|---|---|
| **Status** | Approved |
| **Spec** | [SPEC.md](./SPEC.md) FR-4, AC-24, §10 (deleting all of a user's media), §13 |
| **Issue / Branch** | #599 (part 2 of #585) / `ai/feature/issue-599-delete-user-media` |
| **Author(s)** | Benjamin Aicheler, Claude |
| **Created** | 2026-11-26 |
| **Last updated** | 2026-11-26 |

## 1. Summary of Approach

One gateway call deletes one page of a user's uploads (`DELETE /_synapse/admin/v1/users/{userId}/media`, always `from=0`). `UserService.DeleteAllMediaAsync` repeats it until a request deletes nothing, a cap of 100 requests, cancellation or an error, then reads the media list once (`limit=1`) to learn how many are left. It returns the deleted and remaining counts with a localized message for each outcome, and reports progress through `IProgress<int>`. The user page adds "Delete all media" next to "Quarantine all media", with a confirmation, a progress line with a Stop button, and a reload of the media tab afterwards. Tuwunel 1.8.2 and 2.0.0 serve the endpoint as Synapse does, so no Tuwunel override is needed; the generic gateway refuses.

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `IMatrixGateway`, `SynapseAdminGateway.BuildUserMediaUrl` | Reads a page of a user's media; the URL builder takes `from`, `limit`, `order_by`, `dir`. | Add `DeleteUserMediaAsync(userId, limit, ct)`: `DELETE` on the same URL with `from=0`, via `SendDeleteAsync<SynapseAdminDeleteMediaResponse>`. |
| `SynapseAdminDeleteMediaResponse` | `deleted_media`, `total` (used by the bulk deletion). | Reused. |
| `GenericMatrixGateway` | Refuses every admin method. | Refuses the new one (covered by `GenericMatrixGatewayTests` by reflection). |
| `TuwunelAdminGateway` | Overrides deviating methods only. | No change (handlers checked, SPEC §10). |
| `UserService` | `QuarantineMediaAsync(userId)`, `GetUserMediaPageAsync`, `GetUserMediaTotalsAsync`. | Add `DeleteAllMediaAsync(userId, progress, token)`. |
| `UserDetails.razor(.cs)` | Actions card: "Quarantine all media" with confirmation and `ReloadMedia()`; media tab with per-item quarantine, protect, delete. | "Delete all media" button, confirmation, progress line with Stop, disabled media actions while running, `ReloadMedia()` afterwards. |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: Loop in `UserService`, next to `QuarantineMediaAsync(userId)` | The user's other media operations are there; one service call per action | `UserService` grows by one method (~80 lines) | ✅ chosen |
| B: A new `IUserMediaService` | Smaller `UserService` | Splits the user media operations across two services | ❌ |
| C: Loop in the page | Progress is easy | Business logic in the presentation layer (AGENTS.md N-tier) | ❌ |
| D: Trust "a request deleted nothing" as "all deleted" | One request fewer | Wrong when files can't be removed (Synapse skips them, Tuwunel leaves out failures) | ❌: read the list at the end |
| E: Advance `from` by the deleted count | Mirrors paging | Skips media, because the remaining list is reordered (reference note) | ❌: always `from=0` |

## 4. Technical Design

### Components affected

- **Gateway:** `IMatrixGateway.DeleteUserMediaAsync(string userId, int limit, CancellationToken)` → `SynapseAdminDeleteMediaResponse?`; `SynapseAdminGateway` sends `DELETE BuildUserMediaUrl(userId, 0, limit, "created_ts", "b")` without a body through `SendDeleteAsync<T>`; `GenericMatrixGateway` refuses.
- **ViewModel:** `UserMediaDeletionViewModel` (`Deleted` int, `Remaining` long?, `Outcome` enum: `AllDeleted`, `SomeRemain`, `CapReached`, `Stopped`, `Failed`, `RemainingUnknown`).
- **Service:** `UserService.DeleteAllMediaAsync(string userId, IProgress<int>? progress, CancellationToken token)` → `OperationResult<UserMediaDeletionViewModel>`, constants `MediaDeletionPageSize = 100` and `MaxMediaDeletionRequests = 100`:
  - Runs inside `RunGatewayCallAsync`. While nothing is deleted yet, a cancellation or an exception propagates and gets the standard handling (silent cancellation, `ErrorDeletingUserMedia`, not-supported warning).
  - Each request: `deleted += response?.Total ?? 0`; `progress?.Report(deleted)`; stop when a request deletes `0` or the cap is reached. Cancellation is checked between requests (`token.ThrowIfCancellationRequested()`).
  - After some deletions, a cancellation or an exception ends the loop with `Stopped` (warning) or `Failed` (error, logged with the exception), without the completion read. A failed request is never repeated.
  - Otherwise, the completion read `GetUserMediaAsync(userId, 0, 1)`: `Total == 0` → `AllDeleted` (success); `> 0` → `SomeRemain` or `CapReached` (warning); a failed read → `RemainingUnknown` (warning, logged).
  - One information log at the end: user ID, deleted count, requests, outcome, remaining. No media IDs are logged.
- **UI (`UserDetails`):**
  - Button "Delete all media" (`Color.Error`) after "Quarantine all media".
  - Confirmation: `DeleteAllUserMediaConfirmation` with the user and the count (from the totals if loaded, else the page's `TotalCount`, else without a number), plus `DeleteAllUserMediaHint` (protected, quarantined and avatar media included; can't be undone).
  - After the confirmation and `IsStillDisplayed`: `_mediaDeletion` state (target, a CTS linked to `_cts`, deleted so far). The page shows "Deleting media of {user}: {n} deleted so far" and a Stop button; the progress callback updates it through `InvokeAsync(StateHasChanged)`.
  - While it runs: "Delete all media", "Quarantine all media" and the per-item media actions are disabled.
  - Afterwards: `Snackbar.ShowResult`, clear the state, and `ReloadMedia()` if the user is still shown. Navigation to another user doesn't stop it (the deletion was confirmed for that user); the result snackbar names the user. Closing the page cancels it through `_cts`.
- **Resources (EN/DE/FR):** `DeleteAllMedia`, `DeleteAllUserMediaTitle`, `DeleteAllUserMediaConfirmation` (with and without a count), `DeleteAllUserMediaHint`, `DeletingUserMedia`, `Stop`, `UserMediaAllDeleted`, `UserMediaSomeRemain`, `UserMediaCapReached`, `UserMediaDeletionStopped`, `UserMediaDeletionFailed`, `UserMediaRemainingUnknown`, `ErrorDeletingUserMedia`.

### Data model changes

None.

### API / interface changes

`IMatrixGateway.DeleteUserMediaAsync`; `IUserService.DeleteAllMediaAsync`. No breaking change.

### Configuration / feature flags

- None. Page size and request cap are named constants in `UserService`.

## 5. Implementation Steps

### Phase 1: Gateway

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | `DeleteUserMediaAsync` on the interface, base, Synapse and Generic gateways; test fakes | Gateways, `SecurityInfrastructureTests`, `UserExperimentalFeaturesTests` | Wire tests: `DELETE /_synapse/admin/v1/users/%40alice%3aexample.com/media?from=0&limit=100&order_by=created_ts&dir=b`, no body; reads `deleted_media`/`total`; empty body → `null`; both error types; cancellation; Tuwunel sends the same request |

### Phase 2: Service

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | `UserMediaDeletionViewModel`, `UserService.DeleteAllMediaAsync` | `Models/ViewModels/`, `Services/UserService.cs`, `Interfaces/IUserService.cs` | Characterization table rows (no session, cancel/throw/refuse before any deletion); `UserMediaDeletionTests`: shrinking pages stop at `0`, every request uses `from=0`, the cap stops at 100 requests, zero deleted with media left → `SomeRemain`, cancellation and failure after some deletions → `Stopped` / `Failed` without the completion read, a failed completion read → `RemainingUnknown`, progress reports, `null` response counts as 0, logs without media IDs |

### Phase 3: UI

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | Resources EN/DE/FR | `Resources/` | Build; tests compare localized strings |
| 3.2 | Button, confirmation, progress and Stop, disabled actions, reload | `UserDetails.razor(.cs)` | `UserDetailsDeleteAllMediaTests`: confirmation text with and without a count, cancel sends nothing, changed user aborts, progress line and Stop cancel the token, actions disabled while running, result snackbar, media reload |

### Phase 4: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | Remove `[Planned]`, decision log (always `from=0`, completion read, cap, location in `UserService`) | `SPEC.md` | Review |
| 4.2 | README feature list, ARCHITECTURE integration list, AGENTS.md Known Pitfalls (this endpoint spares nothing, unlike #542) | `README.md`, `ARCHITECTURE.md`, `AGENTS.md` | Review |
| 4.3 | Update the text of #599 (service location) and reset PLAN.md / TODO.md | GitHub, repo | |

## 6. Testing Strategy

- **Unit & bUnit component tests:** as listed per step, culture-independent.
- **Integration tests:** none new.
- **Manual / homeserver tests:** on a Synapse test server, a user with more than 100 uploads including a protected and a quarantined one: all deleted, the totals show 0; Stop in the middle reports the partial count.
- **Test data:** made-up user and media IDs.
- **Mapping to acceptance criteria:** AC-24 → `SynapseAdminGatewayCharacterizationTests`, `GenericMatrixGatewayTests`, `UserServiceCharacterizationTests`, `UserMediaDeletionTests`, `UserDetailsDeleteAllMediaTests`.

## 7. Rollout & Rollback

- **Deployment approach:** regular release, no configuration.
- **Migration steps:** none.
- **Rollback plan:** revert the PR.
- **Monitoring after release:** the information and error logs of the new method.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| The admin expects protected media or avatars to be kept | Confirmation names them (SPEC §13) |
| A request that deletes nothing is read as "all deleted" | Completion read; "all deleted" only for an empty list |
| A long run (up to 10,000 uploads) looks stuck | Progress line with the count and a Stop button |
| A page of undeletable files makes every request return 0 | The loop stops at the first request that deletes nothing and reports how many are left |

## 9. Open Questions

- [x] Navigating to another user doesn't stop a running deletion: it was confirmed for that user and keeps running until done, stopped or the page closes; the progress line names the user. Decided 2026-11-26.

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-26 | Checked Synapse v1.165.0 (`rest/admin/media.py` `UserMediaRestServlet.on_DELETE`, `media_repository.delete_local_media_ids`) and Tuwunel 1.8.2 / 2.0.0 (`delete_user_media.rs`): both delete the selected page without sparing protected, quarantined or avatar media, count only what they deleted, and take `from`, `limit`, `order_by`, `dir`. Synapse keeps a media row whose file it can't remove (other than `ENOENT`). |
| 2026-11-26 | Plan approved. |
