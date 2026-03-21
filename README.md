# SynapseAdmin.NET

A modern .NET 10 Blazor Server web application for administering Synapse (Matrix homeservers).

## Features

SynapseAdmin.NET provides a comprehensive suite of tools to manage your Matrix homeserver right from your browser. Current capabilities include:

- **Dashboard:** At-a-glance overview of your server's status and metrics.
- **User Management:** Search, view, deactivate, and manage properties of server users.
- **Room Management:** Search and inspect server rooms and their details.
- **Event Reports:** Review and manage reported events/messages from users.
- **Registration Tokens:** Generate and manage tokens to restrict server registration.
- **Federation Destinations:** Check federation status and manage destination queues.
- **Server Notices:** Broadcast important notices directly to users from the server.

## Technologies

- **Framework:** .NET 10 Blazor Server (Interactive Server Mode)
- **UI Component Library:** [MudBlazor](https://mudblazor.com/)
- **Matrix SDK:** [LibMatrix](https://github.com/benjamin-aicheler/LibMatrix) (Git Submodule)
- **Utilities:** [ArcaneLibs](https://github.com/benjamin-aicheler/ArcaneLibs) (Git Submodule)
- **Deployment:** Docker & Docker Compose

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (for local development)
- Docker and Docker Compose (for containerized deployment)
- Git (to clone the repository and submodules)

### Cloning the Repository

Because this project relies on Git submodules, make sure to clone it recursively:

```bash
git clone --recursive https://github.com/benjamin-aicheler/SynapseAdmin.NET.git
cd SynapseAdmin.NET
```

If you have already cloned the repository without the `--recursive` flag, you can initialize the submodules manually:

```bash
git submodule update --init --recursive
```

### Running Locally

To run the application directly on your host machine:

```bash
dotnet run --project src/SynapseAdmin/SynapseAdmin.csproj
```

### Running with Docker

To run the application using Docker Compose:

```bash
docker compose up --build
```

## License

This program is free software: you can redistribute it and/or modify it under the terms of the **GNU Affero General Public License** as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This project incorporates:
- **LibMatrix**: Licensed under AGPL-3.0.
- **ArcaneLibs**: Licensed under AGPL-3.0.
- **MudBlazor**: Licensed under MIT.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the [LICENSE](./LICENSE) file for more details.

### Remote Network Interaction

If you modify this Program, your modified version must prominently offer all users interacting with it remotely through a computer network an opportunity to receive the Corresponding Source of your version by providing access to the Corresponding Source from a network server at no charge.
