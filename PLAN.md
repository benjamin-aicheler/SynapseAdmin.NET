# Implementation Plan: Bulk Local Media Deletion — Synapse Wording and Success Count (#542, ADR 0006 Step 3)

| Field | Value |
|---|---|
| **Status** | Approved |
| **Spec** | [SPEC.md](./SPEC.md) FR-8 (bulk local media deletion), new AC-15 and §10 edge case (to add as `[Planned]` in 1.1) |
| **Issue / Branch** | #542 / `ai/bugfix/issue-542-local-media-deletion-wording` |
| **Author(s)** | Benjamin Aicheler (AI-assisted) |
| **Created** | 2026-11-24 |
| **Last updated** | 2026-11-24 |

## 1. Summary of Approach

The Delete Local Media card on `/media-maintenance` describes the operation the way Synapse runs it, in EN, DE and FR, and in the same words on every homeserver (ADR 0006):
- **Age:** the cutoff is the last access, or the upload for media never accessed.
- **Conditions:** the age and size conditions apply together.
- **Size:** 0 MB means no minimum.
- **Avatars:** "Keep profile pictures and room avatars" is the `keep_profiles` option, and the confirmation says whether such media is kept or deleted.
- **Always kept:** protected and quarantined media.

`MediaService.DeleteLocalMediaAsync` formats the success message with the count the server reports (`Total`), so `{0}` and `{1}` no longer show. `TuwunelAdminGateway` refuses `DeleteLocalMediaAsync` with `OperationNotSupportedByHomeserverException` (step 2), because Tuwunel 1.8.1 compares the upload time and ignores `keep_profiles`, so it would delete more than the admin confirmed. The endpoint, DTO and request parameters don't change.

## 2. Current State Analysis

**Synapse v1.165.0 behavior** (checked in the source on 2026-11-24):
- **Age:** `get_local_media_ids` selects `last_access_ts < before_ts OR (created_ts < before_ts AND last_access_ts IS NULL)`. Synapse doesn't set `last_access_ts` at upload, so never-accessed media counts from its upload.
- **Size:** `AND media_length > size_gt`.
- **Avatars:** `keep_profiles` excludes media used as global or per-room profile avatars, user directory avatars and room avatars.
- **Always kept:** `delete_old_local_media` defaults `delete_quarantined_media` and `delete_protected_media` to `False`, and the admin endpoint passes neither, so quarantined and protected media are never deleted.
- **Response:** `total` is the number of deleted media IDs.

