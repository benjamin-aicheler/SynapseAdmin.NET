# AGENTS.md

## Project Overview

- **Purpose:** Self-hosted .NET 10 Blazor Server web app for administering Matrix Synapse and compatible homeservers (such as Tuwunel >= 1.8.1) via the Matrix Client-Server and Synapse Admin APIs, bundled with `LibMatrix` and `ArcaneLibs` git submodules and licensed under AGPL-3.0.
- **Primary users / consumers:** Matrix homeserver administrators only (not general Matrix end users).
- **Status:** Beta / Active Development (`v1.15.0-beta`) — auth, dashboard, user/room/media management, room gallery, room history, event reports, event forensics, registration tokens, federation destinations, server notices, background updates, bulk media maintenance, localization (EN/DE/FR), Tuwunel >= 1.8.1 support, and MudBlazor themes are implemented (see [SPEC.md](./SPEC.md)).
- **Owner / team:** Benjamin Aicheler

## Tech Stack

| Area | Technology | Version |
|---|---|---|
| Language | C# (Nullable enabled) | .NET 10 (`net10.0`) |
| Framework & UI | ASP.NET Core Blazor Server (Interactive Server) + MudBlazor | .NET 10 / MudBlazor `9.12.0` (`MudBlazor.Translations` `3.6.0`) |
| Matrix SDKs | `LibMatrix` & `ArcaneLibs` (git submodules) | Branch `SynapseAdmin.NET` |
| Storage & State | No local DB (homeserver is source of truth); `ProtectedLocalStorage`, `IMemoryCache`, file keys (`./keys`), Serilog (`./logs`) | Serilog.AspNetCore `10.0.0` |
| Package Manager | NuGet (`.NET CLI`) + Git submodules | `.NET 10 SDK` |
| Testing | xUnit, bUnit, `Microsoft.AspNetCore.Mvc.Testing` | xUnit `2.9.3` / bUnit `2.15.2` |
| CI/CD | GitHub Actions (`.NET`, `Docker Image CI/CD` + `cosign`, `CodeQL`) & Dependabot | — |
| Hosting / Runtime | Docker (`mcr.microsoft.com/dotnet/aspnet:10.0`, non-root `app` UID `1654`) & Docker Compose behind TLS reverse proxy | — |

## Setup & Commands

```bash
# Initialize submodules and restore NuGet dependencies
git submodule update --init --recursive
dotnet restore

# Start local development server (http://localhost:5235 / https://localhost:7256)
dotnet run --project src/SynapseAdmin/SynapseAdmin.csproj

# Build and run with Docker (local build vs pre-built GHCR image)
docker compose -f docker-compose.build.yml up --build
docker compose up -d

# Build & static type/nullability check (C# compiler)
dotnet build

# Run all unit, bUnit component, and integration tests
dotnet test

# Run a single test class or test method
dotnet test --filter "FullyQualifiedName~TestClassOrMethodName"
```

**Prerequisites:** .NET 10 SDK (or Docker + Docker Compose); initialized git submodules (`LibMatrix/`). For Docker bind mounts (`./logs`, `./keys`), directories must be writable by UID `1654` (`chown -R 1654:1654 logs keys`), and `DP_PASSPHRASE` should be set in production.

## Project Structure

```text
/
├── SynapseAdmin.NET.slnx             # .NET 10 solution file
├── src/SynapseAdmin/                 # Main Blazor Server + MVC web application
│   ├── Components/                   # Presentation layer: Pages/, Layout/, Shared/, dialogs (.razor + .razor.cs)
│   ├── Controllers/                  # HTTP endpoints: AuthController, MediaController, CultureController
│   ├── Services/                     # Application layer: Matrix orchestration, session/auth, UI state
│   ├── Interfaces/                   # Contracts for every service and gateway (Gateways/)
│   ├── Infrastructure/               # Gateways/, Security/, DataProtection/, Serialization/, Themes/, Helpers/
│   ├── Models/                       # OperationResult, ViewModels/, Requests/, Responses/
│   ├── Extensions/                   # LoggingExtensions, FormatExtensions, Mapping/
│   ├── Resources/                    # SharedResources*.resx (EN, DE, FR)
│   ├── wwwroot/js/                   # auth-bridge.js, gallery-interop.js, timezone-interop.js
│   ├── Dockerfile                    # Multi-stage .NET 10 build running as non-root `app`
│   └── Program.cs                    # DI registration and ASP.NET Core middleware pipeline
├── tests/SynapseAdmin.Tests/         # xUnit unit tests, bUnit tests (Components/), WebApplicationFactory tests
├── LibMatrix/                        # Matrix SDK + ArcaneLibs git submodules (DO NOT modify for app quirks)
├── reviews/                          # Timestamped code review reports
├── docker-compose.yml                # Production GHCR compose config
├── docker-compose.build.yml          # Local source build compose config
├── .github/workflows/                # dotnet.yml, docker-image.yml, codeql.yml
└── README.md                         # User & deployment documentation
```

