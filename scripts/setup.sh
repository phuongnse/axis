#!/usr/bin/env bash
# Installs every dependency the checks need, including the wheels of the Python sample packages in
# ~/.cache/axis-tools/wheels. Safe to run repeatedly.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/tools.sh
source scripts/lib/retry.sh
source scripts/lib/python-wheels.sh

# Stops a try that runs past its limit, so retry starts the next one, as D147 agrees. Stock macOS has no
# timeout command, and there a try runs without a limit.
limit() {
  if command -v timeout >/dev/null; then
    timeout "${AXIS_STEP_TIME_LIMIT_SECONDS:-600}" "$@"
  else
    "$@"
  fi
}

retry 4 limit dotnet restore Axis.slnx
retry 4 limit dotnet tool restore
retry 4 limit npm ci --prefix web
retry 4 limit npm ci --prefix tests/e2e
retry 4 limit npx --prefix tests/e2e playwright install chromium
fetch_python_wheels
install_tools
# On CI, keeps every container image of the integration, E2E and smoke checks in the cache folder, see docs/development.md#setup.
if [[ "${CI:-}" == "true" ]]; then
  node scripts/lib/docker-images.mjs save
fi
