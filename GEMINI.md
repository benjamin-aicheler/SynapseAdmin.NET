# SynapseAdmin.NET - Project Context

## Project Overview
SynapseAdmin.NET is a .NET 10 Blazor Server Web App for administering Synapse (Matrix homeservers). The application uses `LibMatrix` (and its dependency `ArcaneLibs`) as git submodules to interact with the Matrix protocol and is designed to be containerized with Docker.

- **Framework:** .NET 10 Blazor Server (Interactive Server mode)
- **SDKs:** `LibMatrix` and `ArcaneLibs` (included as git submodules)
- **Logging:** Serilog (Console and rolling File logging to `logs/` directory)
- **Deployment:** Docker & Docker Compose
- **License:** GNU Affero General Public License v3.0 (AGPL-3.0)
- **Status:** Active Development; basic auth, dashboard, room/user management (including creation and memberships), room message history, event reports, registration tokens, federation destinations, server notices, multi-language support (EN, DE, FR), and MudBlazor UI are implemented.

## Building and Running
The project uses the standard .NET 10 CLI and Docker:

- **Build:** `dotnet build`
- **Run (Local):** `dotnet run --project src/SynapseAdmin/SynapseAdmin.csproj`
- **Run (Docker):** `docker compose up --build`
- **Logs:** Application logs are written to stdout (viewable via `docker logs`) and persisted to the `logs/` directory within the container.
- **Test:** `dotnet test` (Infrastructure in place via submodules)

## Development Conventions
- **N-Tier Architecture:** The application follows an N-Tier architecture pattern.
    - **Presentation Layer:** Blazor Components (`.razor` and `.razor.cs`). These should exclusively handle UI state, user interaction, and data display.
    - **Application Layer (Services):** Classes in `src/SynapseAdmin/Services/`. These handle business logic, Matrix protocol orchestration, and mapping data.
    - **Infrastructure Layer:** `LibMatrix` SDK and underlying storage services.
- **Authentication Bridge:** The application uses a "Cookie Bridge" pattern to synchronize authentication between the Blazor circuit and standard ASP.NET Core Controllers.
    - **SessionBridgeService:** A singleton service used to securely hand off authentication data using short-lived GUID keys.
    - **AuthController:** Issues the `matrix_auth` cookie required by standard HTTP requests.
    - **MediaController:** Uses the authentication cookie to support native browser streaming for large files.
- **Interfaces:** ALWAYS extract an Interface (in `src/SynapseAdmin/Interfaces/`) for every service to enable unit testing, mocking, and decoupling.
- **Error Handling:** Standardize all service methods to return `OperationResult` or `OperationResult<T>`. This forces the UI to handle success/failure explicitly.
- **Localization Rule:** 
    - **Services:** Responsible for generating localized, user-friendly messages for **Business Outcomes** (the result of a protocol or data operation) using `IStringLocalizer`.
    - **Code-Behind (.razor.cs):** Allowed to inject `IStringLocalizer` ONLY for **Interaction Logic** (e.g., Dialog titles/messages, complex UI-only alerts, or dynamic Page Titles). They should NEVER perform translations for service-level business logic.
    - **Razor (.razor):** Uses `IStringLocalizer` for **Static Layout** strings (labels, headers, constant button text).
- **Logging:** ALWAYS inject `ILogger<T>` into services and log exceptions/errors to provide server-side traceability for Matrix API failures.
- **ViewModels:** Always use dedicated ViewModels (`src/SynapseAdmin/Models/ViewModels/`) to pass data from services to components. Avoid passing raw SDK models to the UI.
- **Dependency Injection:** Services are registered in `Program.cs` and injected into components using the `[Inject]` attribute.
- **Coding Style:** Standard .NET 10 idiomatic C#.
- **Blazor Components:** Always use the code-behind pattern (e.g., `Page.razor` and `Page.razor.cs`) for all Blazor pages and complex components. Do not use inline `@code` blocks.
- **Submodules:** Core logic is in `LibMatrix/`. Ensure submodules are initialized: `git submodule update --init --recursive`.
- **Licensing:** All contributions must comply with the AGPLv3 license.
- **UI/UX Design:** The interface should be modern, professional, and heavily focused on functionality and data density (as an internal admin tool). We will use **MudBlazor** for Material Design components (data grids, dialogs) instead of raw Bootstrap.
    - **Themes:** Support multiple themes by implementing the `IAppTheme` interface in `src/SynapseAdmin/Infrastructure/Themes/`. Themes are automatically discovered via reflection. User preferences are persisted using `ProtectedLocalStorage`. See [Theme Guide](./THEMING.md).

