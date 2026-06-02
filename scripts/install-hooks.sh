#!/usr/bin/env bash
# One-time: point git at the committed hooks so the pre-push gate runs for everyone.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
git config core.hooksPath scripts/hooks

required_files=(
  scripts/hooks/pre-push
  scripts/verify.sh
  scripts/bootstrap.sh
  scripts/check-vulnerable-packages.sh
  scripts/check-test-project-classification.sh
  scripts/test-unit.sh
)

chmod +x "${required_files[@]}"

for file in "${required_files[@]}"; do
  if [ ! -x "${file}" ]; then
    echo "install-hooks: ${file} is not executable after chmod" >&2
    exit 1
  fi
done

echo "Installed: core.hooksPath = scripts/hooks (pre-push runs scripts/verify.sh - fast local gate; CI runs the full Testcontainers suite)."
