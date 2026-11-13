# Builds Axis from source and runs the server with the purchase request sample. The server is
# the last stage, so it is the default target. The worker stage runs the worker host with its health
# endpoints, the agent stage runs the agent host, the publisher stage runs `axis-admin publisher`, and the purchasing stage runs the purchasing
# system stub from tests/Axis.PurchasingStub.
# The development images hold the built-in modules under modules and the sample modules under samples/modules
# in /modules, each at the id and version of its module.json. They hold the built-in applications under
# applications in /templates, each at the id and version of its template.json.
# For local use: the Development environment and the sample credentials are not for production.
#
# The targets axis-server, axis-worker, axis-agent, axis-admin, axis-controlplane and axis-package-host-dotnet, -node and
# -python are the production images. They run the
# Production environment and hold no sample, sample package, sample module, development realm or
# credential. Build one with `docker build --target axis-server --build-arg AXIS_VERSION=<version> .`.
# AXIS_VERSION is the Axis version, which the images carry as the OCI label
# org.opencontainers.image.version and which `axis-admin version` prints.

# The Axis version. It sets only the informational version of the assemblies, not their assembly version.
ARG AXIS_VERSION=0.0.0-dev

# The SPA and the sign-up page. Vite writes the SPA to ../src/Axis.Server/wwwroot, the server's web root, and the sign-up page
# to ../controlplane/src/Axis.ControlPlane/wwwroot, the control plane's.
FROM node:24-slim AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
# The build bundles the configuration schemas, and the type check also reads the platform texts, the sample process and the Studio test fixture that web tests import.
COPY src/Axis.Configuration/Schemas/ /src/src/Axis.Configuration/Schemas/
COPY src/Axis.Presentation/Texts/ /src/src/Axis.Presentation/Texts/
COPY samples/apps/purchase-requests/processes/purchase-request-approval.json /src/samples/apps/purchase-requests/processes/
COPY tests/e2e/fixtures/studio-connector-oauth2.json /src/tests/e2e/fixtures/
RUN npm run build && npm run build:sign-up

# The built-in and sample modules, installed in /src/artifacts/modules/<id>/<version> by the script that
# scripts/build.sh runs. The id and the version come from each module.json.
FROM node:24-slim AS modules
WORKDIR /src
COPY scripts/lib/install-modules.sh scripts/lib/
COPY samples/modules/ samples/modules/
COPY modules/ modules/
RUN bash scripts/lib/install-modules.sh

# The built-in applications, installed in /src/artifacts/templates/<id>/<version> by the script that
# scripts/build.sh runs. The id and the version come from each template.json.
FROM node:24-slim AS templates
WORKDIR /src
COPY scripts/lib/install-sample-templates.sh scripts/lib/
COPY applications/ applications/
RUN bash scripts/lib/install-sample-templates.sh

# The server, published with the SPA in its web root, the worker, the agent host, the admin tool and the sample extension package.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
# The server embeds reference pages for the MCP tool read_reference.
COPY docs/reference/ docs/reference/
COPY samples/packages/ samples/packages/
COPY --from=web /src/src/Axis.Server/wwwroot src/Axis.Server/wwwroot
RUN dotnet publish src/Axis.Server/Axis.Server.csproj -c Release -o /app \
    && test -f /app/wwwroot/index.html
RUN dotnet publish src/Axis.Worker/Axis.Worker.csproj -c Release -o /worker
RUN dotnet publish src/Axis.Agent/Axis.Agent.csproj -c Release -o /agent
RUN dotnet publish src/Axis.Admin/Axis.Admin.csproj -c Release -o /admin
# The sample extension package, installed in /src/artifacts/extensions/payment-references/1.0.0.
RUN dotnet build samples/packages/PaymentReferences/PaymentReferences.csproj -c Release \
    && test -f /src/artifacts/extensions/payment-references/1.0.0/axis-package.json

# The ASP.NET Core base, because the worker serves its health endpoints. It has bash but no curl.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
WORKDIR /app
COPY --from=build /worker .
COPY --from=build /src/artifacts/extensions /extensions
COPY --from=modules /src/artifacts/modules /modules
USER $APP_UID
ENTRYPOINT ["dotnet", "Axis.Worker.dll"]

