#!/usr/bin/env bash
# Integration tests against real PostgreSQL containers (requires Docker).
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet test --project tests/Axis.Integration.Tests -c Release
