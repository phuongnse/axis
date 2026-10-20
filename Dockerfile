# Builds Axis from source and runs the server with the purchase request sample. The server is
# the last stage, so it is the default target. The worker stage runs the worker host with its health
# endpoints, the publisher stage runs `axis-admin publisher`, and the purchasing stage runs the purchasing
# system stub from tests/Axis.PurchasingStub.
# The images hold the sample module departments in /modules.
# For local use: the Development environment and the sample credentials are not for production.
#
# The targets axis-server, axis-worker and axis-admin are the production images. They run the
# Production environment and hold no sample, sample package, sample module, development realm or
# credential. Build one with `docker build --target axis-server --build-arg AXIS_VERSION=<version> .`.
# AXIS_VERSION is the Axis version, which the images carry as the OCI label
# org.opencontainers.image.version and which `axis-admin version` prints.

# The Axis version. It sets only the informational version of the assemblies, not their assembly version.
ARG AXIS_VERSION=0.0.0-dev

# The SPA. Vite writes it to ../src/Axis.Server/wwwroot, the server's web root.
FROM node:24-slim AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
# The type check also reads the platform texts and the sample process that web tests import.
COPY src/Axis.Presentation/Texts/ /src/src/Axis.Presentation/Texts/
COPY samples/apps/purchase-requests/processes/purchase-request-approval.json /src/samples/apps/purchase-requests/processes/
RUN npm run build

# The server, published with the SPA in its web root, the worker, the admin tool and the sample extension package.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
COPY samples/packages/ samples/packages/
COPY --from=web /src/src/Axis.Server/wwwroot src/Axis.Server/wwwroot
RUN dotnet publish src/Axis.Server/Axis.Server.csproj -c Release -o /app \
    && test -f /app/wwwroot/index.html
RUN dotnet publish src/Axis.Worker/Axis.Worker.csproj -c Release -o /worker
RUN dotnet publish src/Axis.Admin/Axis.Admin.csproj -c Release -o /admin
# The sample extension package, installed in /src/artifacts/extensions/payment-references/1.0.0.
RUN dotnet build samples/packages/PaymentReferences/PaymentReferences.csproj -c Release \
    && test -f /src/artifacts/extensions/payment-references/1.0.0/axis-package.json

# The ASP.NET Core base, because the worker serves its health endpoints. It has bash but no curl.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
WORKDIR /app
COPY --from=build /worker .
COPY --from=build /src/artifacts/extensions /extensions
COPY samples/modules/departments/ /modules/departments/1.0.0/
USER $APP_UID
ENTRYPOINT ["dotnet", "Axis.Worker.dll"]

# The development publisher, which runs the publish requests of Studio. It needs the sample extension package and
# module that the purchase request sample declares, so it is not the production axis-admin image, which holds neither.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS publisher
WORKDIR /app
COPY --from=build /admin .
COPY --from=build /src/artifacts/extensions /extensions
COPY samples/modules/departments/ /modules/departments/1.0.0/
USER $APP_UID
ENTRYPOINT ["dotnet", "Axis.Admin.dll"]

# The purchasing system stub, a test fixture that stands in for the sample's purchasing system.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS purchasing-build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY tests/Axis.PurchasingStub/ tests/Axis.PurchasingStub/
RUN dotnet publish tests/Axis.PurchasingStub/Axis.PurchasingStub.csproj -c Release -o /purchasing

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS purchasing
WORKDIR /app
COPY --from=purchasing-build /purchasing .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Axis.PurchasingStub.dll"]

# The production hosts, published without any samples. This stage copies no samples/ folder, and it deletes
# appsettings.Development.json here, so the file is in no layer of a production image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS production-build
ARG AXIS_VERSION
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
COPY --from=web /src/src/Axis.Server/wwwroot src/Axis.Server/wwwroot
RUN dotnet publish src/Axis.Server/Axis.Server.csproj -c Release -p:InformationalVersion=$AXIS_VERSION -o /out/server \
    && test -f /out/server/wwwroot/index.html
RUN dotnet publish src/Axis.Worker/Axis.Worker.csproj -c Release -p:InformationalVersion=$AXIS_VERSION -o /out/worker
RUN dotnet publish src/Axis.Admin/Axis.Admin.csproj -c Release -p:InformationalVersion=$AXIS_VERSION -o /out/admin
RUN rm -f /out/*/appsettings.Development.json

# Production server. It runs as the non-root user of the base image and can run on a read-only root file
# system. Settings come from environment variables and files in /run/secrets. /extensions and /modules are
# empty mount points for extension packages and modules. The image has no curl.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS axis-server
ARG AXIS_VERSION
LABEL org.opencontainers.image.version=$AXIS_VERSION
ENV DOTNET_ENVIRONMENT=Production ASPNETCORE_ENVIRONMENT=Production
WORKDIR /app
COPY --from=production-build /out/server .
RUN mkdir /extensions /modules
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Axis.Server.dll"]

# Production worker. The ASP.NET Core base serves its health endpoints.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS axis-worker
ARG AXIS_VERSION
LABEL org.opencontainers.image.version=$AXIS_VERSION
ENV DOTNET_ENVIRONMENT=Production
WORKDIR /app
COPY --from=production-build /out/worker .
RUN mkdir /extensions /modules
USER $APP_UID
ENTRYPOINT ["dotnet", "Axis.Worker.dll"]

# Production admin tool, with the PostgreSQL 18 client tools from the PostgreSQL apt repository.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS axis-admin
ARG AXIS_VERSION
LABEL org.opencontainers.image.version=$AXIS_VERSION
RUN apt-get update \
    && apt-get install -y --no-install-recommends postgresql-common \
    && /usr/share/postgresql-common/pgdg/apt.postgresql.org.sh -y \
    && apt-get install -y --no-install-recommends postgresql-client-18 \
    && rm -rf /var/lib/apt/lists/*
ENV DOTNET_ENVIRONMENT=Production PATH=/usr/lib/postgresql/18/bin:$PATH
WORKDIR /app
COPY --from=production-build /out/admin .
RUN mkdir /extensions /modules
USER $APP_UID
ENTRYPOINT ["dotnet", "Axis.Admin.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# The runtime image has no HTTP client, and Compose uses curl for the server health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
COPY --from=build /src/artifacts/extensions /extensions
COPY samples/modules/departments/ /modules/departments/1.0.0/
COPY samples/ /samples/
# The sample templates will be copied here, so the folder exists even before there is one.
RUN mkdir /templates
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Axis.Server.dll"]
