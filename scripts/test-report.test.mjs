import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { copyFileSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, before, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { buildReport, formatAnnotations, formatSummary, parseJunit } from './test-report.mjs'

const script = fileURLToPath(new URL('./test-report.mjs', import.meta.url))
const fixtures = fileURLToPath(new URL('./fixtures/test-report/', import.meta.url))
const root = path.join(fixtures, 'tree')
const suites = ['passing:tests/Sample.Tests', 'failing:web', 'absent:web']

let resultsDir

before(() => {
  // .NET stack traces hold absolute paths, so the failing fixture names the fixture tree.
  resultsDir = mkdtempSync(path.join(tmpdir(), 'test-report-'))
  copyFileSync(path.join(fixtures, 'results/passing.xml'), path.join(resultsDir, 'passing.xml'))
  const failing = readFileSync(path.join(fixtures, 'results/failing.xml'), 'utf8')
  writeFileSync(path.join(resultsDir, 'failing.xml'), failing.replaceAll('__ROOT__', root))
})

after(() => rmSync(resultsDir, { recursive: true, force: true }))

function runCli(suiteArgs, env = {}) {
  const { GITHUB_ACTIONS, GITHUB_STEP_SUMMARY, ...inherited } = process.env
  return spawnSync(process.execPath, [script, '--results-dir', resultsDir, '--root', root, ...suiteArgs], {
    encoding: 'utf8',
    env: { ...inherited, ...env },
  })
}

describe('parseJunit', () => {
  test('reads test cases, statuses, messages and entities', () => {
    const { tests, time } = parseJunit(readFileSync(path.join(fixtures, 'results/failing.xml'), 'utf8'))

    assert.equal(time, 2.4)
    assert.deepEqual(
      tests.map((t) => [t.name, t.status]),
      [
        ['Sample > adds numbers', 'passed'],
        ['Sample > subtracts numbers', 'failed'],
        ['Sample > divides numbers', 'skipped'],
        ['Sample.Tests.SampleTests.Compares_text', 'failed'],
        ['Sample.Tests.SampleTests.Throws', 'failed'],
      ],
    )
    assert.equal(tests[3].message, 'Assert.Equal() Failure: Strings differ\nExpected: "a<b&c"\nActual:   "x"')
    assert.match(tests[3].details, /SampleTests\.cs:line 12/)
  })

  test('sums the outermost suites when the root has no time', () => {
    const xml = '<testsuite time="1"><testsuite time="0.5"><testcase name="a" time="0.5"/></testsuite></testsuite>'
    assert.equal(parseJunit(xml).time, 1)
  })
})

describe('buildReport', () => {
  test('counts a passing suite and its duration', () => {
    const report = buildReport({ suites: ['passing:tests/Sample.Tests'], resultsDir, root })

    assert.equal(report.ok, true)
    assert.deepEqual(report.suites[0], {
      name: 'passing',
      missing: false,
      passed: 2,
      failed: 0,
      skipped: 0,
      duration: 1.5,
      failures: [],
    })
    assert.match(formatSummary(report), /^\| passing \| 2 \| 0 \| 0 \| 1\.5s \|$/m)
    assert.deepEqual(formatAnnotations(report), [])
  })

  test('lists failed tests with their message and repository-relative location', () => {
    const report = buildReport({ suites: ['failing:web'], resultsDir, root })

    assert.equal(report.ok, false)
    const [suite] = report.suites
    assert.deepEqual([suite.passed, suite.failed, suite.skipped], [1, 3, 1])
    assert.deepEqual(
      suite.failures.map((f) => [f.name, f.file, f.line]),
      [
        ['Sample > subtracts numbers', 'web/src/sample.test.ts', 3],
        ['Sample.Tests.SampleTests.Compares_text', 'tests/Sample.Tests/SampleTests.cs', 12],
        ['Sample.Tests.SampleTests.Throws', undefined, undefined],
      ],
    )

    const summary = formatSummary(report)
    assert.match(summary, /^\| failing \| 1 \| 3 \| 1 \| 2\.4s \|$/m)
    assert.match(summary, /^### Failed tests$/m)
    assert.match(summary, /^- \*\*failing\*\*: `Sample > subtracts numbers` \(`web\/src\/sample\.test\.ts:3`\)$/m)
    assert.match(summary, /^ {2}expected 1 to be 2 \/\/ Object\.is equality$/m)
    assert.match(
      summary,
      /^- \*\*failing\*\*: `Sample\.Tests\.SampleTests\.Compares_text` \(`tests\/Sample\.Tests\/SampleTests\.cs:12`\)$/m,
    )
    assert.match(summary, /^- \*\*failing\*\*: `Sample\.Tests\.SampleTests\.Throws`$/m)
  })

  test('emits one annotation per failed test', () => {
    const report = buildReport({ suites: ['failing:web'], resultsDir, root })

    assert.deepEqual(formatAnnotations(report), [
      '::error file=web/src/sample.test.ts,line=3,title=Sample > subtracts numbers::expected 1 to be 2 // Object.is equality',
      '::error file=tests/Sample.Tests/SampleTests.cs,line=12,title=Sample.Tests.SampleTests.Compares_text::' +
        'Assert.Equal() Failure: Strings differ%0AExpected: "a<b&c"%0AActual:   "x"',
      '::error title=Sample.Tests.SampleTests.Throws::Boom, 100%25 broken',
    ])
  })

  test('marks a suite without a results file as missing', () => {
    const report = buildReport({ suites: ['absent:web'], resultsDir, root })

    assert.equal(report.ok, false)
    assert.equal(report.suites[0].missing, true)
    assert.match(formatSummary(report), /^\| absent \(missing: no results file\) \| - \| - \| - \| - \|$/m)
  })
})

describe('command line', () => {
  test('prints the summary and exits 0 when every suite passed', () => {
    const result = runCli(['passing:tests/Sample.Tests'])

    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stdout, /^\| passing \| 2 \| 0 \| 0 \| 1\.5s \|$/m)
    assert.doesNotMatch(result.stdout, /::error/)
  })

  test('reports a --skipped suite as skipped, with no annotation and exit 0', () => {
    const report = buildReport({
      suites: ['passing:tests/Sample.Tests', 'absent:web'],
      resultsDir,
      root,
      skipped: ['absent'],
    })

    assert.equal(report.ok, true)
    assert.match(formatSummary(report), /^\| absent \(skipped: not affected by this change\) \| - \| - \| - \| - \|$/m)
    assert.deepEqual(formatAnnotations(report), [])

    const result = runCli(['--skipped', 'absent', 'passing:tests/Sample.Tests', 'absent:web'], {
      GITHUB_ACTIONS: 'true',
    })
    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stdout, /absent \(skipped: not affected by this change\)/)
    assert.doesNotMatch(result.stdout, /::error/)
  })

  test('exits 1 when a suite failed or is missing', () => {
    assert.equal(runCli(['passing:tests/Sample.Tests', 'failing:web']).status, 1)
    assert.equal(runCli(['passing:tests/Sample.Tests', 'absent:web']).status, 1)
  })

  test('appends the summary to GITHUB_STEP_SUMMARY and prints annotations on GitHub Actions', () => {
    const summaryFile = path.join(resultsDir, 'summary.md')
    writeFileSync(summaryFile, '# Earlier step\n')

    const result = runCli(suites, { GITHUB_ACTIONS: 'true', GITHUB_STEP_SUMMARY: summaryFile })

    assert.equal(result.status, 1, result.stderr)
    const summary = readFileSync(summaryFile, 'utf8')
    assert.ok(summary.startsWith('# Earlier step\n## Test results\n'))
    assert.match(summary, /^\| absent \(missing: no results file\) \| - \| - \| - \| - \|$/m)
    assert.doesNotMatch(result.stdout, /Test results|\| Suite \|/)
    assert.deepEqual(result.stdout.trimEnd().split('\n'), [
      '::error file=web/src/sample.test.ts,line=3,title=Sample > subtracts numbers::expected 1 to be 2 // Object.is equality',
      '::error file=tests/Sample.Tests/SampleTests.cs,line=12,title=Sample.Tests.SampleTests.Compares_text::' +
        'Assert.Equal() Failure: Strings differ%0AExpected: "a<b&c"%0AActual:   "x"',
      '::error title=Sample.Tests.SampleTests.Throws::Boom, 100%25 broken',
      '::error title=absent::absent wrote no test results.',
    ])
  })
})
