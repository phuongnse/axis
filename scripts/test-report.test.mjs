import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { copyFileSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, before, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import {
  buildReport,
  formatAnnotations,
  formatFailedTests,
  formatSummary,
  parseJunit,
} from './test-report.mjs'

const script = fileURLToPath(new URL('./test-report.mjs', import.meta.url))
const repoRoot = fileURLToPath(new URL('..', import.meta.url))
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
  // A suite whose JUnit time (1,297.8 s) is longer than the time measured for its command (462 s).
  writeFileSync(
    path.join(resultsDir, 'measured.xml'),
    '<testsuites time="1297.8"><testsuite><testcase name="t"/></testsuite></testsuites>',
  )
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

  test('lists each failed test as plain text and cuts a long message to 20 lines', () => {
    const report = {
      suites: [
        {
          name: 's',
          failures: [
            {
              suite: 's',
              name: 't',
              message: Array.from({ length: 25 }, (_, i) => `line ${i + 1}`).join('\n'),
            },
          ],
        },
      ],
    }

    const lines = formatFailedTests(report)
    assert.ok(lines.includes('    line 20'))
    assert.ok(!lines.includes('    line 21'))
    assert.equal(lines.at(-1), '    ... 5 more lines, see the job summary')
    assert.deepEqual(formatFailedTests(buildReport({ suites: ['passing:tests/Sample.Tests'], resultsDir, root })), [])
  })

  test('marks a suite without a results file as missing', () => {
    const report = buildReport({ suites: ['absent:web'], resultsDir, root })

    assert.equal(report.ok, false)
    assert.equal(report.suites[0].missing, true)
    assert.match(formatSummary(report), /^\| absent \(missing: no results file\) \| - \| - \| - \| - \|$/m)
  })

  test('shows the measured duration of a suite in place of its JUnit time', () => {
    const report = buildReport({ suites: ['measured:web'], resultsDir, root, durations: { measured: 462 } })

    assert.equal(report.suites[0].duration, 462)
    assert.match(formatSummary(report), /^\| measured \| 1 \| 0 \| 0 \| 7m 42s \|$/m)
  })

  test('leads a passing run with one line and collapses its table', () => {
    const summary = formatSummary(
      buildReport({ suites: ['passing:tests/Sample.Tests', 'absent:web'], resultsDir, root, skipped: ['absent'] }),
    )

    assert.equal(summary.split('\n')[2], '✓ 2 tests passed in 1.5s, 1 suite skipped as not affected by this change')
    assert.match(summary, /<details><summary>Suites<\/summary>\n\n\| Suite \|[\s\S]*?\n\n<\/details>/)
  })

  test('totals the measured times, or the file times when no suite has one', () => {
    const measured = buildReport({
      suites: ['passing:tests/Sample.Tests', 'absent:web'],
      resultsDir,
      root,
      skipped: ['absent'],
      durations: { passing: 462 },
    })
    assert.equal(
      formatSummary(measured).split('\n')[2],
      '✓ 2 tests passed in 7m 42s, 1 suite skipped as not affected by this change',
    )

    const unmeasured = buildReport({ suites: ['passing:tests/Sample.Tests'], resultsDir, root })
    assert.equal(formatSummary(unmeasured).split('\n')[2], '✓ 2 tests passed in 1.5s')
  })

  test('leaves a suite without a measured time out of the total', () => {
    const report = buildReport({
      suites: ['measured:web', 'passing:tests/Sample.Tests'],
      resultsDir,
      root,
      durations: { measured: 462 },
    })

    assert.equal(report.duration, 462)
    assert.equal(formatSummary(report).split('\n')[2], '✓ 3 tests passed in 7m 42s')
  })

  test('lists the failed tests before an open table when a suite failed', () => {
    const summary = formatSummary(buildReport({ suites: ['failing:web'], resultsDir, root }))

    assert.ok(summary.indexOf('### Failed tests') < summary.indexOf('| Suite |'))
    assert.doesNotMatch(summary, /<details><summary>Suites|✓/)
  })

  test('shows an open table with no total when a suite wrote no results', () => {
    const summary = formatSummary(buildReport({ suites: ['absent:web'], resultsDir, root }))

    assert.match(summary, /^\| absent \(missing: no results file\) \| - \| - \| - \| - \|$/m)
    assert.doesNotMatch(summary, /<details><summary>Suites|✓|Slowest tests/)
  })

  test('lists the five slowest test cases of the run, slowest first', () => {
    const report = buildReport({ suites: ['passing:tests/Sample.Tests', 'failing:web'], resultsDir, root })

    assert.deepEqual(
      report.slowest.map((t) => [t.suite, t.name, t.time]),
      [
        ['failing', 'Sample.Tests.SampleTests.Compares_text', 1.25],
        ['passing', 'Sample.Tests.SampleTests.First_passes', 0.9],
        ['passing', 'Sample.Tests.SampleTests.Second_passes', 0.6],
        ['failing', 'Sample > adds numbers', 0.5],
        ['failing', 'Sample > subtracts numbers', 0.25],
      ],
    )
    const summary = formatSummary(report)
    const slowest = summary.slice(summary.indexOf('<details><summary>Slowest tests</summary>'))
    assert.match(slowest, /^\| `Sample\.Tests\.SampleTests\.Compares_text` \| failing \| 1\.3s \|$/m)
    assert.doesNotMatch(slowest, /Throws/)
  })
})

