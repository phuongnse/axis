#!/usr/bin/env bash
# Builds the design-system bundle into artifacts/design-system/axis-design-system.js, one classic script
# that sets window.Axis with a mount function for each widget preview. scripts/design-system-header.mjs
# writes its first line, /* @ds-bundle: {...} */, with the card width and the order of the cards. Then
# scripts/check-design-system.mjs checks the bundle: that line, one script, no import, no literal </script
# or <!--, at most 6 MB. It needs no Chromium and saves no HTML files. The local agent session publishes the
# bundle as the Design System artifact. web/design-system.html is its harness.
set -euo pipefail
cd "$(dirname "$0")/.."

npm run build:design-system --prefix web
node scripts/design-system-header.mjs artifacts/design-system/axis-design-system.js
node scripts/check-design-system.mjs artifacts/design-system
