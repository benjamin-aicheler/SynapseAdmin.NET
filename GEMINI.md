# SynapseAdmin.NET - Project Context

## Project Overview
SynapseAdmin.NET is a .NET 10 Blazor Server Web App for administering Synapse (Matrix homeservers). The application uses `LibMatrix` (and its dependency `ArcaneLibs`) as git submodules to interact with the Matrix protocol and is designed to be containerized with Docker.

- **Framework:** .NET 10 Blazor Server (Interactive Server mode)
- **SDKs:** `LibMatrix` and `ArcaneLibs` (included as git submodules)
- **Deployment:** Docker & Docker Compose
- **License:** GNU Affero General Public License v3.0 (AGPL-3.0)
- **Status:** Active Development; basic auth, dashboard, room/user management, and MudBlazor UI are implemented.

## Building and Running
The project uses the standard .NET 10 CLI and Docker:

- **Build:** `dotnet build`
- **Run (Local):** `dotnet run --project src/SynapseAdmin/SynapseAdmin.csproj`
- **Run (Docker):** `docker compose up --build`
- **Test:** `dotnet test` (Infrastructure in place via submodules)

## Development Conventions
- **Dependency Injection:** `LibMatrix` services are registered in `Program.cs` via `builder.Services.AddRoryLibMatrixServices()`.
- **Coding Style:** Standard .NET 10 idiomatic C#.
- **Submodules:** Core logic is in `LibMatrix/`. Ensure submodules are initialized: `git submodule update --init --recursive`.
- **Licensing:** All contributions must comply with the AGPLv3 license.
- **UI/UX Design:** The interface should be modern, professional, and heavily focused on functionality and data density (as an internal admin tool). We will use **MudBlazor** for Material Design components (data grids, dialogs) instead of raw Bootstrap.

## Upcoming Task List
- [ ] **Registration Tokens:** Build a view to manage and generate registration tokens.
- [x] **Event Reports:** Implement a dashboard to review and act on reported events/messages.
- [x] **Federation Destinations:** Create a view to check federation status and destination queues.
- [x] **Server Notices:** Add functionality to broadcast server notices to users.

## Key Files
- `SynapseAdmin.NET.slnx`: New .NET 10 solution file.
- `src/SynapseAdmin/`: Main web application source code.
- `LibMatrix/`: Matrix SDK submodule.
- `docker-compose.yml`: Root-level Docker Compose configuration.
- `src/SynapseAdmin/Dockerfile`: Multi-stage .NET 10 Dockerfile.
- `README.md`: Project documentation and attribution.