# The ASP.NET Core base, because the agent host serves its health endpoints. It has bash but no curl.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS agent
WORKDIR /app
COPY --from=build /agent .
USER $APP_UID
ENTRYPOINT ["dotnet", "Axis.Agent.dll"]

# The development publisher, which runs the publish requests of Studio. It needs the sample extension package and
# module that the purchase request sample declares, so it is not the production axis-admin image, which holds neither.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS publisher
WORKDIR /app
COPY --from=build /admin .
COPY --from=build /src/artifacts/extensions /extensions
COPY --from=modules /src/artifacts/modules /modules
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

# The production hosts, published without any samples. The control plane is published with the sign-up page in its web
# root. This stage copies no samples/ folder, and it deletes
# appsettings.Development.json here, so the file is in no layer of a production image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS production-build
ARG AXIS_VERSION
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
# The server embeds reference pages for the MCP tool read_reference.
COPY docs/reference/ docs/reference/
COPY controlplane/src/ controlplane/src/
COPY --from=web /src/src/Axis.Server/wwwroot src/Axis.Server/wwwroot
COPY --from=web /src/controlplane/src/Axis.ControlPlane/wwwroot controlplane/src/Axis.ControlPlane/wwwroot
RUN dotnet publish src/Axis.Server/Axis.Server.csproj -c Release -p:InformationalVersion=$AXIS_VERSION -o /out/server \
    && test -f /out/server/wwwroot/index.html
RUN dotnet publish src/Axis.Worker/Axis.Worker.csproj -c Release -p:InformationalVersion=$AXIS_VERSION -o /out/worker
RUN dotnet publish src/Axis.Agent/Axis.Agent.csproj -c Release -p:InformationalVersion=$AXIS_VERSION -o /out/agent
RUN dotnet publish src/Axis.Admin/Axis.Admin.csproj -c Release -p:InformationalVersion=$AXIS_VERSION -o /out/admin
RUN dotnet publish controlplane/src/Axis.ControlPlane/Axis.ControlPlane.csproj -c Release -p:InformationalVersion=$AXIS_VERSION -o /out/controlplane \
    && test -f /out/controlplane/wwwroot/sign-up.html
RUN dotnet publish controlplane/src/Axis.ControlPlane.Admin/Axis.ControlPlane.Admin.csproj -c Release -p:InformationalVersion=$AXIS_VERSION -o /out/controlplane-admin
RUN dotnet publish src/Axis.PackageHost/Axis.PackageHost.csproj -c Release -p:InformationalVersion=$AXIS_VERSION -o /out/package-host
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

# Production agent host. The ASP.NET Core base serves its health endpoints. It runs as the non-root user of the
# base image and can run on a read-only root file system. The image holds no extension package or module.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS axis-agent
ARG AXIS_VERSION
LABEL org.opencontainers.image.version=$AXIS_VERSION
ENV DOTNET_ENVIRONMENT=Production
WORKDIR /app
COPY --from=production-build /out/agent .
USER $APP_UID
ENTRYPOINT ["dotnet", "Axis.Agent.dll"]

