# syntax=docker/dockerfile:1.7

# ── build ──────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore against the full solution so CPM (Directory.Packages.props) resolves.
COPY Directory.Build.props Directory.Packages.props Axis.sln ./
COPY src/ ./src/

RUN dotnet restore src/Axis.Api/Axis.Api.csproj
RUN dotnet publish src/Axis.Api/Axis.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ── runtime ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl is used by the docker-compose healthcheck.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "Axis.Api.dll"]
