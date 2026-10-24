#!/usr/bin/env bash
# Builds the SPA into the server web root and checks its size against web/bundle-budget.json, then the
# .NET solution, which installs the sample extension package in artifacts/extensions. Then installs the
# sample modules in artifacts/modules and the sample templates in artifacts/templates.
set -euo pipefail
cd "$(dirname "$0")/.."

npm run build --prefix web
node scripts/check-bundle.mjs src/Axis.Server/wwwroot
dotnet build Axis.slnx -c Release --no-restore
bash scripts/lib/install-sample-modules.sh
test -f artifacts/modules/departments/1.0.0/module.json
bash scripts/lib/install-sample-templates.sh
test -d artifacts/templates
