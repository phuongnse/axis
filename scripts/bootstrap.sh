#!/usr/bin/env bash
# One-time local setup for contributors. CI remains the merge gate; this only
# installs fast local feedback and checks required commands are on PATH.
set -euo pipefail

require_command() {
  local name="$1"
  if ! command -v "${name}" >/dev/null 2>&1; then
    echo "bootstrap: missing '${name}' in PATH" >&2
    exit 1
  fi
}

require_command git

cd "$(git rev-parse --show-toplevel)"

require_command dotnet
require_command node
require_command npm

./scripts/install-hooks.sh

echo "bootstrap: OK"
