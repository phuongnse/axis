#!/usr/bin/env bash
# Builds the SPA into the server web root, then the .NET solution.
set -euo pipefail
cd "$(dirname "$0")/.."

npm run build --prefix web
dotnet build Axis.slnx -c Release --no-restore
