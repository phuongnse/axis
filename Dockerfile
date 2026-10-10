# Builds Axis from source and runs the server with the purchase request sample. The server is
# the last stage, so it is the default target. The worker stage runs the worker host.
# For local use: the Development environment and the sample credentials are not for production.

# The SPA. Vite writes it to ../src/Axis.Server/wwwroot, the server's web root.
FROM node:24-slim AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
# The type check also reads the platform texts that web tests import.
COPY src/Axis.Presentation/Texts/ /src/src/Axis.Presentation/Texts/
RUN npm run build

# The server, published with the SPA in its web root, and the worker.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
COPY --from=web /src/src/Axis.Server/wwwroot src/Axis.Server/wwwroot
RUN dotnet publish src/Axis.Server/Axis.Server.csproj -c Release -o /app \
    && test -f /app/wwwroot/index.html
RUN dotnet publish src/Axis.Worker/Axis.Worker.csproj -c Release -o /worker

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS worker
WORKDIR /app
COPY --from=build /worker .
USER $APP_UID
ENTRYPOINT ["dotnet", "Axis.Worker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# The runtime image has no HTTP client, and Compose uses curl for the server health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
COPY samples/ /samples/
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Axis.Server.dll"]
