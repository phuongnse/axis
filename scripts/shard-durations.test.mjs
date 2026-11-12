import assert from 'node:assert/strict'
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { describe, test } from 'node:test'
import { budgetSeconds } from './lib/shards.mjs'
import {
  cli,
  earlierResults,
  issuesFrom,
  junitFiles,
  marker,
  maxBody,
  medianDurations,
  perPage,
  refresh,
  runsMeasured,
  splits,
  title,
  weighed,
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

describe('medianDurations', () => {
  test('gives each entry the median of its measures, so one slow run moves nothing', () => {
    const measures = [{ a: 100, b: 10 }, { a: 400, b: 12 }, { a: 102, b: 11 }]
    assert.deepEqual(medianDurations(measures), { a: 102, b: 11 })
  })

  test('keeps the entries of the latest measure, and takes the median of the measures that have each', () => {
    const measures = [{ a: 100, added: 30 }, { a: 110, gone: 50 }]
    assert.deepEqual(medianDurations(measures), { a: 105, added: 30 })
  })

  test('rounds whole seconds to whole seconds and a ratio to two decimals', () => {
    assert.deepEqual(medianDurations([{ a: 10, parallelism: 2.61 }, { a: 13, parallelism: 2.7 }]), { a: 12, parallelism: 2.66 })
  })
})

describe('issuesFrom', () => {
  test('opens one process issue with the new file when a shard of its split passes the budget and the new split is shorter', () => {
    const after = { ...specs, 'specs/service-desk-journey.spec.ts': 570 }
    const file = { path: e2eFile, before: specs, after, held: 640, fresh: 320 }
    const issues = issuesFrom([file], [], [{ number: 7, body: 'Another issue.' }], 42)
    assert.equal(issues.length, 1)
    const [issue] = issues
    assert.equal(issue.action, 'create')
    assert.equal(issue.title, title)
    assert.deepEqual(issue.labels, ['process'])
    const lines = issue.body.split('\n')
    assert.equal(lines[0], '<!-- nexkit:workflow ci.yml 42 -->')
    assert.equal(lines.at(-1), marker)
    assert.match(issue.body, /^CI run \[42\]\(.*\/actions\/runs\/42\) on `main` measured the shards/m)
    assert.ok(issue.body.includes(`### \`${e2eFile}\`, a shard of about 640 s, past the budget of 600 s`))
    assert.match(issue.body, /- \[ \] `tests\/e2e\/spec-durations.json` holds the durations below\./)
    assert.match(issue.body, /- \[ \] `scripts\/test.sh` and `scripts\/lint.sh` pass\./)
    assert.ok(issue.body.includes(JSON.stringify(after, null, 2)))
  })

  test('updates the open issue on a later stale file, and keeps the first line that names the run that opened it', () => {
    const after = { ...specs, 'specs/service-desk-journey.spec.ts': 600 }
    const open = { number: 3700, body: `<!-- nexkit:workflow ci.yml 41 -->\nOlder text.\n\n${marker}` }
    const file = { path: e2eFile, before: specs, after, held: 650, fresh: 330 }
    const issues = issuesFrom([file], [], [{ number: 7, body: 'Other.' }, open], 42)
    assert.equal(issues.length, 1)
    const [issue] = issues
    assert.equal(issue.action, 'edit')
    assert.equal(issue.number, 3700)
    assert.equal(issue.body.split('\n')[0], '<!-- nexkit:workflow ci.yml 41 -->')
    assert.match(issue.body, /\/actions\/runs\/42\)/)
    assert.ok(issue.body.includes(JSON.stringify(after, null, 2)))
  })

  test('opens an issue of its own for an Over budget line, even when no file is stale', () => {
    const file = { path: e2eFile, before: specs, after: specs, held: 300, fresh: 300 }
    const issues = issuesFrom([file], [overBudget], [], 42)
    assert.equal(issues.length, 1)
    const [issue] = issues
    assert.equal(issue.action, 'create')
    assert.equal(issue.title, 'specs/service-desk-journey.spec.ts fits within the shard budget')
    assert.deepEqual(issue.labels, ['process'])
    assert.equal(issue.body.split('\n')[0], '<!-- nexkit:workflow ci.yml 42 -->')
    assert.ok(issue.body.includes(`> ${overBudget.line}`))
    assert.match(issue.body, /node scripts\/lib\/e2e-shard\.mjs --count` prints no `Over budget:` line for it/)
    assert.match(issue.body, /^- \[ \] .*\]\(https:\/\/github\.com\/phuongnse\/axis\/blob\/main\/docs\/testing\.md#rules-for-tests\)/m)
    assert.doesNotMatch(issue.body, /holds the durations below/)
  })

  test('keeps the stale files and the Over budget lines in separate issues', () => {
    const after = { ...specs, 'specs/service-desk-journey.spec.ts': 1020 }
    const file = { path: e2eFile, before: specs, after, held: 1020, fresh: 500 }
    const issues = issuesFrom([file], [overBudget], [], 42)
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
    const file = { path: 'tests/Axis.Integration.Tests/class-durations.json', before, after, held: 900, fresh: 450 }
    const [issue] = issuesFrom([file], [], [], 42)
    assert.equal(issue.action, 'create')
    assert.ok(issue.body.length <= maxBody)
    assert.match(issue.body, /holds the durations below, with the other entries as they are\./)
    assert.ok(issue.body.includes('"Axis.Integration.Tests.Class1Tests": 4000'))
    assert.ok(issue.body.includes('"Axis.Integration.Tests.NewTests": 15'))
    assert.ok(issue.body.includes('Remove these entries: `Axis.Integration.Tests.Class2Tests`.'))
    assert.ok(!issue.body.includes('Class3Tests'))
  })
})

// The keys of each split's units, as its durations file names them. Smoke's suites are fixed.
const unitKeys = {
  test: (n) => Array.from({ length: n }, (_, i) => `file-${i}.test.ts`),
  e2e: (n) => Array.from({ length: n }, (_, i) => `specs/spec-${i}.spec.ts`),
  integration: (n) =>
    Array.from({ length: n }, (_, i) => `Axis.Integration.Tests.Class${String(i).padStart(2, '0')}Tests`),
  smoke: () => ['dev-script', 'compose', 'images', 'onprem', 'ai-eval'],
}

function writeUnder(root, file, text) {
  mkdirSync(path.dirname(path.join(root, file)), { recursive: true })
  writeFileSync(path.join(root, file), text)
}

// Writes the unit `key` of a split under `root`: a Vitest file, a Playwright spec or a test class. Smoke has none.
function writeUnit(root, kind, key) {
  if (kind === 'test') writeUnder(root, `web/${key}`, '')
  if (kind === 'e2e') {
    writeUnder(root, `tests/e2e/${key}`, key.includes('journey') ? "test.describe.configure({ mode: 'serial' })\n" : '')
  }
  if (kind === 'integration') {
    const name = key.split('.').pop()
    writeUnder(
      root,
      `tests/Axis.Integration.Tests/${name}.cs`,
      `namespace Axis.Integration.Tests;\npublic class ${name}\n{\n    [Fact]\n    public void Works() { }\n}\n`,
    )
  }
}

// Runs `run` with a repository under a temporary folder, holding the units of `kind` that `keys` name.
function withUnits(kind, keys, run) {
  const dir = mkdtempSync(path.join(tmpdir(), 'shard-durations-units-'))
  try {
    const root = path.join(dir, 'root')
    for (const key of keys) writeUnit(root, kind, key)
    return run(root)
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

const fill = (count, seconds) => Array.from({ length: count }, () => seconds)

// Each split's cases: `before` and `after` give each unit's seconds. `past` names the longest shard it must get,
// and `over` has one unit, `unit`, that alone passes the budget. Keys default to the split's unitKeys.
const cases = {
  test: {
    within: { before: fill(8, 200), after: fill(8, 290) },
    past: { before: fill(8, 200), after: fill(8, 320), held: 640 },
    over: { before: [700], after: [720], unit: 'file-0.test.ts' },
  },
  e2e: {
    within: { before: fill(8, 200), after: fill(8, 290) },
    past: { before: fill(8, 200), after: fill(8, 320), held: 640 },
    over: { keys: ['specs/journey.spec.ts'], before: [700], after: [720], unit: 'specs/journey.spec.ts' },
  },
  integration: {
    within: { before: fill(6, 100), after: fill(6, 90) },
    past: { before: fill(6, 100), after: fill(6, 110), held: 660 },
    over: { before: [700], after: [720], unit: 'Axis.Integration.Tests.Class00Tests' },
  },
  smoke: {
    within: { before: fill(5, 150), after: fill(5, 140) },
    past: { before: fill(5, 150), after: fill(5, 210), held: 630 },
    over: { before: [700, 10, 10, 10, 10], after: [720, 10, 10, 10, 10], unit: 'dev-script' },
  },
}

describe('the rule for each split, weighed as refresh weighs it', () => {
  const durationsOf = (keys, seconds) => Object.fromEntries(keys.map((key, i) => [key, seconds[i]]))

  // Runs `run` with the file that `weighed` gives for a case of a split, on a repository holding the case's units.
  function fileOf(split, scenario, run) {
    const keys = scenario.keys ?? unitKeys[split.kind](scenario.before.length)
    return withUnits(split.kind, keys, (root) =>
      run(weighed(split, root, durationsOf(keys, scenario.before), durationsOf(keys, scenario.after))),
    )
  }

  for (const split of splits) {
    const { kind } = split
    const { within, past, over } = cases[kind]

    test(`${kind}: entries that moved by over 5% open no issue while the current shards fit the budget`, () => {
      fileOf(split, within, (file) => {
        const total = within.before.reduce((sum, seconds) => sum + seconds, 0)
        const moved = within.before.reduce((sum, seconds, i) => sum + Math.abs(within.after[i] - seconds), 0)
        assert.ok(moved > 0.05 * total)
        assert.ok(file.held <= budgetSeconds)
        assert.deepEqual(issuesFrom([file], [], [], 42), [])
      })
    })

    test(`${kind}: a shard past the budget files the durations, naming its seconds and the budget`, () => {
      fileOf(split, past, (file) => {
        assert.equal(file.held, past.held)
        assert.ok(file.fresh < file.held)
        const issues = issuesFrom([file], [], [], 42)
        assert.equal(issues.length, 1)
        const [issue] = issues
        assert.equal(issue.title, title)
        assert.ok(issue.body.includes(`### \`${split.file}\`, a shard of about ${past.held} s, past the budget of 600 s`))
      })
    })

    test(`${kind}: a unit over the budget gets its own issue and no durations issue when the new file splits it no better`, () => {
      fileOf(split, over, (file) => {
        assert.ok(file.held > budgetSeconds)
        assert.equal(file.fresh, file.held)
        const line = `Over budget: ${over.unit} takes about 720 s, longer than the budget of ${budgetSeconds} s.`
        const issues = issuesFrom([file], [{ kind, line }], [], 42)
        assert.deepEqual(
          issues.map((issue) => issue.title),
          [`${over.unit} fits within the shard budget`],
        )
      })
    })
  }

  test('integration: a split at its cap that cannot fit files no durations issue when the new file splits it no better', () => {
    const integration = splits.find((split) => split.kind === 'integration')
    fileOf(integration, { before: fill(12, 300), after: fill(12, 310) }, (file) => {
      assert.ok(file.held > budgetSeconds)
      assert.equal(file.fresh, file.held)
      assert.deepEqual(issuesFrom([file], [], [], 42), [])
    })
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
        'test-results-smoke/ai-eval.xml',
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
      assert.deepEqual(relative(found.smoke), [
        'test-results-smoke/ai-eval.xml',
        'test-results-smoke/dev-script.xml',
        'test-results-smoke/onprem.xml',
      ])
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })
})