| Area / file | Current behaviour | Required change |
|---|---|---|
| `Components/Pages/MediaMaintenance.razor` (Delete Local Media card) | The age field uses `UploadedBeforeDays` ("Uploaded before (days)"). The description says "uploaded before a specific date, larger than a certain size, **or** not protected". The switch says "Keep profiles / avatar media". | The age field uses the existing `LastAccessedBeforeDays` ("Last accessed before (days)"), which the purge card already uses. New description, switch label, zero-size warning and checkbox text (§4). |
| `Components/Pages/MediaMaintenance.razor.cs` (`ConfirmDeleteLocalMedia`) | The confirmation is `DeleteLocalMediaConfirmation` with days and MB: "…uploaded more than {0} days ago and larger than {1} MB?". It doesn't mention the avatar option, and says "larger than 0 MB" for size 0. There is a stale comment at the success snackbar. | The confirmation is made of three parts: (a) the criteria, with `DeleteLocalMediaConfirmation` for size > 0 or `DeleteLocalMediaConfirmationAnySize` for size 0; (b) the avatar sentence (`DeleteLocalMediaKeepsAvatars` or `DeleteLocalMediaDeletesAvatars`); (c) the existing irreversibility warning, as its own key `DeleteLocalMediaIrreversible`. Remove the stale comment. The zero-size acknowledgement and the button's disabled conditions stay as they are. |
| `Services/MediaService.cs` (`DeleteLocalMediaAsync`) | Returns `L["DeletedLocalMediaSuccess"]` unformatted, so the snackbar shows `{0}` and `{1}`. | `string.Format(L["DeletedLocalMediaSuccess"], result.Total)`, as `PurgeRemoteMediaCacheAsync` does. The message has a single `{0}`. |
| `Infrastructure/Gateways/TuwunelAdminGateway.cs` | Inherits `DeleteLocalMediaAsync`, which sends Synapse's request; Tuwunel then deletes by upload time and ignores `keep_profiles`. | Override: `throw new OperationNotSupportedByHomeserverException();` before anything is sent. The page shows the "not supported" warning after confirmation (AC-14). |
| `Resources/SharedResources{,.de,.fr}.resx` | `UploadedBeforeDays`, `DeleteLocalMediaDescription`, `DeleteLocalMediaConfirmation`, `DeletedLocalMediaSuccess`, `KeepProfiles`, `DeleteAllSizesWarning`, `ConfirmDeleteAllSizes`. | Change the texts (§4). Add `DeleteLocalMediaConfirmationAnySize`, `DeleteLocalMediaKeepsAvatars`, `DeleteLocalMediaDeletesAvatars` and `DeleteLocalMediaIrreversible`. Remove `UploadedBeforeDays`, which becomes unused (its only user is this card). |
| Tests | No bUnit test for `MediaMaintenance`. `MediaServiceCharacterizationTests.DeleteLocal_Success_…` expects the unformatted key. `SynapseAdminGatewayCharacterizationTests` doesn't cover `DeleteLocalMediaAsync`. | See §5 and §6. |
| SPEC.md FR-8, README.md | FR-8: "local media deletion (by age, minimum size MB, and optional profile preservation)". README: "local media cleanup by age/size". | FR-8 says "by last access", and that protected and quarantined media are kept. README stays (still true). Add AC-15 and a §10 edge case for Tuwunel. |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: Synapse wording everywhere; Tuwunel refuses | Follows ADR 0006 and the 2026-11-24 decisions; nothing is deleted against the confirmation. | Tuwunel admins lose bulk local deletion in this app. | ✅ chosen (decided 2026-11-24) |
| B: Wording per server (triage plan) | Correct on both servers. | Against ADR 0006: the UI never varies by server. | ❌ rejected |
| C: Tuwunel gateway emulates Synapse | Keeps the feature. | Tuwunel stores no last-access time, and finding every avatar means scanning all profiles and rooms; the result still wouldn't match. | ❌ rejected |
| D: Keep two placeholders in the success message (`deleted_media.Count` and `Total`) | Shows both fields. | Both are the same number (`total` is the count of `deleted_media`). | ❌ rejected: one count, from `Total`, as the triage plan says |
| E: Separate message when `Total` is 0 | Reads better ("Nothing matched"). | One more key for a case the formatted message already describes correctly. | ❌ rejected (can follow later) |

## 4. Technical Design

### Components affected

- `MediaMaintenance` (`.razor` and `.razor.cs`), `MediaService`, `TuwunelAdminGateway`, and the three `.resx` files.

### Wording (EN · DE · FR)

| Key | EN | DE | FR |
|---|---|---|---|
| `LastAccessedBeforeDays` (reused, unchanged) | Last accessed before (days) | Letzter Zugriff vor (Tagen) | Dernier accès il y a plus de (jours) |
| `DeleteLocalMediaDescription` | Bulk delete local media files that were last accessed more than the given number of days ago (media never accessed counts from its upload) and that are larger than the given size. Both conditions must apply. Protected and quarantined media are always kept. This action cannot be undone. | Löscht lokale Mediendateien, auf die seit mehr als der angegebenen Anzahl von Tagen nicht zugegriffen wurde (bei nie abgerufenen Medien zählt der Upload) und die größer als die angegebene Größe sind. Beide Bedingungen müssen zutreffen. Geschützte Medien und Medien in Quarantäne bleiben immer erhalten. Diese Aktion kann nicht rückgängig gemacht werden. | Supprime en masse les fichiers médias locaux dont le dernier accès remonte à plus du nombre de jours indiqué (pour un média jamais consulté, sa mise en ligne compte) et dont la taille dépasse la taille indiquée. Les deux conditions doivent être remplies. Les médias protégés et en quarantaine sont toujours conservés. Cette action ne peut pas être annulée. |
| `KeepProfiles` | Keep profile pictures and room avatars | Profilbilder und Raum-Avatare behalten | Conserver les photos de profil et les avatars de salon |
| `DeleteAllSizesWarning` | A size of 0 MB means no minimum size: every local media file last accessed before the cutoff is deleted, whatever its size. | Eine Größe von 0 MB bedeutet keine Mindestgröße: Alle lokalen Mediendateien mit letztem Zugriff vor dem Stichtag werden gelöscht, unabhängig von ihrer Größe. | Une taille de 0 Mo signifie aucune taille minimale : tous les fichiers médias locaux dont le dernier accès précède la date limite sont supprimés, quelle que soit leur taille. |
| `ConfirmDeleteAllSizes` | I understand that files of every size will be deleted | Mir ist bewusst, dass Dateien jeder Größe gelöscht werden | Je comprends que les fichiers de toutes tailles seront supprimés |
| `DeleteLocalMediaConfirmation` | Delete all local media files last accessed more than {0} days ago and larger than {1} MB? | Alle lokalen Mediendateien löschen, auf die seit mehr als {0} Tagen nicht zugegriffen wurde und die größer als {1} MB sind? | Supprimer tous les fichiers médias locaux dont le dernier accès remonte à plus de {0} jours et dont la taille dépasse {1} Mo ? |
| `DeleteLocalMediaConfirmationAnySize` (new) | Delete all local media files last accessed more than {0} days ago, whatever their size? | Alle lokalen Mediendateien löschen, auf die seit mehr als {0} Tagen nicht zugegriffen wurde, unabhängig von ihrer Größe? | Supprimer tous les fichiers médias locaux dont le dernier accès remonte à plus de {0} jours, quelle que soit leur taille ? |
| `DeleteLocalMediaKeepsAvatars` (new) | Media used as a profile picture or room avatar is kept. | Medien, die als Profilbild oder Raum-Avatar verwendet werden, bleiben erhalten. | Les médias utilisés comme photo de profil ou avatar de salon sont conservés. |
| `DeleteLocalMediaDeletesAvatars` (new) | Media used as a profile picture or room avatar is deleted too. | Medien, die als Profilbild oder Raum-Avatar verwendet werden, werden ebenfalls gelöscht. | Les médias utilisés comme photo de profil ou avatar de salon sont également supprimés. |
| `DeleteLocalMediaIrreversible` (new) | This action is irreversible! | Diese Aktion ist unumkehrbar! | Cette action est irréversible ! |
| `DeletedLocalMediaSuccess` | Successfully deleted {0} local media files. | {0} lokale Mediendateien erfolgreich gelöscht. | {0} fichiers médias locaux supprimés avec succès. |

