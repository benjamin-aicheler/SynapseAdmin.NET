# Implementation Plan: Room list filters for public and empty rooms

| Field | Value |
|---|---|
| **Status** | Approved |
| **Spec** | [SPEC.md](./SPEC.md) FR-5, AC-30, §10 "Room list filters by server" |
| **Issue / Branch** | #588 (first part) / `ai/feature/issue-588-room-list-filters` |
| **Author(s)** | Claude (for Benjamin Aicheler) |
| **Created** | 2026-11-28 |
| **Last updated** | 2026-11-28 |

## 1. Summary of Approach

The room list gets two three-state filters, "Public rooms" and "Empty rooms" (All / Only / Exclude), next to the search box. They travel as `bool?` from the page through `IRoomService.GetRoomListAsync` and `IMatrixGateway.GetRoomListAsync` to the documented `public_rooms` and `empty_rooms` query flags of `GET /_synapse/admin/v1/rooms`. `null` leaves the flag out. Tuwunel needs no correction: it serves both flags and selects the same rooms (SPEC.md §10), so `TuwunelAdminGateway` passes them through with its existing `dir` correction. This is the first of the #588 parts; the other reads (report details and filters, event context, jump to date, space hierarchy) follow in their own PRs.

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `Interfaces/Gateways/IMatrixGateway.cs:79`, `MatrixGatewayBase.cs:216` | `GetRoomListAsync(offset, limit, orderBy, direction, searchTerm, ct)` | Add `bool? publicRooms = null, bool? emptyRooms = null` before the token |
| `SynapseAdminGateway.cs:324` | Sends `from`, `limit`, `dir`, `order_by`, optional `search_term` | Append `public_rooms` / `empty_rooms` as `true` / `false` only when set |
| `TuwunelAdminGateway.cs:26` | Reverses `dir` for largest-first keys, calls base | Forward both flags; doc comment on why no refusal is needed |
| `GenericMatrixGateway.cs:38` | Throws `OperationNotSupportedByHomeserverException` | Signature only |
| `Interfaces/IRoomService.cs:10`, `Services/RoomService.cs:53` | Forwards search and sort | Forward both flags |
| `Components/Pages/Rooms.razor(.cs)` | Search box, sortable table; "total rooms" unless a search term is set | Two `MudSelect<bool?>`; reload from the first page on change; "matching rooms" when a search term or a filter is set |
| `Home.razor.cs:61` | Dashboard room count | Unchanged (no flags) |
| Test fakes `SecurityInfrastructureTests.cs:937`, `UserExperimentalFeaturesTests.cs:289` | Implement `IMatrixGateway` | Signature only |
| `RoomServiceCharacterizationTests` `List` row | Asserts gateway args by position (token last) | Extend to the new arguments |
| `Resources/SharedResources*.resx` | — | New labels in EN, DE, FR |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: two `bool?` parameters on gateway and service | Mirrors the API flags one to one; no new type | Two more positional parameters | ✅ chosen |
| B: a `RoomListFilter` record (search, public, empty) | One parameter, room for later filters | New model type for two flags; search term would move too, touching every caller | ❌ rejected |
| C: refuse `empty_rooms` on Tuwunel (issue comment of 2026-11-26) | — | Unnecessary: both servers select the same rooms (§10) | ❌ rejected (decision log 2026-11-28) |
| D: one combined "room state" select | One control | Can't express the four combinations of two independent flags | ❌ rejected |

## 4. Technical Design

### Components affected

- Gateways (Synapse, Tuwunel, Generic) and `IMatrixGateway`; `IRoomService` / `RoomService`; `Rooms` page; resources.

### Data model changes

- None. The page keeps two `bool?` fields; the selects bind to them with three items each (`null`, `true`, `false`).

### API / interface changes

