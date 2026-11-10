import assert from 'node:assert/strict'
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { describe, test } from 'node:test'
import {
  cli,
  drift,
  driftShare,
  issuesFrom,
  junitFiles,
  marker,
  maxBody,
  minimumMove,
  perPage,
  refresh,
  title,
} from './shard-durations.mjs'

// About 2,700 seconds, as tests/e2e/spec-durations.json held on 2026-11-10.
const specs = Object.fromEntries([
  ['specs/service-desk-journey.spec.ts', 396],
  ...Array.from({ length: 23 }, (_, i) => [`specs/spec-${String(i).padStart(2, '0')}.spec.ts`, 100]),
])
const e2eFile = 'tests/e2e/spec-durations.json'
const overBudget = {
  kind: 'e2e',
  line: 'Over budget: specs/service-desk-journey.spec.ts takes about 1020 s, longer than the budget of 900 s.',
}

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

describe('issuesFrom', () => {
  test('opens no issue when the files moved by less than the share and nothing passes the budget', () => {
    const after = { ...specs, 'specs/service-desk-journey.spec.ts': 396 + 80 }
    assert.ok(drift(specs, after) < driftShare)
    assert.deepEqual(issuesFrom([{ path: e2eFile, before: specs, after }], [], [], 42), [])
  })

  test('opens one process issue with the new file when a file moves by more than the share', () => {
    const after = { ...specs, 'specs/service-desk-journey.spec.ts': 570 }
    const issues = issuesFrom([{ path: e2eFile, before: specs, after }], [], [{ number: 7, body: 'Another issue.' }], 42)
    assert.equal(issues.length, 1)
    const [issue] = issues
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
    const issues = issuesFrom([{ path: e2eFile, before: specs, after }], [], [{ number: 7, body: 'Other.' }, open], 42)
    assert.equal(issues.length, 1)
    const [issue] = issues
    assert.equal(issue.action, 'edit')
    assert.equal(issue.number, 3700)
    assert.equal(issue.body.split('\n')[0], '<!-- nexkit:workflow ci.yml 41 -->')
    assert.match(issue.body, /\/actions\/runs\/42\)/)
    assert.ok(issue.body.includes(JSON.stringify(after, null, 2)))
  })

  test('opens an issue of its own for an Over budget line, even when no file drifted', () => {
    const issues = issuesFrom([{ path: e2eFile, before: specs, after: specs }], [overBudget], [], 42)
    assert.equal(issues.length, 1)
    const [issue] = issues
    assert.equal(issue.action, 'create')
    assert.equal(issue.title, 'specs/service-desk-journey.spec.ts fits within the shard budget')
    assert.deepEqual(issue.labels, ['process'])
    assert.equal(issue.body.split('\n')[0], '<!-- nexkit:workflow ci.yml 42 -->')
    assert.ok(issue.body.includes(`> ${overBudget.line}`))
    assert.match(issue.body, /node scripts\/lib\/e2e-shard\.mjs --count` prints no `Over budget:` line for it/)
    assert.doesNotMatch(issue.body, /holds the durations below/)
  })

  test('keeps the drifted files and the Over budget lines in separate issues', () => {
    const after = { ...specs, 'specs/service-desk-journey.spec.ts': 1020 }
    const issues = issuesFrom([{ path: e2eFile, before: specs, after }], [overBudget], [], 42)
    assert.deepEqual(
      issues.map((issue) => issue.title),
      [title, 'specs/service-desk-journey.spec.ts fits within the shard budget'],
    )
    assert.doesNotMatch(issues[0].body, /Over budget/)
    assert.doesNotMatch(issues[1].body, /```json/)
  })

  test('opens no second issue for a name that passes the budget while its issue is open', () => {
    const [first] = issuesFrom([], [overBudget], [], 41)
    const again = { ...overBudget, line: overBudget.line.replace('1020', '1100') }
    assert.deepEqual(issuesFrom([], [again, again], [{ number: 3701, body: first.body }], 42), [])
    assert.equal(issuesFrom([], [again, again], [], 42).length, 1)
  })

  test('gives only the changed and removed entries when the full files would pass the body limit', () => {
    const before = Object.fromEntries(Array.from({ length: 3000 }, (_, i) => [`Axis.Integration.Tests.Class${i}Tests`, 20]))
    const after = { ...before, 'Axis.Integration.Tests.Class1Tests': 4000, 'Axis.Integration.Tests.NewTests': 15 }
    delete after['Axis.Integration.Tests.Class2Tests']
    const [issue] = issuesFrom([{ path: 'tests/Axis.Integration.Tests/class-durations.json', before, after }], [], [], 42)
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

// A repository with one file per split and a run's results: the slow spec moved from 100 to 2,000 seconds,
// which passes the budget, and the integration class got its first measure.
function withRun(run) {
  const dir = mkdtempSync(path.join(tmpdir(), 'shard-durations-run-'))
  const write = (file, text) => {
    mkdirSync(path.dirname(path.join(dir, file)), { recursive: true })
    writeFileSync(path.join(dir, file), text)
  }
  try {
    write('root/web/vitest-durations.json', JSON.stringify({ 'a.test.ts': 5 }))
    write('root/web/a.test.ts', '')
    write('root/tests/e2e/spec-durations.json', JSON.stringify({ 'specs/fast.spec.ts': 20, 'specs/slow.spec.ts': 100 }))
    write('root/tests/e2e/specs/fast.spec.ts', '')
    write('root/tests/e2e/specs/slow.spec.ts', '')
    write('root/tests/Axis.Integration.Tests/class-durations.json', '{}')
    write(
      'root/tests/Axis.Integration.Tests/FooTests.cs',
      'namespace Axis.Integration.Tests;\npublic class FooTests\n{\n    [Fact]\n    public void Works() { }\n}\n',
    )
    write('root/scripts/smoke-durations.json', JSON.stringify({ 'dev-script': 10, compose: 10, images: 10, onprem: 10 }))
    write(
      'results/test-results-e2e-1/artifacts/test-results/playwright-1.xml',
      '<testsuites><testsuite name="slow.spec.ts" hostname="chromium" time="2000"></testsuite>' +
        '<testsuite name="fast.spec.ts" hostname="chromium" time="20"></testsuite></testsuites>',
    )
    write(
      'results/test-results-integration-1/Axis.Integration.Tests.xml',
      '<testsuites><testsuite><testcase name="Works" classname="Axis.Integration.Tests.FooTests" time="30"/></testsuite></testsuites>',
    )
    return run(path.join(dir, 'root'), path.join(dir, 'results'), dir)
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

describe('refresh', () => {
  test('rewrites the files of the splits that have results, and gives each split’s Over budget lines', () => {
    withRun((root, results) => {
      const { files, overBudget } = refresh(results, root, () => {})
      assert.deepEqual(files, [
        {
          path: 'tests/e2e/spec-durations.json',
          before: { 'specs/fast.spec.ts': 20, 'specs/slow.spec.ts': 100 },
          after: { 'specs/fast.spec.ts': 20, 'specs/slow.spec.ts': 2000 },
        },
        { path: 'tests/Axis.Integration.Tests/class-durations.json', before: {}, after: { 'Axis.Integration.Tests.FooTests': 30 } },
      ])
      assert.deepEqual(
        overBudget.map((entry) => entry.kind),
        ['e2e'],
      )
      assert.match(overBudget[0].line, /^Over budget: specs\/slow\.spec\.ts takes about 1000 s/)
      const smoke = JSON.parse(readFileSync(path.join(root, 'scripts/smoke-durations.json'), 'utf8'))
      assert.equal(smoke['dev-script'], 10)
    })
  })
})

describe('cli', () => {
  // A gh that serves each page of open issues from open-<page>.json, and logs each other call with its standard input.
  function withGh(dir, open, run) {
    const gh = path.join(dir, 'gh')
    const log = path.join(dir, 'gh.log')
    writeFileSync(
      gh,
      `#!/bin/bash\nif [ "$1" = api ]; then re='[?&]page=([0-9]+)'; [[ "$*" =~ $re ]]; f="${dir}/open-\${BASH_REMATCH[1]}.json"; ` +
        `if [ -f "$f" ]; then cat "$f"; else echo '[]'; fi; exit 0; fi\n` +
        `{ echo "ARGS $*"; cat; echo; } >> "${log}"\necho "https://github.com/phuongnse/axis/issues/9"\n`,
    )
    chmodSync(gh, 0o755)
    const pages = Array.from({ length: Math.max(1, Math.ceil(open.length / perPage)) }, (_, i) =>
      open.slice(i * perPage, (i + 1) * perPage),
    )
    pages.forEach((page, i) => writeFileSync(path.join(dir, `open-${i + 1}.json`), JSON.stringify(page)))
    const saved = process.env.GH
    process.env.GH = gh
    try {
      return run(() => readFileSync(log, 'utf8'))
    } finally {
      if (saved === undefined) delete process.env.GH
      else process.env.GH = saved
    }
  }

  test('opens the durations issue and the Over budget issue with the process label', () => {
    withRun((root, results, dir) =>
      withGh(dir, [], (log) => {
        assert.equal(cli([results, '42'], root, () => {}), 0)
        const calls = log().split('ARGS ').slice(1)
        assert.equal(calls.length, 2)
        assert.match(calls[0], /^issue create --title The shard durations match the runs on main --body-file - --label process\n<!-- nexkit:workflow ci\.yml 42 -->/)
        assert.match(calls[0], /"specs\/slow\.spec\.ts": 2000/)
        assert.match(calls[1], /^issue create --title specs\/slow\.spec\.ts fits within the shard budget --body-file - --label process\n/)
      }),
    )
  })

  test('updates the open durations issue and opens no second Over budget issue', () => {
    withRun((root, results, dir) => {
      const opened = issuesFrom(
        [{ path: e2eFile, before: { 'specs/slow.spec.ts': 100 }, after: { 'specs/slow.spec.ts': 1900 } }],
        [{ kind: 'e2e', line: 'Over budget: specs/slow.spec.ts takes about 950 s, longer than the budget of 900 s.' }],
        [],
        41,
      )
      const open = opened.map((issue, index) => ({ number: 3700 + index, body: issue.body }))
      withGh(dir, open, (log) => {
        assert.equal(cli([results, '42'], root, () => {}), 0)
        const calls = log().split('ARGS ').slice(1)
        assert.equal(calls.length, 1)
        assert.match(calls[0], /^issue edit 3700 --body-file -\n<!-- nexkit:workflow ci\.yml 41 -->/)
        assert.match(calls[0], /\/actions\/runs\/42\)/)
      })
    })
  })

  test('finds the open durations and Over budget issues past the first page, and opens no second one', () => {
    withRun((root, results, dir) => {
      const opened = issuesFrom(
        [{ path: e2eFile, before: { 'specs/slow.spec.ts': 100 }, after: { 'specs/slow.spec.ts': 1900 } }],
        [{ kind: 'e2e', line: 'Over budget: specs/slow.spec.ts takes about 950 s, longer than the budget of 900 s.' }],
        [],
        41,
      )
      const unrelated = Array.from({ length: perPage - 1 }, (_, i) => ({ number: i + 1, body: 'Other.' }))
      const pullRequest = { number: 1000, body: `Quotes it.\n\n${marker}`, pull_request: {} }
      const marked = opened.map((issue, index) => ({ number: 3700 + index, body: issue.body }))
      withGh(dir, [...unrelated, pullRequest, ...marked], (log) => {
        assert.equal(cli([results, '42'], root, () => {}), 0)
        const calls = log().split('ARGS ').slice(1)
        assert.equal(calls.length, 1)
        assert.match(calls[0], /^issue edit 3700 --body-file -\n<!-- nexkit:workflow ci\.yml 41 -->/)
        assert.doesNotMatch(log(), /issue create/)
      })
    })
  })

  test('calls gh only to list the open issues when nothing drifted or passes the budget', () => {
    withRun((root, results, dir) => {
      rmSync(results, { recursive: true, force: true })
      mkdirSync(results)
      writeFileSync(path.join(root, 'tests/e2e/spec-durations.json'), JSON.stringify({ 'specs/fast.spec.ts': 20, 'specs/slow.spec.ts': 100 }))
      withGh(dir, [], (log) => {
        const lines = []
        assert.equal(cli([results, '42'], root, (line) => lines.push(line)), 0)
        assert.throws(() => log(), /ENOENT/)
        assert.deepEqual(lines, ['The shard durations did not drift, and nothing passes the budget.'])
      })
    })
  })
})
