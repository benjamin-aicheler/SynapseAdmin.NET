# TODO

**Related:** [SPEC.md](./SPEC.md) · [PLAN.md](./PLAN.md) · [ARCHITECTURE.md](./ARCHITECTURE.md)
**Last updated:** 2026-11-23

Task: #518, distinguish a requested room deletion from a completed one (FR-5, AC-11). Branch `ai/bugfix/issue-518-room-deletion-status`. One PR, one commit per phase.

## 🔄 In Progress

- [ ] 

## ⏭️ Next Up

### Phase 1: Gateway and DTOs

- Phase 1 done, see ✅ Done.

### Phase 2: Service, ViewModel, strings

- [ ] [2.1] `RoomDeletionState`, `RoomDeletionStatusViewModel`, `RoomMappingExtensions.ToViewModel` + `MappingExtensionsTests`
- [ ] [2.2] `RoomService.DeleteRoomAsync` → `OperationResult<string>` with `RoomDeletionRequested`, new log lines, missing-ID warning, catch and log `JsonException` (non-JSON body)
- [ ] [2.3] `GetRoomDeletionStatusAsync` (`null` → `Unknown`; `failed` logs the error) and `GetActiveRoomDeletionAsync` (first scheduled or active entry; `null` → `Ok(null)`)
- [ ] [2.4] EN/DE/FR strings: `RoomDeletionRequested`, `RoomDeletionInProgress`, `RoomDeletionScheduledDescription`, `RoomDeletionActiveDescription`, `RoomDeletionFailed`, `RoomDeletionStatusUnknown`, `ErrorFetchingRoomDeletionStatus`, `RoomDeleted`
- [ ] [2.5] Service tests in `RoomServiceCharacterizationTests`; `RoomDetails*Tests` stubs to `OperationResult<string>`

### Phase 3: Room page

- [ ] [3.1] Delete handler stays on the page; banner and checks start when a `delete_id` came back and the room is still shown
- [ ] [3.2] Find deletions on load, poll loop, `ApplyRoomDeletionStatus`, `IsCurrentDeletionPoll`, cleanup on route change and in `Dispose`
- [ ] [3.3] Deletion banner (`Warning`, filled, after the tombstone banner)
- [ ] [3.4] `RoomDetailsRoomDeletionTests`; rewrite `DeleteHandler_ShowsItsTexts_AndReturnsToTheRoomListOnlyOnSuccess`; stale-selection tests for the deletion poll

### Phase 4: Room actions and purge failure

- [ ] [4.1] `IsRoomOperationRunning` disables all five Actions buttons during a deletion or a purge
- [ ] [4.2] Purge failure: `PurgeFailed` without `{0}` (EN/DE/FR), no error text in `ApplyPurgeStatus`, remove `UnknownError`
- [ ] [4.3] `GetPurgeHistoryStatusAsync` logs a failed purge's error
- [ ] [4.4] Tests: buttons disabled and enabled again; rewrite `FailedPurgeStatus_ClearsThePurge_AndReportsTheError`; service log test

### Phase 5: Documentation

- [ ] [5.1] SPEC.md: remove `[Planned]` tags, decision log rows
- [ ] [5.2] ARCHITECTURE.md integration row; AGENTS.md "Room deletion status" pitfall
- [ ] [5.3] Reset PLAN.md and TODO.md; open the shared-poller follow-up issue (PLAN §3 E)

## ⛔ Blocked

- [ ] 

## 📋 Backlog

- [ ] Manual test on Synapse: scheduled → active → complete, reopen during a deletion, Synapse restart mid-deletion, Delete & Block
- [ ] Manual test on Tuwunel >= 1.8.1: `DELETE` response body and both status endpoints

## 🐛 Bugs / Follow-ups Found During Work

- [ ] 

## ✅ Done

- [x] 2026-11-23 – SPEC `[Planned]` entries and PLAN.md approved
- [x] 2026-11-23 – [1.1–1.4] DTOs, gateway interface and implementation, wire tests in `SynapseCompatibleGatewayCharacterizationTests` (31 new cases); build 0 warnings, 1115 tests green – not committed yet

## Definition of Done (applies to every task)

- [ ] Code implemented according to [PLAN.md](./PLAN.md) and [AGENTS.md](./AGENTS.md) conventions
- [ ] Unit, bUnit, and/or integration tests added/updated and passing (`dotnet test`)
- [ ] Solution builds cleanly with no warnings/errors introduced (`dotnet build`)
- [ ] [SPEC.md](./SPEC.md) `[Planned]` tags finalized (and [ARCHITECTURE.md](./ARCHITECTURE.md) / localization updated if applicable)
- [ ] No secrets, tokens, or real user PII committed
