#!/usr/bin/env bash
# Installs every dependency the checks need. Safe to run repeatedly.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/tools.sh
source scripts/lib/retry.sh

retry 3 dotnet restore Axis.slnx
retry 3 dotnet tool restore
retry 3 npm ci --prefix web
retry 3 npm ci --prefix tests/e2e
if [[ "${CI:-}" == "true" ]]; then
  retry 3 npx --prefix tests/e2e playwright install --with-deps chromium
else
  retry 3 npx --prefix tests/e2e playwright install chromium
fi
install_tools
