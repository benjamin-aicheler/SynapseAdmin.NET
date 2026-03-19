# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy solution and projects
COPY SynapseAdmin.NET.slnx .
COPY src/SynapseAdmin/SynapseAdmin.csproj src/SynapseAdmin/
COPY LibMatrix/ LibMatrix/

# Restore dependencies
RUN dotnet restore SynapseAdmin.NET.slnx

# Copy everything else and build
COPY . .
WORKDIR /source/src/SynapseAdmin
RUN dotnet publish -c Release -o /app

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "SynapseAdmin.dll"]