For full system design, data flows, and component boundaries see [ARCHITECTURE.md](./ARCHITECTURE.md).

## Code Conventions

- **N-Tier Separation:** Presentation (`.razor` / `.razor.cs`) handles UI state, interaction, and ViewModel display only. Application Services (`Services/`) handle business logic, Matrix orchestration, and ViewModel mapping. Gateways (`Infrastructure/Gateways/`) handle all wire-level HTTP, URI encoding, and JSON concerns and return structured DTOs/nullable models—never raw `HttpResponseMessage` or protocol-leaking SDK objects.
- **Interfaces & DI:** Always extract an interface in `src/SynapseAdmin/Interfaces/` for every service. Register services in `Program.cs` and inject them into components using `[Inject]`.
- **Gateway Pattern & Capability Checks:** Services talk to Matrix only via `IMatrixGateway` (from `IMatrixSessionService`); unauthenticated login/discovery uses `IMatrixAuthGateway` (`HomeserverDiscoveryClient`), which acts as a factory for authenticated `IMatrixGateway` instances. Implement missing Synapse Admin endpoints in `SynapseCompatibleAdminGateway` using `ClientHttpClient`—never modify `LibMatrix/`. In Razor components, check `IMatrixGateway.SupportsAdminApi` (never a concrete gateway type check such as `is SynapseCompatibleAdminGateway`).
- **Blazor & MudBlazor UI:** Use standard .NET 10 idiomatic C# and MudBlazor components (not raw Bootstrap) focused on data density. Always use code-behind (`.razor` + `.razor.cs`) with no inline `@code` blocks, and bind strictly to `Models/ViewModels/`—never pass raw SDK models to the UI.
- **Error Handling & Silent Cancellation:** Standardize all service methods to return `OperationResult` or `OperationResult<T>`. Catch `OperationCanceledException` and return `OperationResult.Cancelled()` (`Severity.Normal`); UI components must check `Severity.Normal` to suppress error snackbars on navigation/reload. Never pass `ex.Message` or stack traces to `OperationResult.Failure`; log technical details server-side and return localized strings.
- **Logging:** Inject `ILogger<T>` into services, log exceptions/errors for traceability, and sanitize untrusted strings with `.SanitizeForLogging()` (`LoggingExtensions`). Serilog writes to stdout (`docker logs`) and rolling files in `logs/`.
- **Localization (`EN`, `DE`, `FR`):** Services localize **Business Outcomes** via `IStringLocalizer<SharedResources>`; `.razor.cs` injects `IStringLocalizer` only for **Interaction Logic** (dialog titles/messages, UI-only alerts, dynamic page titles), never service-level business text; `.razor` uses `IStringLocalizer` for **Static Layout** strings. See [LOCALIZATION.md](./LOCALIZATION.md).
- **Detail Pages & Destructive Actions:** Blazor reuses routed detail pages (`RoomDetails`, `UserDetails`) when only the route parameter changes (e.g., `/rooms/A` → `/rooms/B`). On parameter change, clear the old model, cancel the previous `CancellationTokenSource`, and discard late responses before the first `await`. Capture the *currently displayed* entity ID before confirmation (never read the route parameter), include it in the confirmation prompt, and re-verify it is still displayed afterward; otherwise abort with `SelectionChangedActionAborted`.
- **Timezone & Themes:** Always convert/interpret user-facing timestamps via `ITimeZoneService` (initialized via JS interop in `MainLayout`), never `DateTime.Now` or `TimeZoneInfo.Local`. Add themes by implementing `IAppTheme` in `src/SynapseAdmin/Infrastructure/Themes/` (reflection-discovered, persisted in `ProtectedLocalStorage`; see [THEMING.md](./THEMING.md)).
- **bUnit Component Tests:** Tests in `tests/SynapseAdmin.Tests/Components/` extend `DetailPageTestBase` (MudBlazor + localization + recording `Stub` `DispatchProxy` doubles). Any class deriving from `BunitContext` must implement `IAsyncLifetime` (MudBlazor services are async-disposable only). Keep tests culture-independent (tests may run under `de_DE`): build expected strings via `IStringLocalizer<SharedResources>` and compare against `WebUtility.HtmlDecode(cut.Markup)` (Blazor leaves non-ASCII characters like `ä` unescaped).
- **Licensing:** All contributions must comply with AGPL-3.0.

