# Implementation Plan: Event context on the forensics page

| Field | Value |
|---|---|
| **Status** | Approved |
| **Spec** | [SPEC.md](./SPEC.md) FR-9, AC-32, §10 "Event context" |
| **Issue / Branch** | #588 (third part) / `ai/feature/issue-588-event-context` |
| **Author(s)** | Claude (for Benjamin Aicheler) |
| **Created** | 2026-11-28 |
| **Last updated** | 2026-11-28 |

## 1. Summary of Approach

After Event Forensics has loaded an event with a room ID, it reads the Event Context API (`GET /_synapse/admin/v1/rooms/{roomId}/context/{eventId}?limit=20`) and shows a "Surrounding events" card between the event details and the raw JSON: up to 10 events before, the event itself (marked), up to 10 after, oldest first, each linked to its own forensics view. A new gateway read returns a DTO (`null` on `404`), `EventForensicsService.GetEventContextAsync` maps it to a view model through a new mapper, and the page loads it with the same per-event cancellation token as the event, so a late answer for a previous event is discarded. Report triage gets it through the existing Forensics link of a report row and of the report details dialog. The `start` / `end` tokens are kept in the DTO for the jump-to-date part, not used yet.

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `IMatrixGateway`, `MatrixGatewayBase`, Synapse, Generic, test fakes | `FetchEventAsync` only | New `GetEventContextAsync(roomId, eventId, limit, ct)`; Generic throws `OperationNotSupportedByHomeserverException`; Tuwunel inherits Synapse's (same semantics) |
| `Models/Responses/` | — | `SynapseAdminEventContextResponse` (`start`, `end`, `events_before`, `event`, `events_after` as `JsonObject`; `state` not read) |
| `IEventForensicsService` / `EventForensicsService` | `FetchEventAsync` | `GetEventContextAsync(roomId, eventId, ct)` → `OperationResult<EventContextViewModel>`; `ContextLimit = 20`; `null` → `Failure(L["EventContextNotFound"], Severity.Warning)`; reverses `events_before` to oldest first; `logRefusal` at Debug; logs carry IDs only |
| `Extensions/Mapping/` | No forensics mapper | `EventContextMappingExtensions.ToContextItemViewModel(s)` (event ID, type, sender, state key, timestamp, `content.body` if a string, redacted flag from `unsigned.redacted_because`) |
| `Models/ViewModels/` | `EventForensicsViewModel` | `EventContextViewModel` (`Before`, `After`), `EventContextItemViewModel` |
| `Components/Pages/EventForensics.razor(.cs)` | Loads the event; cancels a superseded load (#521) | After a successful load with a room ID, load the context with the same token; card with progress bar, inline alert for a failure (no snackbar), rows with links; clear the context whenever the event is cleared or replaced |
| Resources | — | Card title, "this event", "redacted", not-found note, error, column headers if missing; EN, DE, FR |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: load the context automatically after the event | One click; the card is where the admin looks next | One more request per event | ✅ chosen |
| B: a "Show surrounding events" button | No request unless wanted | Extra click on every investigation | ❌ rejected |
| C: context also inside the report details dialog | Triage without leaving the list | Second copy of the same view; the dialog already links to Forensics | ❌ rejected (Forensics link is enough) |
| D: report context failures as snackbars | Same as other page loads | The event itself loaded fine; a snackbar for the secondary card is noise | ❌ rejected; inline alert in the card |

## 4. Technical Design

### Components affected

- Gateways, `EventForensicsService`, Forensics page, new mapper, view models, resources.

### Data model changes

- `SynapseAdminEventContextResponse`, `EventContextViewModel`, `EventContextItemViewModel` (new). No change to existing models.

### API / interface changes

- `IMatrixGateway.GetEventContextAsync(string roomId, string eventId, int limit, CancellationToken)` → `SynapseAdminEventContextResponse?`: room ID and event ID form-encoded in the path (as `FetchEventAsync` and the other room paths), `limit` in the query; `null` for `M_NOT_FOUND` or a bare `404`; every other error throws.
- `IEventForensicsService.GetEventContextAsync(string roomId, string eventId, CancellationToken)`.

### Configuration / feature flags

- None. `ContextLimit = 20` is a named constant in `EventForensicsService` (AGENTS.md "Tuning Values").

## 5. Implementation Steps

### Phase 1: Gateway

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | Wire tests: URL and encoding (`!`, `:`, `$`, `+`, `/`), DTO incl. `start`/`end` and the three event lists, `404` / `M_NOT_FOUND` → `null`, `M_FORBIDDEN` throws, Tuwunel sends the same request | `SynapseAdminGatewayCharacterizationTests` | Fail first |
| 1.2 | DTO, gateway method, Generic refusal, test fakes | Models, Gateways, fakes | 1.1 passes; `GenericMatrixGatewayTests` covers the refusal by reflection |

### Phase 2: Mapper and service

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | Mapper + property tests (message, state event, redacted, missing fields, non-string `body`) | `EventContextMappingExtensions`, `MappingExtensionsTests` | Green |
| 2.2 | Service method + characterization: no session, forwarded args (limit 20), cancellation, failure, refusal at Debug, `null` → warning, `events_before` reversed, no event content in any log | `EventForensicsService`, `EventForensicsServiceCharacterizationTests` | Green |

### Phase 3: Page

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | bUnit `EventForensicsContextTests`: context requested with the event's room and ID; no room → no request; rows oldest first with the event marked and links; body shown as text; failure/not-found inline, event still shown, no snackbar; switching events cancels the context request and a late answer shows nothing; the existing stale-selection tests keep passing | new test file | Fail first, then green |
| 3.2 | Page code and card; resources EN, DE, FR | `EventForensics.razor(.cs)`, resx | 3.1 passes |

### Phase 4: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | Finalize SPEC.md `[Planned]`, README feature line, reset PLAN.md and TODO.md | Docs | Review |

## 6. Testing Strategy

- **Unit & bUnit component tests:** as in the phases; culture-independent strings; `LANG=de_DE.UTF-8 dotnet test` before pushing.
- **Integration tests:** none.
- **Manual / homeserver tests:** on Synapse and Tuwunel test servers: an event in the middle of a room, the first event of a room (nothing before), a redacted event nearby, an encrypted room (no body). Not part of CI.
- **Test data:** synthetic IDs and events only.
- **Mapping to acceptance criteria:** AC-32 → the tests named in SPEC.md.

## 7. Rollout & Rollback

- **Deployment approach:** normal release.
- **Migration steps:** none.
- **Rollback plan:** revert the PR.
- **Monitoring after release:** none beyond the usual logs.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Message text logged or rendered as HTML | Logs name only room and event IDs; rows render `body` as text; bUnit test with HTML in the body |
| A late context answer shows another event's neighbours | Same per-event token as the event load; context cleared with the event; bUnit stale test |
| Private member names used by existing tests (`LoadEventAsync`, `eventData`, `_fetchCts`, …) | Search `tests/` before renaming; keep the names |
| An extra request per event on busy servers | One read of 20 events, only after an admin opened an event |

## 9. Open Questions

- [x] Load the context automatically after the event (recommended), or behind a "Show surrounding events" button? **Automatically** (2026-11-28).

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-28 | Plan drafted after the event report details and filters (#615). |
| 2026-11-28 | Approved; the context loads automatically. |
