# Implementation Plan: Gallery search and type filters include media without loaded metadata

| Field | Value |
|---|---|
| **Status** | In Progress |
| **Spec** | [SPEC.md](./SPEC.md): FR-6, AC-10, Edge Cases "Gallery filters and unknown metadata", §13 Risks, §14 Open Questions (all `[Planned]`, #523) |
| **Issue / Branch** | #523 / `ai/bugfix/issue-523-gallery-filter-hydration` |
| **Author(s)** | Claude Code (for Benjamin Aicheler) |
| **Created** | 2026-11-21 |
| **Last updated** | 2026-11-21 |

## 1. Summary of Approach

`RoomGalleryGrid` applies its type and search filters before the metadata of most items is known. It then loads metadata only for the items it already shows, so a match that hasn't been looked up can never qualify (#523). Instead, the grid walks the source-filtered items in room order with a cursor. Items it can classify right away (metadata already loaded, or a search that matches the `mxc://` URI) are classified at once. For the others it looks up the metadata in batches of 20 before classifying them. One load runs at most 5 batches. After that the gallery shows "Checked x of y items" with a "Check more" button until every item is checked. Without a search or type filter nothing changes: the grid shows items page by page and looks up only the displayed ones.

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `Components/Shared/RoomGalleryGrid.razor.cs` `ApplyFiltersAndResetDisplay` (l. 152–185) | Filters `_allMedia` by source, type (`MediaType`) and search (`UploadName` / `MediaId`) at once. An item without metadata fails every type filter except Files, where it passes as a "file". | Filter by source only. Reset the classification cursor, the matches (`_filteredMedia`), `_displayedMedia` and the unknown-type count. Increment `_batchGeneration` (kept). |
| `RoomGalleryGrid.razor.cs` `LoadNextBatch` (l. 187–245) | Shows the next 20 of `_filteredMedia`, then looks up metadata for those that aren't loaded yet (`_loadedMetadataMxcs`). | With a filter that needs metadata: classify segments from the cursor, look up the unknown items of each segment (≤ 20), at most `MaxLookupBatchesPerLoad` = 5 segments, or until one more page of matches is ready. Then show the next page of matches. Without such a filter: unchanged. |
| `RoomGalleryGrid.razor.cs` metadata merge (l. 214–231) | Copies metadata into `_allMedia` and adds each returned ID to `_loadedMetadataMxcs`, also when the lookup found nothing. | Move into a helper used by both paths. "Looked up, no type" is then already recorded and not retried. |
| `RoomGalleryGrid.razor` (l. 46–48, 167–180) | "No media found" when `_filteredMedia` is empty. "All loaded (n / n)" when nothing more is shown. | "No media found" and "All loaded" only when the cursor reached the end. Otherwise "Checked x of y items", plus the "Check more" button when the batch cap stopped a load. A note with the number of items hidden because their type is unknown. |
| `RoomService.GetMediaMetadataBatchAsync` (`RoomService.cs:172–210`) | One admin request per item, 10 in parallel. A failed lookup returns the item without metadata. | No change. |
| `Resources/SharedResources*.resx` | Has `NoMediaFound`, `AllLoaded`, `Loading`. | New `GalleryCheckedItems` ("Checked {0} of {1} items"), `GalleryCheckMore` ("Check more"), `GalleryUnknownTypeHidden` ("{0} items of unknown type are not shown"), in EN/DE/FR. |
| Tests bound to private names | `_displayedMedia`, `OnSourceFilterChanged`, `OnTypeFilterChanged`, the filter values `"all"`, `"local"`, `"remote"`, `"file"`. | Keep all of them (AGENTS.md "Tests Bound to Private Names"). |

The `RoomDetails` table view has no search or type filter and looks up one table page at a time. It's out of scope.

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: Classify in order with a cursor, look up in batches of 20, at most 5 batches per load, then "Check more" | Bounded homeserver load, stable room order, honest status | More state in the grid; the admin may have to click "Check more" in a large room | ✅ proposed (open question 2) |
| B: Look up every unknown item as soon as a filter is set, with a progress bar | Simplest for the admin; complete result without clicks | Thousands of admin requests at once in a large room; cancelling on every keystroke | ❌ unless the admin prefers it |
| C: Filters apply only to loaded items, and the UI says so | Trivial | Doesn't fix the bug; "No media found" stays misleading | ❌ |
| D: Classify out of order (known matches first, unknown ones later) | Shows known matches sooner | Order changes as lookups finish; harder to test | ❌ |
| E: Rely on the infinite-scroll sentinel to keep checking | No button | `IntersectionObserver` fires only when the sentinel *enters* the view. With few matches it stays visible and the search stalls. Unbounded otherwise | ❌ |

## 4. Technical Design

### Components affected

- `RoomGalleryGrid.razor.cs`:
  - **Classification:**
    - `Classify(item)` returns `Match`, `NoMatch` or `Unknown`.
    - `Unknown` means the active filter needs metadata the item doesn't have yet: a type filter other than `all`, or a search term that doesn't match the `mxc://` URI. The item also isn't in `_loadedMetadataMxcs` yet.
    - Once an item has been looked up, it's never `Unknown` again. With a type filter, an item without `MediaType` is `NoMatch` and counted in `_unknownTypeCount`. With a search, an item without `UploadName` is `NoMatch`.
    - The Files filter accepts only a known `MediaType` that isn't an image, video or audio.
  - **Cursor and state:** `_sourceMedia` holds the source-filtered items, and `_classifyCursor` is their index. `_filteredMedia` keeps its meaning: the matches found so far, in room order. A new `_lookupCapReached` (bool) drives the "Check more" button.
  - **`LoadNextBatch`:**
    1. Remember `currentGen`.
    2. While fewer than `_displayedMedia.Count + PageSize` matches are ready, the cursor isn't at the end and fewer than `MaxLookupBatchesPerLoad` segments have been looked up:
       - Take a segment from the cursor until it has `PageSize` unknown items or reaches the end.
       - Look them up through `GetMediaMetadataBatchAsync`.
       - Merge the metadata (always, because the data is valid for any filter).
       - If `currentGen != _batchGeneration`, stop without touching the display.
       - Classify the segment in order, add its matches to `_filteredMedia`, and advance the cursor.
    3. Show up to one page more of `_filteredMedia`. Then, as today, look up the metadata of displayed items that still lack it (only search-by-URI matches can).
    4. `_hasMoreToLoad = _displayedMedia.Count < _filteredMedia.Count || _classifyCursor < _sourceMedia.Count`.
  - **"Check more":** a new handler, `CheckMore`, runs `LoadNextBatch` again (one more budget of 5 batches).
  - **Generation guard:** a filter change increments `_batchGeneration` and resets `_isBatchLoading`, as today. The old loop stops after its next `await`.
- `RoomGalleryGrid.razor`: status line, "Check more" button, unknown-type note, empty state only when complete.
- `SharedResources.resx`, `.de.resx`, `.fr.resx`: three new keys.

### Data model changes

None. `RoomMediaItemViewModel` and `_loadedMetadataMxcs` are reused.

### API / interface changes

None. `IRoomService.GetMediaMetadataBatchAsync` is reused unchanged.

### Configuration / feature flags

- None. `MaxLookupBatchesPerLoad = 5` is a named constant next to `PageSize` (AGENTS.md "Tuning Values").

## 5. Implementation Steps

### Phase 1: Characterize what must not change

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | Characterization tests for unfiltered loading: 21 items → the first load looks up exactly the 20 displayed items, and scrolling looks up the 21st. Source filter only: no extra lookups. A search matching an `mxc://` URI finds an unlooked-up item. | `tests/.../Components/RoomGalleryFilterHydrationTests.cs` (new) | Pass on the current code; committed before the production change |

### Phase 2: Classification in order with bounded lookups

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | Failing tests from AC-10: the Images filter and the file-name search each find item 21. The Files filter doesn't list unlooked-up or unknown-type items. At most 5 lookup batches per load. A filter change during a lookup discards the old result (held `TaskCompletionSource`). | `RoomGalleryFilterHydrationTests.cs` | RED |
| 2.2 | `Classify`, `_sourceMedia`/`_classifyCursor`, the segment loop in `LoadNextBatch`, the metadata merge helper, `MaxLookupBatchesPerLoad`, `_unknownTypeCount`, `_lookupCapReached` | `RoomGalleryGrid.razor.cs` | 2.1 GREEN; all existing gallery tests (`RoomGalleryFilterTests`, `RoomGalleryMergeAndFilterTests`, `RoomGalleryMetadataHydrationTests`, `RoomGalleryDuplicateMediaTests`, `RoomGalleryStaleSelectionTests`, `RoomGallerySnackbarTests`, `MediaPreviewLaunchTests`, `JsDisposalTests`) pass |

### Phase 3: Honest status in the UI

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | Failing bUnit tests: "Checked x of y items" and "Check more" after the cap, and "Check more" continuing. "No media found" only when every item is checked. The unknown-type note with its count. Expected strings come from `IStringLocalizer`, so the tests are culture-independent. | `RoomGalleryFilterHydrationTests.cs` | RED |
| 3.2 | Markup, `CheckMore` handler, three resource keys in EN/DE/FR | `RoomGalleryGrid.razor`, `RoomGalleryGrid.razor.cs`, `Resources/SharedResources*.resx` | 3.1 GREEN; `dotnet build` with 0 warnings; full `dotnet test` |

### Phase 4: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | Remove the `[Planned]` tags in SPEC.md (FR-6, AC-10, the edge case, the risk), move the decided open questions to the Decision Log, and reset PLAN.md / TODO.md to their templates | `SPEC.md`, `PLAN.md`, `TODO.md` | Review |

## 6. Testing Strategy

- **Unit & bUnit component tests:** `RoomGalleryFilterHydrationTests` extends `DetailPageTestBase`. Its `IRoomService` stub records every `GetMediaMetadataBatchAsync` call (IDs and count), returns metadata from a per-item table, and can hold a batch with a `TaskCompletionSource` for the stale-filter test. The sentinel callback is simulated by invoking `OnSentinelIntersected`. Expected strings come from the localizer and are compared against `WebUtility.HtmlDecode(cut.Markup)`.
- **Integration tests:** none needed; no endpoint changes.
- **Manual / homeserver tests:** a Synapse room with more than 100 media items, including uncached remote media. Check Images, Files, file-name search, "Check more", and that the request count in the logs matches the budget.
- **Test data:** synthetic `mxc://hs/...` URIs and file names; no real user data.
- **Mapping to acceptance criteria:** AC-10 → `RoomGalleryFilterHydrationTests` (steps 1.1, 2.1, 3.1); AC-6 (deduplication) → existing `RoomGalleryDuplicateMediaTests` must keep passing.

## 7. Rollout & Rollback

- **Deployment approach:** normal release; no flag.
- **Migration steps:** none.
- **Rollback plan:** revert the PR; no data or configuration involved.
- **Monitoring after release:** `Failed to fetch metadata for media` debug logs and admin API load when filters are used in large rooms.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Admin API load when filtering a large room | 5 × 20 lookups per load, more only on "Check more"; lookups are never repeated within the grid; unfiltered loading unchanged |
| Typing a search term starts a lookup per keystroke | The search field already debounces (`DebounceInterval="300"`). Each filter change increments the generation, and an old loop stops after its current batch (at most 20 lookups) |
| Uncached remote media never gets a type | Counted and reported as "type unknown"; shown only under All; no retries |
| Tests that reach private names break | Keep `_displayedMedia`, `OnSourceFilterChanged`, `OnTypeFilterChanged` and the filter values; search `tests/` before renaming |

## 9. Open Questions

- [x] Items whose type stays unknown: hidden from every type filter with a count note. Decided 2026-11-21.
- [x] Lookup budget: at most 5 batches of 20 per load, then "Check more". Decided 2026-11-21.
- [x] Debounce the search field: already done. The field has `DebounceInterval="300"`, so no change is needed.

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-21 | Draft written from the #523 evaluation; waiting for review. |
| 2026-11-21 | Approved with the proposed answers (hide unknown types with a count note; 5 × 20 lookups per load plus "Check more"). The search field is already debounced. |