- `IMatrixGateway.GetRoomListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, bool? publicRooms = null, bool? emptyRooms = null, CancellationToken cancellationToken = default)`.
- `IRoomService.GetRoomListAsync(int offset, int limit, string orderBy, SortDirection direction, string? searchTerm = null, bool? publicRooms = null, bool? emptyRooms = null, CancellationToken token = default)`.
- Wire: `&public_rooms=true|false`, `&empty_rooms=true|false` (lowercase literals, as Synapse's `parse_boolean` requires), left out when `null`. No other caller changes: callers pass the token by name.

### Configuration / feature flags

- None.

## 5. Implementation Steps

### Phase 1: Gateway and service

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | Wire tests first: each flag absent, `true`, `false`, both together with search and sort; Tuwunel forwards both unchanged and still reverses `dir` for `joined_members` | `SynapseAdminGatewayCharacterizationTests`, `MatrixAuthGatewayProbeTests` | Fail before 1.2 |
| 1.2 | Add the parameters to `IMatrixGateway`, `MatrixGatewayBase`, the three gateways and the two test fakes; build the query; Tuwunel doc comment | Gateways, fakes | 1.1 passes |
| 1.3 | Service forwards both flags; extend the characterization `List` row | `IRoomService`, `RoomService`, `RoomServiceCharacterizationTests` | `dotnet test` green |

### Phase 2: Page

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | bUnit tests first: default sends `null`/`null`; each choice sends its flag; filters combine with search and the current sort; a filter or search change goes back to the first page; the count label switches to "matching rooms" | new `Components/RoomListFilterTests.cs` | Fail before 2.2 |
| 2.2 | Two `MudSelect<bool?>` in the toolbar, change handlers that reset to the first page and reload; label condition | `Rooms.razor`, `Rooms.razor.cs` | 2.1 passes |
| 2.3 | Resources: `PublicRoomsFilter`, `EmptyRoomsFilter`, `FilterAll`, `FilterOnly`, `FilterExclude` (names to confirm against existing keys) in EN, DE, FR | `SharedResources*.resx` | Localization tests green |

### Phase 3: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | Remove `[Planned]` from FR-5, AC-30 and §10; README feature list if it lists room list features; reset PLAN.md and TODO.md | `SPEC.md`, `README.md`, `PLAN.md`, `TODO.md` | Review |

## 6. Testing Strategy

- **Unit & bUnit component tests:** gateway URLs per flag combination (wire level, both gateway classes); service characterization row; `RoomListFilterTests` with the `DetailPageTestBase` stubs, culture-independent labels via `IStringLocalizer`. Existing `RoomListSortingTests` and `ListPageSnackbarTests` keep passing.
- **Integration tests:** none needed (no controller or auth change).
- **Manual / homeserver tests:** on a Synapse and a Tuwunel test server, Only/Exclude for each flag and the total count. Not part of CI.
- **Test data:** synthetic room IDs only.
- **Mapping to acceptance criteria:** AC-30 → `RoomListFilterTests`, `RoomServiceCharacterizationTests`, `SynapseAdminGatewayCharacterizationTests`, `MatrixAuthGatewayProbeTests`.

## 7. Rollout & Rollback

- **Deployment approach:** normal release; the default (All) sends exactly today's request.
- **Migration steps:** none.
- **Rollback plan:** revert the PR.
- **Monitoring after release:** none beyond the usual logs.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| A test reads `GetRoomListAsync` arguments by position and breaks when the token moves | Search `tests/` for `CallsTo("GetRoomListAsync")` / `nameof(IMatrixGateway.GetRoomListAsync)` and update the indexes (sort tests read 2 and 3, unaffected) |
| MudTable doesn't go back to the first page on `ReloadServerData` | Use `table.NavigateTo(0)` (or set the page before reloading) and prove it in 2.1 |
| Synapse stats lag makes a just-emptied room appear late | Documented in SPEC.md §10; no code change |

## 9. Open Questions

- [x] The search box reloads without going back to the first page today (to confirm in 2.1). Apply the same first-page reset to the search box in this PR? **Yes** (2026-11-28): search and filters both go back to the first page.

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-28 | Plan drafted after re-checking #588 (comment posted on the issue). |
| 2026-11-28 | Approved; the search box gets the same first-page reset. |
