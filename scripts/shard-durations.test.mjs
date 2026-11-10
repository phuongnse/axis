import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { describe, test } from 'node:test'
import { drift, driftShare, issueFrom, junitFiles, marker, maxBody, minimumMove, title } from './shard-durations.mjs'

// About 2,700 seconds, as tests/e2e/spec-durations.json held on 2026-11-10.
const specs = Object.fromEntries([
  ['specs/service-desk-journey.spec.ts', 396],
  ...Array.from({ length: 23 }, (_, i) => [`specs/spec-${String(i).padStart(2, '0')}.spec.ts`, 100]),
])
const e2eFile = 'tests/e2e/spec-durations.json'
const overBudgetLine =
  'Over budget: specs/service-desk-journey.spec.ts takes about 1020 s, longer than the budget of 900 s.'

describe('drift', () => {
  test('leaves out moves under the minimum, and counts added and removed entries in full', () => {
    const before = { a: 100, b: 100, gone: 30 }
    assert.equal(drift(before, { a: 100 + minimumMove - 1, b: 100, gone: 30 }), 0)
    assert.equal(drift(before, { a: 100 + minimumMove, b: 100, gone: 30 }), minimumMove / 230)
    assert.equal(drift(before, { a: 100, b: 100, added: 20 }), (30 + 20) / 230)
  })

  test('is all of a file that had no seconds and now has some, and nothing when both are empty', () => {
    assert.equal(drift({}, { a: 12 }), 1)
    assert.equal(drift({}, {}), 0)
  })
})

describe('issueFrom', () => {
  test('opens no issue when the files moved by less than the share', () => {
    const after = { ...specs, 'specs/service-desk-journey.spec.ts': 396 + 80 }
    assert.ok(drift(specs, after) < driftShare)
    assert.deepEqual(issueFrom([{ path: e2eFile, before: specs, after }], [], [], 42), { action: 'none' })
  })

  test('opens one process issue with the new file when a file moves by more than the share', () => {
    const after = { ...specs, 'specs/service-desk-journey.spec.ts': 570 }
    const issue = issueFrom([{ path: e2eFile, before: specs, after }], [], [{ number: 7, body: 'Another issue.' }], 42)
    assert.equal(issue.action, 'create')
    assert.equal(issue.title, title)
    assert.deepEqual(issue.labels, ['process'])
    const lines = issue.body.split('\n')
    assert.equal(lines[0], '<!-- nexkit:workflow ci.yml 42 -->')
    assert.equal(lines.at(-1), marker)
    assert.match(issue.body, /\/actions\/runs\/42\)/)
    assert.match(issue.body, /- \[ \] `tests\/e2e\/spec-durations.json` holds the durations below\./)
    assert.match(issue.body, /- \[ \] `scripts\/test.sh` and `scripts\/lint.sh` pass\./)
    assert.ok(issue.body.includes(JSON.stringify(after, null, 2)))
  })

  test('updates the open issue on a later drift, and keeps the first line that names the run that opened it', () => {
    const after = { ...specs, 'specs/service-desk-journey.spec.ts': 600 }
    const open = { number: 3700, body: `<!-- nexkit:workflow ci.yml 41 -->\nOlder text.\n\n${marker}` }
    const issue = issueFrom([{ path: e2eFile, before: specs, after }], [], [{ number: 7, body: 'Other.' }, open], 42)
    assert.equal(issue.action, 'edit')
    assert.equal(issue.number, 3700)
    assert.equal(issue.body.split('\n')[0], '<!-- nexkit:workflow ci.yml 41 -->')
    assert.match(issue.body, /\/actions\/runs\/42\)/)
    assert.ok(issue.body.includes(JSON.stringify(after, null, 2)))
  })

  test('opens the issue for an Over budget line even when no file drifted, with a criterion to split it', () => {
    const issue = issueFrom([{ path: e2eFile, before: specs, after: specs }], [overBudgetLine], [], 42)
    assert.equal(issue.action, 'create')
    assert.ok(
      issue.body.includes(
        `- [ ] Split specs/service-desk-journey.spec.ts so no single one passes the budget: ${overBudgetLine}`,
      ),
    )
    assert.doesNotMatch(issue.body, /holds the durations below/)
    assert.doesNotMatch(issue.body, /```json/)
  })

  test('gives only the changed and removed entries when the full files would pass the body limit', () => {
    const before = Object.fromEntries(Array.from({ length: 3000 }, (_, i) => [`Axis.Integration.Tests.Class${i}Tests`, 20]))
    const after = { ...before, 'Axis.Integration.Tests.Class1Tests': 4000, 'Axis.Integration.Tests.NewTests': 15 }
    delete after['Axis.Integration.Tests.Class2Tests']
    const issue = issueFrom([{ path: 'tests/Axis.Integration.Tests/class-durations.json', before, after }], [], [], 42)
    assert.equal(issue.action, 'create')
    assert.ok(issue.body.length <= maxBody)
    assert.match(issue.body, /holds the durations below, with the other entries as they are\./)
    assert.ok(issue.body.includes('"Axis.Integration.Tests.Class1Tests": 4000'))
    assert.ok(issue.body.includes('"Axis.Integration.Tests.NewTests": 15'))
    assert.ok(issue.body.includes('Remove these entries: `Axis.Integration.Tests.Class2Tests`.'))
    assert.ok(!issue.body.includes('Class3Tests'))
  })
})

describe('junitFiles', () => {
  test('finds each split’s JUnit files in the artifacts’ sub-folders, and nothing else', () => {
    const dir = mkdtempSync(path.join(tmpdir(), 'shard-durations-'))
    try {
      const write = (file) => {
        mkdirSync(path.dirname(path.join(dir, file)), { recursive: true })
        writeFileSync(path.join(dir, file), '<testsuites/>')
      }
      const files = [
        'test-results-test-1/Axis.Server.Tests.xml',
        'test-results-test-2/web-2.xml',
        'test-results-test-3/web-3.xml',
        'test-results-e2e-1/artifacts/test-results/playwright-1.xml',
        'test-results-e2e-2/artifacts/test-results/playwright-2.xml',
        'test-results-integration-1/Axis.ControlPlane.Integration.Tests.xml',
        'test-results-integration-1/Axis.Integration.Tests.xml',
        'test-results-integration-2/Axis.Integration.Tests.xml',
        'test-results-integration-2/scenarios-ServiceDesk.xml',
        'test-results-smoke/dev-script.xml',
        'test-results-smoke/onprem.xml',
        'test-results-smoke/onprem.log',
      ]
      for (const file of files) write(file)
      const found = junitFiles(dir)
      const relative = (list) => list.map((file) => path.relative(dir, file).split(path.sep).join('/'))
      assert.deepEqual(relative(found.test), ['test-results-test-2/web-2.xml', 'test-results-test-3/web-3.xml'])
      assert.deepEqual(relative(found.e2e), [
        'test-results-e2e-1/artifacts/test-results/playwright-1.xml',
        'test-results-e2e-2/artifacts/test-results/playwright-2.xml',
      ])
      assert.deepEqual(relative(found.integration), [
        'test-results-integration-1/Axis.ControlPlane.Integration.Tests.xml',
        'test-results-integration-1/Axis.Integration.Tests.xml',
        'test-results-integration-2/Axis.Integration.Tests.xml',
      ])
      assert.deepEqual(relative(found.smoke), ['test-results-smoke/dev-script.xml', 'test-results-smoke/onprem.xml'])
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })
})
