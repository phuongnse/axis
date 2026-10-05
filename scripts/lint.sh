#!/usr/bin/env bash
# Formatting, analyzers and type checks for backend, frontend and E2E code.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet format Axis.slnx --verify-no-changes
npm run lint --prefix web
npx --prefix tests/e2e tsc -p tests/e2e/tsconfig.json
