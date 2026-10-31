#!/usr/bin/env bash
# Image scan: builds the production Axis images and scans them and the pinned identity provider image
# with Trivy. Fails on a known high or critical vulnerability that the accepted list does not cover.
set -euo pipefail
cd "$(dirname "$0")/.."

if [ -z "${TRIVY:-}" ]; then
  source scripts/lib/tools.sh
  install_trivy
  export TRIVY="$trivy"
fi

exec node scripts/scan-images.mjs "$@"
