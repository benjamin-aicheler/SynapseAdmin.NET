# Build Stage
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /source

# Copy solution and projects
COPY SynapseAdmin.NET.slnx .
COPY src/SynapseAdmin/SynapseAdmin.csproj src/SynapseAdmin/
COPY LibMatrix/ LibMatrix/

# Restore dependencies
RUN dotnet restore SynapseAdmin.NET.slnx -a $TARGETARCH

# Copy everything else and build
COPY . .
WORKDIR /source/src/SynapseAdmin
RUN dotnet publish -c Release -o /app -a $TARGETARCH

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Pre-create logs and keys directories and set ownership to non-root app user
RUN mkdir -p /app/logs /app/keys && chown -R app:app /app/logs /app/keys

USER app
ENTRYPOINT ["dotnet", "SynapseAdmin.dll"]
