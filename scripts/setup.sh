#!/usr/bin/env bash
# Installs every dependency the checks need. Safe to run repeatedly.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/tools.sh
source scripts/lib/retry.sh

retry 4 dotnet restore Axis.slnx
retry 4 dotnet tool restore
retry 4 npm ci --prefix web
retry 4 npm ci --prefix tests/e2e
if [[ "${CI:-}" == "true" ]]; then
  retry 4 npx --prefix tests/e2e playwright install-deps chromium
fi
retry 4 npx --prefix tests/e2e playwright install chromium
install_tools
