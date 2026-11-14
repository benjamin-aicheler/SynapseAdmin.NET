# REFACTOR_PLAN.md

> **Audit type:** read-only code-quality and architecture audit (no source, test or config file was modified).
> **Audited revision:** `main` @ `e34a2f3` (2026-11-14).
> **Scope:** `src/SynapseAdmin/**` and `tests/SynapseAdmin.Tests/**`. The `LibMatrix/` submodule is **out of scope**, and AGENTS.md forbids modifying it.
> **Execution rules:** every task below must be executed under [REFACTOR_RULES.md](./REFACTOR_RULES.md): one task ID per pass, < 200 changed lines, mechanical and structural changes kept apart, pre-flight and post-edit build + test, characterization tests before Phase 2/3 structural edits.
> **Line numbers** refer to the audited revision. Re-locate them before editing.

---

## 0. Audit Snapshot

| Area | Finding |
|---|---|
| Build | 0 errors. **0 warnings in `src/` and `tests/`**. All 151 warnings come from the `LibMatrix/` and `ArcaneLibs/` submodules. |
| Tests | 177 / 177 pass (xUnit + bUnit + WebApplicationFactory). |
| Lint | Not configured: no `.editorconfig`, `Directory.Build.props`, analyzer settings or `dotnet format` step in CI. |
| Unused usings (IDE0005) | 17 in `src/`, 1 in `tests/`. |
| Formatting (IDE0055) | 95 locations in 36 files, almost all whitespace-only lines that contain trailing spaces. |
| Swallowed exceptions | 10 bare `catch`/`catch { }` blocks. 3 of them swallow `OperationCanceledException` (see §6). |
| Duplicated service boilerplate | 49 `if (Gateway == null) return …NotAuthenticated` guards, 59 `catch (OperationCanceledException)` blocks. |
| Duplicated UI boilerplate | 57 `if (result.Severity != Severity.Normal) Snackbar.Add(...)` blocks across 21 component files. |
| Cross-module duplicates | MIME-type classification (4 copies, semantics differ), media preview launcher (3), local-user check (3–4), claim-type strings (3 files), `SortDirection → dir` mapping (4, **two different semantics**), test localizer stubs (5). |

### Test-coupling warning (applies to all Phase 2/3 component tasks)

bUnit tests reach into components **by member name via reflection** (`DetailPageTestBase.InvokeHandler`, `GetField`). Renaming or removing these members breaks tests even though the compiler won't notice:

| Component | Private methods invoked by name | Private fields read by name |
|---|---|---|
| `RoomDetails` | `DeleteRoom`, `DeleteAndBlockRoom`, `QuarantineMedia`, `BlockRoom`, `LoadLocalMedia`, `LoadRemoteMedia` | `room` |
| `UserDetails` | `DeactivateUser`, `QuarantineAllMedia`, `LoginAsUser`, `LoadMediaPage` | `user`, `_displayedUserId`, `_mediaPage` |
| `RoomGalleryGrid` | `OnSourceFilterChanged` (with the literal values `"local"`, `"remote"`, `"all"`) | `_displayedMedia` |

**Rule for every task:** keep these names and signatures. If you extract logic, the named member stays as a thin wrapper.

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
| Lint | **None configured.** | — |

The C# compiler is the only type checker (`<Nullable>enable</Nullable>` in both projects). There is no separate type-check step.

### 1.2 Audit-only analyzer commands (non-gating, used to measure Phase 1 progress)

These commands enable the SDK's built-in analyzers through MSBuild properties. **They don't modify tracked files**, but they do overwrite `obj/`. Run `dotnet build --no-incremental` afterwards to restore normal build outputs. Run a normal `dotnet build` first so the `LibMatrix` project references exist.

```bash
# A1: style diagnostics (IDE0005 unused usings, IDE0055 formatting, IDE0044/0040/0036/1006, ...)
dotnet build src/SynapseAdmin/SynapseAdmin.csproj --no-incremental -p:BuildProjectReferences=false \
  -p:EnforceCodeStyleInBuild=true -p:GenerateDocumentationFile=true -p:AnalysisModeStyle=All -p:NoWarn=CS1591 \
  2>&1 | grep -E ": warning IDE" | sed -E 's/ \[[^]]*\]$//' | sort -u

# A1-tests: same for the test project (swap the project path)
dotnet build tests/SynapseAdmin.Tests/SynapseAdmin.Tests.csproj --no-incremental -p:BuildProjectReferences=false \
  -p:EnforceCodeStyleInBuild=true -p:GenerateDocumentationFile=true -p:AnalysisModeStyle=All -p:NoWarn=CS1591 \
  2>&1 | grep -E ": warning IDE0005" | sort -u

# A2: quality diagnostics (CA1822, CA1869, CA1508, CA1307/1310, CA2213, ...)
dotnet build src/SynapseAdmin/SynapseAdmin.csproj --no-incremental -p:BuildProjectReferences=false \
  -p:AnalysisMode=All -p:GenerateDocumentationFile=true -p:NoWarn=CS1591 \
  2>&1 | grep -E ": warning CA" | sed -E 's/ \[[^]]*\]$//' | sort -u

# Restore normal outputs afterwards
dotnet build --no-incremental
```

Diagnostic messages may appear in German on this machine, but the `warning IDExxxx` / `warning CAxxxx` tokens stay English, so the `grep` patterns work.

### 1.3 Recorded baseline (2026-11-14)

| Check | Baseline |
|---|---|
| `dotnet build --no-incremental` | 0 errors. 151 warnings, **all** in `LibMatrix/**` and `ArcaneLibs/**`. 0 in `src/` and `tests/`. |
| `dotnet test` | 177 passed, 0 failed, 0 skipped. |
| A1 `IDE0005` | 17 (src) + 1 (tests) |
| A1 `IDE0055` | 95 |
| A2 `CA1869` | 1 (`EventForensicsService.cs:60`) |
| A2 `CA1508` | 3 (`MatrixAuthGateway.cs:162`, `BackgroundUpdates.razor.cs:74`, `UserDetails.razor.cs:159`) |

### 1.4 Standard Done Criteria (SDC), required for every task in addition to its own criteria

1. `dotnet build --no-incremental` → 0 errors, and **0 warnings outside `LibMatrix/` and `ArcaneLibs/`** (total still 151).
2. `dotnet test` → all tests pass, with a test count ≥ 177 plus any characterization tests added by the task.
3. `git diff --stat` touches only the task's **Target Files** (plus new test files where stated), with < 200 changed lines.
4. No existing assertion was weakened or deleted, and no public signature, route, JSON shape, claim name, storage key, cache key or config key changed unless the task explicitly says so.
5. Tests run culture-independently. Also run `LANG=de_DE.UTF-8 dotnet test` once for any task that touches strings or formatting.

---

## 2. Suspected Dead Code (Requires Human Confirmation)

None of the items below may be deleted by an executing agent. Each needs an explicit owner decision, recorded in the PR.

### 2.1 Statically unreferenced in production code

> **All nine items were decided in #397 and resolved in the PR that closes it.** The last column records the outcome.

