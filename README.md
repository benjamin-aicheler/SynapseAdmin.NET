# SynapseAdmin.NET

<p>
  <img src="https://img.shields.io/badge/AI-Assisted-blue" alt="AI Assisted" />
  <a href="./LICENSE"><img src="https://img.shields.io/badge/License-AGPL%20v3-blue.svg" alt="License: AGPL v3" /></a>
  <a href="https://github.com/benjamin-aicheler/SynapseAdmin.NET/actions"><img src="https://img.shields.io/github/actions/workflow/status/benjamin-aicheler/SynapseAdmin.NET/dotnet.yml?branch=main" alt="GitHub build status" /></a>
  <a href="https://github.com/benjamin-aicheler/SynapseAdmin.NET/pkgs/container/synapseadmin.net"><img src="https://img.shields.io/github/v/release/benjamin-aicheler/SynapseAdmin.NET?label=Docker&logo=github&color=blue" alt="GHCR Docker Image" /></a>
  <img src="https://img.shields.io/badge/.NET_10-512BD4?style=flat&logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Blazor-512BD4?style=flat&logo=blazor&logoColor=white" alt="Blazor" />
  <img src="https://img.shields.io/badge/C%23-239120?style=flat&logo=c-sharp&logoColor=white" alt="C#" />
</p>

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
- **Multi-Language Support:** Fully localized interface with support for English, German, and French.

## Technologies

- **Architecture:** N-Tier Service Pattern (Separation of UI and Logic)
- **Framework:** .NET 10 Blazor Server (Interactive Server Mode)
- **UI Component Library:** [MudBlazor](https://mudblazor.com/)
- **Logging:** [Serilog](https://serilog.net/) (File & Console)
- **Matrix SDK:** [LibMatrix](https://github.com/Rory-LibMatrix/LibMatrix) (Git Submodule)
- **Utilities:** [ArcaneLibs](https://github.com/TheArcaneBrony/ArcaneLibs) (Git Submodule)
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

You can use the official pre-built images from the [GitHub Container Registry](https://github.com/benjamin-aicheler/SynapseAdmin.NET/pkgs/container/synapseadmin.net):

```bash
docker pull ghcr.io/benjamin-aicheler/synapseadmin.net:latest
```

Alternatively, to run the application using Docker Compose with the provided `docker-compose.yml`:

```bash
docker compose up --build
```

The application logs are stored in the `/app/logs` directory within the container. You can mount a volume to this directory to persist logs on the host:

```yaml
services:
  synapseadmin:
    image: ghcr.io/benjamin-aicheler/synapseadmin.net:latest
    volumes:
      - ./logs:/app/logs
```

### Reverse Proxy Configuration

If you are running the application behind a reverse proxy (like Nginx, Traefik, or Cloudflare) with SSL termination, the app is already configured to respect `X-Forwarded-For` and `X-Forwarded-Proto` headers.

Ensure your proxy is configured to pass these headers. For example, in **Nginx**:

```nginx
proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
proxy_set_header X-Forwarded-Proto $scheme;
```

## License

This program is free software: you can redistribute it and/or modify it under the terms of the **GNU Affero General Public License** as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This project incorporates:
- **LibMatrix**: Licensed under AGPL-3.0.
- **ArcaneLibs**: Licensed under AGPL-3.0.
- **MudBlazor**: Licensed under MIT.
- **Serilog**: Licensed under Apache-2.0.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the [LICENSE](./LICENSE) file for more details.

### Remote Network Interaction

If you modify this Program, your modified version must prominently offer all users interacting with it remotely through a computer network an opportunity to receive the Corresponding Source of your version by providing access to the Corresponding Source from a network server at no charge.

### AI Assistance Disclosure
Parts of the code and documentation in this project have been generated or refactored with the assistance of AI tools.
