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

- Phase 2 done, see ✅ Done.

### Phase 3: Room page

- Phase 3 done, see ✅ Done.

### Phase 4: Room actions and purge failure

- [ ] [4.1] `IsRoomOperationRunning` disables all five Actions buttons during a deletion or a purge
- [ ] [4.2] Purge failure: `PurgeFailed` without `{0}` (EN/DE/FR), no error text in `ApplyPurgeStatus`, remove `UnknownError`
- [ ] [4.3] `GetPurgeHistoryStatusAsync` logs a failed purge's error
- [ ] [4.4] Tests: buttons disabled and enabled again; rewrite `FailedPurgeStatus_ClearsThePurge_AndReportsTheError`; service log test

### Phase 5: Documentation

- [ ] [5.1] SPEC.md: remove `[Planned]` tags, decision log rows
- [ ] [5.2] ARCHITECTURE.md integration row; AGENTS.md "Room deletion status" pitfall; add `DetailPageTestBase.WithData<T>` to the AGENTS.md test double list
- [ ] [5.3] Reset PLAN.md and TODO.md; open the shared-poller follow-up issue (PLAN §3 E)

## ⛔ Blocked

- [ ] 

## 📋 Backlog

- [ ] Manual test on Synapse: scheduled → active → complete, reopen during a deletion, Synapse restart mid-deletion, Delete & Block
- [ ] Manual test on Tuwunel >= 1.8.1: `DELETE` response body and both status endpoints

## 🐛 Bugs / Follow-ups Found During Work

- [ ] DE `PurgeInProgressDescription` says "…ist derzeit eine Bereinigung des Verlaufs active" (English "active" instead of "aktiv"). Fix with the purge strings in [4.2].

## ✅ Done

- [x] 2026-11-23 – SPEC `[Planned]` entries and PLAN.md approved
- [x] 2026-11-23 – [1.1–1.4] DTOs, gateway interface and implementation, wire tests in `SynapseCompatibleGatewayCharacterizationTests` (31 new cases); build 0 warnings, 1115 tests green – `c4d8849`
- [x] 2026-11-23 – [2.1–2.5] `RoomDeletionState`, `RoomDeletionStatusViewModel`, mapper, `RoomService` delete / status / lookup, EN/DE/FR strings (key parity 463/463/463), service and mapping tests, `WithData<T>` bUnit helper; build 0 warnings, 1150 tests green – `c2ca944`
- [x] 2026-11-23 – [3.1–3.4] Room page stays after a delete, deletion banner, lookup on load, status poll with the #522-style guard, "room deleted" state on completion; `RoomDetailsRoomDeletionTests` (17 cases), `DeleteHandler_ShowsItsTexts_AndStaysOnTheRoomPage`; unused `RoomListPath` and `NavigationManager` removed; build 0 warnings, 1167 tests green – not committed yet

## Definition of Done (applies to every task)

- [ ] Code implemented according to [PLAN.md](./PLAN.md) and [AGENTS.md](./AGENTS.md) conventions
- [ ] Unit, bUnit, and/or integration tests added/updated and passing (`dotnet test`)
- [ ] Solution builds cleanly with no warnings/errors introduced (`dotnet build`)
- [ ] [SPEC.md](./SPEC.md) `[Planned]` tags finalized (and [ARCHITECTURE.md](./ARCHITECTURE.md) / localization updated if applicable)
- [ ] No secrets, tokens, or real user PII committed
