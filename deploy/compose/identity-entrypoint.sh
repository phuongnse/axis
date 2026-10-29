#!/usr/bin/env bash
# Starts Keycloak with its database settings and its bootstrap admin client secret read from secret files.
# Keycloak has no _FILE variables, so each file /run/secrets/KC_* becomes the variable of the same name.
set -euo pipefail
for file in /run/secrets/KC_*; do export "$(basename "$file")=$(<"$file")"; done
export KC_BOOTSTRAP_ADMIN_CLIENT_SECRET="$(</run/secrets/IdentityProvider__AdminClientSecret)"
exec /opt/keycloak/bin/kc.sh "$@"
