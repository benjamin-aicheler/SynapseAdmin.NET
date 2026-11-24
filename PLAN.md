# Implementation Plan: Hold every Tuwunel gateway call to the Synapse contract

| Field | Value |
|---|---|
| **Status** | In Progress |
| **Spec** | [SPEC.md](./SPEC.md) (§1 Summary, §10 Edge Cases, §11 Dependencies, §15 Decision Log), [ARCHITECTURE.md](./ARCHITECTURE.md) ADR 0006 |
| **Issue / Branch** | — / `ai/bugfix/tuwunel-admin-api-audit` |
| **Author(s)** | Benjamin Aicheler, Claude |
| **Created** | 2026-11-24 |
| **Last updated** | 2026-11-24 |

## 1. Summary of Approach

I checked every `IMatrixGateway` method that calls the Synapse Admin API against two sources:
- Tuwunel's [status page](./tuwunel-admin-api-2026-11-24.md)
- Tuwunel's handler source at v1.8.1, v1.8.2 and v2.0.0, compared with the [Synapse reference](./synapse-admin-api-2026-11-23.md)

§2 has the result. Three kinds of deviation remain:
- **Endpoints Tuwunel doesn't serve:** 15 methods. Tuwunel answers `404 M_UNRECOGNIZED`, and the admin sees the operation's ordinary error message.
- **One endpoint that deletes more than documented:** the remote media cache purge.
- **Two read quirks:** the user list ignores `order_by`, and `creation_ts` is `0`.

`TuwunelAdminGateway` refuses the first two kinds with `OperationNotSupportedByHomeserverException` before sending anything, as for #542 and #559. It refuses a user list sorted by anything but `name`, and it turns a `creation_ts` of `0` into "unknown".

Tuwunel 1.8.1 doesn't serve seven endpoints the app calls, and 1.8.2 serves all of them, so the login probe will require Tuwunel >= 1.8.2.

Some pages read data on their own when they load: the dashboard and user details. A refused read there is logged at `Debug`, as for #559, so opening those pages on Tuwunel doesn't log an error every time.

## 2. Current State Analysis

The audit covers v2.0.0 unless noted. Legend:
- ✅ matches Synapse
- 🔧 corrected in the gateway
- ⛔ refused in the gateway
- ⚠️ a read-only difference that is accepted and recorded in SPEC §10

### Already handled

| Gateway method | Endpoint | Result | Where |
|---|---|---|---|
| `GetRoomListAsync` | `GET /v1/rooms` | 🔧 `dir` reversed for the largest-first keys | #541 |
| `DeleteLocalMediaAsync` | `POST /v1/media/delete` | ⛔ upload time instead of last access, ignores `keep_profiles` | #542 |
| `CreateRegistrationTokenAsync`, `UpdateRegistrationTokenAsync` | `/v1/registration_tokens…` | ⛔ only a create or update that leaves the token invalid | #548 |
| `DeleteRoomAsync`, `GetRoomDeleteStatusAsync`, `GetRoomDeleteStatusesAsync` | `/v2/rooms/…` | ⛔ purges after failed evictions, hides failed purge steps | #559 |
| `GetRoomMessagesAsync` | `GET /v1/rooms/{id}/messages` | ✅ the first page without `from` | #549 |
| `DeleteMediaAsync` | `DELETE /v1/media/{server}/{id}` | ✅ no body | #550 |
| `GetFederationDestinationListAsync` | `GET /v1/federation/destinations` | ✅ `next_token` paging (from 1.8.2) | #551 |
| `LoginAsUserAsync` | `POST /v1/users/{id}/login` | ✅ from 1.8.2 | #558 |
| `GetUserDetailsAsync` for a missing user | `GET /v2/users/{id}` | ✅ `404 M_NOT_FOUND` | #566 |

### Matches Synapse (no change)