The confirmation is joined as `"{criteria} {avatars} {irreversible}"`, with the criteria formatted from days and MB.

### Data model changes

- None. `SynapseAdminDeleteMediaResponse` already has `Total`.

### API / interface changes

- None. The request stays `POST /_synapse/admin/v1/media/delete?before_ts=…&size_gt=…&keep_profiles=…`. On Tuwunel, no request is sent.

### Configuration / feature flags

- None.

## 5. Implementation Steps

Tests come first (TDD). Each commit builds and passes the tests.

### Phase 1: Spec

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | SPEC.md, all tagged `[Planned]`. **FR-8:** "local media deletion by last access (upload for media never accessed), minimum size and optional keeping of profile pictures and room avatars; protected and quarantined media are kept". **AC-15 (FR-8, #542):** the card's label, description and confirmation; the avatar sentence follows the switch; size 0 is described as no minimum; the success message shows `Total`; on Tuwunel the operation is refused after confirmation and nothing is sent. **§10:** an edge case for the Tuwunel refusal. | `SPEC.md` | — |

### Phase 2: Success count

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | `MediaServiceCharacterizationTests`: `DeleteLocal_Success_…` expects the message formatted with `Total`, for totals 0 and 2. The test localizer formats `DeletedLocalMediaSuccess` as it does `PurgedCacheSuccess`. | `tests/…/MediaServiceCharacterizationTests.cs` | The tests fail (red). |
| 2.2 | `string.Format(L["DeletedLocalMediaSuccess"], result.Total)`, and the new single-placeholder text in EN, DE and FR. | `MediaService.cs`, `SharedResources*.resx` | 2.1 passes. |

### Phase 3: Wording

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | New bUnit test class `MediaMaintenanceTests` (on `DetailPageTestBase`, with the expected strings built through `IStringLocalizer`). Check that: the age field's label is `LastAccessedBeforeDays`; the description and the switch label are the new texts; for size 10 MB with avatars kept, the confirmation is criteria + `DeleteLocalMediaKeepsAvatars` + irreversible; for size 0 with the acknowledgement and avatars not kept, it is `…AnySize` + `DeleteLocalMediaDeletesAvatars` + irreversible; after confirming, the real `MediaService` with a stub gateway returning `Total = 2` makes the snackbar show "…deleted 2…". The suite also runs under `de_DE`. | new `tests/…/Components/MediaMaintenanceTests.cs` | The tests fail (red). |
| 3.2 | Change the page, the code-behind and the resources as §4 says; remove `UploadedBeforeDays` from all three `.resx` files. | `MediaMaintenance.razor`, `.razor.cs`, `SharedResources*.resx` | 3.1 passes. `grep -rn UploadedBeforeDays src tests` is empty. |
| 3.3 | Resource test: every changed and new key exists in DE and FR with a translation of its own; `DeletedLocalMediaSuccess` has `{0}` and no `{1}` in all three languages; `DeleteLocalMediaConfirmation` has `{0}` and `{1}`; `…AnySize` has `{0}` and no `{1}`. | the resource test next to `NotSupportedWarning_IsTranslated` | Passes. |

### Phase 4: Tuwunel refuses

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | Wire tests in `SynapseAdminGatewayCharacterizationTests`. `SynapseAdminGateway.DeleteLocalMediaAsync` sends `POST /_synapse/admin/v1/media/delete?before_ts=…&size_gt=…&keep_profiles=true` (and `false`) with the body `{}`; this is characterization, so it passes before any change. `TuwunelAdminGateway.DeleteLocalMediaAsync` throws `OperationNotSupportedByHomeserverException` (`Operation == "DeleteLocalMediaAsync"`) and the fake homeserver records no request. | `tests/…/SynapseAdminGatewayCharacterizationTests.cs` | The Synapse test passes, the Tuwunel test fails (red). |
| 4.2 | The override in `TuwunelAdminGateway`, with a doc comment naming both deviations (upload time instead of last access, `keep_profiles` ignored). | `TuwunelAdminGateway.cs` | 4.1 passes. |

### Phase 5: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 5.1 | SPEC.md: remove the `[Planned]` tags, name the tests in AC-15, and add a decision row (#542: Synapse wording; Tuwunel refuses). | `SPEC.md` | No `[Planned]` left for #542. |
| 5.2 | AGENTS.md "Known Pitfalls": a short entry. `POST /_synapse/admin/v1/media/delete` selects by last access (upload for media never accessed), never deletes protected or quarantined media, and `keep_profiles` keeps avatars. `TuwunelAdminGateway` refuses it (#542). | `AGENTS.md` | — |
| 5.3 | Reset PLAN.md and TODO.md to the templates from `main`. | `PLAN.md`, `TODO.md` | `git diff main -- PLAN.md TODO.md` is empty. |

## 6. Testing Strategy

- **Unit tests:**
  - `MediaServiceCharacterizationTests`: the formatted count, for 0 and 2.
  - `SynapseAdminGatewayCharacterizationTests`: Synapse's request on the wire, and Tuwunel's refusal with no request sent.
  - The resource test: translations exist, and the placeholders match.
- **bUnit tests:** `MediaMaintenanceTests` checks the label, description, switch, both confirmation variants and the success snackbar, end to end through the real `MediaService`. It is culture-independent: expected strings come from the localizer and are compared with `WebUtility.HtmlDecode(cut.Markup)`.
- **Manual / homeserver tests:** if a Synapse test server is available, delete with a short cutoff and a large size, and check that the snackbar shows the reported count. On Tuwunel, check that the warning appears and nothing is deleted.
- **Test data:** stubs and fake homeservers only.
- **Acceptance criteria:** AC-15 → 2.1, 3.1, 3.3 and 4.1. AC-14 stays covered for this operation by `MediaServiceCharacterizationTests` (step 2).

## 7. Rollout & Rollback

- **Deployment approach:** normal release. Release notes mention that Tuwunel servers now refuse bulk local media deletion.
- **Migration steps:** none.
- **Rollback plan:** revert the PR.
- **Monitoring after release:** a refusal is logged under "Error deleting local media …" with `OperationNotSupportedByHomeserverException`.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Tuwunel admins who relied on this card lose it. | Deliberate (2026-11-24): it deleted more than the confirmation said. The warning explains why nothing happens; SPEC.md §10 documents it. |
| A later Synapse version changes the selection (e.g. also deletes protected media). | The wording cites what v1.165.0 does; the `synapse-admin-api-*.md` reference is re-checked when it is updated. |
| The translations are less precise than the English text. | The resource test checks that the translations and placeholders exist; the DE and FR texts are in this plan for review. |

## 9. Open Questions

- [x] Is the wording in §4 right in all three languages? Yes (2026-11-24).
- [x] Is the success message for `Total = 0` acceptable? Yes; no separate message (2026-11-24).

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-24 | Plan drafted. Synapse v1.165.0 behavior checked in `synapse/storage/databases/main/media_repository.py`, `synapse/media/media_repository.py` and `synapse/rest/admin/media.py`. |
| 2026-11-24 | Plan approved: wording as in §4, one success message for every total. |
