# Implementation Plan: User Devices and Recent Connections (#598)

| Field | Value |
|---|---|
| **Status** | Approved |
| **Spec** | [SPEC.md](./SPEC.md) FR-4, AC-23, §10 (devices entries), §13 |
| **Issue / Branch** | #598 (part 1 of #585) / `ai/feature/issue-598-user-devices` |
| **Author(s)** | Benjamin Aicheler, Claude |
| **Created** | 2026-11-26 |
| **Last updated** | 2026-11-26 |

## 1. Summary of Approach

Four new gateway methods call the Synapse Admin API directly through `ClientHttpClient`: list devices, rename a device, delete devices (`delete_devices` for one or several) and `whois`. `GenericMatrixGateway` refuses them, and `TuwunelAdminGateway` inherits Synapse's implementation, because Tuwunel 1.8.2 and 2.0.0 differ only in what the reads return (checked in their handlers). A new `IUserDeviceService` maps the answers to ViewModels and refuses deleting the app's own session device. `IMatrixGateway` gains `DeviceId`, read from the `/whoami` answer that every login and session restore already fetches. The user page gets two new tabs, Devices and Recent connections. Each tab is its own component (`UserDevicesPanel`, `UserConnectionsPanel`), so `UserDetails` (550 lines) barely grows, and MudTabs renders, and therefore loads, a panel only while its tab is open.

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `Interfaces/Gateways/IMatrixGateway.cs`, `MatrixGatewayBase.cs` | Session info: `UserId`, `Username`, `AccessToken`, … ; no device ID. `Homeserver.WhoAmI` (LibMatrix, set by `Initialise()` in `MatrixAuthGateway.CreateAuthenticatedGatewayAsync`) already holds `device_id`. | Add `string? DeviceId => Homeserver.WhoAmI.DeviceId`; add 4 abstract methods. |
| `SynapseAdminGateway.cs` | User endpoints with `.UrlEncode()`, `PostEmptyAsync`, `SendDeleteAsync`, `GetWithCompatibilityOptionsAsync`. No device or whois calls. | Implement the 4 methods. |
| `GenericMatrixGateway.cs` | Every admin method throws `OperationNotSupportedByHomeserverException`. | Same for the 4 new methods. |
| `TuwunelAdminGateway.cs` | Overrides only deviating methods. | No change: rename and delete behave as on Synapse; the read differences are accepted (SPEC §10). |
| `LoginResponse.DeviceId` | Read, then dropped. | Unchanged: `/whoami` covers password login, token login and restore alike. |
| `Services/UserService.cs` (458 lines) | No device logic. | Unchanged; a new `IUserDeviceService` / `UserDeviceService`. |
| `Components/Pages/UserDetails.razor(.cs)` | Tabs Rooms, Media, Experimental features; `IsOwnAccount` (#584). | Two more tabs that host the new panels, passing the displayed user's ID. |
| Test fakes `SecurityInfrastructureTests.StubMatrixGateway`, `UserExperimentalFeaturesTests.FakeExperimentalFeaturesGateway` | Implement `IMatrixGateway` by hand. | Add the new members (throwing `NotImplementedException`, as for their other unused members). |
| `tests/.../Components/DetailPageTestBase.cs` | Registers stubs for the services the pages use. | Register a `Stub` for `IUserDeviceService`. |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: `delete_devices` for one and for several devices | One gateway method, one wire test, the same result handling | Doesn't use the single-device `DELETE` the reference also lists | ✅ chosen: both servers implement both endpoints with the same effect (Synapse calls the same `delete_devices` handler) |
| B: `DELETE …/devices/{id}` for one, `delete_devices` for several | Mirrors the reference's two operations | Two code paths for the same effect | ❌ |
| C: Remember the device ID from `LoginResponse` in `MatrixSessionService` | No dependency on LibMatrix's `WhoAmI` | Token login and session restore have no login response; would need its own `/whoami` call and storage | ❌ |
| D: Read `DeviceId` from the gateway (`WhoAmI`, already fetched) | Covers all three login paths, no new request, no new state | Depends on LibMatrix's `WhoAmIResponse` | ✅ chosen |
| E: Devices as a card next to Overview and Actions (as #598 suggests) | Visible without a click | Loads on every user page open; the page code-behind grows | ❌ |
| F: Devices and Recent connections as tabs with their own components | Loaded only when opened; isolated state, stale guard and tests; matches Rooms/Media | One click to reach | ✅ chosen |
| G: Put the methods in `IUserService` | No new service | `UserService` is already 458 lines | ❌ |

## 4. Technical Design

### Components affected

- **Gateway:** `IMatrixGateway`, `MatrixGatewayBase`, `SynapseAdminGateway`, `GenericMatrixGateway`.
- **Models:** `Models/Responses/SynapseAdminUserDeviceListResult.cs` (`devices[]`: `device_id`, `display_name`, `last_seen_ip`, `last_seen_user_agent`, `last_seen_ts`, `user_id`, `dehydrated` as `bool?`; `total`), `Models/Responses/SynapseAdminWhoisResponse.cs` (`user_id`, `devices`: dictionary → `sessions[]` → `connections[]`: `ip`, `last_seen`, `user_agent`, all nullable), `Models/Requests/SynapseAdminDeviceRequests.cs` (`SynapseAdminDeviceUpdateRequest { display_name }`, `SynapseAdminDeleteDevicesRequest { devices }`).
- **ViewModels:** `UserDeviceViewModel` (`DeviceId`, `DisplayName?`, `LastSeenIp?`, `LastSeenUserAgent?`, `LastSeen` as `DateTimeOffset?`, `Dehydrated`), `UserConnectionViewModel` (`Ip?`, `UserAgent?`, `LastSeen?`).
- **Mapping:** `Extensions/Mapping/UserDeviceMappingExtensions.cs`: `ToViewModels()` for both; `last_seen_ts`/`last_seen` via `FromUnixTimeMilliseconds`; `null` or `0` → `null`; `dehydrated` `null` → `false`; the whois connections of every device key and session flattened, newest first.
- **Service:** `Interfaces/IUserDeviceService.cs`, `Services/UserDeviceService.cs`, registered scoped in `Program.cs`:
  - `GetUserDevicesAsync(userId, token)` → `OperationResult<List<UserDeviceViewModel>>`; a `null` answer → empty list.
  - `GetUserConnectionsAsync(userId, token)` → `OperationResult<List<UserConnectionViewModel>>`; `null` → empty list.
  - `RenameUserDeviceAsync(userId, deviceId, displayName, token)` → `OperationResult`; an empty name or more than 100 characters (counted in code points, as both servers do) → warning, nothing sent.
  - `DeleteUserDevicesAsync(userId, IReadOnlyCollection<string> deviceIds, token)` → `OperationResult`; an empty list → nothing sent; for the signed-in user (`userId == gateway.UserId`, ordinal), a list containing `gateway.DeviceId`, or any list if `DeviceId` is `null` → warning `CannotDeleteOwnSessionDevice`, nothing sent. Runs inside `RunGatewayCallAsync`'s `call`, after the session check.
  - Every method uses `RunGatewayCallAsync`; the two reads pass no `logRefusal` (on a server without admin API the page shows the admin-API notice instead and never renders the tabs). Log templates carry the user ID and device IDs only, never names, IPs or user agents.
- **UI:**
  - `Components/Shared/UserDevicesPanel.razor(.cs)`: parameters `UserId` (the loaded user's ID, not the route), `IsOwnAccount`. Loads on parameter change with its own `CancellationTokenSource`, clears the old list and discards late answers (AGENTS.md Detail Pages). `MudTable` with `MultiSelection`, sortable columns, local time via `ITimeZoneService`, "unknown" for missing values, a Dehydrated chip. Row actions Rename and Delete; a toolbar button "Delete selected (n)". The own session device's Delete is disabled, and "Delete selected" is disabled while the selection contains it, with a caption explaining why (also when `DeviceId` is `null`: all own devices). Confirmations via `IDialogService.ShowMessageBox` with the user ID, device IDs, and the sign-out / keys / password sentence; afterwards a check that `UserId` is unchanged (else `SelectionChangedActionAborted`), then `Snackbar.ShowResult` and a reload.
  - `Components/Shared/UserConnectionsPanel.razor(.cs)`: parameter `UserId`, same load guard, a read-only table (IP, user agent, last seen) with a note that this is recent connection history reported by the homeserver.
  - `Components/Pages/RenameDeviceDialog.razor(.cs)`: `MudForm` with a required `MudTextField` (`MaxLength="100"`, counter), prefilled with the current name; returns the new name. The dialog names the user and the device ID.
  - `UserDetails.razor`: two `MudTabPanel`s (`Devices`, `RecentConnections`) after Media.
- **Resources:** EN/DE/FR keys such as `Devices`, `RecentConnections`, `DeviceId`, `DeviceName`, `LastSeen`, `LastSeenIp`, `UserAgent`, `Dehydrated`, `Unknown` (reuse if present), `RenameDevice`, `DeleteDevice`, `DeleteSelectedDevices`, `ConfirmDeleteDevices…`, `DeviceDeletionHint`, `OwnSessionDeviceNote`, `CannotDeleteOwnSessionDevice`, `DeviceNameRequired`, `DeviceNameTooLong`, `DevicesDeleted`, `DeviceRenamed`, `ErrorFetchingDevices`, `ErrorFetchingConnections`, `ErrorRenamingDevice`, `ErrorDeletingDevices`, `RecentConnectionsHint`.

### Data model changes

None (no local database). New request/response DTOs only.

### API / interface changes

`IMatrixGateway`: `string? DeviceId { get; }`, `GetUserDevicesAsync`, `RenameUserDeviceAsync`, `DeleteUserDevicesAsync`, `GetUserWhoisAsync`. Wire format:

| Call | Request |
|---|---|
| List | `GET /_synapse/admin/v2/users/{userId.UrlEncode()}/devices` |
| Rename | `PUT /_synapse/admin/v2/users/{userId.UrlEncode()}/devices/{Uri.EscapeDataString(deviceId)}`, body exactly `{"display_name":"…"}` |
| Delete | `POST /_synapse/admin/v2/users/{userId.UrlEncode()}/delete_devices`, body exactly `{"devices":["…"]}` |
| Whois | `GET /_synapse/admin/v1/whois/{userId.UrlEncode()}` |

New public service interface `IUserDeviceService`. No breaking change.

### Configuration / feature flags

- None. The 100-character limit is a named constant (`MaxDeviceDisplayNameLength`) in `UserDeviceService`, shared with the dialog.

## 5. Implementation Steps

### Phase 1: Gateway

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | Request/response DTOs | `Models/Requests/SynapseAdminDeviceRequests.cs`, `Models/Responses/SynapseAdminUserDeviceListResult.cs`, `SynapseAdminWhoisResponse.cs` | `dotnet build` |
| 1.2 | `DeviceId` and the 4 methods on `IMatrixGateway` / `MatrixGatewayBase`; Synapse implementation; Generic refusals; test fakes updated | Gateways, two test fakes | Wire tests in `SynapseAdminGatewayCharacterizationTests`: method, path, encoding of a user and device ID with `@`, `:`, `/`, `+` and a space, exact bodies; both error exception types; `GenericMatrixGatewayTests` refusal before any request; `DeviceId` from `/whoami` (with and without `device_id`) |

### Phase 2: Service and mapping

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | ViewModels and `UserDeviceMappingExtensions` | `Models/ViewModels/`, `Extensions/Mapping/` | `MappingExtensionsTests`: property-by-property, `null` → defaults, `0` timestamps, whois flattening across device keys and sessions, ordering |
| 2.2 | `IUserDeviceService` / `UserDeviceService`, DI registration | `Interfaces/`, `Services/`, `Program.cs` | `UserDeviceServiceCharacterizationTests` (table-driven): success, `null` answer, failure, cancellation, not supported, not authenticated; rename validation (empty, 100 vs 101 code points, nothing sent); own-device refusal (contained, `DeviceId` `null`, another user unaffected); log assertions with `RecordingLogger` that no IP, user agent or name is logged |

### Phase 3: UI

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | Resources EN/DE/FR | `Resources/SharedResources*.resx` | Existing resource-completeness tests |
| 3.2 | `RenameDeviceDialog` | `Components/Pages/` | `RenameDeviceDialogTests` with MudBlazor's real `DialogService` (AGENTS.md bUnit pitfalls): prefill, required, limit, cancel |
| 3.3 | `UserDevicesPanel` | `Components/Shared/` | `UserDevicesPanelTests`: list, unknown values, local time, rename flow, single and multi delete, confirmation text names user and IDs, own-device disabled (and all own devices when `DeviceId` is `null`), `SelectionChangedActionAborted`, late answer for a previous user ignored, snackbars via `AssertSnackbars` |
| 3.4 | `UserConnectionsPanel` | `Components/Shared/` | `UserConnectionsPanelTests`: list, ordering, unknown values, late answer ignored |
| 3.5 | Tabs on `UserDetails` | `UserDetails.razor` | Existing `UserDetails*` tests stay green; a test that the tabs pass the loaded user's ID |

### Phase 4: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | Remove `[Planned]` in SPEC (FR-4, AC-23, §8, §10, §13), add decision log rows (tabs, `delete_devices` for one device, own-device rule) | `SPEC.md` | Review |
| 4.2 | README feature list, ARCHITECTURE (service list / components) and AGENTS.md project structure (`Components/Shared` list) if they enumerate them | `README.md`, `ARCHITECTURE.md`, `AGENTS.md` | Review |
| 4.3 | Reset PLAN.md and TODO.md to the templates | | |

## 6. Testing Strategy

- **Unit & bUnit component tests:** Gateway wire tests, mapper tests, the service characterization table, and the three component test classes above, all culture-independent (expected strings from `IStringLocalizer<SharedResources>`).
- **Integration tests:** None new; no HTTP endpoint of the app changes.
- **Manual / homeserver tests:** On a Synapse test server: list, rename, delete one, delete several, recent connections; check that a deleted device's access token stops working; check that the own session device is protected. On Tuwunel 1.8.2 or 2.0.0 if available: user agent shown as unknown, one connection per device.
- **Test data:** Made-up user IDs, device IDs, documentation IP ranges (`192.0.2.0/24`, `2001:db8::/32`) and generic user agents.
- **Mapping to acceptance criteria:** AC-23 → `SynapseAdminGatewayCharacterizationTests`, `GenericMatrixGatewayTests`, `MappingExtensionsTests`, `UserDeviceServiceCharacterizationTests`, `UserDevicesPanelTests`, `UserConnectionsPanelTests`, `RenameDeviceDialogTests`. AC-5 (stale selection) → the panel tests.

## 7. Rollout & Rollback

- **Deployment approach:** Regular release; no configuration.
- **Migration steps:** None.
- **Rollback plan:** Revert the PR; nothing is stored.
- **Monitoring after release:** Error logs for the new failure templates.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Admin deletes the app's own session device and is signed out mid-action | Page disables it; the service refuses it as well; tests for both, including a `null` `DeviceId` |
| An admin believes deleting devices secures the account | Confirmation says the password is unchanged (SPEC §13 row) |
| IPs or user agents end up in logs, e.g. through an exception message that quotes the response | Log only templates with user and device IDs; never log response bodies; `RecordingLogger` tests |
| `whois` returns many connections for a heavily used account | Client-side table paging; the request is made only when the tab is opened |
| A test stub `IMatrixGateway` misses the new members | Two hand-written fakes are updated in step 1.2; `DispatchProxy` stubs need nothing |

## 9. Open Questions

- [x] Service name: `IUserDeviceService` (devices and connections only). #599 and #600 choose their own home; their issue text gets a short update when they start. Decided 2026-11-26.
- [x] Rename with an empty name: not offered; the name is required. Decided 2026-11-26.

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-26 | Checked Synapse v1.165.0 (`rest/admin/devices.py`, `handlers/device.py`) and Tuwunel 1.8.2 / 2.0.0 (`admin/devices/*`, `users/whois.rs`): both limit names to 100 code points, both treat deleting an unknown device as success, rename of an unknown device is `404` on both; Tuwunel returns no user agent and builds `whois` from each device's last IP and time. |
| 2026-11-26 | Plan approved with `IUserDeviceService` and a required device name. |
| 2026-11-26 | [1.2] The device ID in the rename path takes `Uri.EscapeDataString`, not `.UrlEncode()`: clients choose device IDs, which may contain a space, and a path keeps the `+` form encoding sends (as for room aliases, #580). |
