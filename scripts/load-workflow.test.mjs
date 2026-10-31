import assert from 'node:assert/strict'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// The load test of D82 runs in exactly one workflow, only once a week and on demand, and keeps its results whatever
// the run's result. See docs/reference/performance.md#load-test. The workflows are read line by line, since the
// scripts' tests have no YAML dependency.
const root = fileURLToPath(new URL('..', import.meta.url))

const indentOf = (line) => line.length - line.trimStart().length

const isContent = (line) => line.trim() !== '' && !line.trimStart().startsWith('#')

// The lines of the block that starts after `start` and ends before the next content line at `indent` or less.
const blockAfter = (lines, start, indent) => {
  const block = []
  for (const line of lines.slice(start + 1)) {
    if (isContent(line) && indentOf(line) <= indent) break
    block.push(line)
  }
  return block
}

const unquote = (value) => value.trim().replace(/^(['"])(.*)\1$/, '$2')

const days = /^([0-7]|sun|mon|tue|wed|thu|fri|sat)$/i

const isWeekly = (cron) => {
  const fields = cron.split(/\s+/)
  return (
    fields.length === 5 &&
    /^\d+$/.test(fields[0]) &&
    /^\d+$/.test(fields[1]) &&
    fields[2] === '*' &&
    fields[3] === '*' &&
    days.test(fields[4])
  )
}

// The step blocks of a workflow: each runs from a `- ` line to the next `- ` line at the same or lower indentation.
const stepsOf = (lines) => {
  const steps = []
  lines.forEach((line, index) => {
    if (!/^\s*-\s/.test(line)) return
    const indent = indentOf(line)
    const rest = []
    for (const next of lines.slice(index + 1)) {
      if (isContent(next) && indentOf(next) <= indent && (/^\s*-\s/.test(next) || indentOf(next) < indent)) break
      rest.push(next)
    }
    steps.push([line.replace('-', ' '), ...rest])
  })
  return steps
}

const valueOf = (step, key) => {
  const line = step.find((candidate) => new RegExp(`^\\s*${key}:`).test(candidate))
  return line === undefined ? undefined : unquote(line.replace(new RegExp(`^\\s*${key}:`), ''))
}

const loadWorkflowProblems = (files) => {
  const running = Object.keys(files).filter((path) =>
    files[path].split(/\r?\n/).some((line) => /^\s*(-\s+)?run:\s*scripts\/load\.sh\s*$/.test(line)),
  )
  if (running.length !== 1) {
    return [`exactly one workflow must run scripts/load.sh, found ${running.length}: ${running.join(', ')}`]
  }

  const [path] = running
  const lines = files[path].split(/\r?\n/)
  const problems = []

  const on = lines.findIndex((line) => /^["']?on["']?:\s*$/.test(line))
  if (on === -1) {
    problems.push(`${path} has no on: block`)
  } else {
    const block = blockAfter(lines, on, 0).filter(isContent)
    const indent = Math.min(...block.map(indentOf))
    const triggers = block.filter((line) => indentOf(line) === indent).map((line) => line.trim().replace(/:.*$/, ''))
    if ([...triggers].sort().join(',') !== 'schedule,workflow_dispatch') {
      problems.push(`${path} must run only on schedule and workflow_dispatch, not on ${triggers.join(', ')}`)
    }
    const crons = block
      .filter((line) => /^\s*(-\s+)?cron:/.test(line))
      .map((line) => unquote(line.replace(/^\s*(-\s+)?cron:/, '')))
    if (crons.length === 0) problems.push(`${path} has no cron schedule`)
    for (const cron of crons.filter((value) => !isWeekly(value))) {
      problems.push(`${path} runs on '${cron}', which is not once a week`)
    }
  }

  const uploads = stepsOf(lines).filter(
    (step) =>
      step.some((line) => /^\s*uses:\s*actions\/upload-artifact@/.test(line)) &&
      valueOf(step, 'path') === 'artifacts/load-results',
  )
  const always = uploads.some((step) => /always\(\)|!cancelled\(\)/.test(valueOf(step, 'if') ?? ''))
  if (!always) problems.push(`${path} must upload artifacts/load-results with if: always() or !cancelled()`)

  return problems
}

const good = `name: Load test
on:
  schedule:
    - cron: '0 3 * * 1'
  workflow_dispatch:
jobs:
  load:
    runs-on: ubuntu-24.04
    steps:
      - uses: actions/checkout@v7
      - name: Load test
        run: scripts/load.sh
      - name: Upload load results
        if: always()
        uses: actions/upload-artifact@v7
        with:
          name: load-results
          path: artifacts/load-results
`

describe('load test workflow', () => {
  test('the repository has one workflow that runs the load test weekly and on demand and keeps its results', () => {
    const folder = join(root, '.github', 'workflows')
    const files = Object.fromEntries(
      readdirSync(folder)
        .filter((name) => /\.ya?ml$/.test(name))
        .map((name) => [`.github/workflows/${name}`, readFileSync(join(folder, name), 'utf8')]),
    )
    assert.deepEqual(loadWorkflowProblems(files), [])
  })

  test('a good workflow has no problems', () => {
    assert.deepEqual(loadWorkflowProblems({ 'load.yml': good }), [])
  })

  test('the upload may run under !cancelled()', () => {
    assert.deepEqual(loadWorkflowProblems({ 'load.yml': good.replace('if: always()', 'if: ${{ !cancelled() }}') }), [])
  })

  test('a pull_request trigger is a problem', () => {
    const problems = loadWorkflowProblems({
      'load.yml': good.replace('  workflow_dispatch:', '  workflow_dispatch:\n  pull_request:'),
    })
    assert.equal(problems.length, 1)
    assert.match(problems[0], /pull_request/)
  })

  test('a push trigger is a problem', () => {
    const problems = loadWorkflowProblems({
      'load.yml': good.replace('  workflow_dispatch:', '  workflow_dispatch:\n  push:\n    branches: [main]'),
    })
    assert.equal(problems.length, 1)
    assert.match(problems[0], /push/)
  })

  test('a daily cron is a problem', () => {
    const problems = loadWorkflowProblems({ 'load.yml': good.replace("'0 3 * * 1'", "'0 3 * * *'") })
    assert.deepEqual(problems, ["load.yml runs on '0 3 * * *', which is not once a week"])
  })

  test('an upload step without an always-run condition is a problem', () => {
    const problems = loadWorkflowProblems({ 'load.yml': good.replace('        if: always()\n', '') })
    assert.equal(problems.length, 1)
    assert.match(problems[0], /must upload artifacts\/load-results/)
  })

  test('an upload of another path is a problem', () => {
    const problems = loadWorkflowProblems({
      'load.yml': good.replace('path: artifacts/load-results', 'path: artifacts'),
    })
    assert.equal(problems.length, 1)
    assert.match(problems[0], /must upload artifacts\/load-results/)
  })

  test('no workflow that runs the load test is a problem', () => {
    assert.deepEqual(loadWorkflowProblems({ 'ci.yml': good.replace('run: scripts/load.sh', 'run: scripts/test.sh') }), [
      'exactly one workflow must run scripts/load.sh, found 0: ',
    ])
  })

  test('two workflows that run the load test are a problem', () => {
    assert.equal(loadWorkflowProblems({ 'a.yml': good, 'b.yml': good }).length, 1)
  })
})