## Security
- **Data Protection:** The application uses ASP.NET Core Data Protection to encrypt sensitive session data (Matrix access tokens). 
    - **Docker:** Keys are persisted to the host via a volume mount to `./keys` (\`/app/keys\` in the container).
    - **Host:** Keys are stored in the application's root directory (\`./keys\`).
- **Encryption at Rest:** Implemented a universal passphrase-based encryption (AES-256-GCM) for DataProtection keys to ensure they are encrypted on disk.

## Git & GitHub Workflow
We strictly follow the **GitHub Flow**. Direct commits to the `main` branch are prohibited.

### 1. Branching
- **Always update:** Before starting, sync your local `main`: `git checkout main && git pull origin main`.
- **Task Isolation:** Create a new branch for every task (Issue, Feature, Bugfix).
- **Issue Association:** If a task is started and the user has not provided an existing GitHub issue number, ask the user if they want to provide a number or if an issue should be created. An issue is not mandatory if the user declines.
- **Naming Convention:**
    - Features: `ai/feature/issue-<number>-<description>` (or omit `issue-<number>-` if no issue is associated)
    - Bugfixes: `ai/bugfix/issue-<number>-<description>` (or omit `issue-<number>-` if no issue is associated)
    - *Example:* `git checkout -b ai/feature/issue-42-add-login`

### 2. Commits
- **Conventional Commits:** Use the [Conventional Commits](https://www.conventionalcommits.org/) specification (e.g., `feat:`, `fix:`, `docs:`, `refactor:`).
- **Atomic & Focused:** Keep commits small and focused on a single change.
- **Issue Linking:** Link GitHub issues in the commit message (e.g., `Closes #42` or `Fixes #42`) to trigger automatic closing on merge.

### 3. Pull Requests & Verification
- **Local Build Verification:** ALWAYS run `dotnet build` locally and ensure it passes before pushing your commits to the remote branch.
- **Pushing:** Push your branch to the remote: `git push -u origin <branch-name>`.
- **Creation:** Generate a Pull Request against `main` (e.g., `gh pr create`).
- **Description:** Provide a clear title and reference the issue in the PR description (e.g., "Resolves #42").

### 4. Constraints & Cleanup
- **No Force Push:** Never `git push --force` on shared branches.
- **Conflict Resolution:** Resolve merge conflicts locally in your feature branch before updating the PR.
- **Cleanup:** Delete the local feature branch after the PR is merged.

## Key Files
- `SynapseAdmin.NET.slnx`: New .NET 10 solution file.
- `src/SynapseAdmin/`: Main web application source code.
- `LibMatrix/`: Matrix SDK submodule.
- `docker-compose.yml`: Root-level Docker Compose configuration.
- `src/SynapseAdmin/Dockerfile`: Multi-stage .NET 10 Dockerfile.
- `README.md`: Project documentation and attribution.

## Automated Code Review Workflow
You can ask the AI agent to perform a code review on a specific branch, commit, or set of files.

### Process:
1. **Template:** Use `CODEREVIEWTEMPLATE.md` as the baseline for all reviews.
2. **Analysis:** The agent will inspect the requested files against the template criteria (Security, Bugs, Performance, Architecture, Error Handling) specifically keeping Blazor Server, .NET 10, and MudBlazor conventions in mind.
3. **Report Generation:** The agent will fill out the template with its findings.
4. **Saving:** The completed report MUST be saved in the `reviews/` directory with a timestamped filename (e.g., `reviews/YYYY-MM-DD_HH-MM-SS_review.md`).
5. **Action:** The agent will summarize the Action Items in the chat and ask if you would like it to implement the High/Medium priority fixes.
