#!/usr/bin/env bash
# Installs every dependency the checks need. Safe to run repeatedly.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet restore Axis.slnx
dotnet tool restore
npm ci --prefix web
npm ci --prefix tests/e2e
if [[ "${CI:-}" == "true" ]]; then
  npx --prefix tests/e2e playwright install --with-deps chromium
else
  npx --prefix tests/e2e playwright install chromium
fi