## Workflow

We strictly follow **GitHub Flow** (direct commits to `main` are prohibited):

- **Before starting:** Sync `main` (`git checkout main && git pull origin main`). Read [SPEC.md](./SPEC.md), [ARCHITECTURE.md](./ARCHITECTURE.md), and if an active task is in progress, [PLAN.md](./PLAN.md) / [TODO.md](./TODO.md).
- **Feature Lifecycle (`SPEC → PLAN → TODO`):** [SPEC.md](./SPEC.md) is the living system specification—extend it for new features first (tag new additions `[Planned]` until implemented). For non-trivial changes, fill single-task [PLAN.md](./PLAN.md) (`Draft` → `Approved`), then populate [TODO.md](./TODO.md) and execute one task at a time (ignore `PLAN.md` / `TODO.md` unless `PLAN.md` is active in `Approved` or `In Progress`). When finished (before PR merge or when starting the next task), finalize `[Planned]` updates in [SPEC.md](./SPEC.md) (and [ARCHITECTURE.md](./ARCHITECTURE.md) if structure/flows changed) and clear/reset [PLAN.md](./PLAN.md) and [TODO.md](./TODO.md) back to their blank templates.
- **Branching:** Create a dedicated branch per task (`ai/feature/issue-<number>-<description>` or `ai/bugfix/issue-<number>-<description>`, omitting `issue-<number>-` if no issue is linked). If no GitHub issue number was provided, ask the user whether to provide one or create one (optional if declined).
- **Commits:** Use [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `refactor:`). Keep commits atomic and link issues (`Closes #42`, `Fixes #42`).
- **Pre-push verification & PRs:** Always run `dotnet build` and `dotnet test` locally and verify both pass before pushing (`git push -u origin <branch-name>`). Open a PR against `main` (`gh pr create`) referencing the issue (`Resolves #42`). Resolve conflicts locally and delete the local branch after merge.
- **Automated Code Review:** When reviewing code, use [CODEREVIEWTEMPLATE.md](./CODEREVIEWTEMPLATE.md) (checking Security, Bugs, Performance, Architecture, Error Handling for Blazor/.NET 10/MudBlazor), save the report to `reviews/YYYY-MM-DD_HH-MM-SS_review.md`, summarize Action Items in chat, and ask before implementing High/Medium fixes.

## Boundaries