// The Playwright results of a run under `folder`, in which the slow spec takes `slow` seconds.
function writeE2eRun(folder, slow) {
  const junit = path.join(folder, 'test-results-e2e-1/artifacts/test-results/playwright-1.xml')
  mkdirSync(path.dirname(junit), { recursive: true })
  writeFileSync(
    junit,
    `<testsuites><testsuite name="slow.spec.ts" hostname="chromium" time="${slow}"></testsuite>` +
      '<testsuite name="fast.spec.ts" hostname="chromium" time="20"></testsuite></testsuites>',
  )
}

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
    write('root/scripts/smoke-durations.json', JSON.stringify({ 'dev-script': 10, compose: 10, images: 10, onprem: 10, 'ai-eval': 10 }))
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
          held: 1010,
          fresh: 1000,
        },
        {
          path: 'tests/Axis.Integration.Tests/class-durations.json',
          before: {},
          after: { 'Axis.Integration.Tests.FooTests': 30 },
          held: 30,
          fresh: 30,
        },
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

  test('measures each run from the file as it was and keeps the median, so one slow run moves nothing', () => {
    withRun((root, results, dir) => {
      const earlier = ['earlier-1', 'earlier-2'].map((name, index) => {
        writeE2eRun(path.join(dir, name), 100 + 4 * index)
        return path.join(dir, name)
      })
      const { files } = refresh([results, ...earlier], root, () => {})
      const e2e = files.find((file) => file.path === e2eFile)
      assert.deepEqual(e2e.before, { 'specs/fast.spec.ts': 20, 'specs/slow.spec.ts': 100 })
      assert.deepEqual(e2e.after, { 'specs/fast.spec.ts': 20, 'specs/slow.spec.ts': 104 })
      assert.deepEqual(JSON.parse(readFileSync(path.join(root, e2eFile), 'utf8')), e2e.after)
    })
  })
})

