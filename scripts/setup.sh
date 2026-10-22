#!/usr/bin/env bash
# Installs every dependency the checks need. Safe to run repeatedly.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/browser.sh
source scripts/lib/tools.sh

dotnet restore Axis.slnx
dotnet tool restore
npm ci --prefix web
npm ci --prefix tests/e2e
if [[ "${CI:-}" == "true" ]]; then
  install_browser_ci
else
  install_browser
fi
install_tools
