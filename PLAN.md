# Implementation Plan: Jump to a date in room messages

| Field | Value |
|---|---|
| **Status** | Approved |
| **Spec** | [SPEC.md](./SPEC.md) FR-5, AC-33, §10 "Jump to date" |
| **Issue / Branch** | #588 (fourth part) / `ai/feature/issue-588-jump-to-date` |
| **Author(s)** | Claude (for Benjamin Aicheler) |
| **Created** | 2026-11-28 |
| **Last updated** | 2026-11-28 |

## 1. Summary of Approach

The Messages tab of the room page gets a date field, a time field and a "Jump to date" button. `RoomService.GetRoomMessagesAtAsync` asks `timestamp_to_event` for the last event at or before that time (`dir=b`, then `dir=f` if there is none), reads the event context around it (`limit=100`) through a typed gateway read, and returns the room messages newest first (newer, the event, older) with two tokens: the context's `start` to page older (the existing "Load more", `dir=b`) and its `end` to page newer (a new "Load newer", `dir=f`). The existing message mapping moves from `RoomService` into `RoomMappingExtensions` so both reads share it. "Refresh" returns to the newest messages.

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `SynapseAdminGateway.GetRoomMessagesAsync` | `/messages` with optional `from`, `dir`, `limit` (`from` form-encoded) | Unchanged; used with the context tokens |
| `SynapseAdminGateway.GetEventContextAsync` (#616) | Context as `JsonObject` events for Forensics | Same request through a private generic helper; new typed `GetRoomMessageContextAsync` returns `SynapseAdminEvent` lists |
| Gateways, `IMatrixGateway`, fakes | — | New `GetEventForTimestampAsync(roomId, ts, dir)` (`null` on `404`) and `GetRoomMessageContextAsync(roomId, eventId, limit)`; Generic refuses both; Tuwunel inherits |
| `RoomService.GetRoomMessagesAsync` (`:476`) | Maps `m.room.message` events with an inline initializer | Mapping moves to `RoomMappingExtensions.ToMessageItemViewModels` (behavior unchanged); new `GetRoomMessagesAtAsync` |
| `RoomMessagesViewModel` | `Messages`, `EndToken` | Add `NewerToken`, `AnchorEventId` |
| `RoomDetails.razor(.cs)` Messages tab | Refresh, Load more (older), newest-first table | Date/time fields, Jump button, Load newer, anchor highlight, "jumped to" line; dedupe by event ID when adding pages |
| Resources | — | Labels, button texts, the "jumped to" line, the no-event warning, the error; EN, DE, FR |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: list both sides of the time (context `limit=100`), page older and newer | Shows what happened around the time; both tokens come with one read | One more button | ✅ chosen |
| B: list only the time and older | Matches the tab's one-direction paging | Hides what came right after, which an investigation usually needs | ❌ rejected |
| C: context with `limit=0`, then `/messages` from `start` | Fewer events read | Tuwunel 2.0.0 may treat `0` as its default; two reads instead of one | ❌ rejected |
| D: convert the Forensics context's `JsonObject` events in the service | No new gateway method | JSON conversion outside the gateway (AGENTS.md N-tier rule) | ❌ rejected; typed gateway read |

## 4. Technical Design

### Components affected

- Gateways, `RoomService`, `RoomMappingExtensions`, `RoomMessagesViewModel`, `RoomDetails` Messages tab, resources.

### Data model changes

- `SynapseAdminTimestampToEventResponse` (`event_id`, `origin_server_ts`).
- `SynapseAdminRoomMessageContextResponse` (`start`, `end`, `events_before`, `event`, `events_after` as `SynapseAdminEvent`).
- `RoomMessagesViewModel.NewerToken`, `.AnchorEventId` (both `null` for the existing read).

### API / interface changes

- `IMatrixGateway.GetEventForTimestampAsync(string roomId, long ts, string dir, CancellationToken)`: `GET /_synapse/admin/v1/rooms/{roomId}/timestamp_to_event?ts=…&dir=…`; `null` for `M_NOT_FOUND` / bare `404`.
- `IMatrixGateway.GetRoomMessageContextAsync(string roomId, string eventId, int limit, CancellationToken)`: same request as `GetEventContextAsync`, typed.
- `IRoomService.GetRoomMessagesAtAsync(string roomId, DateTimeOffset at, CancellationToken)` → `OperationResult<RoomMessagesViewModel>`; no event → `Failure(L["NoMessagesAtDate"], Severity.Warning)`; `JumpContextLimit = 100` (Tuwunel's cap) as a named constant.

### Configuration / feature flags

- None.

## 5. Implementation Steps

### Phase 1: Gateway

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | Wire tests: timestamp URL (`ts`, `dir`, room encoding), DTO, `404` / `M_NOT_FOUND` → `null`, other errors throw; typed context URL and DTO; Tuwunel same requests; Forensics context tests unchanged | `SynapseAdminGatewayCharacterizationTests` | Fail first |
| 1.2 | DTOs, gateway methods (generic context helper), Generic refusal, fakes | Models, Gateways, fakes | 1.1 passes |

### Phase 2: Service and mapper

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | Move the message mapping into `RoomMappingExtensions.ToMessageItemViewModels`; property test; existing message tests unchanged | `RoomMappingExtensions`, `RoomService`, `MappingExtensionsTests` | Existing tests green |
| 2.2 | `GetRoomMessagesAtAsync`: `dir=b`, then `dir=f`; warning when neither; context `limit=100`; order newer → event → older, messages only, tokens, anchor; logs without content; characterization rows | `RoomService`, `IRoomService`, `RoomServiceCharacterizationTests` | Green |

### Phase 3: Page

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | bUnit `RoomDetailsJumpToDateTests`: button disabled until date and time are set; local time converted through `ITimeZoneService`; list order and highlight; "jumped to" line; Load more uses `start` and appends; Load newer uses `end` with `dir=f` and prepends; no duplicates; buttons hide without tokens; Refresh leaves the jump; warning or failure keeps the list; a late answer for another room changes nothing | new test file | Fail first, then green |
| 3.2 | Toolbar fields and buttons, handlers, highlight, resources EN, DE, FR | `RoomDetails.razor(.cs)`, resx | 3.1 passes |

### Phase 4: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | Finalize SPEC.md `[Planned]`, README room line, reset PLAN.md and TODO.md | Docs | Review |

## 6. Testing Strategy

- **Unit & bUnit component tests:** as in the phases; culture-independent strings; `LANG=de_DE.UTF-8 dotnet test` before pushing.
- **Integration tests:** none.
- **Manual / homeserver tests:** on Synapse and Tuwunel test servers: a time in the middle of a room's history, before its first event (falls back to `dir=f`), after its last event, and paging both ways to the ends. Not part of CI.
- **Test data:** synthetic IDs, tokens and events only.
- **Mapping to acceptance criteria:** AC-33 → the tests named in SPEC.md.

## 7. Rollout & Rollback

- **Deployment approach:** normal release.
- **Migration steps:** none.
- **Rollback plan:** revert the PR.
- **Monitoring after release:** none beyond the usual logs.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| A page from the previous view lands in the new one (a Load more answer after a jump) | The buttons are disabled while messages load; answers for another room are discarded (existing `_loadedRoomId` check); dedupe by event ID |
| A missing token silently reloads the newest messages | The buttons show only with a token; `null` is never sent as `from` for paging |
| Private names used by existing tests (`LoadMessages`, `LoadMoreMessages`, `messages`, `loadingMessages`) | Search `tests/` before renaming; keep them |
| The message mapping moves | Behavior-preserving move with the existing tests green before and after (REFACTOR_RULES.md) |

## 9. Open Questions

- [x] List the messages on both sides of the time and page both ways (recommended), or only at and before the time? **Both sides** (2026-11-28).

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-28 | Plan drafted after the event context (#616). |
| 2026-11-28 | Approved; both sides of the time. |
