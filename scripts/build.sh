#!/usr/bin/env bash
# Builds the SPA into the server web root, checks its size against web/bundle-budget.json and its font files
# and license texts, then builds the Keycloak login theme `axis` into artifacts/keycloak-theme and checks its
# files and fonts, and builds the email theme `axis` beside it and checks its files, then builds the .NET solution,
# which installs the sample extension package in artifacts/extensions. Then installs the built-in and sample modules in artifacts/modules and the sample templates in
# artifacts/templates.
set -euo pipefail
cd "$(dirname "$0")/.."

npm run build --prefix web
node scripts/check-bundle.mjs src/Axis.Server/wwwroot
node scripts/check-fonts.mjs src/Axis.Server/wwwroot
npm run build:keycloak-theme --prefix web
theme=artifacts/keycloak-theme/axis/login
test -f "$theme/theme.properties"
test -f "$theme/frame.ftl"
test -f "$theme/login.ftl"
test -f "$theme/login-otp.ftl"
test -f "$theme/login-reset-password.ftl"
test -f "$theme/login-update-password.ftl"
test -f "$theme/login-config-totp.ftl"
test -f "$theme/resources/assets/axis-sign-in.js"
node scripts/check-fonts.mjs "$theme/resources"
email=artifacts/keycloak-theme/axis/email
test -f "$email/theme.properties"
test -f "$email/html/template.ftl"
test -f "$email/html/executeActions.ftl"
test -f "$email/html/password-reset.ftl"
test -f "$email/text/executeActions.ftl"
test -f "$email/text/password-reset.ftl"
test -f "$email/messages/messages_en.properties"
test -f "$email/messages/messages_vi.properties"
dotnet build Axis.slnx -c Release --no-restore
bash scripts/lib/install-modules.sh
test -f artifacts/modules/department-budgets/1.2.0/module.json
test -f artifacts/modules/organization/1.0.0/module.json
test -f artifacts/modules/approvals/1.0.0/module.json
bash scripts/lib/install-sample-templates.sh
test -d artifacts/templates
