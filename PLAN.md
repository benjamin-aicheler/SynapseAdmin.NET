# Implementation Plan: Refuse room deletion and its status reads on Tuwunel

| Field | Value |
|---|---|
| **Status** | In Progress |
| **Spec** | [SPEC.md](./SPEC.md) (FR-5, AC-11, §10 "Room deletion status (#518)") |
| **Issue / Branch** | #559 / `ai/bugfix/issue-559-tuwunel-room-deletion` |
| **Author(s)** | Benjamin Aicheler, Claude |
| **Created** | 2026-11-24 |
| **Last updated** | 2026-11-24 |

## 1. Summary of Approach

Tuwunel (v1.8.1 and v2.0.0) serves all three room deletion routes and the same status names as Synapse. Its `complete` doesn't mean the same thing, though:
- It purges a room even when local users couldn't be evicted, where Synapse refuses to purge.
- It logs some purge errors, such as a failed event deletion, without returning them.

The app shows `complete` as "Room deleted successfully". Under ADR 0006 a deviation the gateway can't correct is refused. `TuwunelAdminGateway` therefore overrides three methods to throw `OperationNotSupportedByHomeserverException` before sending anything: `DeleteRoomAsync`, `GetRoomDeleteStatusAsync` and `GetRoomDeleteStatusesAsync`. Services and UI stay the same for every server.