# The PostgreSQL 18 client tools from the PostgreSQL apt repository, for the production admin tool. The stage takes no
# build argument, so its layer is the same for every Axis version and scripts/smoke.sh can save it and reuse it,
# see docs/development.md#smoke-tests. The image scan builds without that saved layer.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS admin-tools
RUN apt-get update \
    && apt-get install -y --no-install-recommends postgresql-common \
    && /usr/share/postgresql-common/pgdg/apt.postgresql.org.sh -y \
    && apt-get install -y --no-install-recommends postgresql-client-18 \
    && rm -rf /var/lib/apt/lists/*

# Production admin tool, built on the admin-tools stage so a build with another version reuses the package layer,
# see docs/production-images.md#production-images-and-axisadmin.
FROM admin-tools AS axis-admin
ARG AXIS_VERSION
LABEL org.opencontainers.image.version=$AXIS_VERSION
ENV DOTNET_ENVIRONMENT=Production PATH=/usr/lib/postgresql/18/bin:$PATH
WORKDIR /app
COPY --from=production-build /out/admin .
RUN mkdir /extensions /modules
USER $APP_UID
ENTRYPOINT ["dotnet", "Axis.Admin.dll"]

# Production control plane, which a region runs on a cluster of its own. It runs as the non-root user of the base
# image and can run on a read-only root file system. Settings come from environment variables and files in
# /run/secrets. The image also holds the operator's commands in /app/admin. It has no curl and holds no extension
# package or module.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS axis-controlplane
ARG AXIS_VERSION
LABEL org.opencontainers.image.version=$AXIS_VERSION
ENV DOTNET_ENVIRONMENT=Production ASPNETCORE_ENVIRONMENT=Production
WORKDIR /app
COPY --from=production-build /out/controlplane .
COPY --from=production-build /out/controlplane-admin ./admin
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Axis.ControlPlane.dll"]

# Trivy at the version that scripts/lib/trivy.sh pins, downloaded and checked against the pinned checksum by the same
# function that scripts/scan-images.sh uses. The stage takes no build argument, so its layer is the same for every Axis version.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS trivy
WORKDIR /src
COPY scripts/lib/retry.sh scripts/lib/trivy.sh scripts/lib/
RUN bash -c 'source scripts/lib/trivy.sh && install_trivy && cp "$trivy" /trivy'

# The Node.js 24 runtime and npm of the package host image, copied from the Node.js image onto the .NET runtime base. The
# stage takes no build argument, so its layer is the same for every Axis version.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS package-host-node
COPY --from=node:24-slim /usr/local/bin/node /usr/local/bin/
COPY --from=node:24-slim /usr/local/lib/node_modules/npm /usr/local/lib/node_modules/npm
RUN ln -s ../lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
    && node --version \
    && npm --version

# The Python 3 runtime and pip of the package host image. The stage takes no build argument, so its layer is the same for
# every Axis version.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS package-host-python
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 python3-pip \
    && rm -rf /var/lib/apt/lists/* \
    && python3 -m pip --version

# Production package host for C# packages: Axis.PackageHost and Trivy, with the .NET runtime of the base image. A platform
# operator builds a package with `docker run --rm -v <source>:/source:ro -v <extensions>:/extensions <image> build /source`,
# see docs/reference/extension-packages.md#building-a-package. It runs as the non-root user of the base image. It needs a
# writable root file system, because the build writes a temporary copy to /tmp and Trivy writes its database to its cache.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS axis-package-host-dotnet
ARG AXIS_VERSION
LABEL org.opencontainers.image.version=$AXIS_VERSION
ENV DOTNET_ENVIRONMENT=Production Extensions__Path=/extensions
WORKDIR /app
COPY --from=trivy /trivy /usr/local/bin/trivy
COPY --from=production-build /out/package-host .
RUN mkdir /extensions
USER $APP_UID
ENTRYPOINT ["dotnet", "Axis.PackageHost.dll"]

# Production package host for Node.js packages: the same as axis-package-host-dotnet, with Node.js and npm, and the
# Node.js runner that the host starts for a `node` package, see
# docs/reference/extension-packages.md#the-runner-protocol.
FROM package-host-node AS axis-package-host-node
ARG AXIS_VERSION
LABEL org.opencontainers.image.version=$AXIS_VERSION
ENV DOTNET_ENVIRONMENT=Production Extensions__Path=/extensions
WORKDIR /app
COPY --from=trivy /trivy /usr/local/bin/trivy
COPY --from=production-build /out/package-host .
COPY sdk/node/ ./sdk/node/
RUN mkdir /extensions
USER $APP_UID
ENTRYPOINT ["dotnet", "Axis.PackageHost.dll"]

# Production package host for Python packages: the same as axis-package-host-dotnet, with Python 3 and pip.
FROM package-host-python AS axis-package-host-python
ARG AXIS_VERSION
LABEL org.opencontainers.image.version=$AXIS_VERSION
ENV DOTNET_ENVIRONMENT=Production Extensions__Path=/extensions
WORKDIR /app
COPY --from=trivy /trivy /usr/local/bin/trivy
COPY --from=production-build /out/package-host .
RUN mkdir /extensions
USER $APP_UID
ENTRYPOINT ["dotnet", "Axis.PackageHost.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# The runtime image has no HTTP client, and Compose uses curl for the server health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
COPY --from=build /src/artifacts/extensions /extensions
COPY --from=modules /src/artifacts/modules /modules
COPY samples/ /samples/
COPY --from=templates /src/artifacts/templates /templates
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Axis.Server.dll"]