| # | Item | Location | Evidence | Why it might still be live / wanted | Decision (#397) |
|---|---|---|---|---|---|
| D-01 | `SynapseAdminGateway` (whole class) | `Infrastructure/Gateways/SynapseAdminGateway.cs` | Nothing instantiates it. `MatrixAuthGateway` always returns `SynapseCompatibleAdminGateway`. Marked `[Obsolete]` "emergency fallback". | Documented in AGENTS.md and ARCHITECTURE.md as a deliberate fallback, and it is the only consumer of LibMatrix `.Admin` APIs. | **Deleted.** Docs updated (AGENTS.md, ARCHITECTURE.md, CODEREVIEWTEMPLATE.md). |
| D-02 | `IMatrixGateway.DownloadMediaAsync` + `MatrixGatewayBase.DownloadMediaAsync` | `Interfaces/Gateways/IMatrixGateway.cs:83`, `MatrixGatewayBase.cs:33-62` | Only tests call it. Production media flows use `OpenMediaAsync`. | Public interface member that test doubles implement. It may be planned for thumbnails. Its bare `catch` hides errors (see §6). | **Removed** from the interface, the base class and the test doubles. |
| D-03 | `ILoginRateLimiter.IsAllowed`, `ILoginRateLimiter.RecordFailedAttempt` and their backing `ILoginRateLimitStore.IsAllowed` / `Increment` | `Interfaces/ILoginRateLimiter.cs`, `Services/LoginRateLimiter.cs:26-30`, `MemoryLoginRateLimitStore.cs` | Production uses only `TryAcquire` and `ResetAttempts`. The others are used only in `SecurityInfrastructureTests`. | Security component. Tests use them to assert bucket state, and they may be intended for future pluggable stores. | **Removed from both interfaces.** `IsAllowed` kept as an `internal` test helper. |
| D-04 | `internal LoginRateLimiter(IMemoryCache)` constructor | `Services/LoginRateLimiter.cs:18` | Test-only, reachable via `InternalsVisibleTo`. | Test convenience. | **Removed.** The one test builds the store explicitly. |
| D-05 | `FederationDestinationListViewModel.FailureReason` | `Models/ViewModels/FederationDestinationListViewModel.cs:12` | Never assigned (`FederationMappingExtensions` doesn't map it), never read, and has no matching DTO field. | Possibly a planned UI column. | **Removed.** |
| D-06 | `RoomMessagesViewModel.StartToken` | `Models/ViewModels/RoomMessagesViewModel.cs:6` | Assigned in `RoomService.cs:367`, never read. | May be needed for backward pagination. | **Removed.** |
| D-07 | `[Inject] NavigationManager Navigation` in `UserDetails` | `Components/Pages/UserDetails.razor.cs:16-17` | Never read in `.razor` or `.razor.cs`. | DI-populated, and a bUnit test base may register `NavigationManager`. | **Removed.** |
| D-08 | `MudForm? form` field in `RegistrationTokenDialog` (IDE0052) | `Components/Pages/RegistrationTokenDialog.razor.cs:24` | Written only by `@ref`, never read. | Removing it also requires removing `@ref="form"` from the `.razor` file. | **Removed** (field and `@ref`). |
| D-09 | Resource key `Files` | `Resources/SharedResources{,.de,.fr}.resx` | No static `L["Files"]` reference. | `IStringLocalizer` keys are strings, so a key could be built dynamically. Only `ExperimentalFeature_*` is built that way today. | **Kept and used:** it was the label for the gallery's "file" type filter, which had no chip. The chip was added. |

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
| `UserService.MediaStatisticsSearchTerm`, `SynapseCompatibleAdminGateway.Build*Url` (`internal`) | Tests via `InternalsVisibleTo` |
| JS modules `auth-bridge.js`, `gallery-interop.js`, `timezone-interop.js` | `IJSRuntime.InvokeAsync("import", "./js/...")` string paths |
| Private members listed in the §0 test-coupling table | Reflection in bUnit tests |

---

## 3. Phase 1: Mechanical Cleanup (Zero Logic Risk)

> Mechanical only: no control-flow, expression or member changes. Each task is one concern.

### P1-01: Remove unused `using` directives in services, gateways, models and interfaces
- **Target Files:** `Services/EventForensicsService.cs`, `Services/LoginRateLimiter.cs`, `Infrastructure/Gateways/MatrixAuthGateway.cs`, `Infrastructure/Gateways/SynapseCompatibleAdminGateway.cs`, `Infrastructure/Security/ClientIdentityProvider.cs`, `Interfaces/IRoomService.cs`, `Models/Responses/SynapseAdminDeleteMediaResponse.cs`, `Models/ViewModels/LoginViewModel.cs` (all under `src/SynapseAdmin/`).
- **Exact Scope:** Delete only the `using` lines that analyzer A1 reports as `IDE0005` in these files (9 diagnostics since `SynapseAdminGateway.cs` was deleted in #397). Examples: `using System.Net;` in `EventForensicsService.cs:1`, `using Microsoft.Extensions.DependencyInjection;` in `LoginRateLimiter.cs:1`, `using System.Collections.Generic;` (already implicit) in the model files. **Do not** reorder the remaining usings or touch any other line. Do not add global usings.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus A1 reports 0 `IDE0005` in the target files.

### P1-02: Remove unused `using` directives in components
- **Target Files:** `Components/Layout/NavMenu.razor.cs`, `Components/Pages/PublishRoomDialog.razor.cs`, `Components/Pages/RoomAliasesDialog.razor.cs`, `Components/Pages/RoomDirectory.razor.cs`, `Components/Shared/RoomGalleryGrid.razor.cs`.
- **Exact Scope:** Delete only the A1 `IDE0005` usings (7 diagnostics). Examples: `using System; using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;` in `RoomDirectory.razor.cs:5-8` and `RoomAliasesDialog.razor.cs:6-9` (redundant with `ImplicitUsings`), and `Microsoft.Extensions.Localization` / `SynapseAdmin.Resources` where `L` is injected only by `_Imports.razor`. Don't touch `.razor` files.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus A1 reports 0 `IDE0005` under `Components/`.

### P1-03: Remove the unused `using` in tests
- **Target Files:** `tests/SynapseAdmin.Tests/TimeZoneAndPurgeTimestampTests.cs`
- **Exact Scope:** Delete the single using reported by A1-tests (line 11). Nothing else.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus A1-tests reports 0 `IDE0005`.

### P1-04: Remove redundant `@inject IStringLocalizer<SharedResources> L` lines
- **Target Files:** `Components/Shared/RoomGalleryGrid.razor:2`, `Components/Pages/RenewAccountDialog.razor:3`, `Components/Pages/PurgeHistoryDialog.razor:3`, `Components/Pages/RoomGallery.razor:4`.
- **Exact Scope:** `Components/_Imports.razor:20` already injects the same service under the same name for every component in `Components/**`. Remove only the duplicate `@inject` line in each file. Keep `@using` lines and markup unchanged.
- **Risk Level:** Low
- **Done Criteria:** SDC. The `RoomGalleryDuplicateMediaTests`, `TimeZoneAndPurgeTimestampTests` and `UserDetails*` tests still pass, which proves the localizer still resolves in these components.

### P1-05: Replace redundant fully qualified type names with existing usings (gateways)
- **Target Files:** `Infrastructure/Gateways/MatrixGatewayBase.cs`, `Infrastructure/Gateways/SynapseCompatibleAdminGateway.cs`
- **Exact Scope:**
  - `MatrixGatewayBase.cs` already has `using LibMatrix.StructuredData;` and `using LibMatrix.Homeservers;`, so shorten `LibMatrix.StructuredData.MxcUri` (lines 246, 254, 262, 269, 276) and `LibMatrix.Homeservers.RoomDirectoryFilter` / `RoomDirectoryVisibilityResponse` (lines 286-289, 327-328).
  - `SynapseCompatibleAdminGateway.cs`: shorten `LibMatrix.StructuredData.MxcUri` (375) by adding `using LibMatrix.StructuredData;`. Leave `System.Net.HttpStatusCode` and `LibMatrix.MatrixException` as they are, because adding `using System.Net;` / `using LibMatrix;` could introduce name ambiguities, which this mechanical task must not resolve.
  - Change no expressions.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus the IL is unchanged (optional check: `git stash` and compare the `SynapseAdmin.dll` metadata, or just rely on the full test pass).

### P1-06: Replace redundant fully qualified type names (services and components)
- **Target Files:** `Services/RoomService.cs` (`System.Text.Json.Nodes.JsonNode` and `JsonValue` at 642-645, with `using System.Text.Json;` already present: add `using System.Text.Json.Nodes;`), `Components/Pages/RoomDetails.razor.cs:26` (`Microsoft.Extensions.Logging.ILogger<>` is implicit), `Controllers/MediaController.cs:153` (`Infrastructure.Helpers.MediaHelper`: add `using SynapseAdmin.Infrastructure.Helpers;`), `Components/Pages/UserDetails.razor.cs:206-207` (`System.Net.WebUtility`).
- **Exact Scope:** Type-name shortening only. No other edits.
- **Risk Level:** Low
- **Done Criteria:** SDC.

### P1-07: Drop the redundant `TaskCanceledException` from an exception filter
- **Target Files:** `Services/AuthCookieService.cs:51`
- **Exact Scope:** `ex is OperationCanceledException or JSDisconnectedException or TaskCanceledException` becomes `ex is OperationCanceledException or JSDisconnectedException`. `TaskCanceledException` derives from `OperationCanceledException`, so the match set is identical. Don't change the `JSException` branch.
- **Risk Level:** Low
- **Done Criteria:** SDC, and `AuthCookieServiceTests` pass.

### P1-08: Normalize modifiers, `readonly` fields and explicit default initializers
- **Target Files:** `Models/OperationResult.cs` (IDE0036: `public new static` becomes `public static new` at lines 26, 31), `Components/Pages/Login.razor.cs:25,29` and `Components/Pages/UserCreateDialog.razor.cs:15` (IDE0044: add `readonly`), `Components/Pages/RegistrationTokenDialog.razor.cs:10`, `RenewAccountDialog.razor.cs:10`, `UserCreateDialog.razor.cs:10` (IDE0040: add explicit `private`), CA1805 redundant `= false` / `= 0` initializers in `BackgroundUpdates.razor.cs:24-25`, `MediaMaintenance.razor.cs:25-26`, `ServerNotices.razor.cs:18`, `RoomGalleryGrid.razor.cs:29-31`, `Services/ThemeService.cs:19`.
- **Exact Scope:** Only the listed diagnostics. **Do not** add `readonly` to fields the generated Razor code writes (`@bind`, `@ref`). If adding `readonly` causes a compile error, revert that one line. Don't rename anything.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus A1 and A2 report 0 `IDE0036`/`IDE0040`/`IDE0044`/`CA1805` in the target files.

### P1-09a / P1-09b / P1-09c: Whitespace normalization (IDE0055)
- **Target Files:**
  - **09a:** `Services/**`, `Controllers/**`, `Program.cs`
  - **09b:** `Components/**/*.razor.cs`
  - **09c:** `Infrastructure/**`, `Models/**`, `Extensions/**`, `Interfaces/**`
- **Exact Scope:** Run `dotnet format whitespace src/SynapseAdmin/SynapseAdmin.csproj --include <folder>` for the task's folders only, then inspect the diff. Accept only whitespace changes (trailing spaces on blank lines, indentation). Revert any hunk that changes brace placement, line breaks inside expressions or blank-line count. **Prerequisite (human decision H-01):** confirm that no `.editorconfig` should be introduced first, since an `.editorconfig` would change what "correct" formatting means.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `git diff --ignore-all-space --stat` for the task is **empty**, plus A1 reports 0 `IDE0055` in the task's folders.

### P1-10: Convert HTML comments in `.razor` markup to Razor comments
- **Target Files:** `Components/Shared/RoomGalleryGrid.razor` (lines 6, 38, 52, 57, 59, 74, 103, 122, 167), `Components/Pages/BackgroundUpdates.razor:38`, and any other `.razor` file `grep -rn "<!--" --include=*.razor src` finds.
- **Exact Scope:** `<!-- text -->` becomes `@* text *@`. Comment text stays the same. This removes developer comments from the HTML sent to browsers. No element or attribute changes.
- **Risk Level:** Low (rendered markup loses the comment nodes; no logic change)
- **Done Criteria:** SDC, plus `grep -rn "<!--" --include=*.razor src` is empty.

### P1-11: Name the purge-polling and filter magic values as private constants (no behavior change)
- **Target Files:** `Components/Pages/RoomDetails.razor.cs`
- **Exact Scope:** Introduce `private const` values for the literals that already exist: purge status strings `"complete"`, `"failed"`, `"active"` (442, 453, 559), poll interval `5000` ms (500), the failure threshold `5` (484, 486, also used in the log text), the shadow page size `50` (186), and `"/rooms"` (226, 249). Values must stay byte-identical. **Don't** restructure `StartPolling` (that is P2-11).
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `RoomDetailsStaleSelectionTests` pass.

---

## 4. Phase 2: Module-Level Simplification (Low–Medium Risk)

> Each task stays inside one module. Under REFACTOR_RULES §4.2, write **characterization tests first** wherever "Characterize first" is listed, commit them, and confirm they pass **before** the production change.

### P2-01: `SynapseCompatibleAdminGateway`: extract the "GET + compat-JSON deserialize" helper
- **Target Files:** `Infrastructure/Gateways/SynapseCompatibleAdminGateway.cs`, new test file `tests/SynapseAdmin.Tests/SynapseCompatibleGatewayCharacterizationTests.cs`
- **Exact Scope:** Five methods repeat `GetAsync → EnsureSuccessStatusCode → ReadAsStreamAsync → JsonSerializer.DeserializeAsync<T>(…, SynapseCompatibilityJsonOptions, ct)`: `GetUserListAsync` (51-54), `GetUserMediaStatisticsAsync` (111-114), `GetUserMediaAsync` (139-142), `GetFederationDestinationListAsync` (263-266), `GetEventReportListAsync` (292-295). Extract `private async Task<T?> GetWithCompatibilityOptionsAsync<T>(string url, CancellationToken ct)`. **Must not** switch to `GetFromJsonAsync`. That is the Known Pitfall in AGENTS.md: `MatrixHttpClient` mutates options. Keep `protected static readonly SynapseCompatibilityJsonOptions` unchanged, because `UserMediaPaginationTests.GatewayProbe` reads it. URL building stays untouched.
- **Characterize first:** Using the fake-`HttpMessageHandler` homeserver setup already present in `MediaRangeForwardingTests`, assert for each of the 5 methods that (a) a numeric and a string `next_token` both deserialize, (b) a non-2xx response throws `HttpRequestException`, and (c) the request URL is unchanged.
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus the new characterization tests pass before and after, plus `grep -c "SynapseCompatibilityJsonOptions, cancellationToken" SynapseCompatibleAdminGateway.cs` == 1.

### P2-02: `SynapseCompatibleAdminGateway`: extract the raw DELETE helper
- **Target Files:** `Infrastructure/Gateways/SynapseCompatibleAdminGateway.cs`, the characterization test file from P2-01
- **Exact Scope:** `DeleteRoomAsync` (200-209, with JSON body), `DeleteEventReportAsync` (298-304), `DeleteRegistrationTokenAsync` (349-355) and `DeleteMediaAsync` (393-399) become calls to `private async Task SendDeleteAsync(string url, HttpContent? content, CancellationToken ct)`. Keep `new HttpRequestMessage(HttpMethod.Delete, url)` + `ClientHttpClient.SendAsync(req, ct)` + `EnsureSuccessStatusCode` (AGENTS.md "Always" rule: LibMatrix `DeleteAsync` lacks a token overload). The DeleteRoom body must still be `JsonSerializer.Serialize(request)` with default options, UTF-8 and `application/json`.
- **Characterize first:** Capture method, URL, body and `Content-Type` for all 4 calls.
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus the captured requests are identical before and after.

### P2-03: `SynapseCompatibleAdminGateway`: extract the empty-body POST/PUT helpers
- **Target Files:** `Infrastructure/Gateways/SynapseCompatibleAdminGateway.cs`, the characterization test file from P2-01
- **Exact Scope:** 7 call sites send `new { }` and then call only `EnsureSuccessStatusCode`: `QuarantineMediaByUserIdAsync` (72, PUT), `QuarantineMediaByRoomIdAsync` (214, PUT), `ResetFederationConnectionTimeoutAsync` (272, POST), `QuarantineMediaAsync` (382, PUT), `UnquarantineMediaAsync` (389, POST), `ProtectMediaAsync` (404, POST), `UnprotectMediaAsync` (411, POST). `LoginAsUserAsync` (79), `PurgeRemoteMediaCacheAsync` (418) and `DeleteLocalMediaAsync` (426) also send `{}` but read a response body; leave those three as they are. Extract `PostEmptyAsync(url, ct)` and `PutEmptyAsync(url, ct)` that preserve the `{}` JSON body exactly. Leave `ProtectMediaAsync`/`UnprotectMediaAsync` ignoring `serverName` (interface contract).
- **Characterize first:** Assert method, URL and body `{}` for each site.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus the request captures are identical.

### P2-04: `MatrixAuthGateway`: flatten `CreateAuthenticatedGatewayAsync` and extract the version-check helper
- **Target Files:** `Infrastructure/Gateways/MatrixAuthGateway.cs`, new tests in `tests/SynapseAdmin.Tests/MatrixAuthGatewayProbeTests.cs`
- **Exact Scope:** The method is ~130 lines with nesting depth 6.
  1. Extract `internal static bool IsTuwunelAdminCapable(string version)`: strip the suffix after the first `-` if `dashIdx > 0`, `Version.TryParse`, `>= 1.8.1`, and **unparseable ⇒ true**.
  2. Extract `ProbeSynapseVersionAsync`, `ProbeFederationVersionAsync` and `ProbeAdminApiAsync` as private methods that return tuples.
  3. Remove the always-true `if (authenticatedHomeserver is AuthenticatedHomeserverGeneric generic)` (CA1508, line 162) by using the variable directly.
  - **Preserve exactly** the probe `catch` semantics found on `main` at execution time. **Prerequisite: #398 (fix for H-05 / #391) must be merged first.** After that merge, the four probe catches (153, 210, 236, 243) are `catch (Exception) when (!cancellationToken.IsCancellationRequested)`. Keep those filters as they are.
  - Preserve the order of probes, the brand/version defaults `"Generic"` / `"Unknown"` and the gateway types returned.
  - Don't touch caching (`GetAuthenticatedAsync`, `CreateAndCacheGatewayAsync`).
- **Characterize first:** Table tests for `IsTuwunelAdminCapable`: `"1.8.1"`→true, `"1.8.0"`→false, `"1.9.0-dev"`→true, `"-1.0"`→(dashIdx 0, unparseable)→true, `"abc"`→true, `"1.8"`→false. Write them against the extracted function in the same PR, but extract it as a verbatim copy first and assert before switching callers. `SecurityInfrastructureTests.TestableMatrixAuthGateway` overrides this method, so it currently has **no** coverage.
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus the new table tests pass, plus A2 reports no `CA1508` in this file, plus the method body is < 60 lines.

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
- **Exact Scope:** Lines 371-401 build `RoomMessageItemViewModel` twice. Compute `content` (`GetRawText()` or `null`) and `body` first, then build the view model once. Preserve: `Null`/`Undefined` ⇒ `Content == null`, `Body == null`; non-object JSON ⇒ `Content` = raw text, `Body == null`; object without a string `body` ⇒ `bodyProp.GetString()` semantics, **including throwing** on a non-string `body`. The whole call then returns `ErrorFetchingRoomMessages`, and that must stay as it is.
- **Characterize first:** The four content shapes above, plus `"body": 5`, which today yields a failure result.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus the characterization passes.

### P2-07: `RoomService`: deduplicate the homeserver-scoped cache-key prefix
- **Target Files:** `Services/RoomService.cs`
- **Exact Scope:** `GetRoomNameCacheKey` (27-28) and `GetActivePurgeCacheKey` (422-423) both compute `Gateway?.HomeserverUrl?.TrimEnd('/').ToLowerInvariant()`. Extract `private string? HomeserverCacheScope`. Keys must stay byte-identical (`room_name:{scope}:{roomId}` / `active_purge:{scope}:{roomId}`). Move the `ActivePurgeCacheOptions` field next to `RoomNameCacheOptions`. That is a declaration move only.
- **Characterize first:** Unit test asserting both key strings for a gateway whose URL is `https://HS.example/`.
- **Risk Level:** Low
- **Done Criteria:** SDC.

### P2-08a / P2-08b / P2-08c: Move inline DTO→ViewModel mappings into `Extensions/Mapping`
- **Target Files:**
  - **08a:** `Services/RoomService.cs` (public rooms, 506-523) → `Extensions/Mapping/RoomMappingExtensions.cs`
  - **08b:** `Services/UserService.cs` (media page items 139-148, top media users 300-306, memberships 363-367) → `Extensions/Mapping/UserMappingExtensions.cs`
  - **08c:** `Services/BackgroundUpdatesService.cs` (25-37) → new `Extensions/Mapping/BackgroundUpdatesMappingExtensions.cs`
- **Exact Scope:** Move the object initializers verbatim into extension methods. Services keep their try/catch and `OperationResult` handling. Don't merge with existing mappings. **Never touch** the `creation_ts` seconds/milliseconds conversions (AGENTS.md "Do not unify").
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
- **Target Files:** `Components/Pages/RoomDetails.razor.cs`, `tests/SynapseAdmin.Tests/Components/RoomDetailsStaleSelectionTests.cs` (new test cases only)
- **Exact Scope:** The 4-line block `RoomService.ClearActivePurgeId(roomId); activePurgeId = null; activePurgeStatus = null; …` appears 4 times (444-446, 455-457, 490-492, 514-516). Extract `private void ClearActivePurge(string roomId)`. Extract the `status == complete/failed` handling into `private bool ApplyPurgeStatus(string roomId, string? status, string? error)`, which returns `shouldStop`. Keep the `Task.Run` + `InvokeAsync` threading model, the cancellation checks, the 5-failure threshold, the delay and the snackbar texts. Depends on P1-11 (constants).
- **Characterize first:** Using a `Pending<T>()` status source and a short delay override (only if one can be injected without changing production code; otherwise test `ApplyPurgeStatus` directly through `InvokeHandler`), cover "complete", "failed" with and without an error, and 5 consecutive failures.
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus `StartPolling` is < 60 lines, plus every §0 reflection-accessed name is unchanged.

### P2-12: `RoomDetails`: deduplicate the delete handlers and the media-table reload
- **Target Files:** `Components/Pages/RoomDetails.razor.cs`
- **Exact Scope:** `DeleteRoom` (208-229) and `DeleteAndBlockRoom` (231-252) differ only in resource keys and the `block` flag. Make both thin wrappers over `private Task ConfirmAndDeleteRoomAsync(bool block, string titleKey, string confirmKey, string yesKey)`. **Both names must remain** (reflection, see §0). Also extract `private async Task ReloadMediaTablesAsync()` for the repeated `localMediaTable?.ReloadServerData()` / `remoteMediaTable?.ReloadServerData()` pairs (300-301, 322-323, 345-346). `ToggleMediaProtection` reloads only the local table (365); leave it as it is.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `RoomDetailsStaleSelectionTests` (`DeleteRoom`, `DeleteAndBlockRoom`) pass unchanged.

### P2-13: `UserDetails`: extract the MXC resolution and item-patch helpers
- **Target Files:** `Components/Pages/UserDetails.razor.cs`
- **Exact Scope:** Five methods start with `if (MatrixSession.Gateway == null) return; var mxc = MediaHelper.ToMxcUri(x, MatrixSession.Gateway.ServerName);` (278-279, 307-308, 336-337, 360-361, 387-388). Extract `private string? ResolveMxc(string mediaId)` (null ⇒ return early). Extract `private void PatchMediaItem(string mediaId, Action<UserMediaItemViewModel> patch)` for 295-300 and 324-329. Keep the early-return order before confirmation dialogs. Keep the optimistic value `QuarantinedBy = "Admin"`. Keep the names `LoadMediaPage`, `DeactivateUser`, `QuarantineAllMedia`, `LoginAsUser` and the fields `user`, `_displayedUserId`, `_mediaPage`.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `UserDetailsMediaTabTests` and `UserDetailsStaleSelectionTests` pass.

### P2-14: `RoomGalleryGrid`: deduplicate the local/remote merge loop and name filter values
- **Target Files:** `Components/Shared/RoomGalleryGrid.razor.cs`, `Components/Shared/RoomGalleryGrid.razor`
- **Exact Scope:** Lines 113-130 repeat the same loop for `Local` and `Remote`. Extract `private void AddUnique(IEnumerable<RoomMediaItemViewModel>? items, bool isLocal, HashSet<string> seen)`. Preserve the ordinal comparer, local-first ordering and the skip of empty IDs (#384). Introduce `private const string` values for the filter literals `"all"`, `"local"`, `"remote"`, `"image"`, `"video"`, `"audio"`, `"file"` and use them in both the `.razor.cs` and `.razor` files. The **values stay identical**, because tests pass `"local"`, `"remote"` and `"all"`. Keep `OnSourceFilterChanged` and `_displayedMedia`.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `RoomGalleryDuplicateMediaTests` pass.

### P2-15: `RoomGalleryGrid` and `TimeZoneService`: add Debug logging to the empty disposal catches
- **Target Files:** `Components/Shared/RoomGalleryGrid.razor.cs:429,438`, `Services/TimeZoneService.cs:96`
- **Exact Scope:** Turn `catch { }` into `catch (Exception ex) { Logger.LogDebug(ex, "...disposal failed (circuit likely gone)"); }`. **Don't** narrow the caught types and don't rethrow. Control flow stays identical. Only a Debug log line is added. `TimeZoneService` needs an `ILogger<TimeZoneService>`. **Stop and ask** if it doesn't already have one, because adding a constructor dependency changes the DI signature.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `grep -n "catch { }" src -r` returns no results in the target files.

### P2-16: `HomeserverDiscoveryClient`: extract URL normalization and the failure log (security-sensitive)
- **Target Files:** `Infrastructure/Gateways/HomeserverDiscoveryClient.cs`
- **Exact Scope:** `uri.GetLeftPart(UriPartial.Path).TrimEnd('/')` appears 3 times (68, 89, 105). Extract `private static string ToBase(Uri)`. The identical debug-log block in `TryGetJsonAsync`/`IsSuccessAsync` (246-249, 265-268) becomes `private void LogDiscoveryFailure(Exception, string url)`. **Don't** touch exception filters, policy calls (`EnsureEntryAllowed`, `EnsureHopAllowed`, `IsAddressAllowed`), `ConnectAsync`, redirect handling or the DTO classes.
- **Risk Level:** Medium (SSRF boundary, needs extra review)
- **Done Criteria:** SDC, plus `SecurityInfrastructureTests` and `HomeserverDestinationPolicyTests` pass, plus a reviewer confirms that the order of policy checks in the diff is unchanged.

### P2-17: `MediaController`: extract the pure inline-safety check (security-sensitive)
- **Target Files:** `Controllers/MediaController.cs`, `tests/SynapseAdmin.Tests/MediaControllerConditionalTests.cs` (new cases)
- **Exact Scope:** Extract lines 204-208 into `internal static bool RequiresSandbox(string finalMimeType)` with **identical** semantics, including the current culture-sensitive `StartsWith(string)` (see H-07 before changing that). Header value and placement stay unchanged.
- **Characterize first:** Table tests for `image/png`→false, `image/svg+xml`→true, `text/html`→true, `video/mp4`→false, `application/pdf`→true, `IMAGE/PNG`→true (case-sensitive today).
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus the new table tests pass.

### P2-18: `Program.cs`: deduplicate the cookie redirect events
- **Target Files:** `src/SynapseAdmin/Program.cs`
- **Exact Scope:** `OnRedirectToLogin` and `OnRedirectToAccessDenied` (154-173) differ only in the status code. Use one local static function `Task RedirectOrStatus(RedirectContext<CookieAuthenticationOptions> ctx, int status)`. Keep the `/Media` prefix check via `StartsWithSegments`. Don't touch middleware order, cookie policy or the rate limiter.
- **Characterize first:** If no test exists yet, add a `WebApplicationFactory` test asserting an unauthenticated `GET /Media/Preview?mxc=x` returns 401 and `/rooms` does not return 401.
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus the new integration test passes.

---

## 5. Phase 3: Cross-Module Deduplication (Medium Risk)

> Start a Phase 3 task only after the Phase 1/2 tasks for every module it touches are merged. Each task introduces **one** shared utility and migrates its call sites. Where the call-site count would push the diff over 200 lines, the task is split.

### P3-01: Shared MIME-type classification helper
- **Depends on:** P2-14, P2-17
- **Target Files:** `Infrastructure/Helpers/MediaHelper.cs`, `Components/Shared/RoomGalleryGrid.razor.cs` (283-299), `Components/Pages/RoomDetails.razor.cs` (377-387), `Components/Pages/UserDetails.razor.cs` (379-383), `Controllers/MediaController.cs` (`RequiresSandbox` from P2-17), `tests/SynapseAdmin.Tests/HelperAndOperationResultTests.cs`
- **Exact Scope:** Add `MediaHelper.HasTopLevelType(string? mime, string type, StringComparison comparison)` plus `IsImage`/`IsVideo`/`IsAudio` overloads that take a `StringComparison`. Migrate call sites **with their current comparison**:
  - The gallery passes `OrdinalIgnoreCase`.
  - `RoomDetails`, `UserDetails` and `MediaController` pass `CurrentCulture` (today's `StartsWith(string)` default).
  - Null policy stays at each call site: `RoomDetails.IsPreviewable` returns **true** for null, `UserDetails` returns **false**.
  - Keep the private `IsPreviewable` / `IsImageType` wrappers, because `.razor` files call them.
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus new tests cover all comparison × null combinations, plus no `StartsWith("image/")` / `"video/"` / `"audio/"` remains outside `MediaHelper`.

### P3-02: Shared media-preview launcher
- **Depends on:** P2-12, P2-13, P2-14
- **Target Files:** new `Components/Shared/MediaPreviewLauncher.cs` (static helper or `IDialogService` extension), `Components/Pages/RoomDetails.razor.cs` (389-403), `Components/Pages/UserDetails.razor.cs` (385-400), `Components/Shared/RoomGalleryGrid.razor.cs` (278-281, 317-329)
- **Exact Scope:** All three build `"/Media/Preview?mxc={Uri.EscapeDataString(mxc)}&mimeType={Uri.EscapeDataString(type ?? "")}"` and show `MediaPreviewDialog` with the same `DialogOptions` and parameters. Extract `BuildPreviewUrl(string mxc, string? mediaType)` and `ShowMediaPreviewAsync(this IDialogService, string mxc, string? uploadName, string mediaId, string? mediaType, string fallbackTitle)`. The URL, title fallback (`UploadName ?? MediaId` for the parameter and `UploadName ?? L["MediaPreview"]` for the dialog title) and options must stay identical. `UserDetails` keeps resolving the MXC before calling.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus a unit test for `BuildPreviewUrl` with characters that need escaping, plus `grep -rn "Media/Preview?mxc" src` matches only the helper.

### P3-03: Shared "is local Matrix ID" helper
- **Target Files:** new `Infrastructure/Helpers/MatrixIdHelper.cs`, `Components/Pages/RoomDetails.razor.cs` (369-375), `Components/Pages/EventForensics.razor.cs` (82-87), `Services/UserService.cs` (51-55), new tests
- **Exact Scope:** Add `MatrixIdHelper.IsOnServer(string? id, string? serverName)`, which returns false for empty ID or empty server and otherwise checks `EndsWith($":{serverName}", OrdinalIgnoreCase)`. `RoomDetails` and `EventForensics` call it directly. `UserService` must preserve "**allow** when `serverName` is empty", so it calls `string.IsNullOrEmpty(serverName) || MatrixIdHelper.IsOnServer(userId, serverName)`. **Exclude** `RoomAliasesDialog.razor.cs:76`, which is case-sensitive and culture-sensitive today (see H-08).
- **Risk Level:** Low
- **Done Criteria:** SDC, plus tests for empty ID, empty server, mixed case and a suffix-collision case (`@a:example.org.evil` vs server `example.org` → false).

### P3-04: Shared `SortDirection` → Synapse `dir` mapping
- **Target Files:** new `Extensions/SortDirectionExtensions.cs`, `Services/RoomService.cs:44`, `Services/UserService.cs:28`, `Services/EventReportService.cs:23`, `Services/FederationService.cs:23`, new tests
- **Exact Scope:** The two existing semantics differ for `SortDirection.None`:
  - Room and User services use `Descending ? "b" : "f"`, so `None` ⇒ `"f"`.
  - EventReport and Federation services use `Ascending ? "f" : "b"`, so `None` ⇒ `"b"`.
  - Add `ToSynapseDir(this SortDirection d, string whenUnsorted)` and migrate each service with its **current** default. **Don't unify** the defaults (see H-09).
- **Risk Level:** Low
- **Done Criteria:** SDC, plus a test matrix covering 3 enum values × both defaults.

### P3-05: Claim-type constants
- **Target Files:** new `Infrastructure/Security/MatrixClaimTypes.cs`, `Controllers/AuthController.cs` (59-60), `Controllers/MediaController.cs` (18-19), `Services/MatrixAuthenticationStateProvider.cs` (after P2-09)
- **Exact Scope:** `public const string Homeserver = "Homeserver"; public const string AccessToken = "AccessToken";`. **The values must not change**: they are persisted inside issued authentication cookies. Don't touch `ClaimTypes.*` or the `"MatrixAuth"` auth type.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `grep -rn '"Homeserver"\|"AccessToken"' src --include=*.cs` matches only the constants file (`L["AccessToken"]` in `UserDetails` is a resource key and is out of scope).

### P3-06: Gateway brand/version sentinel constants and the admin path constant
- **Target Files:** new `Infrastructure/Gateways/GatewayConstants.cs`, `Infrastructure/Gateways/MatrixGatewayBase.cs` (28-29), `Infrastructure/Gateways/SynapseCompatibleAdminGateway.cs` (19-20, 361), `Infrastructure/Gateways/MatrixAuthGateway.cs` (136, 143, 147, 159, 164-165, 177-178, 223), `Services/MatrixSessionService.cs:200`
- **Exact Scope:** `const string UnknownVersion = "Unknown"`, `GenericBrand = "Generic"`, `SynapseBrand = "Synapse"`, `ServerVersionPath = "/_synapse/admin/v1/server_version"`. They must be `const`, because they are used as default parameter values. Leave the case-insensitive comparisons (`"tuwunel"`, `"synapse"`) as literals or add separate constants. Values stay identical.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `grep -rn '"Unknown"' src --include=*.cs` matches only the constants file.

### P3-07a / P3-07b / P3-07c: Snackbar result helper
- **Target Files:**
  - **07a:** new `Extensions/SnackbarExtensions.cs` + tests, then migrate `Components/Pages/RoomDetails.razor.cs`
  - **07b:** migrate `UserDetails.razor.cs`, `Shared/RoomGalleryGrid.razor.cs`, `RegistrationTokens.razor.cs`, `RoomAliasesDialog.razor.cs`, `BackgroundUpdates.razor.cs`
  - **07c:** migrate the remaining 15 files listed by `grep -rc "Severity != Severity.Normal" src/SynapseAdmin/Components`
- **Depends on:** P2-11 to P2-14 for the files they touch
- **Exact Scope:** Two helpers, because the code base mixes two semantics:
  - `ShowResult(this ISnackbar, OperationResult r)` shows **success and failure** when `r.Severity != Normal`.
  - `ShowFailure(this ISnackbar, OperationResult r)` shows **only** `!r.Success && r.Severity != Normal` (used in `Users`, `Rooms`, `RoomGalleryGrid:233`, `RoomDetails:201`, `FederationDestinations:68`, …).
  - Migrate each site to the helper that matches its current condition exactly. Sites that also set `errorMessage` (`LoadRoomDetails`, `LoadUserDetails`) keep that assignment.
- **Risk Level:** Medium (many sites; the wrong helper silently drops or adds a success toast)
- **Done Criteria:** SDC, plus helper unit tests for {Success, Failure, Cancelled} × {ShowResult, ShowFailure}, plus `SnackbarStub` assertions in the existing bUnit tests are unchanged.

### P3-08: Shared clipboard helper
- **Target Files:** new `Extensions/JsRuntimeExtensions.cs`, `Components/Pages/RegistrationTokens.razor.cs` (115-125), `Components/Pages/UserDetails.razor.cs` (196-204), `Components/Pages/EventForensics.razor.cs` (70-78)
- **Exact Scope:** Add `Task<bool> TryCopyToClipboardAsync(this IJSRuntime js, string text)`, which wraps `navigator.clipboard.writeText` in the **same** catch-all and returns false on any exception. Each caller keeps its own snackbar messages and severities. `UserDetails` shows `result.Message` alone on failure.
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `grep -rn "navigator.clipboard" src` matches only the helper.

### P3-09: Shared media-metadata copy
- **Target Files:** `Models/ViewModels/RoomDetailViewModel.cs` (`RoomMediaItemViewModel`), `Services/RoomService.cs` (189-194), `Components/Shared/RoomGalleryGrid.razor.cs` (222-227), `Extensions/Mapping/RoomMappingExtensions.cs`
- **Exact Scope:** Move the vm→vm copy of the 6 metadata fields in the gallery into `RoomMediaItemViewModel.CopyMetadataFrom(RoomMediaItemViewModel other)`. Move the `MediaInfo` → vm copy in `RoomService` into `RoomMappingExtensions.ApplyMetadata(this RoomMediaItemViewModel vm, MediaInfo meta)`. Preserve `SafeFromQuarantine ?? false` in the service copy (the gallery copies a bool).
- **Risk Level:** Low
- **Done Criteria:** SDC, plus `RoomMediaDeduplicationTests` and `RoomGalleryDuplicateMediaTests` pass.

### P3-10: Shared test localizer doubles (tests only)
- **Target Files:** new `tests/SynapseAdmin.Tests/TestSupport/KeyLocalizer.cs` and `FormattingKeyLocalizer.cs`. Replace the nested classes in `AuthCookieServiceTests.cs:26`, `RoomMediaDeduplicationTests.cs:28`, `UserMediaPaginationTests.cs:22` (identical `KeyLocalizer`, arguments **ignored**), `TimeZoneAndPurgeTimestampTests.cs:265` and `SecurityInfrastructureTests.cs:913` (identical formatting localizer, `string.Format(name, args)`).
- **Exact Scope:** Two shared classes that keep the semantics above. **Keep** `UserExperimentalFeaturesTests.FakeStringLocalizer`, which has dictionary semantics and is not a duplicate. Change no assertions.
- **Risk Level:** Low
- **Done Criteria:** SDC with an unchanged test count (177 plus earlier additions), and `grep -rn "class .*Localizer : IStringLocalizer" tests` shows 3 classes (2 shared + `FakeStringLocalizer`).

### P3-11 (approval-gated): Service-layer result boilerplate helper
- **Status:** **Do not start without explicit owner approval.** This may count as a "paradigm rewrite" under REFACTOR_RULES §2.
- **Target Files (one sub-task per service, executed separately):** **11a** `MediaService`, **11b** `RegistrationTokenService`, **11c** `EventReportService` + `FederationService`, **11d** `BackgroundUpdatesService`, **11e** `UserService` (split in two if > 200 lines), **11f** `RoomService` (split in two), plus a new `Services/GatewayServiceBase.cs`
- **Exact Scope:** 49 `Gateway == null → NotAuthenticated` guards and 59 `OperationCanceledException → Cancelled()` / `Exception → LogError + Failure(L[key])` ladders. A protected helper such as `Task<OperationResult<T>> RunAsync<T>(Func<IMatrixGateway, Task<OperationResult<T>>> body, string failureKey, Action<ILogger, Exception> log)` would remove them. It must preserve, per method:
  - the exact log template and arguments
  - the failure key
  - pre-`try` validation order (`UserService.GetUserDetailsAsync` remote check, the `EventForensicsService` triple guard, the `MediaService` blank-MXC guards)
  - `MediaService`'s extra `!sessionService.IsLoggedIn` condition (see H-11)
  - `null` → `Ok(empty)` vs `null` → `Failure` per method
- **Risk Level:** Medium
- **Done Criteria:** SDC, plus a per-service characterization suite (gateway null, gateway throws OCE, gateway throws, gateway returns null, success) is written before migration and is green after.

### P3-12 (optional, low value): `DialogOptions` presets
- **Target Files:** new `Components/Shared/DialogPresets.cs`, plus the 10 `new DialogOptions { … }` sites (`grep -rn "new DialogOptions" src`)
- **Exact Scope:** Factory **methods** (`DialogPresets.Small()`, `Medium()`, `SmallWithCloseButton()`, `MediumWithCloseButton()`) that return new instances. Don't share instances, because MudBlazor may mutate options. Keep the existing combinations exactly: `RoomDirectory` uses `CloseButton = true` without `CloseOnEscapeKey`.
- **Risk Level:** Low
- **Done Criteria:** SDC.

---

## 6. Human Decisions & Observed Defects (out of scope for behavior-preserving refactors)

These issues came up during the audit. Fixing them **changes behavior**, so they must not be folded into the tasks above. File each as a GitHub issue / `ai/bugfix/*` branch, or record a decision.

| ID | Category | Finding | Location | Note |
|---|---|---|---|---|
| H-01 | Config | No `.editorconfig` or analyzer configuration. Formatting and analyzer findings have no agreed baseline. | repo root | Decide before P1-09. Adding one, or adding `dotnet format --verify-no-changes` to CI, needs approval (AGENTS.md: ask before modifying workflows). |
| H-02 | Convention breach (#393, fix in #400) | User-facing timestamps are rendered in **UTC/server time** without `ITimeZoneService`: `Home.razor:74`, `Users.razor:74`, `EventReports.razor:54`, `FederationDestinations.razor:58,64`, `RegistrationTokens.razor:47`, `EventForensics.razor:124`. | Components | Violates the AGENTS.md "Timezone" rule, and the fix changes displayed values. |
| H-03 | Bug (#393, fix in #400) | `RegistrationTokenDialog.razor.cs:52` converts `DateTime` to `DateTimeOffset` implicitly, which uses the **server's** `TimeZoneInfo.Local`. `ExistingExpiryDate` comes from `FromUnixTimeMilliseconds(...).DateTime` (UTC wall time, `Kind=Unspecified`), so expiries shift when the server TZ ≠ UTC. | `RegistrationTokenDialog.razor.cs:52`, `RegistrationTokenViewModel.cs:11` | Same family as H-02. |
| H-04 | Inconsistent formatting | Two byte-size formatters with different output: `FormatExtensions.FormatSize` (1024-based, up to EB, `0.#`, current culture) vs `RoomGalleryGrid.FormatFileSize` (KB/MB only, `F1`). | `Extensions/FormatExtensions.cs`, `RoomGalleryGrid.razor.cs:310` | Consolidating them changes what users see. |
| H-05 | Swallowed cancellation (#391, fix in #398) | Bare `catch` blocks in `MatrixAuthGateway.CreateAuthenticatedGatewayAsync` (153, 210, 243) swallow `OperationCanceledException`. A cancelled first request can fall through to `GenericMatrixGateway` (no admin API), which is then **cached** (15 min sliding / 1 h absolute) for that token. | `MatrixAuthGateway.cs` | Fix: rethrow on OCE. Needs a regression test. |
| H-06 | Swallowed errors | ~~`MatrixGatewayBase.DownloadMediaAsync` returns `null` for any non-OCE exception, without logging~~ (method removed in #397). Clipboard helpers and `ThemeService.DiscoverThemesStatic` also use catch-alls without logging. | `ThemeService.cs:128`, `EventForensics.razor.cs:75`, `UserDetails.razor.cs:201`, `RegistrationTokens.razor.cs:122` | Only Debug logging is behavior-neutral (P2-15 pattern). |
| H-07 | Security hardening (#392) | `MediaController.Preview` decides whether to apply the CSP sandbox with culture-sensitive, case-sensitive `StartsWith("image/")` etc. Culture-sensitive comparison ignores some zero-width/soft-hyphen characters, so a crafted MIME string could be classified as "safe inline" while evading the exact `"image/svg+xml"` check. | `MediaController.cs:204-208` | Recommend `StringComparison.Ordinal` (or `OrdinalIgnoreCase` after lower-casing). Behavior change, so do it after P2-17 adds the tests. |
| H-08 | Inconsistent comparison | `RoomAliasesDialog.razor.cs:73,76` uses culture-sensitive `StartsWith("#")` / `EndsWith($":{server}")` (case-sensitive), while every other server-suffix check is `OrdinalIgnoreCase`. `MediaHelper.GetMediaIdFromMxc` (culture) and `ToMxcUri` (`OrdinalIgnoreCase`) disagree on the `mxc://` prefix. | listed files | Decide on the intended semantics. |
| H-09 | Inconsistent defaults | `SortDirection.None` maps to `"f"` in the Room and User services but to `"b"` in the EventReport and Federation services. | 4 services | P3-04 preserves both. Decide whether that is intended. |
| H-10 | Inconsistent state reset | `MatrixSessionService.LoginAsync` leaves the previous `Gateway` in place on failure, while `LoginWithTokenAsync` and `RestoreSessionAsync` reset it to `null`. | `MatrixSessionService.cs:64-83` | Decide whether a failed password login should clear the session. |
| H-11 | Redundant guard | `MediaService.OpenMediaAsync` / `GetMediaMetadataAsync` check `!sessionService.IsLoggedIn \|\| Gateway == null`. In production `IsLoggedIn ≡ Gateway != null`, but `DispatchProxy` stubs in tests return `false` for an unconfigured `IsLoggedIn`. | `MediaService.cs:18,39` | Simplifying it may change test-double behavior. |
| H-12 | Inconsistent null semantics | Gateway `null` results map to `Ok(empty)` for lists (`GetRoomListAsync`, `GetUserListAsync`, `GetTopMediaUsersAsync`, `GetUserMembershipsAsync`, …) but to `Failure` for others (`GetPublicRoomsAsync`, `GetLocalRoomAliasesAsync`, `PurgeRoomHistoryAsync`, …). | Services | Document the intended contract before P3-11. |
| H-13 | Boundary validation (#394, fix in #399) | `CultureController.Set` persists any `culture` string in a cookie for 1 year without checking it against the supported list (`en-US`, `de-DE`, `fr-FR`), which is duplicated in `Program.cs:212` and `LanguageSelector.razor:2-4`. `new RequestCulture(culture)` can throw `CultureNotFoundException` for malformed names, which would surface as an unhandled error. | `CultureController.cs`, `Program.cs:212`, `LanguageSelector.razor` | Validation and a shared culture list are a behavior change (invalid input → 400/redirect). |
| H-14 | Architecture convention | `NavMenu` and `Login` inject the concrete `MatrixAuthenticationStateProvider`, which has no interface (AGENTS.md: "Always extract an interface"). It is registered twice (`Program.cs:143-144`). | `NavMenu.razor.cs:11`, `Login.razor.cs`, `Program.cs` | An interface extraction is a DI and contract change. |
| H-15 | Config drift | Hard-coded timeouts, limits and lifetimes are spread across classes, and they are not configurable: gateway cache 15 min/1 h, discovery 10 min, room-name cache 30 min/2 h, purge cache 24 h, discovery `ConnectTimeout` 10 s / `Timeout` 30 s / 1 MiB buffer, login limiter 5/min, `/Auth` limiter 10/min, auth cookie 1 day, culture cookie 1 year, purge polling 5 s × 5, shadow-login 1 h, `DownloadMediaAsync` 5 MiB, `HomeserverDiscoveryClient` device name and LibMatrix `AppName` (both `"SynapseAdmin.NET"`). | multiple | Naming them as constants is in scope (P1-11 pattern). Turning them into config keys is a **feature** and needs SPEC.md. |
| H-16 | Duplicate header | `X-Content-Type-Options: nosniff` is appended globally (`Program.cs:193`) and set again per response in `MediaController` (168, 198). | `Program.cs`, `MediaController.cs` | Harmless (the controller overwrites). Removing either copy is a security-review decision. |
| H-17 | Dead-code decisions | D-01 … D-09 in §2.1. | — | — |

---

## 7. Recommended Execution Order

1. **Phase 1:** P1-01 → P1-02 → P1-03 → P1-04 → P1-05 → P1-06 → P1-07 → P1-08 → (H-01 decision) → P1-09a/b/c → P1-10 → P1-11
2. **Phase 2, infrastructure first:** P2-10 → P2-07 → P2-06 → P2-05 → P2-08a/b/c → P2-09 → P2-01 → P2-02 → P2-03 → P2-04 → P2-16 → P2-17 → P2-18
3. **Phase 2, components:** P2-12 → P2-11 → P2-13 → P2-14 → P2-15
4. **Phase 3:** P3-10 → P3-05 → P3-06 → P3-04 → P3-03 → P3-01 → P3-02 → P3-09 → P3-08 → P3-07a/b/c → (owner approval) P3-11a–f → P3-12

Per AGENTS.md workflow: each task (or a small, same-phase group of tasks) gets its own branch, e.g. `ai/feature/refactor-p1-01-unused-usings`, a Conventional Commit (`refactor: …`), local `dotnet build` + `dotnet test`, and a PR against `main`.
