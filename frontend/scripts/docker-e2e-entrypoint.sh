#!/bin/sh
set -eu

mkdir -p /home/pwuser/.pki/nssdb /tmp/axis-e2e/test-results /tmp/axis-e2e/playwright-report

if [ ! -f /home/pwuser/.pki/nssdb/cert9.db ]; then
  certutil -d sql:/home/pwuser/.pki/nssdb -N --empty-password
fi

certutil -d sql:/home/pwuser/.pki/nssdb -D -n axis-local-root >/dev/null 2>&1 || true
certutil -d sql:/home/pwuser/.pki/nssdb -A -t 'C,,' -n axis-local-root -i /https/rootCA.pem

node ./scripts/wait-for-e2e-targets.mjs

if [ "$#" -gt 0 ]; then
  exec npm run test:e2e -- "$@"
fi

exec npm run test:e2e