describe('cli', () => {
  // A gh that serves each page of open issues from open-<page>.json, the successful dispatched runs from runs.txt and
  // no runs for a list of other events, and each run's artifacts from runs/<id>, failing for a run without that
  // folder. It logs each other call with its standard input.
  function withGh(dir, open, run) {
    const gh = path.join(dir, 'gh')
    const log = path.join(dir, 'gh.log')
    writeFileSync(
      gh,
      `#!/bin/bash\nif [ "$1" = api ]; then re='[?&]page=([0-9]+)'; [[ "$*" =~ $re ]]; f="${dir}/open-\${BASH_REMATCH[1]}.json"; ` +
        `if [ -f "$f" ]; then cat "$f"; else echo '[]'; fi; exit 0; fi\n` +
        `if [ "$1 $2" = 'run list' ]; then [[ " $* " == *' --event workflow_dispatch '* ]] && cat "${dir}/runs.txt" 2>/dev/null; ` +
        `exit 0; fi\n` +
        `if [ "$1 $2" = 'run download' ]; then [ -d "${dir}/runs/$3" ] || exit 1; mkdir -p "\${@: -1}"; ` +
        `cp -r "${dir}/runs/$3/." "\${@: -1}"; exit 0; fi\n` +
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

  test('downloads the results of the latest successful dispatched runs but this one, and leaves out a run without any', () => {
    withRun((root, results, dir) =>
      withGh(dir, [], () => {
        writeFileSync(path.join(dir, 'runs.txt'), '42\n41\n40\n39\n38\n37\n')
        for (const id of ['41', '39', '38', '37']) writeE2eRun(path.join(dir, 'runs', id), 100)
        const scratch = mkdtempSync(path.join(dir, 'earlier-'))
        const lines = []
        const folders = earlierResults(process.env.GH, '42', scratch, 3, (line) => lines.push(line))
        assert.deepEqual(
          folders.map((folder) => path.basename(folder)),
          ['41', '39', '38'],
        )
        assert.equal(junitFiles(folders[0]).e2e.length, 1)
        assert.deepEqual(lines, ['Run 40 has no test results to download, so it is left out.'])
      }),
    )
  })

  test('opens the durations issue and the Over budget issue with the process label', () => {
    withRun((root, results, dir) =>
      withGh(dir, [], (log) => {
        const earlier = ['41', '40', '39', '38', '37', '36']
        writeFileSync(path.join(dir, 'runs.txt'), `${earlier.join('\n')}\n`)
        for (const id of earlier) writeE2eRun(path.join(dir, 'runs', id), 2000)
        assert.equal(cli([results, '42'], root, () => {}), 0)
        const calls = log().split('ARGS ').slice(1)
        assert.equal(calls.length, 2)
        assert.match(calls[0], /^issue create --title The shard durations match the runs on main --body-file - --label process\n<!-- nexkit:workflow ci\.yml 42 -->/)
        const measured = ['42', ...earlier.slice(0, runsMeasured - 1)].map((id) => `\\[${id}\\]\\([^)]*\\/runs\\/${id}\\)`)
        assert.match(calls[0], new RegExp(`^CI runs ${measured.join(', ')} on \`main\` measured the shards`, 'm'))
        assert.match(calls[0], /"specs\/slow\.spec\.ts": 2000/)
        assert.match(calls[1], /^issue create --title specs\/slow\.spec\.ts fits within the shard budget --body-file - --label process\n/)
      }),
    )
  })

  test('updates the open durations issue and opens no second Over budget issue', () => {
    withRun((root, results, dir) => {
      const opened = issuesFrom(
        [{ path: e2eFile, before: { 'specs/slow.spec.ts': 100 }, after: { 'specs/slow.spec.ts': 1900 }, held: 1010, fresh: 1000 }],
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
        [{ path: e2eFile, before: { 'specs/slow.spec.ts': 100 }, after: { 'specs/slow.spec.ts': 1900 }, held: 1010, fresh: 1000 }],
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

  test('calls gh only to read the runs and the open issues when no file needs a new split and nothing passes the budget', () => {
    withRun((root, results, dir) => {
      rmSync(results, { recursive: true, force: true })
      mkdirSync(results)
      writeFileSync(path.join(root, 'tests/e2e/spec-durations.json'), JSON.stringify({ 'specs/fast.spec.ts': 20, 'specs/slow.spec.ts': 100 }))
      withGh(dir, [], (log) => {
        const lines = []
        assert.equal(cli([results, '42'], root, (line) => lines.push(line)), 0)
        assert.throws(() => log(), /ENOENT/)
        assert.deepEqual(lines, ['No durations file needs a new split, and nothing passes the budget.'])
      })
    })
  })
})