| Gateway method | Endpoint | Checked |
|---|---|---|
| `GetUserDetailsAsync` | `GET /v2/users/{id}` | Fields as documented, except `creation_ts` (see below) |
| `UpdateUserAsync` (create only) | `PUT /v2/users/{id}` | The app sends `password`, `displayname`, `admin` and `deactivated`, which Tuwunel honors. Tuwunel ignores `user_type`, `external_ids` and `approved` and differs on reactivation; the app sends neither |
| `DeactivateUserAsync` | `POST /v1/deactivate/{id}` | Leaves rooms, clears the profile. `erase` marks the user erased and hides their events from later joiners |
| `GetUserMembershipsAsync` | `GET /v1/users/{id}/memberships` | Room → membership map |
| `GetUserMediaAsync` | `GET /v1/users/{id}/media` | Offset paging, `order_by=created_ts` and `dir` honored. `last_access_ts` is always 0; the app doesn't show it |
| `GetUserMediaStatisticsAsync` | `GET /v1/statistics/users/media` | `order_by`, `dir` and `search_term` (localpart or display name) |
| `SendServerNoticeAsync` | `POST /v1/send_server_notice` | Notice room created on demand, as in Synapse |
| `GetRoomDetailsAsync`, `GetRoomMembersAsync`, `GetRoomStateAsync` | `GET /v1/rooms/{id}[/members, /state?type=]` | Documented fields, joined members, state filtered by type |
| `BlockRoomAsync` | `PUT /v1/rooms/{id}/block` | Blocks joins for local non-admins, as in Synapse |
| `GetRoomMediaListAsync` | `GET /v1/room/{id}/media` | `url` and `info.thumbnail_url` of unencrypted events, duplicates kept (#384) |
| `PurgeRoomHistoryAsync`, `GetPurgeHistoryStatusAsync` | `POST /v1/purge_history/{id}`, `GET …_status/{id}` | Keeps state events and the boundary event; `delete_local_events` honored; `error` on failure |
| `ResetFederationConnectionTimeoutAsync` | `POST /v1/federation/destinations/{d}/reset_connection` | `404` for an unknown destination, `M_UNKNOWN` when there's nothing to reset |
| `FetchEventAsync` | `GET /v1/fetch_event/{id}` | Raw event, soft-failed marker as in Synapse |
| `GetMediaMetadataAsync` | `GET /v1/media/{server}/{id}` | `404` for unknown media; quarantine fields always unset |
| `GetSynapseVersionAsync` | `GET /v1/server_version` | Unauthenticated |
| `GetRegistrationTokensAsync`, `DeleteRegistrationTokenAsync` | `/v1/registration_tokens…` | As in Synapse (#548 covers the invalid tokens) |

### To change

| Gateway method | Endpoint | Tuwunel (v2.0.0 status) | Planned |
|---|---|---|---|
| `QuarantineMediaAsync`, `UnquarantineMediaAsync` | `POST /v1/media/(un)quarantine/{server}/{id}` | 🟥 not served | ⛔ |
| `QuarantineMediaByRoomIdAsync` | `POST /v1/room/{id}/media/quarantine` | 🟥 | ⛔ |
| `QuarantineMediaByUserIdAsync` | `POST /v1/user/{id}/media/quarantine` | 🟥 | ⛔ |
| `ProtectMediaAsync`, `UnprotectMediaAsync` | `POST /v1/media/(un)protect/{id}` | 🟥 | ⛔ |
| `GetEventReportListAsync`, `DeleteEventReportAsync` | `/v1/event_reports…` | 🟥 | ⛔ (the list is a read: `Debug`) |
| `RenewAccountValidityAsync` | `POST /v1/account_validity/validity` | 🟥 | ⛔ |
| `GetUserExperimentalFeaturesAsync`, `SetUserExperimentalFeaturesAsync` | `/v1/experimental_features/{id}` | ⬛ not applicable | ⛔ (the read: `Debug`) |
| `GetBackgroundUpdatesStatusAsync`, `SetBackgroundUpdatesEnabledAsync`, `StartBackgroundUpdatesJobAsync` | `/v1/background_updates/…` | ⬛ | ⛔ (the status read: `Debug`) |
| `GetLargestRoomsAsync` | `GET /v1/statistics/database/rooms` | ⬛ | ⛔ (read: `Debug`) |
| `PurgeRemoteMediaCacheAsync` | `POST /v1/purge_media_cache` | ✅ served, but selects by the file's modification time (≈ download), not by last access | ⛔ deletes recently used remote media that Synapse keeps |
| `GetUserListAsync` | `GET /v3/users` | ✅ served, but ignores `order_by` and always sorts by user ID | ⛔ when `orderBy != "name"`; `name` passes through |
| `GetUserListAsync`, `GetUserDetailsAsync` | `GET /v3/users`, `GET /v2/users/{id}` | `creation_ts` is `0` (Tuwunel stores none; 1.8.1 omits it, which the app also reads as `0`) | 🔧 `0` becomes `null`, shown as "unknown" |

### Accepted read-only differences (SPEC §10 only)

| Endpoint | Difference | Why it is accepted |
|---|---|---|
| `GET /v1/rooms?search_term=` | Matches a room ID only when equal to the search term lowercased, so a mixed-case ID never matches; matches the whole alias instead of its localpart | Read-only; the gateway can't correct it without loading every room |
| `GET /v3/users?name=` | Case-sensitive substring | The Synapse docs don't specify case |
| `GET /v3/users` before 2.0.0 | Can list non-local accounts, fixed in Tuwunel 2.0.0 (`062bf561`) | Read-only; filtering in the gateway would break totals and paging |
| Purge status after a restart | Tuwunel keeps tasks in memory only | Already reported as "status unknown", as after Synapse's retention ends |

### Tuwunel 1.8.1

v1.8.1 doesn't serve these seven endpoints: `GET /v1/media/{server}/{id}`, `GET /v1/room/{id}/media`, `POST /v1/purge_media_cache`, `GET /v1/statistics/users/media`, `GET /v1/federation/destinations`, `POST /v1/send_server_notice` and `POST /v1/users/{id}/login`. v1.8.2 (2026-07-17) serves all of them; only `PUT /v1/users/{id}/admin` is still planned, and the app doesn't call it. `MatrixAuthGateway.IsTuwunelAdminCapable` accepts `>= 1.8.1` (`MatrixAuthGateway.cs:292`).

### Code affected

| Area / file | Current behaviour | Required change |
|---|---|---|
| `Infrastructure/Gateways/TuwunelAdminGateway.cs` | Overrides for #541, #542, #548, #559 | 16 refusing overrides; refusal of `GetUserListAsync` with any key but `name`; `creation_ts` 0 → `null` in `GetUserListAsync` and `GetUserDetailsAsync` |
| `Infrastructure/Gateways/MatrixAuthGateway.cs` | Tuwunel >= 1.8.1 gets the admin API | >= 1.8.2 |
| `Models/Responses/SynapseAdminUserListResult.cs` | `long CreationTs` | `long? CreationTs` |
| `Extensions/Mapping/UserMappingExtensions.cs`, `UserListViewModel`, `UserDetailViewModel` | `long CreationTs`, `DateTimeOffset CreationTsDateTime` | Nullable. The ms (list) and s (details) conversions stay as they are (AGENTS.md) |
| `Users.razor`, `UserDetails.razor`, `Home.razor` | Show `CreationTsDateTime` | A localized "unknown" when it is `null` |
| `Extensions/GatewayCallExtensions.cs` | `logFailure` receives every exception, the refusal included | Optional `logRefusal` callback for the refusal; without it, behavior is unchanged |
| `RoomService.GetActiveRoomDeletionAsync` | Its own `if` for the refusal (#559) | Uses `logRefusal` |
| `EventReportService` (list), `RoomService.GetLargestRoomsAsync`, `BackgroundUpdatesService` (status), `UserService.GetUserExperimentalFeaturesAsync`, `UserService.GetUserListAsync` | A refusal is logged at `Error` | `logRefusal` at `Debug` |
| `UserService.GetUserDetailsAsync` (`FetchExperimentalFeaturesSafeAsync`) | Logs every failure at `Warning` and continues | Refusal at `Debug` |
| `Components/Pages/Home.razor.cs` | Total users and latest users come from one `creation_ts` request | Total users from its own `name` request (`limit=1`, as the room count does), so that the count stays when the latest-users read is refused |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: Refuse unserved endpoints in the gateway before sending | Localized warning instead of an error; no request; one rule for every gap | 16 small overrides | ✅ chosen |
| B: Let Tuwunel answer `404 M_UNRECOGNIZED` | No code | The admin sees "error …", as if something broke | ❌ |
| C: Keep 1.8.1 and turn `M_UNRECOGNIZED` into a refusal | Covers future gaps | `404` already means "none" for status reads and triggers the media fallback; per-method care | ❌ (user chose ≥ 1.8.2) |
| D: Sort the user list in the gateway (fetch every user, sort, page) | Correct order | One request returns every account; `creation_ts` can't be sorted at all | ❌ |
| E: Allow the remote cache purge on Tuwunel | Remote media can be fetched again | Deletes more than the confirmation says (#542 rule) | ❌ |
| F: Hide unsupported pages or buttons per server | Fewer clicks into a warning | UI would vary by server (ADR 0006) | ❌ |

## 4. Technical Design

### Components affected

- **Refusals:** each override is `=> throw new OperationNotSupportedByHomeserverException();`, as in the existing ones. The `mxc` overloads in `MatrixGatewayBase` call the `(serverName, mediaId)` methods, so overriding those four is enough. `GetUserListAsync` throws only when `orderBy` isn't `name` (ordinal), so the sort is checked before anything is sent.
- **`creation_ts`:** the Tuwunel overrides call the base method and set `CreationTs = null` when it is `0`. The mapping keeps `FromUnixTimeMilliseconds` (list) and `FromUnixTimeSeconds` (details) for a value, and passes `null` through.
- **`RunGatewayCallAsync`:** a new optional parameter `Action<OperationNotSupportedByHomeserverException>? logRefusal = null`. If it is given, a refusal calls it instead of `logFailure`. The returned result is the same warning either way.

### Data model changes

`SynapseAdminUserListResultUser.CreationTs` becomes `long?`. JSON with a number reads as before. A missing field or `null` reads as `null`, where it was `0` before.

### API / interface changes

`IMatrixGateway` is unchanged. `MatrixAuthGateway` accepts Tuwunel >= 1.8.2.

### Configuration / feature flags

None.

## 5. Implementation Steps

### Phase 1: Specification

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | `[Planned]` in SPEC.md: Tuwunel >= 1.8.2 (§1, §11, AC for the probe); §10 bullets for unserved operations, remote cache purge, user list sort and unknown creation dates, and the accepted read-only differences; update the #558 sentence on 1.8.1; Decision Log rows | `SPEC.md` | Review |

### Phase 2: Gateway (tests first)

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | Wire tests: each refused Tuwunel method throws with its `Operation` name and sends no request; `GetUserListAsync` with `name` sends the Synapse request, with any other key refuses; `creation_ts` 0 → `null` on list and details, other values unchanged; the Synapse cases unchanged | `SynapseAdminGatewayCharacterizationTests.cs` | Red |
| 2.2 | Probe tests: Tuwunel 1.8.1 → no admin API; 1.8.2, 1.9.0 and 2.0.0 → `TuwunelAdminGateway`; an unparseable version stays capable | `MatrixAuthGatewayProbeTests.cs` | Red |
| 2.3 | Overrides, the version threshold, nullable `CreationTs` and its mapping | `TuwunelAdminGateway.cs`, `MatrixAuthGateway.cs`, DTO, mapper, view models | 2.1, 2.2 and `MappingExtensionsTests` green |

### Phase 3: Logging and pages (tests first)

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | `GatewayCallExtensionsTests`: with `logRefusal` only it is called for a refusal; without it, `logFailure` is, as before | `GatewayCallExtensionsTests.cs`, `GatewayCallExtensions.cs` | Green |
| 3.2 | Service tests: the refused reads listed in §2 log one `Debug` entry and return the warning; refused actions still log at `Error`; #559's test still holds | `*ServiceCharacterizationTests.cs` | Green |
| 3.3 | Dashboard: total users from a `name` request; a refused latest-users read leaves the card empty without a snackbar | `Home.razor.cs`, its bUnit test | Green |
| 3.4 | "Unknown" creation date in `Users`, `UserDetails` and `Home`, as a new resource key in EN, DE and FR | `.razor`, `SharedResources*.resx`, bUnit | Green; `dotnet build`, `dotnet test` |

### Phase 4: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | Remove the `[Planned]` tags; Tuwunel >= 1.8.2 in AGENTS.md, ARCHITECTURE.md (§6.1 capability detection) and README.md; a Known Pitfall listing what Tuwunel refuses and why | Docs | Review |
| 4.2 | Reset PLAN.md and TODO.md; open the PR | `PLAN.md`, `TODO.md` | CI green |

## 6. Testing Strategy

- **Unit & bUnit component tests:** steps 2.1–3.4. Expected strings come from `IStringLocalizer`; the log tests use `RecordingLogger` and assert templates.
- **Integration tests:** none. The gateway wire tests use the loopback homeserver.
- **Manual / homeserver tests:** none. Everything rests on the source of Tuwunel v1.8.1, v1.8.2 and v2.0.0 and on its status page.
- **Mapping to acceptance criteria:** every ⛔ row → 2.1; ≥ 1.8.2 → 2.2; the `Debug` reads → 3.2; dashboard → 3.3; unknown date → 2.1 and 3.4.

## 7. Rollout & Rollback

- **Deployment approach:** a normal release. Synapse behavior doesn't change, except that the dashboard sends one more request (the user count).
- **Breaking change:** a Tuwunel 1.8.1 server loses the admin pages; the release notes tell its admins to update to ≥ 1.8.2.
- **Rollback plan:** revert the PR.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| A later Tuwunel release serves one of the refused endpoints, and the app still refuses it | Refusals are per method and documented with their status-page entry, so lifting one is one deleted override. Re-check the status page with each Tuwunel release |
| The audit misread a handler | Each finding names its file; the wire tests pin the app's side. No live server test |
| Admins click a sort header on `/users` and get a warning on Tuwunel | Matches ADR 0006: the UI stays the same, and the warning says why |

## 9. Open Questions

- [x] Issue number: none for this task (user, 2026-11-24).

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-24 | Audit done against Tuwunel v1.8.1, v1.8.2 and v2.0.0. The user chose: require ≥ 1.8.2, refuse the remote cache purge, refuse user list sorts other than `name`. |
