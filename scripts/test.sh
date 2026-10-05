#!/usr/bin/env bash
# Unit tests: no Docker or external services needed.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet test --project tests/Axis.Server.Tests -c Release
dotnet test --project tests/Axis.Configuration.Tests -c Release
npm test --prefix web
