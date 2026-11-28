# Implementation Plan: Event report details and filters

| Field | Value |
|---|---|
| **Status** | Approved |
| **Spec** | [SPEC.md](./SPEC.md) FR-9, AC-31, §10 "Event report details and filters" |
| **Issue / Branch** | #588 (second part) / `ai/feature/issue-588-event-report-details-filters` |
| **Author(s)** | Claude (for Benjamin Aicheler) |
| **Created** | 2026-11-28 |
| **Last updated** | 2026-11-28 |

## 1. Summary of Approach

The report list's single search box becomes three fields (room ID, reporter, reported user), carried as one `EventReportListFilter` record from the page through `IEventReportService` to `IMatrixGateway.GetEventReportListAsync`, which sends `room_id`, `user_id` and `event_sender_user_id`. A new gateway read `GetEventReportDetailsAsync` (`GET /_synapse/admin/v1/event_reports/{id}`, `null` on `M_NOT_FOUND` / `404`) feeds a new service method and a details dialog, opened from a row button. The service formats the event JSON with a static indented `JsonSerializerOptions` and never logs it. Tuwunel refuses the details read as it already refuses the list.

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `SynapseAdminGateway.GetEventReportListAsync` (`:478`) | One `searchTerm`: `!…` → `room_id`, else `user_id` | Take `EventReportListFilter?`; send each set field, form-encoded |
| `IMatrixGateway`, `MatrixGatewayBase`, Generic, Tuwunel, two test fakes | List signature with `searchTerm` | New signature; new `GetEventReportDetailsAsync` (Generic and Tuwunel throw `OperationNotSupportedByHomeserverException`) |
| `IEventReportService` / `EventReportService` | `GetEventReportsAsync(…, searchTerm)` | Take the filter (trimmed, blanks dropped); add `GetEventReportDetailsAsync(reportId)` → `OperationResult<EventReportDetailViewModel>` |
| `Models/Responses/SynapseAdminEventReportListResult.cs` | List DTO (`id` read through `GetWithCompatibilityOptionsAsync`) | New `SynapseAdminEventReportDetailsResponse` (same fields + `event_json` as `JsonObject`) |
| `Extensions/Mapping/EventReportMappingExtensions.cs` | `ToViewModel(s)` | `ToDetailViewModel(formattedEventJson)` |
| `Components/Pages/EventReports.razor(.cs)` | Search box; Inspect + Dismiss buttons | Three fields, first-page reset, count label; Details button and loading guard |
| New `Components/Pages/EventReportDetailsDialog.razor(.cs)` | — | Read-only dialog: report fields, Forensics link, `<pre>` JSON |
| `Home.razor.cs:62` | Latest reports | Unchanged (no filter) |
| Resources | `SearchUserIdOrRoomId` used only here | New labels, hints and messages in EN, DE, FR; drop the unused key |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: details in a dialog from a row button, loaded on click | No new route; list stays as it was; matches the other read-only dialogs | Not linkable | ✅ chosen |
| B: routed page `/reports/{id}` | Linkable | New detail page with the stale-selection rules; more code for one read | ❌ rejected |
| C: keep one search box plus a field selector | Smaller toolbar | Can't combine filters, which AC-31 requires | ❌ rejected |
| D: three `string?` parameters on gateway and service | No new type | Three adjacent strings are easy to swap | ❌ rejected; one `EventReportListFilter` record |

## 4. Technical Design

### Components affected

- Gateways, `IEventReportService` / `EventReportService`, `EventReports` page, new dialog, mapper, resources.

### Data model changes

- `Models/Requests/EventReportListFilter.cs`: `sealed record EventReportListFilter(string? RoomId, string? ReporterUserId, string? SenderUserId)`.
- `Models/Responses/SynapseAdminEventReportDetailsResponse.cs`: list fields plus `[JsonPropertyName("event_json")] JsonObject? EventJson`.
- `Models/ViewModels/EventReportDetailViewModel.cs`: the list fields, `RoomName`, `EventJson` (formatted string).

### API / interface changes

