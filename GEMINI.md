# SynapseAdmin.NET - Project Context

## Project Overview
SynapseAdmin.NET is a .NET 10 Blazor Server Web App for administering Synapse (Matrix homeservers). The application uses `LibMatrix` (and its dependency `ArcaneLibs`) as git submodules to interact with the Matrix protocol and is designed to be containerized with Docker.

- **Framework:** .NET 10 Blazor Server (Interactive Server mode)
- **SDKs:** `LibMatrix` and `ArcaneLibs` (included as git submodules)
- **Deployment:** Docker & Docker Compose
- **License:** GNU Affero General Public License v3.0 (AGPL-3.0)
- **Status:** Active Development; basic auth, dashboard, room/user management, event reports, registration tokens, federation destinations, server notices, and MudBlazor UI are implemented.

## Building and Running
The project uses the standard .NET 10 CLI and Docker:

- **Build:** `dotnet build`
- **Run (Local):** `dotnet run --project src/SynapseAdmin/SynapseAdmin.csproj`
- **Run (Docker):** `docker compose up --build`
- **Test:** `dotnet test` (Infrastructure in place via submodules)

## Development Conventions
- **Dependency Injection:** `LibMatrix` services are registered in `Program.cs` via `builder.Services.AddRoryLibMatrixServices()`.
- **Coding Style:** Standard .NET 10 idiomatic C#.
- **Blazor Components:** Always use the code-behind pattern (e.g., `Page.razor` and `Page.razor.cs`) for all Blazor pages and complex components. Do not use inline `@code` blocks.
- **Submodules:** Core logic is in `LibMatrix/`. Ensure submodules are initialized: `git submodule update --init --recursive`.
- **Licensing:** All contributions must comply with the AGPLv3 license.
- **UI/UX Design:** The interface should be modern, professional, and heavily focused on functionality and data density (as an internal admin tool). We will use **MudBlazor** for Material Design components (data grids, dialogs) instead of raw Bootstrap.

## Git & GitHub Workflow
We strictly follow the **GitHub Flow**. Direct commits to the `main` branch are prohibited.

### 1. Branching
- **Always update:** Before starting, sync your local `main`: `git checkout main && git pull origin main`.
- **Task Isolation:** Create a new branch for every task (Issue, Feature, Bugfix).
- **Naming Convention:**
    - Features: `ai/feature/issue-<number>-<description>`
    - Bugfixes: `ai/bugfix/issue-<number>-<description>`
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