### ✅ Always
- Extract an interface in `Interfaces/` for every new service and return `OperationResult` / `OperationResult<T>`.
- Propagate `CancellationToken` through services and gateways (for raw HTTP `DELETE`, instantiate `HttpRequestMessage(HttpMethod.Delete, url)` and call `ClientHttpClient.SendAsync(reqMessage, cancellationToken)` because `LibMatrix`'s `DeleteAsync`/`DeleteAsJsonAsync` lack token overloads).
- Dispose `MediaDownload` (owns the `HttpResponseMessage`).
- Run `dotnet build` and `dotnet test` before pushing commits.

### ⚠️ Ask first
- Before creating a new GitHub issue when none was provided, or before implementing High/Medium fixes after generating a code review report.
- Before adding NuGet dependencies, modifying `.github/workflows/`, changing Docker security settings, or updating the `LibMatrix/` submodule.

### 🚫 Never
- Commit or push directly to `main`, or run `git push --force` on shared branches.
- Commit secrets, credentials, access tokens, `DP_PASSPHRASE` production values, or real user PII.
- Modify the `LibMatrix/` submodule to work around Synapse Admin API gaps or quirks.
- Put bridge keys or tokens in URLs/query strings, or reintroduce a GET sign-in endpoint.
- Expose raw `ex.Message`, stack traces, or `HomeserverNotAllowedException` text to the UI.
- Read `IHttpContextAccessor` directly for client IP inside Blazor circuit handlers, or use per-circuit random rate-limit keys.
- Use `LibMatrix`'s `HomeserverResolverService`/`HomeserverProviderService` or `hsProvider.GetAuthenticatedWithToken`/`Login`/`GetRemoteHomeserver` on the auth path, or `GetMediaStreamAsync`/`GetMediaUrlAsync` for media downloads (no token/range; triggers an extra undisposed probe `GET` per request).
- Synthesize HTTP `206` locally from a non-seekable stream, or reintroduce `max-age` on protected media.
- Use `DateTime.Now`/`TimeZoneInfo.Local`, inline `@code` blocks, concrete gateway type checks in UI, or hard-coded English strings in bUnit tests.

## Security & Data Protection

- **Cookie Bridge (`SessionBridgeService` → `AuthCookieService` → `AuthController.SignIn`):** Singleton `SessionBridgeService` creates single-use, 60-second keys bound to the circuit's `AntiforgeryStateProvider` token; circuit-scoped `IAuthCookieService` (`AuthCookieService`, the only entry point for `Login` and `NavMenu`) redeems them via `wwwroot/js/auth-bridge.js` (`POST /Auth/SignIn` with JSON `{ key }` and `RequestVerificationToken` header, returning `204` + `.AspNetCore.Cookies`, followed by `forceLoad: true`). Consumption is atomic (`Interlocked`); a mismatched binding rejects the request **without burning the key**.
- **Data Protection at Rest:** Keys persist in `./keys` (`/app/keys` in Docker) and are encrypted with AES-256-GCM (PBKDF2-SHA256) using `DP_PASSPHRASE` (`AesXmlEncryptor` / `AesXmlDecryptor`).
- **Reverse Proxy & Throttling:** `ForwardedHeadersMiddleware` (`ForwardLimit = 1`) trusts loopback + RFC 1918/4193 ranges by default, or `TRUSTED_PROXIES`. `ILoginRateLimiter` throttles password logins per IP *and* cross-IP `(homeserver, account)`, and token logins **per IP only** (#368—never reintroduce a homeserver-only bucket). `POST /Auth/SignIn` is separately guarded by ASP.NET Core's `"auth"` rate limiter. Always get client IP via `IClientIdentityProvider` (`ClientIdentityCircuitHandler` populates `ClientIdentityProvider` on open/reconnect, falling back to ambient `HttpContext` outside circuits; missing IP falls back to shared `"unknown"` bucket with a warning).
- **SSRF / Cleartext Policy (`IHomeserverDestinationPolicy`):** `HomeserverDiscoveryClient` enforces HTTPS (unless `http://host:port` is in `HOMESERVER_ALLOWLIST`) and refuses internal/private addresses (`loopback`, `link-local`, RFC 1918, CGNAT, ULA) across `.well-known` hops, redirects, and resolved socket IPs (`SocketsHttpHandler.ConnectCallback`). A non-empty allowlist is a hard gate on entry hosts (misconfigured entries fail closed); `MatrixSessionService` maps `HomeserverNotAllowedException` to localized `HomeserverNotAllowed`. `MatrixAuthGateway` constructs `AuthenticatedHomeserver*` directly from validated `WellKnownUris`. *Known limitation:* post-login token-bearing calls use `LibMatrix`'s static `MatrixHttpClient` (validated base URLs, but resolved IPs are not re-checked at connect time).
- **Media Streaming (`MediaController`):** `Download`, `Preview`, and `Avatar` (cookie-authenticated native browser streaming for large files) must authorize, restore session, and open `IMatrixGateway.OpenMediaAsync` (`ResponseHeadersRead`) **before** evaluating `If-None-Match` (including `*` and `Range` requests, #370)—missing/quarantined media returns `404`, never `304`, and `304` disposes the unread stream. Forward single `bytes` ranges (with strong `If-Range` match) upstream through `IMatrixGateway.OpenMediaAsync` (#372): relay `206` via `PartialMediaResult` with `Content-Range`, pass through `416`, or serve `200` (homeserver ignored range) in full. `MatrixGatewayBase.OpenMediaAsync` calls `/_matrix/client/v1/media/download` via `SendUnhandledAsync` and falls back to `/_matrix/media/v3/download` only on `M_UNRECOGNIZED` (or bare `404`/`405`), never on `M_NOT_FOUND`. Serve protected media with `Cache-Control: private, no-cache` so deletion, quarantine, or revocation takes effect immediately.

## Known Pitfalls

- **`GetFromJsonAsync` Option Mutation:** `LibMatrix`'s `MatrixHttpClient` mutates `JsonSerializerOptions` passed to `GetFromJsonAsync`. On endpoints with inconsistent types (e.g., numeric vs string `next_token`), always use `ClientHttpClient.GetAsync` + `JsonSerializer.DeserializeAsync(..., SynapseCompatibilityJsonOptions)` (`protected static readonly` field in `SynapseCompatibleAdminGateway`, using `JsonStringConverter`).
- **Synapse Field & Timestamp Quirks:** Room Admin API uses `avatar` while User Admin/CS APIs use `avatar_url`. On `/_synapse/admin/v3/users`, `creation_ts` is in **milliseconds** (`FromUnixTimeMilliseconds` in `UserMappingExtensions.ToViewModel`); on `/_synapse/admin/v2/users/{userId}`, it is in **seconds** (`FromUnixTimeSeconds` in `UserMappingExtensions.ToDetailViewModel`). **Do not unify or alter these two conversions.**
- **Room Size Semantics:** `/_synapse/admin/v1/rooms` `order_by=size` sorts by `joined_members` (member count), not DB bytes. DB byte size (`estimated_size`) comes from `/_synapse/admin/v1/statistics/database/rooms` without room names, so `RoomService` caches resolved names in `IMemoryCache`.
- **User Media Pagination (#371):** `/_synapse/admin/v1/users/{userId}/media` `from` is an item offset (`next_token` = `from` + returned count, max `limit` default 100); page server-side with `from = page × pageSize` (`created_ts`/`b`). Totals across all uploads come from `/_synapse/admin/v1/statistics/users/media` searched by **localpart** (Synapse filters `user_id LIKE '@%' || term || '%:%'`, so full Matrix IDs never match and exact `user_id` must be picked from results); label page stats separately from totals.
- **Room Media Duplicates (#384):** `/_synapse/admin/v1/room/{roomId}/media` emits one entry per event reference (same MXC URI can appear in `local` or `remote`). `RoomService` keeps first occurrence and `RoomGalleryGrid` deduplicates defensively. Compare whole MXC URIs ordinally (never bare media ID or case-insensitively), and never key a dictionary on raw MXC URIs from this endpoint without deduplicating first.
- **Tuwunel & Generic Fallback:** Compatible servers (Tuwunel >= 1.8.1) return `AuthenticatedHomeserverGeneric`, so `MatrixAuthGateway` probes federation version + `/_synapse/admin/v1/server_version` and wraps them in `SynapseCompatibleAdminGateway`, which Synapse servers use as well. The former SDK-based `[Obsolete]` `SynapseAdminGateway` was removed in #397.

## Further Documentation

- [ARCHITECTURE.md](./ARCHITECTURE.md) – system context, N-tier design, flows, ADRs, and risks
- [SPEC.md](./SPEC.md) – living system specification (baseline requirements and acceptance criteria; extended per feature)
- [README.md](./README.md) – installation, Docker, reverse proxy, and scaling guide
- [THEMING.md](./THEMING.md) – adding reflection-discovered MudBlazor `IAppTheme` themes
- [LOCALIZATION.md](./LOCALIZATION.md) – adding `.resx` languages and UI cultures
- [CODEREVIEWTEMPLATE.md](./CODEREVIEWTEMPLATE.md) – checklist for automated code reviews
- [PLAN.md](./PLAN.md) & [TODO.md](./TODO.md) – single-task implementation plan and execution checklist (cleared/reset per task)
- [synapse-admin-api-2026-04-18.md](./synapse-admin-api-2026-04-18.md) – Synapse Admin API reference
