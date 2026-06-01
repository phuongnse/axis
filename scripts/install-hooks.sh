#!/usr/bin/env bash
# One-time: point git at the committed hooks so the pre-push gate runs for everyone.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
git config core.hooksPath scripts/hooks
chmod +x scripts/hooks/* scripts/verify.sh 2>/dev/null || true
echo "Installed: core.hooksPath = scripts/hooks (pre-push runs scripts/verify.sh fast)."
