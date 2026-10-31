import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { chmodSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, before, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// Runs scripts/dependabot-issues.mjs with a stub `gh` command that prints recorded alerts and open
// issues, so the tests need no network and no access to the repository's alerts.
const script = fileURLToPath(new URL('./dependabot-issues.mjs', import.meta.url))
const fixtures = fileURLToPath(new URL('./fixtures/dependabot-issues/', import.meta.url))

let stubs

before(() => {
  stubs = mkdtempSync(path.join(tmpdir(), 'dependabot-issues-'))
  const gh = path.join(stubs, 'gh')
  writeFileSync(
    gh,
    [
      '#!/usr/bin/env bash',
      'case "$1" in',
      '  api) cat "$GH_ALERTS"; exit "${GH_EXIT:-0}" ;;',
      '  issue) cat "$GH_ISSUES"; exit "${GH_EXIT:-0}" ;;',
      'esac',
      'exit 2',
    ].join('\n'),
  )
  chmodSync(gh, 0o755)
})

after(() => rmSync(stubs, { recursive: true, force: true }))

function run({ alerts, issues = `${fixtures}issues-none.json`, ...env }) {
  return spawnSync('node', [script], {
    encoding: 'utf8',
    env: { ...process.env, GH: path.join(stubs, 'gh'), GH_ALERTS: `${fixtures}${alerts}`, GH_ISSUES: issues, ...env },
  })
}

function issuesOf(result) {
  assert.equal(result.status, 0, result.stdout + result.stderr)
  return JSON.parse(result.stdout)
}

describe('scripts/dependabot-issues.mjs', () => {
  test('prints one issue for an uncovered critical alert, with the facts of the alert', () => {
    const issues = issuesOf(run({ alerts: 'alerts-critical.json' }))
    assert.equal(issues.length, 1)
    const [issue] = issues
    assert.equal(issue.title, 'Update or remove `minimist` for critical advisory GHSA-xvch-5gv4-984h')
    assert.deepEqual(issue.labels, ['security', 'dependencies'])
    assert.match(issue.body, /GHSA-xvch-5gv4-984h/)
    assert.match(issue.body, /`minimist` in `web\/package-lock\.json`/)
    assert.match(issue.body, /Severity: critical/)
    assert.match(issue.body, /Alert date: 2026-10-20/)
    assert.match(issue.body, /time frame ends 2026-10-27/)
    assert.match(issue.body, /## Acceptance criteria/)
    assert.match(issue.body, /- \[ \] web\/package-lock\.json no longer resolves minimist in < 1\.2\.6: it is updated to 1\.2\.6 or later, or removed\./)
    assert.match(issue.body, /- \[ \] scripts\/scan\.sh prints no finding of GHSA-xvch-5gv4-984h\./)
  })

  test('gives a high alert a time frame of 30 days', () => {
    const issues = issuesOf(run({ alerts: 'alerts-mixed.json' }))
    assert.equal(issues.length, 1)
    assert.match(issues[0].body, /Severity: high/)
    assert.match(issues[0].body, /time frame ends 2026-11-21/)
  })

  test('prints nothing for medium and low alerts', () => {
    const issues = issuesOf(run({ alerts: 'alerts-mixed.json' }))
    assert.doesNotMatch(JSON.stringify(issues), /postcss|left-pad|GHSA-aaaa|GHSA-dddd/)
  })

  test('prints nothing when an open issue names the advisory id, in any case', () => {
    const issues = issuesOf(run({ alerts: 'alerts-critical.json', issues: `${fixtures}issues-covering.json` }))
    assert.deepEqual(issues, [])
  })

  test('prints one issue for two alerts of one advisory and lists both manifests', () => {
    const issues = issuesOf(run({ alerts: 'alerts-one-advisory-two-manifests.json' }))
    assert.equal(issues.length, 1)
    assert.match(issues[0].body, /web\/package-lock\.json/)
    assert.match(issues[0].body, /tests\/e2e\/package-lock\.json/)
    assert.match(issues[0].body, /Alert date: 2026-10-18/, 'the earliest alert starts the time frame')
    assert.match(issues[0].body, /time frame ends 2026-11-17/)
  })

  test('prints nothing on a second run once the printed issue is open', () => {
    for (const alerts of ['alerts-critical.json', 'alerts-mixed.json', 'alerts-one-advisory-two-manifests.json']) {
      const printed = issuesOf(run({ alerts }))
      assert.ok(printed.length > 0)
      const open = path.join(stubs, 'open-issues.json')
      writeFileSync(open, JSON.stringify(printed.map((issue, index) => ({ number: index + 1, ...issue }))))
      assert.deepEqual(issuesOf(run({ alerts, issues: open })), [], alerts)
    }
  })

  test('prints an empty list when there are no alerts', () => {
    const empty = path.join(stubs, 'no-alerts.json')
    writeFileSync(empty, '[]')
    const result = spawnSync('node', [script], {
      encoding: 'utf8',
      env: { ...process.env, GH: path.join(stubs, 'gh'), GH_ALERTS: empty, GH_ISSUES: `${fixtures}issues-none.json` },
    })
    assert.deepEqual(issuesOf(result), [])
  })

  test('fails when gh fails', () => {
    const result = run({ alerts: 'alerts-critical.json', GH_EXIT: '1' })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /Dependabot issues failed/)
    assert.equal(result.stdout, '')
  })

  test('fails when gh prints output that is not JSON', () => {
    const notJson = path.join(stubs, 'not-json.txt')
    writeFileSync(notJson, 'HTTP 403')
    const result = run({ alerts: 'alerts-critical.json', issues: notJson })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /did not print JSON/)
  })
})
