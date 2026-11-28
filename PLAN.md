# Implementation Plan: Space children on the room page

| Field | Value |
|---|---|
| **Status** | Approved |
| **Spec** | [SPEC.md](./SPEC.md) FR-5, AC-34, §10 "Space children" |
| **Issue / Branch** | #588 (fifth and last part) / `ai/feature/issue-588-space-hierarchy` |
| **Author(s)** | Claude (for Benjamin Aicheler) |
| **Created** | 2026-11-28 |
| **Last updated** | 2026-11-28 |

## 1. Summary of Approach

For a space (`RoomType == "m.space"`), the room page reads the Admin Space Hierarchy API (`GET /_synapse/admin/v1/rooms/{roomId}/hierarchy?max_depth=1&limit=50`) after the room details and shows a "Space children" tab. The list of children comes from the space's own entry (`children_state`, first page only); the details come from the other entries of every page. "Load more" follows `next_batch` unchanged with the same `max_depth` and `limit`, and fills in details. A child without details says so, because the admin API doesn't ask other servers. This closes #588.

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| Gateways, `IMatrixGateway`, fakes | — | `GetRoomHierarchyAsync(roomId, from, limit, maxDepth, ct)`; Generic refuses; Tuwunel inherits (same semantics) |
| `Models/Responses/` | — | `SynapseAdminRoomHierarchyResponse` (`rooms`, `next_batch`), room entries with `children_state` (`state_key`, `type`, `content.via`, `content.suggested`) |
| `RoomService` | — | `GetSpaceChildrenAsync(spaceId, from, ct)` → `OperationResult<SpaceChildrenPageViewModel>` |
| `Extensions/Mapping/RoomMappingExtensions` | — | Space child (from a `m.space.child` event with a `via`) and child summary mappers |
| `RoomDetails.razor(.cs)` | Messages, Members, State, Media tabs | "Space children" tab for spaces; load after the room details with the page's token; Load more; inline failure |
| Resources | — | Tab, column headers, chips, notes, error; EN, DE, FR |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: children from the space's `children_state`, details from the entries | Lists every child, also ones this server isn't in | Two sources to merge | ✅ chosen |
| B: list only the entries the API returns | Simplest | Hides children this server isn't in, so the list looks complete when it isn't | ❌ rejected |
| C: `max_depth` unlimited (whole tree) | Sub-spaces too | Deep trees, many requests; a sub-space has its own room page with its own tab | ❌ rejected; `max_depth=1` |
| D: load when the tab is opened | No request until wanted | Tab-change handling the page doesn't have yet; one request only for spaces | ❌ rejected; load with the room |

## 4. Technical Design

### Components affected

- Gateways, `RoomService`, `RoomMappingExtensions`, view models, `RoomDetails`, resources.

### Data model changes

- `SynapseAdminRoomHierarchyResponse`, `SynapseAdminRoomHierarchyRoom`, `SynapseAdminSpaceChildEvent` (new DTOs; `content` as `JsonElement?`).
- `SpaceChildrenPageViewModel` (`Children` — first page only, `null` when the space's entry is missing; `Summaries`; `NextBatch`), `SpaceChildViewModel` (`RoomId`, `Suggested`), `SpaceChildSummaryViewModel` (`RoomId`, `Name`, `CanonicalAlias`, `JoinedMembers`, `JoinRule`, `RoomType`).

### API / interface changes

- `IMatrixGateway.GetRoomHierarchyAsync(string roomId, string? from, int limit, int maxDepth, CancellationToken)`: `from` form-encoded and left out when `null`; errors throw (no `null` case: an unknown space answers `200` with no rooms).
- `IRoomService.GetSpaceChildrenAsync(string spaceId, string? from, CancellationToken)`; constants `SpaceHierarchyLimit = 50`, `SpaceHierarchyMaxDepth = 1`; a read the page runs by itself, so `logRefusal` at `Debug`; logs name only the space.

### Configuration / feature flags

- None.

## 5. Implementation Steps

### Phase 1: Gateway

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | Wire tests: URL without and with `from` (encoding), DTO incl. `children_state`, errors throw, Tuwunel same request | `SynapseAdminGatewayCharacterizationTests` | Fail first |
| 1.2 | DTOs, gateway method, Generic refusal, fakes | Models, Gateways, fakes | 1.1 passes |

### Phase 2: Service and mappers

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | Mappers + property tests (child with/without `suggested`, event without `via` or of another type skipped, summary fields, `null` defaults) | `RoomMappingExtensions`, `MappingExtensionsTests` | Green |
| 2.2 | `GetSpaceChildrenAsync` + characterization: forwarded args, first page with and without the space's entry, later pages (no children list), the space not in `Summaries`, token passed through, cancellation, failure, refusal at `Debug` | `RoomService`, `IRoomService`, `RoomServiceCharacterizationTests` | Green |

### Phase 3: Page

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | bUnit `RoomDetailsSpaceChildrenTests`: tab and request only for spaces; rows in order with details, links and chips; child without details; Load more sends the token and fills details; button hides without a token or with a repeated one; no space entry note; failure inline without snackbar; a late answer for another room changes nothing | new test file | Fail first, then green |
| 3.2 | Tab markup, load after room details, Load more, resources EN, DE, FR | `RoomDetails.razor(.cs)`, resx | 3.1 passes |

### Phase 4: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | Finalize SPEC.md `[Planned]`, README room line, reset PLAN.md and TODO.md | Docs | Review |

## 6. Testing Strategy

- **Unit & bUnit component tests:** as in the phases; culture-independent strings; `LANG=de_DE.UTF-8 dotnet test` before pushing.
- **Integration tests:** none.
- **Manual / homeserver tests:** on Synapse and Tuwunel test servers: a space with local children, a remote child this server isn't in, a sub-space, more than 49 children (Load more). Not part of CI.
- **Test data:** synthetic IDs only.
- **Mapping to acceptance criteria:** AC-34 → the tests named in SPEC.md.

## 7. Rollout & Rollback

- **Deployment approach:** normal release.
- **Migration steps:** none.
- **Rollback plan:** revert the PR.
- **Monitoring after release:** none beyond the usual logs.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| A child without details read as a broken link | The row says "No details on this server", and the tab's hint says why |
| A token reused with other parameters (Synapse answers `400`) | Constants for `limit` and `max_depth`; the token goes back unchanged for the same room |
| A late answer for the previous room | The page's per-room token and `_loadedRoomId` check, as for the other room reads |
| Existing room page tests rely on the four tabs' order | The new tab is added last, only for spaces |

## 9. Open Questions

- [x] Children of the space only (`max_depth=1`, recommended), or the whole tree? **Direct children** (2026-11-28).

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-28 | Plan drafted after jump to date (#617). |
| 2026-11-28 | Approved; direct children only. |
