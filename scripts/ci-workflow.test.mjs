import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { blockAfter, indentOf, isContent, stepsOf, valueOf } from './lib/workflow-lines.mjs'

// In CI, a failed upload of test results does not fail a job whose checks passed, and the Checks step alone decides
// the job's result. See docs/delivery.md#ci-on-main. The workflow is read line by line with
// scripts/lib/workflow-lines.mjs.
const workflow = fileURLToPath(new URL('../.github/workflows/ci.yml', import.meta.url))

const ciWorkflowProblems = (text) => {
  const lines = text.split(/\r?\n/)
  const steps = stepsOf(lines)
  const problems = []

  // The checks job's own keys: the lines of its block at the indentation of its first key, which excludes its steps.
  const job = lines.findIndex((line) => /^\s*checks:\s*$/.test(line))
  if (job === -1) {
    problems.push('ci.yml has no checks job')
  } else {
    const block = blockAfter(lines, job, indentOf(lines[job])).filter(isContent)
    const keys = block.filter((line) => indentOf(line) === indentOf(block[0]))
    if (keys.some((line) => /^\s*continue-on-error:/.test(line))) {
      problems.push('the checks job must not have continue-on-error')
    }
  }

  const uploads = steps.filter(
    (step) =>
      step.some((line) => /^\s*uses:\s*actions\/upload-artifact@/.test(line)) &&
      step.some((line) => /^\s*name:\s*test-results-\$\{\{\s*matrix\.name\s*\}\}\s*$/.test(line)),
  )
  if (uploads.length !== 1) {
    problems.push(`ci.yml must have one step that uploads test-results-\${{ matrix.name }}, found ${uploads.length}`)
  } else if (valueOf(uploads[0], 'continue-on-error') !== 'true') {
    problems.push('the upload of test results must have continue-on-error: true')
  }

  const checks = steps.filter((step) => valueOf(step, 'name') === 'Checks')
  if (checks.length !== 1) {
    problems.push(`ci.yml must have one step named Checks, found ${checks.length}`)
  } else if (valueOf(checks[0], 'continue-on-error') !== undefined) {
    problems.push('the Checks step must not have continue-on-error')
  }

  return problems
}

const good = `name: CI
jobs:
  checks:
    runs-on: ubuntu-24.04
    steps:
      - uses: actions/checkout@v7
      - name: Checks
        run: scripts/test.sh
      - name: Upload test results
        if: \${{ !cancelled() }}
        continue-on-error: true
        uses: actions/upload-artifact@v7
        with:
          name: test-results-\${{ matrix.name }}
          path: artifacts/test-results
`

describe('CI workflow', () => {
  test('a failed upload of test results does not fail a job, and the Checks step decides it', () => {
    assert.deepEqual(ciWorkflowProblems(readFileSync(workflow, 'utf8')), [])
  })

  test('a good workflow has no problems', () => {
    assert.deepEqual(ciWorkflowProblems(good), [])
  })

  test('an upload of test results without continue-on-error is a problem', () => {
    assert.deepEqual(ciWorkflowProblems(good.replace('        continue-on-error: true\n', '')), [
      'the upload of test results must have continue-on-error: true',
    ])
  })

  test('a Checks step with continue-on-error is a problem', () => {
    const text = good.replace('      - name: Checks\n', '      - name: Checks\n        continue-on-error: true\n')
    assert.deepEqual(ciWorkflowProblems(text), ['the Checks step must not have continue-on-error'])
  })

  test('a checks job with continue-on-error is a problem', () => {
    const text = good.replace('    runs-on: ubuntu-24.04\n', '    runs-on: ubuntu-24.04\n    continue-on-error: true\n')
    assert.deepEqual(ciWorkflowProblems(text), ['the checks job must not have continue-on-error'])
  })

  test('a workflow without the upload of test results is a problem', () => {
    const text = good.replace('test-results-${{ matrix.name }}', 'other')
    assert.deepEqual(ciWorkflowProblems(text), [
      'ci.yml must have one step that uploads test-results-${{ matrix.name }}, found 0',
    ])
  })
})
