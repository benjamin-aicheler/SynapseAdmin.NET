# REFACTOR_PLAN.md

> **What this is:** the task list for a behavior-preserving refactor of SynapseAdmin.NET, built from a read-only code-quality and architecture audit.
> **Current as of:** `main` @ `836c091` (2026-11-14). **All line numbers refer to this revision.** The audit itself ran at `e34a2f3`; findings fixed since then are listed in [Appendix A](#appendix-a-findings-resolved-since-the-audit).
> **Scope:** `src/SynapseAdmin/**` and `tests/SynapseAdmin.Tests/**`. The `LibMatrix/` and `ArcaneLibs/` submodules are **out of scope** (AGENTS.md forbids modifying them).
> **Execution rules:** every task is executed under [REFACTOR_RULES.md](./REFACTOR_RULES.md): one task ID per pass, < 200 changed lines, mechanical and structural changes kept apart, pre-flight and post-edit build + test, characterization tests before Phase 2/3 structural edits. Project conventions in [AGENTS.md](./AGENTS.md) apply throughout.

---

## 0. Snapshot

| Area | State at `836c091` |
|---|---|
| Build | 0 errors. **0 warnings in `src/` and `tests/`**. All 151 warnings come from the `LibMatrix/` and `ArcaneLibs/` submodules. |
| Tests | 212 / 212 pass (xUnit + bUnit + WebApplicationFactory), also under `LANG=de_DE.UTF-8`. |
| Formatting reference | Repo-root `.editorconfig` (formatting only, no analyzer severities). No lint or formatting step in CI. |
| Unused usings (IDE0005) | 16 in `src/`, 1 in `tests/`. |
| Formatting (IDE0055) | 95 locations in 36 files, almost all whitespace-only lines with trailing spaces. 9 `.cs`/`.razor` files lack a final newline. |
| Swallowed exceptions | 8 catch-all blocks swallow exceptions without logging: clipboard calls, JS disposal, theme discovery, a polling cleanup (see §6.1 O-01). |
| Duplicated service boilerplate | 49 `if (Gateway == null) return …NotAuthenticated` guards, 59 `catch (OperationCanceledException)` blocks. |
| Duplicated UI boilerplate | 57 `if (result.Severity != Severity.Normal) Snackbar.Add(...)` blocks across 21 component files. |
| Cross-module duplicates | MIME-type classification (4 copies, semantics differ), media preview launcher (3), local-user check (3), claim-type strings (3 files), `SortDirection → dir` mapping (4, **two intended defaults**), test localizer stubs (7: 5 identical + 2 identical formatting variants). |

### Test-coupling warning (applies to all component tasks)

Several tests reach into components **by member name via reflection** (`DetailPageTestBase.InvokeHandler` / `GetField`, and direct `GetMethod` / `GetProperty` / `GetField` calls). Renaming or removing these members breaks tests even though the compiler won't notice:

| Component | Private methods invoked by name | Private fields / properties accessed by name |
|---|---|---|
| `RoomDetails` | `DeleteRoom`, `DeleteAndBlockRoom`, `QuarantineMedia`, `BlockRoom`, `LoadLocalMedia`, `LoadRemoteMedia` | `room` |
| `UserDetails` | `DeactivateUser`, `QuarantineAllMedia`, `LoginAsUser`, `LoadMediaPage` | `user`, `_displayedUserId`, `_mediaPage` |
| `RoomGalleryGrid` | `OnSourceFilterChanged` (values `"local"`, `"remote"`, `"all"`), `OnTypeFilterChanged` (value `"file"`) | `_displayedMedia` |
| `RegistrationTokenDialog` | `OnInitializedAsync`, `Submit` | `MudDialog` (property), `expiryDate` |
| `RoomAliasesDialog` | `ValidateAliasFormat` | `L` (injected property) |

**Rule for every task:** keep these names, signatures and literal values. If you extract logic, the named member stays as a thin wrapper.

---

## 1. Verification Baseline

### 1.1 Commands found in the repository (AGENTS.md, `.github/workflows/dotnet.yml`)

| Purpose | Command | Source |
|---|---|---|
| Submodules | `git submodule update --init --recursive` | AGENTS.md, CI `checkout: submodules: recursive` |
| Restore | `dotnet restore` | AGENTS.md, CI |
| Build + type/nullability check | `dotnet build` (CI: `dotnet build --no-restore`) | AGENTS.md, CI |
| Tests | `dotnet test` (CI: `dotnet test --no-build --verbosity normal`) | AGENTS.md, CI |
| Single test class/method | `dotnet test --filter "FullyQualifiedName~<Name>"` | AGENTS.md |
| Formatting | `dotnet format whitespace …` against the repo-root `.editorconfig` (not run in CI) | AGENTS.md "Formatting" |
| Lint | **None configured.** | — |

The C# compiler is the only type checker (`<Nullable>enable</Nullable>` in both projects).

### 1.2 Audit-only analyzer commands (non-gating, used to measure Phase 1 progress)

These commands enable the SDK's built-in analyzers through MSBuild properties. **They don't modify tracked files**, but they do overwrite `obj/`. Run a normal `dotnet build` first so the `LibMatrix` project references exist, and `dotnet build --no-incremental` afterwards to restore normal build outputs.

```bash
# A1: style diagnostics (IDE0005 unused usings, IDE0055 formatting, IDE0044/0040/0036/1006, ...)
dotnet build src/SynapseAdmin/SynapseAdmin.csproj --no-incremental -p:BuildProjectReferences=false \
  -p:EnforceCodeStyleInBuild=true -p:GenerateDocumentationFile=true -p:AnalysisModeStyle=All -p:NoWarn=CS1591 \
  2>&1 | grep -E ": warning IDE" | sed -E 's/ \[[^]]*\]$//' | sort -u

# A1-tests: same for the test project
dotnet build tests/SynapseAdmin.Tests/SynapseAdmin.Tests.csproj --no-incremental -p:BuildProjectReferences=false \
  -p:EnforceCodeStyleInBuild=true -p:GenerateDocumentationFile=true -p:AnalysisModeStyle=All -p:NoWarn=CS1591 \
  2>&1 | grep -E ": warning IDE0005" | sort -u

# A2: quality diagnostics (CA1822, CA1869, CA1508, CA1805, CA1307/1310, CA2213, ...)
dotnet build src/SynapseAdmin/SynapseAdmin.csproj --no-incremental -p:BuildProjectReferences=false \
  -p:AnalysisMode=All -p:GenerateDocumentationFile=true -p:NoWarn=CS1591 \
  2>&1 | grep -E ": warning CA" | sed -E 's/ \[[^]]*\]$//' | sort -u

# Restore normal outputs afterwards
dotnet build --no-incremental
```

Diagnostic messages may appear in German on this machine, but the `warning IDExxxx` / `warning CAxxxx` tokens stay English, so the `grep` patterns work.

### 1.3 Recorded baseline (`836c091`)

| Check | Baseline |
|---|---|
| `dotnet build --no-incremental` | 0 errors. 151 warnings, **all** in `LibMatrix/**` and `ArcaneLibs/**`. |
| `dotnet test` | 212 passed, 0 failed, 0 skipped. |
| A1 `IDE0005` | 16 (src) + 1 (tests) |
| A1 `IDE0055` | 95 |
| A1 `IDE0036` / `IDE0040` / `IDE0044` | 2 / 3 / 3 |
| A2 `CA1805` | 9 |
| A2 `CA1869` | 1 (`EventForensicsService.cs:60`) |
| A2 `CA1508` | 3 (`MatrixAuthGateway.cs:163`, `BackgroundUpdates.razor.cs:74`, `UserDetails.razor.cs:157`) |

### 1.4 Standard Done Criteria (SDC), required for every task in addition to its own criteria

1. `dotnet build --no-incremental` → 0 errors, and **0 warnings outside `LibMatrix/` and `ArcaneLibs/`** (total still 151).
2. `dotnet test` → all tests pass, with a test count ≥ 212 plus any characterization tests added by the task.
3. `git diff --stat` touches only the task's **Target Files** (plus new test files where stated), with < 200 changed lines.
4. No existing assertion was weakened or deleted, and no public signature, route, JSON shape, claim name, storage key, cache key or config key changed unless the task explicitly says so.
5. Tests run culture-independently. Also run `LANG=de_DE.UTF-8 dotnet test` once for any task that touches strings or formatting.

---

## 2. Dead Code

### 2.1 Suspected dead code awaiting a decision

**None.** Every statically unreferenced member found by the audit has been decided and resolved (see Appendix A, D-01 to D-09). If a task uncovers new candidates, list them here for an owner decision instead of deleting them.

### 2.2 Looks unreferenced but is dynamically wired. **Do NOT delete.**

| Item | Wiring mechanism |
|---|---|
| `RoomGalleryGrid.OnSentinelIntersected` | `[JSInvokable]`, called from `wwwroot/js/gallery-interop.js` |
| `PartialMediaResult.ExecuteResultAsync` | ASP.NET Core `IActionResult` pipeline |
| All controller actions (`MediaController.Avatar/Download/Preview`, `AuthController.SignIn/SignOutAction`, `CultureController.Set`) | Attribute/convention routing (`[controller]/[action]`). JS and `NavigationManager` call them by URL string. |
| All `IAppTheme` implementations in `Infrastructure/Themes/` | Reflection discovery (`ThemeService.DiscoverThemesStatic`) |
| `AesXmlDecryptor` | Instantiated by ASP.NET Core Data Protection from `typeof(AesXmlDecryptor)` stored in key XML |
| Private DTO classes in `HomeserverDiscoveryClient` and every type under `Models/Responses`, `Models/Requests` | `System.Text.Json` (de)serialization |
| `ClientIdentityCircuitHandler`, `ClientIdentityProvider` | DI (`TryAddEnumerable(CircuitHandler)`) |
| `GenericMatrixGateway` `NotSupportedException` overrides | Required by the abstract base and returned by `MatrixAuthGateway` |
| `public partial class Program;` | `WebApplicationFactory<Program>` in tests |
| `internal` test helpers: `UserService.MediaStatisticsSearchTerm`, `SynapseCompatibleAdminGateway.Build*Url`, `LoginRateLimiter.IsAllowed`, `MemoryLoginRateLimitStore.IsAllowed` | Tests via `InternalsVisibleTo` |
| JS modules `auth-bridge.js`, `gallery-interop.js`, `timezone-interop.js` | `IJSRuntime.InvokeAsync("import", "./js/...")` string paths |
| Members listed in the §0 test-coupling table | Reflection in tests |

---

## 3. Phase 1: Mechanical Cleanup (Zero Logic Risk)

> Mechanical only: no control-flow, expression or member changes. Each task is one concern. Paths are relative to `src/SynapseAdmin/` unless stated.

### P1-01: Remove unused `using` directives in services, gateways, models and interfaces
- **Target Files:** `Services/EventForensicsService.cs`, `Services/LoginRateLimiter.cs`, `Infrastructure/Gateways/MatrixAuthGateway.cs`, `Infrastructure/Gateways/SynapseCompatibleAdminGateway.cs`, `Infrastructure/Security/ClientIdentityProvider.cs`, `Interfaces/IRoomService.cs`, `Models/Responses/SynapseAdminDeleteMediaResponse.cs`, `Models/ViewModels/LoginViewModel.cs`
- **Exact Scope:** Delete only the `using` lines that A1 reports as `IDE0005` in these files (9 diagnostics). Examples: `using System.Net;` in `EventForensicsService.cs:1`, `using Microsoft.Extensions.DependencyInjection;` in `LoginRateLimiter.cs:1`, `using System.Collections.Generic;` (already implicit) in the two model files. **Do not** reorder the remaining usings or touch any other line. Do not add global usings.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus A1 reports 0 `IDE0005` in the target files.

### P1-02: Remove unused `using` directives in components
- **Target Files:** `Components/Layout/NavMenu.razor.cs`, `Components/Pages/PublishRoomDialog.razor.cs`, `Components/Pages/RoomAliasesDialog.razor.cs`, `Components/Pages/RoomDirectory.razor.cs`, `Components/Shared/RoomGalleryGrid.razor.cs`
- **Exact Scope:** Delete only the A1 `IDE0005` usings (7 diagnostics; A1 reports one diagnostic per contiguous block). Examples: `using System; using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;` in `RoomDirectory.razor.cs:5-8` and `RoomAliasesDialog.razor.cs:6-9` (redundant with `ImplicitUsings`), and `Microsoft.Extensions.Localization` / `SynapseAdmin.Resources` where `L` is injected only by `_Imports.razor`. Don't touch `.razor` files.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus A1 reports 0 `IDE0005` under `Components/`.

### P1-03: Remove the unused `using` in tests
- **Target Files:** `tests/SynapseAdmin.Tests/TimeZoneAndPurgeTimestampTests.cs`
- **Exact Scope:** Delete the single using reported by A1-tests (line 11). Nothing else.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus A1-tests reports 0 `IDE0005`.

### P1-04: Remove redundant `@inject IStringLocalizer<SharedResources> L` lines
- **Target Files:** `Components/Shared/RoomGalleryGrid.razor:2`, `Components/Pages/RenewAccountDialog.razor:3`, `Components/Pages/PurgeHistoryDialog.razor:3`, `Components/Pages/RoomGallery.razor:4`
- **Exact Scope:** `Components/_Imports.razor:20` already injects the same service under the same name for every component in `Components/**`. Remove only the duplicate `@inject` line in each file. Keep `@using` lines and markup unchanged.
- **Risk Level:** Low
- **Done Criteria:** SDC. The `RoomGallery*`, `TimeZoneAndPurgeTimestampTests` and `UserDetails*` tests still pass, which proves the localizer still resolves in these components.

### P1-05: Replace redundant fully qualified type names with existing usings (gateways)
- **Target Files:** `Infrastructure/Gateways/MatrixGatewayBase.cs`, `Infrastructure/Gateways/SynapseCompatibleAdminGateway.cs`
- **Exact Scope:**
  - `MatrixGatewayBase.cs` already has `using LibMatrix.StructuredData;` and `using LibMatrix.Homeservers;`, so shorten `LibMatrix.StructuredData.MxcUri` (lines 215, 223, 231, 238, 245), `LibMatrix.Homeservers.RoomDirectoryFilter` (255, 258) and `LibMatrix.Homeservers.RoomDirectoryVisibilityResponse` (296-297).
  - `SynapseCompatibleAdminGateway.cs`: shorten `LibMatrix.StructuredData.MxcUri` (375) by adding `using LibMatrix.StructuredData;`. Leave `System.Net.HttpStatusCode` and `LibMatrix.MatrixException` as they are, because adding `using System.Net;` / `using LibMatrix;` could introduce name ambiguities, which this mechanical task must not resolve.
  - Change no expressions.
- **Risk Level:** Low
- **Done Criteria:** SDC.

### P1-06: Replace redundant fully qualified type names (services, controllers and components)
- **Target Files:** `Services/RoomService.cs` (`System.Text.Json.Nodes.JsonNode` / `JsonValue` at 643, 646: add `using System.Text.Json.Nodes;`), `Components/Pages/RoomDetails.razor.cs:26` (`Microsoft.Extensions.Logging.ILogger<>` is implicit), `Controllers/MediaController.cs:153` (`Infrastructure.Helpers.MediaHelper`: add `using SynapseAdmin.Infrastructure.Helpers;`), `Components/Pages/UserDetails.razor.cs:204-205` (`System.Net.WebUtility`)
- **Exact Scope:** Type-name shortening only. No other edits.
- **Risk Level:** Low
- **Done Criteria:** SDC.

### P1-07: Drop the redundant `TaskCanceledException` from an exception filter
- **Target Files:** `Services/AuthCookieService.cs:51`
- **Exact Scope:** `ex is OperationCanceledException or JSDisconnectedException or TaskCanceledException` becomes `ex is OperationCanceledException or JSDisconnectedException`. `TaskCanceledException` derives from `OperationCanceledException`, so the match set is identical. Don't change the `JSException` branch.
- **Risk Level:** Low
- **Done Criteria:** SDC, and `AuthCookieServiceTests` pass.

### P1-08: Normalize modifiers, `readonly` fields and explicit default initializers
- **Target Files:**
  - `Models/OperationResult.cs` (IDE0036 at 26, 31: `public new static` becomes `public static new`)
  - `Components/Pages/Login.razor.cs:25, 29` and `Components/Pages/UserCreateDialog.razor.cs:15` (IDE0044: add `readonly`)
  - `Components/Pages/RegistrationTokenDialog.razor.cs:11`, `RenewAccountDialog.razor.cs:10`, `UserCreateDialog.razor.cs:10` (IDE0040: add explicit `private` to the `MudDialog` cascading parameters)
  - CA1805 redundant `= false` / `= 0` initializers in `BackgroundUpdates.razor.cs:24-25`, `MediaMaintenance.razor.cs:25-26`, `ServerNotices.razor.cs:18`, `RoomGalleryGrid.razor.cs:29-31`, `Services/ThemeService.cs:19`
- **Exact Scope:** Only the listed diagnostics. **Do not** add `readonly` to fields the generated Razor code writes (`@bind`, `@ref`); if adding `readonly` causes a compile error, revert that one line. Don't rename anything: `RegistrationTokenDialog.MudDialog` is accessed by name in tests (§0). The two IDE1006 naming findings (`MainLayout._isRtl`, `Login.selectedTabIndex`) are out of scope, because both names are bound from `.razor` markup.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus A1 and A2 report 0 `IDE0036` / `IDE0040` / `IDE0044` / `CA1805` in the target files.

### P1-09a / P1-09b / P1-09c: Whitespace normalization (IDE0055)
- **Target Files:**
  - **09a:** `Services/**`, `Controllers/**`, `Program.cs` (31 diagnostics)
  - **09b:** `Components/**/*.razor.cs` (43 diagnostics)
  - **09c:** `Infrastructure/**`, `Models/**` (21 diagnostics; `Extensions/**` and `Interfaces/**` have none)
- **Exact Scope:** Run `dotnet format whitespace src/SynapseAdmin/SynapseAdmin.csproj --include <folder>` for the task's folders only, then inspect the diff. Formatting follows the repo-root `.editorconfig`: LF, UTF-8, 4-space indent, braces on their own line, trim trailing whitespace, final newline. Accept only whitespace changes: trailing spaces on blank lines, indentation, and a missing final newline. Revert any hunk that changes brace placement, line breaks inside expressions or blank-line count.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `git diff --ignore-all-space --stat` for the task is **empty**, plus A1 reports 0 `IDE0055` in the task's folders.

### P1-10: Convert HTML comments in `.razor` markup to Razor comments
- **Target Files:** `Components/Shared/RoomGalleryGrid.razor` (lines 6, 39, 53, 58, 60, 75, 104, 123, 168), `Components/Pages/BackgroundUpdates.razor` (38, 77, 116, 125, 141), `Components/Pages/MediaMaintenance.razor` (24, 55), `Components/Pages/EventForensics.razor` (60, 132)
- **Exact Scope:** `<!-- text -->` becomes `@* text *@` (18 comments). Comment text stays the same. This removes developer comments from the HTML sent to browsers. No element or attribute changes.
- **Risk Level:** Low (rendered markup loses the comment nodes; no logic change)
- **Done Criteria:** SDC, plus `grep -rn "<!--" --include=*.razor src` is empty.

### P1-11: Name the purge-polling and navigation magic values as private constants
- **Target Files:** `Components/Pages/RoomDetails.razor.cs`
- **Exact Scope:** Introduce `private const` values for literals that already exist: purge status strings `"complete"` (442), `"failed"` (453), `"active"` (559); poll interval `5000` ms (500); the failure threshold `5` (486, also written into the log text at 484); the history page size `50` (186); and `"/rooms"` (226, 249). Values must stay byte-identical. **Don't** restructure `StartPolling` (that is P2-11).
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `RoomDetailsStaleSelectionTests` pass.

### P1-12: Simplify the redundant auth guard in `MediaService`
- **Target Files:** `Services/MediaService.cs` (lines 18, 39)
- **Exact Scope:** `if (!sessionService.IsLoggedIn || Gateway == null)` becomes `if (Gateway == null)`, the guard every other service method uses. `MatrixSessionService.IsLoggedIn` is defined as `Gateway != null`, so production behavior is identical. Change nothing else, including the blank-MXC guards that follow.
- **Risk Level:** Low
- **Done Criteria:** SDC (the real `MediaService` is exercised by `MediaRangeForwardingTests`), plus `grep -n "IsLoggedIn" src/SynapseAdmin/Services/MediaService.cs` is empty.

---

## 4. Phase 2: Module-Level Simplification (Low–Medium Risk)

> Each task stays inside one module. Under REFACTOR_RULES §4.2, write **characterization tests first** wherever "Characterize first" is listed, commit them, and confirm they pass **before** the production change. Two harnesses already exist for gateway-level tests against a real HTTP server: the Kestrel fake homeservers in `MediaRangeForwardingTests` and `MatrixAuthGatewayProbeTests`.

### P2-01: `SynapseCompatibleAdminGateway`: extract the "GET + compat-JSON deserialize" helper
- **Target Files:** `Infrastructure/Gateways/SynapseCompatibleAdminGateway.cs`, new `tests/SynapseAdmin.Tests/SynapseCompatibleGatewayCharacterizationTests.cs`
- **Exact Scope:** Five methods repeat `GetAsync → EnsureSuccessStatusCode → ReadAsStreamAsync → JsonSerializer.DeserializeAsync<T>(…, SynapseCompatibilityJsonOptions, ct)`: `GetUserListAsync` (51-54), `GetUserMediaStatisticsAsync` (111-114), `GetUserMediaAsync` (139-142), `GetFederationDestinationListAsync` (263-266), `GetEventReportListAsync` (292-295). Extract `private async Task<T?> GetWithCompatibilityOptionsAsync<T>(string url, CancellationToken ct)`. **Must not** switch to `GetFromJsonAsync` (AGENTS.md Known Pitfall: `MatrixHttpClient` mutates options). Keep `protected static readonly SynapseCompatibilityJsonOptions` unchanged, because `UserMediaPaginationTests.GatewayProbe` reads it. URL building stays untouched.
- **Characterize first:** With a Kestrel fake homeserver, assert for each of the 5 methods that (a) a numeric and a string `next_token` both deserialize, (b) a non-2xx response throws `HttpRequestException`, and (c) the request URL is unchanged.
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus the characterization tests pass before and after, plus `grep -c "SynapseCompatibilityJsonOptions, cancellationToken" SynapseCompatibleAdminGateway.cs` == 1.

### P2-02: `SynapseCompatibleAdminGateway`: extract the raw DELETE helper
- **Target Files:** `Infrastructure/Gateways/SynapseCompatibleAdminGateway.cs`, the characterization test file from P2-01
- **Exact Scope:** `DeleteRoomAsync` (200-209, with JSON body), `DeleteEventReportAsync` (298-304), `DeleteRegistrationTokenAsync` (349-355) and `DeleteMediaAsync` (393-399) become calls to `private async Task SendDeleteAsync(string url, HttpContent? content, CancellationToken ct)`. Keep `new HttpRequestMessage(HttpMethod.Delete, url)` + `ClientHttpClient.SendAsync(req, ct)` + `EnsureSuccessStatusCode` (AGENTS.md "Always" rule: LibMatrix `DeleteAsync` lacks a token overload). The `DeleteRoomAsync` body must still be `JsonSerializer.Serialize(request)` with default options, UTF-8 and `application/json`.
- **Characterize first:** Capture method, URL, body and `Content-Type` for all 4 calls.
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus the captured requests are identical before and after.

### P2-03: `SynapseCompatibleAdminGateway`: extract the empty-body POST/PUT helpers
- **Target Files:** `Infrastructure/Gateways/SynapseCompatibleAdminGateway.cs`, the characterization test file from P2-01
- **Exact Scope:** 7 call sites send `new { }` and then call only `EnsureSuccessStatusCode`: `QuarantineMediaByUserIdAsync` (72, PUT), `QuarantineMediaByRoomIdAsync` (214, PUT), `ResetFederationConnectionTimeoutAsync` (272, POST), `QuarantineMediaAsync` (382, PUT), `UnquarantineMediaAsync` (389, POST), `ProtectMediaAsync` (404, POST), `UnprotectMediaAsync` (411, POST). `LoginAsUserAsync` (79), `PurgeRemoteMediaCacheAsync` (418) and `DeleteLocalMediaAsync` (426) also send `{}` but read a response body; leave those three as they are. Extract `PostEmptyAsync(url, ct)` and `PutEmptyAsync(url, ct)` that preserve the `{}` JSON body exactly. Leave `ProtectMediaAsync` / `UnprotectMediaAsync` ignoring `serverName` (interface contract).
- **Characterize first:** Assert method, URL and body `{}` for each site.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus the request captures are identical.

### P2-04: `MatrixAuthGateway`: flatten `CreateAuthenticatedGatewayAsync` and extract the version check
- **Target Files:** `Infrastructure/Gateways/MatrixAuthGateway.cs`, `tests/SynapseAdmin.Tests/MatrixAuthGatewayProbeTests.cs`
- **Exact Scope:** The method (124-256) is ~130 lines with nesting depth 6.
  1. Extract `internal static bool IsTuwunelAdminCapable(string version)`: strip the suffix after the first `-` if `dashIdx > 0`, `Version.TryParse`, `>= 1.8.1`, and **unparseable ⇒ true**.
  2. Extract `ProbeSynapseVersionAsync`, `ProbeFederationVersionAsync` and `ProbeAdminApiAsync` as private methods that return tuples.
  3. Remove the always-true `if (authenticatedHomeserver is AuthenticatedHomeserverGeneric generic)` (CA1508, line 163) by using the variable directly.
  - **Preserve exactly** the four probe catches (154, 211, 237, 244): `catch (Exception) when (!cancellationToken.IsCancellationRequested)`. Probe failures are soft, but cancellation must propagate so that no gateway without admin API gets cached.
  - Preserve the order of probes, the brand/version defaults `"Generic"` / `"Unknown"` and the gateway types returned.
  - Don't touch caching (`GetAuthenticatedAsync`, `CreateAndCacheGatewayAsync`).
- **Characterize first:** `MatrixAuthGatewayProbeTests` already covers admin detection on a Tuwunel-like server and cancellation during a probe. Add, before restructuring: a server reporting `"Synapse"` via federation, a non-admin server (admin probe 404 → `GenericMatrixGateway`), and table tests for `IsTuwunelAdminCapable`: `"1.8.1"`→true, `"1.8.0"`→false, `"1.9.0-dev"`→true, `"-1.0"`→true (dash at index 0, unparseable), `"abc"`→true, `"1.8"`→false. Extract the version check as a verbatim copy first, and assert it before switching callers.
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus the new tests pass, plus A2 reports no `CA1508` in this file, plus the method body is < 60 lines.

### P2-05: `RoomService.GetRoomDetailsAsync`: extract pure mapping helpers
- **Target Files:** `Services/RoomService.cs`, `tests/SynapseAdmin.Tests/RoomMediaDeduplicationTests.cs` (or a new `RoomDetailsMappingTests.cs`)
- **Exact Scope:** Extract `private static` pure functions from lines 92-144:
  - `ApplyTombstone(RoomDetailViewModel, SynapseAdminRoomStateResult?)`
  - `MapMembers(SynapseAdminRoomMemberListResult?, SynapseAdminRoomStateResult?)`, which flattens the 4-level nested `if` at 113-129
  - `MapStateEvents(...)`
  - Keep `GetSafeString` semantics, the `GroupBy(StateKey).First()` choice, `?? []` defaults and the `UniqueMxcs` call order. No change to the gateway calls or to the `Task.WhenAll` fan-out.
- **Characterize first:** Room with no tombstone; tombstone in state but not in the admin response; member without a state event; member event with non-string `displayname`; duplicate member events (first wins).
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus the new tests pass, plus `GetRoomDetailsAsync` is < 50 lines.

### P2-06: `RoomService.GetRoomMessagesAsync`: merge the duplicated view-model initializers
- **Target Files:** `Services/RoomService.cs`, new test cases (same test file as P2-05)
- **Exact Scope:** Lines 368-400 build `RoomMessageItemViewModel` twice. Compute `content` (`GetRawText()` or `null`) and `body` first, then build the view model once. Preserve: `Null`/`Undefined` ⇒ `Content == null`, `Body == null`; non-object JSON ⇒ `Content` = raw text, `Body == null`; object without a string `body` ⇒ `bodyProp.GetString()` semantics, **including throwing** on a non-string `body`. The whole call then returns `ErrorFetchingRoomMessages`, and that must stay as it is.
- **Characterize first:** The four content shapes above, plus `"body": 5`, which today yields a failure result.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus the characterization passes.

### P2-07: `RoomService`: deduplicate the homeserver-scoped cache-key prefix
- **Target Files:** `Services/RoomService.cs`
- **Exact Scope:** `GetRoomNameCacheKey` (27-28) and `GetActivePurgeCacheKey` (421-422) both compute `Gateway?.HomeserverUrl?.TrimEnd('/').ToLowerInvariant()`. Extract `private string? HomeserverCacheScope`. Keys must stay byte-identical (`room_name:{scope}:{roomId}` / `active_purge:{scope}:{roomId}`). Move the `ActivePurgeCacheOptions` field (416) next to `RoomNameCacheOptions` (19). That is a declaration move only.
- **Characterize first:** Unit test asserting both key strings for a gateway whose URL is `https://HS.example/`.
- **Risk Level:** Low
- **Done Criteria:** SDC.

### P2-08a / P2-08b / P2-08c: Move inline DTO→ViewModel mappings into `Extensions/Mapping`
- **Target Files:**
  - **08a:** `Services/RoomService.cs` (public rooms, 506-523) → `Extensions/Mapping/RoomMappingExtensions.cs`
  - **08b:** `Services/UserService.cs` (media page items 139-148, top media users 300-306, memberships 363-367) → `Extensions/Mapping/UserMappingExtensions.cs`
  - **08c:** `Services/BackgroundUpdatesService.cs` (25-37) → new `Extensions/Mapping/BackgroundUpdatesMappingExtensions.cs`
- **Exact Scope:** Move the object initializers verbatim into extension methods. Services keep their try/catch, their `null`-result handling (see §6.2 S-02) and their `OperationResult` handling. Don't merge with existing mappings. **Never touch** the `creation_ts` seconds/milliseconds conversions (AGENTS.md "Do not unify").
- **Characterize first:** Add a mapping test per moved mapper to `MappingExtensionsTests.cs` (property-by-property, including `null` → `[]` defaults).
- **Risk Level:** Low
- **Done Criteria:** SDC, plus the new mapping tests pass, plus no `new …ViewModel {` initializer remains in the touched service methods.

### P2-09: `MatrixAuthenticationStateProvider`: extract the authenticated-state factory
- **Target Files:** `Services/MatrixAuthenticationStateProvider.cs`, new `tests/SynapseAdmin.Tests/AuthenticationStateFactoryTests.cs`
- **Exact Scope:** Three identical claim arrays + `ClaimsIdentity(claims, "MatrixAuth")` + `AuthenticationState` blocks (50-60, 94-105, 122-133) become one `internal static AuthenticationState CreateAuthenticatedState(IMatrixGateway gateway)`. Keep claim order, claim types and authentication type `"MatrixAuth"`. Keep the storage writes and `NotifyAuthenticationStateChanged` at the call sites. **Don't** merge the two `DeleteAsync` pairs: one is inside the outer `try`, the other has its own `try/catch` (different failure semantics).
- **Characterize first:** Test the extracted factory against a stub gateway: 4 claims, exact types and values, `IsAuthenticated == true`, `AuthenticationType == "MatrixAuth"`.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `grep -c "new Claim(" MatrixAuthenticationStateProvider.cs` == 4.

### P2-10: `EventForensicsService`: cache the indented `JsonSerializerOptions`
- **Target Files:** `Services/EventForensicsService.cs`
- **Exact Scope:** `new JsonSerializerOptions { WriteIndented = true }` (line 60, CA1869) becomes `private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };`. The output string must be identical.
- **Characterize first:** Unit test with a stub gateway returning a fixed `SynapseAdminFetchEventResponse`, asserting the exact `RawJson`.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus A2 reports no `CA1869`.

### P2-11: `RoomDetails.StartPolling`: extract `ClearActivePurge()` and flatten the loop
- **Depends on:** P1-11
- **Target Files:** `Components/Pages/RoomDetails.razor.cs`, `tests/SynapseAdmin.Tests/Components/RoomDetailsStaleSelectionTests.cs` (new test cases only)
- **Exact Scope:** `StartPolling` (412-527). The block `RoomService.ClearActivePurgeId(roomId); activePurgeId = null; activePurgeStatus = null;` appears 4 times (444-446, 455-457, 490-492, 514-516). Extract `private void ClearActivePurge(string roomId)`. Extract the `complete`/`failed` handling into `private bool ApplyPurgeStatus(string roomId, string? status, string? error)`, which returns `shouldStop`. Keep the `Task.Run` + `InvokeAsync` threading model, the cancellation checks, the 5-failure threshold, the delay and the snackbar texts.
- **Characterize first:** Using a `Pending<T>()` status source and a short delay override (only if one can be injected without changing production code; otherwise test `ApplyPurgeStatus` directly through `InvokeHandler`), cover "complete", "failed" with and without an error, and 5 consecutive failures.
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus `StartPolling` is < 60 lines, plus every §0 reflection-accessed name is unchanged.

### P2-12: `RoomDetails`: deduplicate the delete handlers and the media-table reload
- **Target Files:** `Components/Pages/RoomDetails.razor.cs`
- **Exact Scope:** `DeleteRoom` (208-229) and `DeleteAndBlockRoom` (231-252) differ only in resource keys and the `block` flag. Make both thin wrappers over `private Task ConfirmAndDeleteRoomAsync(bool block, string titleKey, string confirmKey, string yesKey)`. **Both names must remain** (§0). Also extract `private async Task ReloadMediaTablesAsync()` for the repeated `localMediaTable?.ReloadServerData()` / `remoteMediaTable?.ReloadServerData()` pairs (300-301, 322-323, 345-346). `ToggleMediaProtection` reloads only the local table (365); leave it as it is.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `RoomDetailsStaleSelectionTests` pass unchanged.

### P2-13: `UserDetails`: extract the MXC resolution and item-patch helpers
- **Target Files:** `Components/Pages/UserDetails.razor.cs`
- **Exact Scope:** Five methods start with `if (MatrixSession.Gateway == null) return; var mxc = MediaHelper.ToMxcUri(x, MatrixSession.Gateway.ServerName);` (276-277, 305-306, 334-335, 358-359, 385-386). Extract `private string? ResolveMxc(string mediaId)` (null ⇒ return early). Extract `private void PatchMediaItem(string mediaId, Action<UserMediaItemViewModel> patch)` for 293-298 and 322-327. Keep the early-return order before confirmation dialogs. Keep the optimistic value `QuarantinedBy = "Admin"`. Keep every §0 name for `UserDetails`.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `UserDetailsMediaTabTests` and `UserDetailsStaleSelectionTests` pass.

### P2-14: `RoomGalleryGrid`: deduplicate the local/remote merge loop and name filter values
- **Target Files:** `Components/Shared/RoomGalleryGrid.razor.cs`, `Components/Shared/RoomGalleryGrid.razor`
- **Exact Scope:** Lines 113-130 repeat the same loop for `Local` and `Remote`. Extract `private void AddUnique(IEnumerable<RoomMediaItemViewModel>? items, bool isLocal, HashSet<string> seen)`. Preserve the ordinal comparer, local-first ordering and the skip of empty IDs. Introduce `private const string` values for the filter literals `"all"`, `"local"`, `"remote"`, `"image"`, `"video"`, `"audio"`, `"file"` and use them in both the `.razor.cs` file and the `MudChip Value` attributes in the `.razor` file. The **values stay identical**, because tests pass them as literals (§0). Keep `OnSourceFilterChanged`, `OnTypeFilterChanged` and `_displayedMedia`.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `RoomGalleryDuplicateMediaTests` and `RoomGalleryFilterTests` pass.

### P2-15: `RoomGalleryGrid` and `TimeZoneService`: add Debug logging to the empty disposal catches
- **Target Files:** `Components/Shared/RoomGalleryGrid.razor.cs` (422, 431), `Services/TimeZoneService.cs` (96)
- **Exact Scope:** Turn the empty catch-alls into `catch (Exception ex) { Logger.LogDebug(ex, "...disposal failed (circuit likely gone)"); }`. **Don't** narrow the caught types and don't rethrow. Control flow stays identical. Only a Debug log line is added. Both classes already have a logger (`RoomGalleryGrid.Logger`, `TimeZoneService`'s `logger` constructor parameter), so no DI change is needed.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus the target files contain no empty `catch { }` / `catch` block.

### P2-16: `HomeserverDiscoveryClient`: extract URL normalization and the failure log (security-sensitive)
- **Target Files:** `Infrastructure/Gateways/HomeserverDiscoveryClient.cs`
- **Exact Scope:** `uri.GetLeftPart(UriPartial.Path).TrimEnd('/')` appears 3 times (68, 89, 105). Extract `private static string ToBase(Uri)`. The identical debug-log block in `TryGetJsonAsync` / `IsSuccessAsync` (246-249, 265-268) becomes `private void LogDiscoveryFailure(Exception, string url)`. **Don't** touch exception filters, policy calls (`EnsureEntryAllowed`, `EnsureHopAllowed`, `IsAddressAllowed`), `ConnectAsync`, redirect handling or the DTO classes.
- **Risk Level:** Medium (SSRF boundary; needs extra review)
- **Done Criteria:** SDC, plus `SecurityInfrastructureTests` and `HomeserverDestinationPolicyTests` pass, plus a reviewer confirms that the order of policy checks in the diff is unchanged.

### P2-17: `MediaController`: extract the pure inline-safety check (security-sensitive)
- **Target Files:** `Controllers/MediaController.cs`, `tests/SynapseAdmin.Tests/MediaControllerConditionalTests.cs` (new cases)
- **Exact Scope:** Extract the classification at 208-212 (`isSafeInlineType` plus the `image/svg+xml` / `text/html` checks) into `internal static bool RequiresSandbox(string finalMimeType)` with **identical** semantics, including the current culture-sensitive `StartsWith(string)`. The CSP header value and its placement (215) stay unchanged. Fixing the comparison is open finding O-02 (§6.1), which follows directly after this task.
- **Characterize first:** Table tests for `image/png`→false, `image/svg+xml`→true, `text/html`→true, `video/mp4`→false, `application/pdf`→true, `IMAGE/PNG`→true (case-sensitive today).
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus the new table tests pass.

### P2-18: `Program.cs`: deduplicate the cookie redirect events
- **Target Files:** `src/SynapseAdmin/Program.cs`
- **Exact Scope:** `OnRedirectToLogin` and `OnRedirectToAccessDenied` (155-174) differ only in the status code. Use one local static function `Task RedirectOrStatus(RedirectContext<CookieAuthenticationOptions> ctx, int status)`. Keep the `/Media` prefix check via `StartsWithSegments`. Don't touch middleware order, cookie policy or the rate limiter.
- **Characterize first:** If no test asserts it yet, add a `WebApplicationFactory` test that an unauthenticated `GET /Media/Preview?mxc=x` returns 401 and `/rooms` does not.
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus the new integration test passes.

### P2-19: Extract `IMatrixAuthStateService` from `MatrixAuthenticationStateProvider`
- **Target Files:** new `Interfaces/IMatrixAuthStateService.cs`, `Services/MatrixAuthenticationStateProvider.cs`, `Program.cs` (144-145), `Components/Layout/NavMenu.razor.cs` (11), `Components/Pages/Login.razor.cs` (13)
- **Why:** AGENTS.md requires an interface for every service, but `NavMenu` and `Login` inject the concrete provider, which also makes them hard to test.
- **Exact Scope:**
  - Add an interface with exactly the members the two components use today: `GetAuthenticationStateAsync()`, `LoginAsync(...)`, `LoginWithTokenAsync(...)`, `LogoutAsync(NavigationManager?)`, `GetAccessToken()`, `GetUserId()`, `GetUsername()`, `GetHomeserver()`, with the same signatures. The provider implements it.
  - Register it as an alias for the **same scoped instance**: `AddScoped<IMatrixAuthStateService>(sp => (MatrixAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>())`. **Keep** `AddScoped<AuthenticationStateProvider, MatrixAuthenticationStateProvider>()`, because Blazor resolves the base type. Remove the concrete-type alias (145) only if `grep` shows no remaining consumer of the concrete type.
  - Switch `NavMenu` and `Login` to inject the interface. No logic changes.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus a DI or `WebApplicationFactory` test asserting that `IMatrixAuthStateService` and `AuthenticationStateProvider` resolve to the **same** instance within one scope, plus `grep -rn "MatrixAuthenticationStateProvider" src/SynapseAdmin/Components` is empty.

---

## 5. Phase 3: Cross-Module Deduplication (Medium Risk)

> Start a Phase 3 task only after the Phase 1/2 tasks for every module it touches are merged. Each task introduces **one** shared utility and migrates its call sites. Where the call-site count would push the diff over 200 lines, the task is split.

### P3-01: Shared MIME-type classification helper
- **Depends on:** P2-14, P2-17 (and O-02, if it has been fixed by then)
- **Target Files:** `Infrastructure/Helpers/MediaHelper.cs`, `Components/Shared/RoomGalleryGrid.razor.cs` (283-299), `Components/Pages/RoomDetails.razor.cs` (377-387), `Components/Pages/UserDetails.razor.cs` (377-381), `Controllers/MediaController.cs` (`RequiresSandbox` from P2-17), `tests/SynapseAdmin.Tests/HelperAndOperationResultTests.cs`
- **Exact Scope:** Add `MediaHelper.HasTopLevelType(string? mime, string type, StringComparison comparison)` plus `IsImage` / `IsVideo` / `IsAudio` overloads that take a `StringComparison`. Migrate call sites **with their current comparison**:
  - The gallery passes `OrdinalIgnoreCase`.
  - `RoomDetails` and `UserDetails` pass `CurrentCulture` (today's `StartsWith(string)` default).
  - `MediaController` passes whatever `RequiresSandbox` uses at that point (`CurrentCulture` today, `Ordinal` once O-02 is fixed).
  - Null policy stays at each call site: `RoomDetails.IsPreviewable` returns **true** for null, `UserDetails.IsPreviewable` returns **false**.
  - Keep the private `IsPreviewable` / `IsImageType` / `IsVideoType` / `IsAudioType` wrappers, because `.razor` files call them.
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus new tests cover all comparison × null combinations, plus no `StartsWith("image/")` / `"video/"` / `"audio/"` remains outside `MediaHelper`.

### P3-02: Shared media-preview launcher
- **Depends on:** P2-12, P2-13, P2-14
- **Target Files:** new `Components/Shared/MediaPreviewLauncher.cs` (static helper or `IDialogService` extension), `Components/Pages/RoomDetails.razor.cs` (`ShowPreview`, 389-403), `Components/Pages/UserDetails.razor.cs` (`ShowPreview`, 383-398), `Components/Shared/RoomGalleryGrid.razor.cs` (`GetPreviewUrl` 278-281, `ShowPreview` 310-322)
- **Exact Scope:** All three build `"/Media/Preview?mxc={Uri.EscapeDataString(mxc)}&mimeType={Uri.EscapeDataString(type ?? "")}"` and show `MediaPreviewDialog` with the same `DialogOptions` and parameters. Extract `BuildPreviewUrl(string mxc, string? mediaType)` and `ShowMediaPreviewAsync(this IDialogService, string mxc, string? uploadName, string mediaId, string? mediaType, string fallbackTitle)`. The URL, title fallback (`UploadName ?? MediaId` for the parameter and `UploadName ?? L["MediaPreview"]` for the dialog title) and options must stay identical. `UserDetails` keeps resolving the MXC before calling.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus a unit test for `BuildPreviewUrl` with characters that need escaping, plus `grep -rn "Media/Preview?mxc" src` matches only the helper.

### P3-03: Shared "is local Matrix ID" helper
- **Target Files:** new `Infrastructure/Helpers/MatrixIdHelper.cs`, `Components/Pages/RoomDetails.razor.cs` (`IsLocalUser`, 369-375), `Components/Pages/EventForensics.razor.cs` (`IsLocalUser`, from 86), `Services/UserService.cs` (51-55), new tests
- **Exact Scope:** Add `MatrixIdHelper.IsOnServer(string? id, string? serverName)`, which returns false for an empty ID or server and otherwise checks `EndsWith($":{serverName}", StringComparison.OrdinalIgnoreCase)`. `RoomDetails` and `EventForensics` call it directly. `UserService` must preserve "**allow** when `serverName` is empty", so it calls `string.IsNullOrEmpty(serverName) || MatrixIdHelper.IsOnServer(userId, serverName)`. **Exclude** the alias validation in `RoomAliasesDialog`: it is deliberately ordinal **and case-sensitive** (§6.2 S-04).
- **Risk Level:** Low
- **Done Criteria:** SDC, plus tests for empty ID, empty server, mixed case and a suffix-collision case (`@a:example.org.evil` vs server `example.org` → false).

### P3-04: Shared `SortDirection` → Synapse `dir` mapping
- **Target Files:** new `Extensions/SortDirectionExtensions.cs`, `Services/RoomService.cs:44`, `Services/UserService.cs:28`, `Services/EventReportService.cs:23`, `Services/FederationService.cs:23`, new tests
- **Exact Scope:** The services use two **intended** defaults for `SortDirection.None` (§6.2 S-01):
  - Room and User services use `Descending ? "b" : "f"`, so `None` ⇒ `"f"`.
  - EventReport and Federation services use `Ascending ? "f" : "b"`, so `None` ⇒ `"b"`.
  - Add `ToSynapseDir(this SortDirection d, string whenUnsorted)` and migrate each service with its current default, passed as a named argument (`whenUnsorted: "f"` / `whenUnsorted: "b"`). Don't unify the defaults.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus a test matrix covering 3 enum values × both defaults.

### P3-05: Claim-type constants
- **Depends on:** P2-09
- **Target Files:** new `Infrastructure/Security/MatrixClaimTypes.cs`, `Controllers/AuthController.cs` (59-60), `Controllers/MediaController.cs` (18-19), `Services/MatrixAuthenticationStateProvider.cs` (the factory from P2-09)
- **Exact Scope:** `public const string Homeserver = "Homeserver"; public const string AccessToken = "AccessToken";`. **The values must not change**: they are persisted inside issued authentication cookies. Don't touch `ClaimTypes.*` or the `"MatrixAuth"` authentication type.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `grep -rn '"Homeserver"\|"AccessToken"' src --include=*.cs` matches only the constants file (`L["AccessToken"]` in `UserDetails` is a resource key and is out of scope).

### P3-06: Gateway brand/version sentinel constants and the admin path constant
- **Target Files:** new `Infrastructure/Gateways/GatewayConstants.cs`, `Infrastructure/Gateways/MatrixGatewayBase.cs` (28-29), `Infrastructure/Gateways/SynapseCompatibleAdminGateway.cs` (19-20, 361), `Infrastructure/Gateways/MatrixAuthGateway.cs` (136, 143, 147, 160, 165-166, 178-179, 224), `Services/MatrixSessionService.cs:199`
- **Exact Scope:** `const string UnknownVersion = "Unknown"`, `GenericBrand = "Generic"`, `SynapseBrand = "Synapse"`, `ServerVersionPath = "/_synapse/admin/v1/server_version"`. They must be `const`, because they are used as default parameter values. Leave the case-insensitive brand comparisons (`"tuwunel"`, `"synapse"`) as literals or add separate constants. Values stay identical.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `grep -rn '"Unknown"' src --include=*.cs` matches only the constants file.

### P3-07a / P3-07b / P3-07c: Snackbar result helper
- **Depends on:** P2-11 to P2-14 for the files they touch
- **Target Files:**
  - **07a:** new `Extensions/SnackbarExtensions.cs` + tests, then migrate `Components/Pages/RoomDetails.razor.cs` (11 sites)
  - **07b:** migrate `UserDetails.razor.cs` (10), `Shared/RoomGalleryGrid.razor.cs` (6), `RegistrationTokens.razor.cs` (4), `RoomAliasesDialog.razor.cs` (3), `BackgroundUpdates.razor.cs` (3)
  - **07c:** migrate the remaining 15 files (20 sites) listed by `grep -rc "Severity != Severity.Normal" src/SynapseAdmin/Components`
- **Exact Scope:** Two helpers, because the code base mixes two semantics:
  - `ShowResult(this ISnackbar, OperationResult r)` shows **success and failure** when `r.Severity != Normal`.
  - `ShowFailure(this ISnackbar, OperationResult r)` shows **only** `!r.Success && r.Severity != Normal` (for example `Users`, `Rooms`, `RoomGalleryGrid.razor.cs:233`, `RoomDetails.razor.cs:201`, `FederationDestinations.razor.cs:72`).
  - Migrate each site to the helper that matches its current condition exactly. Sites that also set `errorMessage` (`LoadRoomDetails`, `LoadUserDetails`) keep that assignment.
- **Risk Level:** Medium (many sites; the wrong helper silently drops or adds a success toast)
- **Done Criteria:** SDC, plus helper unit tests for {Success, Failure, Cancelled} × {ShowResult, ShowFailure}, plus the snackbar-stub assertions in the existing bUnit tests are unchanged.

### P3-08: Shared clipboard helper
- **Target Files:** new `Extensions/JsRuntimeExtensions.cs`, `Components/Pages/RegistrationTokens.razor.cs` (`CopyToClipboard`, from 119), `Components/Pages/UserDetails.razor.cs` (194-202), `Components/Pages/EventForensics.razor.cs` (75-83)
- **Exact Scope:** Add `Task<bool> TryCopyToClipboardAsync(this IJSRuntime js, string text)`, which wraps `navigator.clipboard.writeText` in the **same** catch-all and returns false on any exception. Each caller keeps its own snackbar messages and severities. `UserDetails` shows `result.Message` alone on failure.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `grep -rn "navigator.clipboard" src` matches only the helper.

### P3-09: Shared media-metadata copy
- **Target Files:** `Models/ViewModels/RoomDetailViewModel.cs` (`RoomMediaItemViewModel`), `Services/RoomService.cs` (189-194), `Components/Shared/RoomGalleryGrid.razor.cs` (222-227), `Extensions/Mapping/RoomMappingExtensions.cs`
- **Exact Scope:** Move the gallery's vm→vm copy of the 6 metadata fields into `RoomMediaItemViewModel.CopyMetadataFrom(RoomMediaItemViewModel other)`. Move the `MediaInfo` → vm copy in `RoomService` into `RoomMappingExtensions.ApplyMetadata(this RoomMediaItemViewModel vm, MediaInfo meta)`. Preserve `SafeFromQuarantine ?? false` in the service copy (the gallery copies a bool).
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `RoomMediaDeduplicationTests`, `RoomGalleryDuplicateMediaTests` and `RoomGalleryFilterTests` pass.

### P3-10: Shared test localizer doubles (tests only)
- **Target Files:** new `tests/SynapseAdmin.Tests/TestSupport/KeyLocalizer.cs` and `FormattingKeyLocalizer.cs`; replace the nested classes in
  - `AuthCookieServiceTests.cs:26`, `RoomMediaDeduplicationTests.cs:28`, `UserMediaPaginationTests.cs:22`, `OrdinalComparisonTests.cs:57`, `NullResultContractTests.cs:75` (identical `KeyLocalizer`, format arguments **ignored**)
  - `TimeZoneAndPurgeTimestampTests.cs:342` (`StubStringLocalizer`) and `SecurityInfrastructureTests.cs:947` (`FakeAuthStringLocalizer`): identical, `string.Format(name, args)`
- **Exact Scope:** Two shared classes that keep the semantics above. **Keep** `UserExperimentalFeaturesTests.FakeStringLocalizer`, which has dictionary semantics and is not a duplicate. Change no assertions.
- **Risk Level:** Low
- **Done Criteria:** SDC with an unchanged test count, and `grep -rn "class .*Localizer : IStringLocalizer" tests` shows 3 classes (2 shared + `FakeStringLocalizer`).

### P3-11 (approval-gated): Service-layer result boilerplate helper
- **Status:** **Do not start without explicit owner approval.** It may count as a "paradigm rewrite" under REFACTOR_RULES §2.
- **Depends on:** P1-12
- **Target Files (one sub-task per service, executed separately):** **11a** `MediaService`, **11b** `RegistrationTokenService`, **11c** `EventReportService` + `FederationService`, **11d** `BackgroundUpdatesService`, **11e** `UserService` (split in two if > 200 lines), **11f** `RoomService` (split in two), plus a new `Services/GatewayServiceBase.cs`
- **Exact Scope:** 49 `Gateway == null → NotAuthenticated` guards and 59 `OperationCanceledException → Cancelled()` / `Exception → LogError + Failure(L[key])` ladders. A protected helper such as `Task<OperationResult<T>> RunAsync<T>(Func<IMatrixGateway, Task<OperationResult<T>>> body, string failureKey, Action<ILogger, Exception> log)` would remove them. It must preserve, per method:
  - the exact log template and arguments
  - the failure key
  - pre-`try` validation order (`UserService.GetUserDetailsAsync` remote check, the `EventForensicsService` triple guard, the `MediaService` blank-MXC guards)
  - the null-result contract (§6.2 S-02): `null` → `Ok(empty)` for collection reads, `null` → `Failure` for single-resource reads and operations. All services follow it, so the helper can take it as a parameter per method kind.
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus a per-service characterization suite (gateway null, gateway throws OCE, gateway throws, gateway returns null, success) is written before migration and is green after.

### P3-12 (optional, low value): `DialogOptions` presets
- **Target Files:** new `Components/Shared/DialogPresets.cs`, plus the 10 `new DialogOptions { … }` sites (`grep -rn "new DialogOptions" src`)
- **Exact Scope:** Factory **methods** (`DialogPresets.Small()`, `Medium()`, `SmallWithCloseButton()`, `MediumWithCloseButton()`) that return new instances. Don't share instances, because MudBlazor may mutate options. Keep the existing combinations exactly: `RoomDirectory` uses `CloseButton = true` without `CloseOnEscapeKey`.
- **Risk Level:** Low
- **Done Criteria:** SDC.

---

## 6. Open Findings and Standing Decisions

### 6.1 Open findings (change behavior, so not part of any task above)

| ID | Finding | Location | Plan |
|---|---|---|---|
| O-01 | Catch-all blocks that swallow exceptions without logging: theme type instantiation, three clipboard calls, a polling-cleanup fallback. (The two JS disposal catches in `RoomGalleryGrid` and the one in `TimeZoneService` get Debug logging in P2-15.) | `Services/ThemeService.cs:128`, `Components/Pages/EventForensics.razor.cs:80`, `UserDetails.razor.cs:199`, `RegistrationTokens.razor.cs:126`, `RoomDetails.razor.cs:521` | Adding Debug logging is behavior-neutral and can follow the P2-15 pattern. Narrowing the caught types is a behavior change and needs a separate decision. |
| O-02 | `MediaController.Preview` decides whether to apply the CSP sandbox with culture-sensitive, case-sensitive `StartsWith("image/")` etc. Culture-aware comparison ignores some zero-width and soft-hyphen characters, so a crafted MIME string could count as "safe inline" while evading the exact `"image/svg+xml"` check. | `Controllers/MediaController.cs:208-212` | Tracked in GitHub issue #392. Fix right after P2-17 by switching `RequiresSandbox` to `StringComparison.Ordinal` (the value is already lower-cased) and flipping the corresponding table test. |

### 6.2 Standing decisions (constraints for every task)

| ID | Decision | Where it is recorded |
|---|---|---|
| S-01 | `SortDirection.None` keeps each list's natural order: `"f"` for Users/Rooms (A→Z), `"b"` for Event Reports (newest first). Federation always passes `Ascending`. Don't unify. | P3-04 |
| S-02 | **Null gateway results:** collection reads return `Ok` with an empty collection, while single-resource reads and operations return `Failure`. | AGENTS.md "Null Gateway Results" |
| S-03 | **Login failures** never clear an existing session. Only `RestoreSessionAsync` clears the gateway on failure. | AGENTS.md "Login Failures" |
| S-04 | Identifier checks compare **ordinally**. The room alias server-name suffix is **case-sensitive** (Synapse compares it exactly); the `mxc://` scheme is matched case-insensitively. | `RoomAliasesDialog`, `MediaHelper`, `OrdinalComparisonTests` |
| S-05 | Tuning values (timeouts, cache lifetimes, rate limits, cookie lifetimes) stay **named constants**; making one configurable is a feature and needs a SPEC.md entry. | AGENTS.md "Tuning Values" |
| S-06 | `MediaController` sets `X-Content-Type-Options: nosniff` in addition to the global middleware on purpose (defense in depth for user uploads). Don't remove it as a duplicate. | AGENTS.md "Media Streaming", comments in `MediaController` |
| S-07 | The repo-root `.editorconfig` is the formatting reference. It sets no analyzer severities, and CI doesn't check formatting. | AGENTS.md "Formatting" |
| S-08 | Media sizes are formatted with `FormatSize()` everywhere. | `Extensions/FormatExtensions.cs` |

---

## 7. Recommended Execution Order

1. **Phase 1:** P1-01 → P1-02 → P1-03 → P1-04 → P1-05 → P1-06 → P1-07 → P1-08 → P1-09a/b/c → P1-10 → P1-11 → P1-12
2. **Phase 2, infrastructure first:** P2-10 → P2-07 → P2-06 → P2-05 → P2-08a/b/c → P2-09 → P2-01 → P2-02 → P2-03 → P2-04 → P2-16 → P2-17 → **O-02 fix** → P2-18 → P2-19
3. **Phase 2, components:** P2-12 → P2-11 → P2-13 → P2-14 → P2-15
4. **Phase 3:** P3-10 → P3-05 → P3-06 → P3-04 → P3-03 → P3-01 → P3-02 → P3-09 → P3-08 → P3-07a/b/c → (owner approval) P3-11a–f → P3-12

Per AGENTS.md workflow: each task (or a small, same-phase group of tasks) gets its own branch, e.g. `ai/feature/refactor-p1-01-unused-usings`, a Conventional Commit (`refactor: …`), local `dotnet build` + `dotnet test`, and a PR against `main`. **Line numbers** are exact for the revision in the header. Once tasks land, later line numbers drift; every location also names its method or member, so it can be found again.

---

## Appendix A: Findings resolved since the audit

Fixed or decided between the audit (`e34a2f3`) and `836c091`. Kept for traceability; nothing here needs action.

| ID | Finding | Resolution | Ref |
|---|---|---|---|
| H-01 | No formatting baseline | Repo-root `.editorconfig` added (S-07) | #396, PR #401 |
| H-02 | Timestamps rendered in UTC, bypassing `ITimeZoneService` | Six pages convert through `ITimeZoneService` | #393, PR #400 |
| H-03 | Token expiry converted with the server's time zone | `RegistrationTokenDialog` uses `ITimeZoneService` | #393, PR #400 |
| H-04 | Three different media-size formatters | All use `FormatSize()` (S-08) | #395, PR #402 |
| H-05 | Cancelled admin probe could cache a gateway without admin API | Probe catches let cancellation propagate | #391, PR #398 |
| H-08 | Culture-sensitive alias and MXC prefix checks | Ordinal comparisons (S-04) | #395, PR #402 |
| H-09 | Different unsorted defaults | Decided intended (S-01) | #395 |
| H-10 | Failed token login left a half logged-out session | Existing session kept (S-03) | #395, PR #402 |
| H-11 | Redundant `IsLoggedIn` guard in `MediaService` | Became task P1-12 | #395 |
| H-12 | Inconsistent null-result handling | Contract defined, two outliers fixed (S-02) | #395, PR #402 |
| H-13 | `CultureController.Set` accepted any culture | Only supported cultures are persisted; shared `SupportedCultures` list | #394, PR #399 |
| H-14 | Components inject the concrete auth state provider | Became task P2-19 | #396 |
| H-15 | Hard-coded tuning values | Decided: constants only (S-05) | #396 |
| H-16 | Duplicate `nosniff` header | Decided: keep both, commented (S-06) | #396, PR #401 |
| D-01 | Unused `SynapseAdminGateway` | Deleted; docs updated | #397, PR #403 |
| D-02 | Unused `IMatrixGateway.DownloadMediaAsync` | Removed | #397, PR #403 |
| D-03 | Test-only rate-limiter methods on public interfaces | Removed from the interfaces; `IsAllowed` kept as an internal test helper | #397, PR #403 |
| D-04 | Test-only `LoginRateLimiter(IMemoryCache)` constructor | Removed | #397, PR #403 |
| D-05 | Unused `FailureReason` view-model property | Removed | #397, PR #403 |
| D-06 | Unused `StartToken` view-model property | Removed | #397, PR #403 |
| D-07 | Unused `NavigationManager` injection in `UserDetails` | Removed | #397, PR #403 |
| D-08 | Unused `MudForm` reference in `RegistrationTokenDialog` | Removed | #397, PR #403 |
| D-09 | Unreferenced `Files` resource key | Was the label of a missing gallery filter chip; chip added | #397, PR #403 |
