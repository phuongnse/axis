#!/bin/sh
set -eu

mkdir -p /tmp/axis-e2e/test-results /tmp/axis-e2e/playwright-report
sh ./scripts/import-browser-ca.sh /home/pwuser/.pki/nssdb /https/rootCA.pem

node ./scripts/wait-for-e2e-targets.mjs

if [ "$#" -gt 0 ]; then
  exec npm run test:e2e -- "$@"
fi

exec npm run test:e2e
