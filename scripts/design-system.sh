#!/usr/bin/env bash
# Saves each widget preview of the design system as a standalone HTML file in light and dark mode:
# artifacts/design-system/<preview>-<mode>.html and an index.html. It builds the preview page into
# artifacts/design-system-app first and draws it in Chromium. The files hold the drawn page and its styles
# and no scripts. The local agent session publishes them as the Design System artifact.
set -euo pipefail
cd "$(dirname "$0")/.."

npm run build:design-system --prefix web
node scripts/design-system.mjs
