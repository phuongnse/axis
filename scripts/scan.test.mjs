import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { chmodSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, before, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// Runs scripts/scan.sh with stub `dotnet` and `npm` commands that print recorded scan output for
// real advisories, so the tests need no network and do not change when an advisory changes.
const script = fileURLToPath(new URL('./scan.sh', import.meta.url))
const fixtures = fileURLToPath(new URL('./fixtures/scan/', import.meta.url))

let stubs

before(() => {
  stubs = mkdtempSync(path.join(tmpdir(), 'scan-'))
  const dotnet = path.join(stubs, 'dotnet')
  writeFileSync(
    dotnet,
    [
      '#!/usr/bin/env bash',
      'case "$1" in',
      '  restore) exit "${DOTNET_RESTORE_EXIT:-0}" ;;',
      '  list) cat "$DOTNET_FIXTURE"; exit "${DOTNET_LIST_EXIT:-0}" ;;',
      'esac',
      'exit 2',
    ].join('\n'),
  )
  const npm = path.join(stubs, 'npm')
  writeFileSync(
    npm,
    [
      '#!/usr/bin/env bash',
      'if [ "$1" != audit ]; then exit 2; fi',
      'fixture="NPM_FIXTURE_$(basename "$PWD")"',
      'cat "${!fixture:-$NPM_FIXTURE}"',
      '# npm audit exits non-zero when it finds something.',
      'exit "${NPM_EXIT:-1}"',
    ].join('\n'),
  )
  chmodSync(dotnet, 0o755)
  chmodSync(npm, 0o755)
})

after(() => rmSync(stubs, { recursive: true, force: true }))

// The accepted-list fixtures count their days back from this date.
const today = '2026-10-28'

function scan({ dotnet = 'dotnet-clean.json', npm = 'npm-clean.json', accepted, ...env } = {}) {
  return spawnSync('bash', [script], {
    encoding: 'utf8',
    env: {
      ...process.env,
      DOTNET: path.join(stubs, 'dotnet'),
      NPM: path.join(stubs, 'npm'),
      DOTNET_FIXTURE: `${fixtures}${dotnet}`,
      NPM_FIXTURE: `${fixtures}${npm}`,
      SCAN_TODAY: today,
      SCAN_ACCEPTED: accepted ? `${fixtures}${accepted}` : path.join(path.dirname(script), 'scan-accepted.json'),
      ...env,
    },
  })
}

describe('scripts/scan.sh', () => {
  test('passes when no scan finds anything', () => {
    const result = scan()
    assert.equal(result.status, 0, result.stdout + result.stderr)
    assert.match(result.stdout, /No known vulnerabilities/)
  })

  test('fails on a high NuGet advisory and prints the package and the advisory id', () => {
    const result = scan({ dotnet: 'dotnet-high.json' })
    assert.equal(result.status, 1)
    assert.match(result.stdout, /Newtonsoft\.Json 12\.0\.1/)
    assert.match(result.stdout, /GHSA-5crp-9r3c-p9vr/)
    assert.match(result.stdout, /1 high or critical/)
  })

  test('fails on a critical npm advisory and prints the package and the advisory id', () => {
    const result = scan({
      npm: 'npm-clean.json',
      NPM_FIXTURE_e2e: `${fixtures}npm-critical.json`,
    })
    assert.equal(result.status, 1)
    assert.match(result.stdout, /npm tests\/e2e\s+critical\s+minimist\s+GHSA-xvch-5gv4-984h/)
    assert.doesNotMatch(result.stdout, /mkdirp/, 'a via entry that names another package is not a finding')
  })

  test('prints moderate findings but passes', () => {
    const result = scan({ dotnet: 'dotnet-moderate.json', npm: 'npm-moderate.json' })
    assert.equal(result.status, 0, result.stdout + result.stderr)
    assert.match(result.stdout, /System\.Text\.Encodings\.Web 4\.5\.0/)
    assert.match(result.stdout, /GHSA-ghhp-997w-qr28/)
    assert.match(result.stdout, /postcss/)
    assert.match(result.stdout, /0 high or critical/)
  })

  test('fails when dotnet restore fails', () => {
    const result = scan({ DOTNET_RESTORE_EXIT: '1' })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /Scan failed, nuget/)
  })

  test('fails when dotnet list package fails', () => {
    const result = scan({ DOTNET_LIST_EXIT: '1' })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /Scan failed, nuget/)
  })

  test('fails when dotnet list package prints output that is not JSON', () => {
    const result = scan({ dotnet: 'not-json.txt' })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /did not print JSON/)
  })

  test('fails when npm audit prints output that is not JSON', () => {
    const result = scan({ npm: 'not-json.txt' })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /Scan failed, npm web/)
    assert.match(result.stderr, /Scan failed, npm tests\/e2e/)
  })

  test('fails when npm audit reports an error', () => {
    const result = scan({ npm: 'npm-error.json' })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /advisory service failed/)
  })

  test('still reports findings of the other scans when one scan fails', () => {
    const result = scan({ dotnet: 'dotnet-high.json', npm: 'not-json.txt' })
    assert.equal(result.status, 1)
    assert.match(result.stdout, /GHSA-5crp-9r3c-p9vr/)
    assert.match(result.stderr, /Scan failed, npm web/)
  })

  test('passes a high finding that was accepted 20 days ago and prints its deadline', () => {
    const result = scan({ dotnet: 'dotnet-high.json', accepted: 'accepted-high-20-days.json' })
    assert.equal(result.status, 0, result.stdout + result.stderr)
    assert.match(result.stdout, /GHSA-5crp-9r3c-p9vr.*accepted until 2026-11-07/)
    assert.match(result.stdout, /1 high or critical, 1 accepted within their time frame/)
  })

  test('fails a high finding that was accepted 31 days ago and prints the missed deadline', () => {
    const result = scan({ dotnet: 'dotnet-high.json', accepted: 'accepted-high-31-days.json' })
    assert.equal(result.status, 1)
    assert.match(result.stdout, /GHSA-5crp-9r3c-p9vr.*past its time frame since 2026-10-27/)
  })

  test('fails a critical finding that was accepted 8 days ago', () => {
    const result = scan({
      npm: 'npm-clean.json',
      NPM_FIXTURE_e2e: `${fixtures}npm-critical.json`,
      accepted: 'accepted-critical-8-days.json',
    })
    assert.equal(result.status, 1)
    assert.match(result.stdout, /GHSA-xvch-5gv4-984h.*past its time frame since 2026-10-27/)
  })

  test('passes a critical finding that was accepted 3 days ago', () => {
    const result = scan({
      npm: 'npm-clean.json',
      NPM_FIXTURE_e2e: `${fixtures}npm-critical.json`,
      accepted: 'accepted-critical-3-days.json',
    })
    assert.equal(result.status, 0, result.stdout + result.stderr)
    assert.match(result.stdout, /GHSA-xvch-5gv4-984h.*accepted until 2026-11-01/)
  })

  test('does not let an accepted entry cover another package or advisory', () => {
    const result = scan({ dotnet: 'dotnet-high.json', accepted: 'accepted-critical-3-days.json' })
    assert.equal(result.status, 1)
    assert.doesNotMatch(result.stdout, /accepted until/)
  })

  test('fails when an accepted entry has no date and names the entry', () => {
    const result = scan({ dotnet: 'dotnet-high.json', accepted: 'accepted-missing-date.json' })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /accepted list: GHSA-5crp-9r3c-p9vr Newtonsoft\.Json: found must be a date/)
  })

  test('fails when an accepted entry has an invalid date and names the entry', () => {
    const result = scan({ dotnet: 'dotnet-high.json', accepted: 'accepted-invalid-date.json' })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /accepted list: GHSA-5crp-9r3c-p9vr Newtonsoft\.Json: found must be a date/)
  })

  test('fails when the accepted list cannot be read', () => {
    const result = scan({ accepted: 'missing.json' })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /Scan failed, accepted list/)
  })
})