The page runs the lookup by room ID (`GetActiveRoomDeletionAsync`) whenever a room opens. A refusal there is logged at `Debug` instead of `Error`, so that opening a room on Tuwunel doesn't write an error line every time. SPEC.md §10 records the result, and "not yet verified on Tuwunel" is removed.

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `Infrastructure/Gateways/SynapseAdminGateway.cs` (`DeleteRoomAsync`, `GetRoomDeleteStatusAsync`, `GetRoomDeleteStatusesAsync`) | Sends `DELETE /_synapse/admin/v2/rooms/{roomId}` and the two `GET …/delete_status` reads; Tuwunel inherits all three | None |
| `Infrastructure/Gateways/TuwunelAdminGateway.cs` | Overrides the room list sort, bulk local media deletion (#542) and registration tokens (#548) | Three new overrides that throw `OperationNotSupportedByHomeserverException`, with a doc comment that gives the reason |
| `Services/RoomService.cs` `DeleteRoomAsync` | `RunGatewayCallAsync` turns the refusal into the `OperationNotSupportedByHomeserver` warning and logs it at `Error` ("Error deleting room {RoomId}") | None: the admin asked for the deletion, and a refused destructive request may be logged at `Error`, as for #542 |
| `Services/RoomService.cs` `GetRoomDeletionStatusAsync` | Same handling; only reached with a `delete_id`, which Tuwunel can no longer give the app | None |
| `Services/RoomService.cs` `GetActiveRoomDeletionAsync` | Runs on every room open; a refusal would be logged at `Error` with the exception | `logFailure` logs `OperationNotSupportedByHomeserverException` at `Debug` (without the exception) and every other exception at `Error`, as before |
| `Components/Pages/RoomDetails.razor.cs` | `ConfirmAndDeleteRoomAsync` shows the result and polls only on success; `FindRunningDeletionAsync` ignores a failed lookup | None (verified by tests) |
| SPEC.md §10, FR-5, AC-11 | "not yet verified on Tuwunel, #559" | Record Tuwunel's behavior and the refusal; state where Synapse's retention figure comes from |
| AGENTS.md Known Pitfalls "Room Deletion Status (#518)" | No Tuwunel note | Add one sentence, as for #542 and #548 |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: Refuse all three calls on Tuwunel | Never reports a deletion as done when it isn't; matches #542; no request leaves the app | Rooms can't be deleted through the app on Tuwunel; the button is still shown (the UI doesn't vary by server) | ✅ chosen |
| B: Send `purge: false` on Tuwunel (evict and block only) | Something still happens | Does less than "Delete" says, varies per server; ruled out by ADR 0006 | ❌ |
| C: Check before the DELETE that no local users are joined | Keeps deletion | Eviction runs later inside Tuwunel's task, so the result can't be predicted; doesn't catch the swallowed purge errors | ❌ |
| D: Allow the deletion, map `complete` with `failed_to_kick_users` to `failed` | Covers failed evictions | Purge errors aren't in the response, so `complete` can still be false; there is also no `error` field, `scheduled` is missing from the room list, and the tracker lives in process memory | ❌ |
| E: Refuse only the DELETE and keep the status reads | The banner would show deletions started elsewhere | Such a deletion ends with "Room deleted successfully", which may be untrue | ❌ |
| F: Use Tuwunel's v1 deletion or `LibMatrix`'s wrappers | — | Same deletion service with the same semantics; `LibMatrix` must not change | ❌ |

## 4. Technical Design

### Components affected

- `TuwunelAdminGateway`: three overrides that throw at once, so no request is sent. `[CallerMemberName]` fills in `Operation`.
- `RoomService.GetActiveRoomDeletionAsync`: only the log level in `logFailure` changes. The result stays the `OperationNotSupportedByHomeserver` warning, which `FindRunningDeletionAsync` already ignores.

### Data model changes

None.

### API / interface changes

None. `IMatrixGateway` is unchanged, and no `Supports…` flag is added.

### Configuration / feature flags

None. There are no per-version checks (SPEC §14), so every Tuwunel version, 1.8.1 and 2.0.0 alike, is refused.

## 5. Implementation Steps

### Phase 1: Specification

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | Write the Tuwunel behavior in SPEC.md as `[Planned]`: replace the §10 sentence; add an "on Tuwunel" sentence to FR-5 and AC-11 (deletion refused after confirmation with the warning, nothing sent; opening a room shows no banner and no error); add a Decision Log entry. Qualify the retention figure: the API docs say 24 h or until a restart, and the v1.165.0 scheduler keeps finished tasks in its database for 7 days | `SPEC.md` | Review |

### Phase 2: Tests first (red)

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | Wire tests: `Tuwunel_DeleteRoom_RefusesWithoutARequest` (block true and false), `Tuwunel_GetRoomDeleteStatus_RefusesWithoutARequest`, `Tuwunel_GetRoomDeleteStatuses_RefusesWithoutARequest`. Each checks `error.Operation` and that no request was sent. The existing Synapse cases stay unchanged | `tests/…/SynapseAdminGatewayCharacterizationTests.cs` | Fail before 3.1 |
| 2.2 | Service tests: a `GatewayRefuses_ReturnsTheNotSupportedWarning` theory over `Delete`, `DeletionStatus` and `ActiveDeletion`: the result is `OperationNotSupportedByHomeserver` with `Severity.Warning`. `Delete` and `DeletionStatus` log at `Error` with the refusal; `ActiveDeletion` logs a single `Debug` entry with the room ID | `tests/…/RoomServiceCharacterizationTests.cs` | The `Debug` case fails before 3.2 |
| 2.3 | bUnit: Delete and Delete & Block with a refused result show the warning snackbar, no banner and no status call; an `ActiveDeletion` lookup refused with a warning shows no banner and no snackbar (extend `NoRunningDeletionOrFailedLookup_OnLoad…`) | `tests/…/Components/RoomDetailsRoomDeletionTests.cs` | Should already pass: the page needs no change |

### Phase 3: Implementation (green)

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | Three overrides in `TuwunelAdminGateway` with a shared comment that gives the reason (#559) | `TuwunelAdminGateway.cs` | 2.1 green |
| 3.2 | `logFailure` in `GetActiveRoomDeletionAsync`: `Debug` for the refusal, `Error` otherwise | `RoomService.cs` | 2.2 green; `dotnet build`, `dotnet test` |

### Phase 4: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | Remove the `[Planned]` tags from SPEC.md; add a Tuwunel sentence to AGENTS.md "Room Deletion Status (#518)" and correct its retention sentence the same way | `SPEC.md`, `AGENTS.md` | Review |
| 4.2 | Reset PLAN.md and TODO.md to their templates; open a PR with "Resolves #559" | `PLAN.md`, `TODO.md` | CI green |

## 6. Testing Strategy

- **Unit & bUnit component tests:** steps 2.1–2.3. Expected strings come from `IStringLocalizer`, so the tests don't depend on the culture.
- **Integration tests:** none needed; the loopback homeserver in the gateway tests checks that no request is sent.
- **Manual / homeserver tests:** none. No room is deleted on a live server. Before allowing deletion on Tuwunel again, test on a disposable homeserver that the purge stops when local users are still joined and that failed purge steps are reported (§9).
- **Mapping to acceptance criteria:** #559 AC 1 → 1.1/4.1; #559 AC 2 → 2.1 (wire), 2.2/2.3 (warning).

## 7. Rollout & Rollback

- **Deployment approach:** a normal release. Synapse behavior doesn't change.
- **Rollback plan:** revert the PR.
- **Monitoring after release:** a refused deletion appears as an `Error` entry "Error deleting room {RoomId}" with `OperationNotSupportedByHomeserverException`.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Tuwunel admins lose deletion through the app | Documented in SPEC §10. Tuwunel's own admin commands still work. Can be allowed again once Tuwunel fixes the guard (follow-up) |
| A deletion started elsewhere on Tuwunel shows no banner | Documented. It is better than a false success message; the room details still load |
| The refusal hides a real error at `Debug` | Only this exact exception type is lowered; every other exception stays at `Error` |

## 9. Open Questions

- [ ] Report the missing guard after eviction and the swallowed purge errors upstream to Tuwunel? (outside this PR)

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-24 | Plan drafted from the triage comment on #559; the user chose `Debug` for the refused lookup when a room opens. |