describe('command line', () => {
  test('prints the summary and exits 0 when every suite passed', () => {
    const result = runCli(['passing:tests/Sample.Tests'])

    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stdout, /^\| passing \| 2 \| 0 \| 0 \| 1\.5s \|$/m)
    assert.doesNotMatch(result.stdout, /::error/)
  })

  test('reports a --skipped suite as skipped, with a notice and exit 0', () => {
    const report = buildReport({
      suites: ['passing:tests/Sample.Tests', 'absent:web'],
      resultsDir,
      root,
      skipped: ['absent'],
    })

    assert.equal(report.ok, true)
    assert.match(formatSummary(report), /^\| absent \(skipped: not affected by this change\) \| - \| - \| - \| - \|$/m)
    const notice =
      "::notice title=absent skipped::absent did not run: no file changed since NexKit's commit can affect it."
    assert.deepEqual(formatAnnotations(report), [notice])

    const result = runCli(['--skipped', 'absent', 'passing:tests/Sample.Tests', 'absent:web'], {
      GITHUB_ACTIONS: 'true',
    })
    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stdout, /absent \(skipped: not affected by this change\)/)
    assert.doesNotMatch(result.stdout, /::error/)
    assert.ok(result.stdout.split('\n').includes(notice), result.stdout)

    assert.doesNotMatch(runCli(['--skipped', 'absent', 'passing:tests/Sample.Tests', 'absent:web']).stdout, /::notice/)
  })

  test('shows a --duration in place of the JUnit time of its suite', () => {
    const result = runCli(['--duration', 'measured=462', 'measured:web'])

    assert.equal(result.status, 0, result.stderr)
    assert.match(result.stdout, /^\| measured \| 1 \| 0 \| 0 \| 7m 42s \|$/m)
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
      'Failed tests:',
      '- failing: Sample > subtracts numbers (web/src/sample.test.ts:3)',
      '    expected 1 to be 2 // Object.is equality',
      '- failing: Sample.Tests.SampleTests.Compares_text (tests/Sample.Tests/SampleTests.cs:12)',
      '    Assert.Equal() Failure: Strings differ',
      '    Expected: "a<b&c"',
      '    Actual:   "x"',
      '- failing: Sample.Tests.SampleTests.Throws',
      '    Boom, 100% broken',
    ])
  })

  test('prints no failed tests list when every test passed on GitHub Actions', () => {
    const summaryFile = path.join(resultsDir, 'passing-summary.md')

    const result = runCli(['passing:tests/Sample.Tests'], { GITHUB_ACTIONS: 'true', GITHUB_STEP_SUMMARY: summaryFile })

    assert.equal(result.status, 0, result.stderr)
    assert.doesNotMatch(result.stdout, /Failed tests/)
    assert.equal(result.stdout.trim(), '')
  })
})

describe('scripts/lib/test-results.sh', () => {
  // Sources the library in bash with a clock that the test sets, so no test asserts on real time. The
  // suite web-2 moves the fake clock on by 462 s while its command fails. scenarios-x is registered with
  // expect_suite and writes its JUnit file, which says 100 s, after that.
  function runLibrary(clock) {
    const { GITHUB_ACTIONS, GITHUB_STEP_SUMMARY, ...inherited } = process.env
    const dir = mkdtempSync(path.join(tmpdir(), 'test-results-'))
    try {
      const script = `set -euo pipefail
source scripts/lib/test-results.sh
now=1000000000000000
${clock}
slow() {
  now=$((now + 462000000))
  echo '<testsuites time="1297.8"><testsuite><testcase name="t"/></testsuite></testsuites>' > "$results_dir/web-2.xml"
  return 1
}
run_suite web-2 web slow
expect_suite scenarios-x .
echo '<testsuites time="100"><testsuite><testcase name="s"/></testsuite></testsuites>' > "$results_dir/scenarios-x.xml"
finish_report
`
      return spawnSync('bash', ['-c', script], {
        cwd: repoRoot,
        encoding: 'utf8',
        env: { ...inherited, AXIS_TEST_RESULTS_DIR: dir },
      })
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  }

  test('shows the time that a failing suite command took, and exits 1', () => {
    const result = runLibrary('suite_clock() { echo "$now"; }')

    assert.equal(result.status, 1, result.stderr)
    assert.match(result.stdout, /^\| web-2 \| 1 \| 0 \| 0 \| 7m 42s \|$/m)
  })

  test('shows the JUnit time of a suite registered with expect_suite', () => {
    const result = runLibrary('suite_clock() { echo "$now"; }')

    assert.match(result.stdout, /^\| scenarios-x \| 1 \| 0 \| 0 \| 1m 40s \|$/m)
  })

  test('shows the JUnit time of a suite when bash gives no clock', () => {
    const result = runLibrary('suite_clock() { :; }')

    assert.equal(result.status, 1, result.stderr)
    assert.match(result.stdout, /^\| web-2 \| 1 \| 0 \| 0 \| 21m 38s \|$/m)
  })
})
