#!/usr/bin/env bash
# Builds the SPA into the server web root, checks its size against web/bundle-budget.json and its font files
# and license texts, then builds the Keycloak login theme `axis` into artifacts/keycloak-theme and checks its
# files and fonts, then builds the .NET solution, which installs the sample extension package in
# artifacts/extensions. Then installs the sample modules in artifacts/modules and the sample templates in
# artifacts/templates.
set -euo pipefail
cd "$(dirname "$0")/.."

npm run build --prefix web
node scripts/check-bundle.mjs src/Axis.Server/wwwroot
node scripts/check-fonts.mjs src/Axis.Server/wwwroot
npm run build:keycloak-theme --prefix web
theme=artifacts/keycloak-theme/axis/login
test -f "$theme/theme.properties"
test -f "$theme/login.ftl"
test -f "$theme/login-otp.ftl"
test -f "$theme/login-reset-password.ftl"
test -f "$theme/login-update-password.ftl"
test -f "$theme/login-config-totp.ftl"
test -f "$theme/resources/assets/axis-sign-in.js"
node scripts/check-fonts.mjs "$theme/resources"
dotnet build Axis.slnx -c Release --no-restore
bash scripts/lib/install-sample-modules.sh
test -f artifacts/modules/departments/1.0.0/module.json
bash scripts/lib/install-sample-templates.sh
test -d artifacts/templates
