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

- **Flexible Login:** Sign in using either standard Matrix credentials (Username/Password) or an existing Access Token.
- **Dashboard:** At-a-glance overview of your server's status and metrics, including Synapse version, room storage usage, and user media consumption. Features integrated quick-links to detailed user and room management views.
- **User Management:** Search, view, create, deactivate, and manage properties of server users, including their room memberships, uploaded media, and extended metadata like Appservice IDs and Consent status.
- **Media Management:** Directly download or preview user-uploaded media (images, video, audio) with native browser streaming support. Includes capabilities to quarantine or unquarantine media and provides clear status indicators for quarantined items.
- **Room Management:** Search and inspect server rooms and their details, including room avatars, message history, Room Type/Spaces identification, and room-specific media auditing with download/preview support.
- **Event Reports:** Review and manage reported events/messages from users.
- **Registration Tokens:** Generate and manage tokens to restrict server registration.
- **Federation Destinations:** Check federation status and manage destination queues.
- **Server Notices:** Broadcast important notices directly to users from the server.
- **Background Updates:** Monitor and manage database background updates. Pause, resume, or manually trigger maintenance jobs (e.g., room statistics recalculation or user directory regeneration).
- **Multi-Language Support:** Fully localized interface with support for English, German, and French (see [Localization Guide](./LOCALIZATION.md)).
- **Multiple Themes:** Support for various themes (Matrix, Nord, Cyberpunk, etc.) with persistent user preferences (see [Theme Guide](./THEMING.md)).

## Security

SynapseAdmin.NET implements **AES-256-GCM** encryption for session tokens stored on disk. This ensures that even if someone has access to your server's storage, they cannot read your Matrix access tokens without the passphrase.

> [!IMPORTANT]
> **Always** set a secure, unique `DP_PASSPHRASE` environment variable in production. If this value is changed, all existing sessions will be invalidated, but your data will remain secure.

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

You can run the application using our pre-built images from the [GitHub Container Registry](https://github.com/benjamin-aicheler/SynapseAdmin.NET/pkgs/container/synapseadmin.net).

#### Using Docker Compose (Recommended)
The easiest way to get started is by using the default `docker-compose.yml` file. **Important:** Change the `DP_PASSPHRASE` value in the file to a secure, random string to encrypt your session keys on disk.

```bash
docker compose up -d
```

> [!IMPORTANT]
> **Container Security & Permissions:**
> The Docker container runs under a non-root user context (`app`, UID `1654`) for safety.
> - **If using Docker Named Volumes:** Permissions are set automatically.
> - **If using local Bind Mounts** (as configured in the default `docker-compose.yml`), the host directories **must be writable** by UID `1654`. Run the following command on your host *before* starting the container:
>   ```bash
>   mkdir -p logs keys
>   sudo chown -R 1654:1654 logs keys
>   ```

This will:
- Pull the latest image from GHCR.
- Map port `8080` for the web interface.
- Persist application logs to a `./logs` directory on your host.
- Persist encryption keys to a `./keys` directory on your host (keeps you logged in across restarts).
- **Encrypt** the persisted keys using the `DP_PASSPHRASE`.

#### Using Docker CLI
Alternatively, you can run the container directly. Make sure to provide a secure passphrase, and that the host directories are writable by UID `1654` (as described in the Docker Compose section above):

```bash
docker run -d \
  -p 8080:8080 \
  -e DP_PASSPHRASE="your-secure-passphrase-here" \
  -v ./logs:/app/logs \
  -v ./keys:/app/keys \
  --name synapseadmin \
  ghcr.io/benjamin-aicheler/synapseadmin.net:latest
```

#### Building from Source
If you prefer to build the image yourself, use the provided build-specific compose file:

```bash
docker compose -f docker-compose.build.yml up --build
```

#### Migrating from Older Versions (Root to Non-Root, v1.5.x and earlier)

If you are upgrading from an older version of SynapseAdmin.NET that ran as `root`, existing files in your persistent storage will be owned by `root`. Since the container now runs as the non-root `app` user (UID `1654`), you will need to update the ownership of your existing files so the container can access them:

- **If you are using Bind Mounts:**
  Run the following command on your host from the project root:
  ```bash
  sudo chown -R 1654:1654 logs keys
  ```

- **If you are using Docker Named Volumes:**
  Run the following one-off containers to fix file ownership inside your volumes (replace `synapseadmin_keys` and `synapseadmin_logs` with your actual volume names if different):
  ```bash
  docker run --rm -v synapseadmin_keys:/keys -u root alpine chown -R 1654:1654 /keys
  docker run --rm -v synapseadmin_logs:/logs -u root alpine chown -R 1654:1654 /logs
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