- `IMatrixGateway.GetEventReportListAsync(int offset, int limit, string direction, EventReportListFilter? filter = null, CancellationToken)`.
- `IMatrixGateway.GetEventReportDetailsAsync(string reportId, CancellationToken)` → `SynapseAdminEventReportDetailsResponse?`, read through `GetWithCompatibilityOptionsAsync` (integer `id`); `null` for `404` / `M_NOT_FOUND` (as `FetchEventAsync`).
- `IEventReportService.GetEventReportsAsync(int offset, int limit, SortDirection direction, EventReportListFilter? filter = null, CancellationToken)`; `GetEventReportDetailsAsync(string reportId, CancellationToken)`: `null` → `Failure(L["EventReportDetailsNotFound"], Severity.Warning)`; refusal → the usual "not supported" warning; logs carry only the report ID.

### Configuration / feature flags

- None.

## 5. Implementation Steps

### Phase 1: Gateway

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | Wire tests: each filter field, all three together, blanks left out, encoding (`+`, `:`); details URL, DTO incl. integer `id` and `event_json`, `404` / `M_NOT_FOUND` → `null`, other errors throw; Tuwunel and Generic refuse details without a request | `SynapseAdminGatewayCharacterizationTests`, `GenericMatrixGatewayTests` | Fail first |
| 1.2 | Filter record, details DTO, gateway methods and overrides, test fakes | Models, Gateways, fakes | 1.1 passes |

### Phase 2: Service and mapper

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | Mapper `ToDetailViewModel` + property test (incl. `null` → empty defaults) | `EventReportMappingExtensions`, `MappingExtensionsTests` | Green |
| 2.2 | Service: filter trimming; details read with not-found warning, indented JSON, logs without JSON or reason; characterization rows | `EventReportService`, `EventReportServiceCharacterizationTests` | Green |

### Phase 3: Page and dialog

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | bUnit `EventReportsFilterTests`: each field and all together reach the service, first-page reset, count label, dashboard unchanged | new test file | Fail first, then green |
| 3.2 | bUnit `EventReportDetailsTests`: Details opens the dialog with the report and JSON as text (an HTML `formatted_body` stays escaped), not-found warning and failure keep the list, a second click cancels the first load and a late answer opens nothing, Forensics link | new test file (real `DialogService` + `MudDialogProvider`) | Fail first, then green |
| 3.3 | Page fields and Details button; `EventReportDetailsDialog`; resources EN, DE, FR | `EventReports.razor(.cs)`, new dialog, resx | 3.1, 3.2 pass |

### Phase 4: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | Finalize SPEC.md `[Planned]`, README feature list, AGENTS.md if a new rule appears; reset PLAN.md and TODO.md | Docs | Review |

## 6. Testing Strategy

- **Unit & bUnit component tests:** as in the phases; culture-independent strings via `IStringLocalizer`; `LANG=de_DE.UTF-8 dotnet test` before pushing.
- **Integration tests:** none (no controller or auth change).
- **Manual / homeserver tests:** on a Synapse test server: a report on a live event, one on a purged event (`404` warning), each filter alone and together, the count. Not part of CI.
- **Test data:** synthetic IDs and event JSON only.
- **Mapping to acceptance criteria:** AC-31 → the tests named in SPEC.md.

## 7. Rollout & Rollback

- **Deployment approach:** normal release.
- **Migration steps:** none.
- **Rollback plan:** revert the PR.
- **Monitoring after release:** none beyond the usual logs.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Event JSON or report reasons end up in logs | Log templates name only the report ID; a characterization test asserts the log arguments |
| Event content rendered as HTML | Render in `<pre>` as text; bUnit test with an HTML `formatted_body` |
| Tests bound to `searchTerm` / `_searchTerm` / `OnSearchChanged` by name | Search `tests/` for the names before removing them (AGENTS.md "Tests Bound to Private Names") |
| Admins expect partial matches on Reported user | Field hint says the whole user ID is needed (Synapse matches exactly) |

## 9. Open Questions

- [x] Details as a dialog opened from each row (recommended), rather than a routed page `/reports/{id}`? **Dialog** (2026-11-28).

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-28 | Plan drafted after the room list filters (#614). |
| 2026-11-28 | Approved; details open in a dialog. |
