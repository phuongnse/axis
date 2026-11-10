import assert from 'node:assert/strict'
import { execSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { configProblems, maxShards, shardedChecks } from './lib/shards.mjs'

// NexKit and CI run the E2E, integration and unit tests in as many shards as each split's --count prints, up to the
// check's max, which matches the split's cap. See docs/development.md#shards.
const root = fileURLToPath(new URL('..', import.meta.url))
const config = JSON.parse(readFileSync(new URL('../.nexkit/config.json', import.meta.url), 'utf8'))

const sharded = (name, max = maxShards[shardedChecks[name].split]) => ({
  name,
  run: shardedChecks[name].run,
  timeout_minutes: 30,
  shards: { count: `node scripts/lib/${shardedChecks[name].split}-shard.mjs --count`, max },
})
const valid = {
  checks: [
    { name: 'build', run: 'scripts/build.sh', group: 'build-lint' },
    { name: 'lint', run: 'scripts/lint.sh', group: 'build-lint' },
    sharded('test'),
    { name: 'smoke', run: 'scripts/smoke.sh' },
    sharded('integration'),
    sharded('e2e'),
  ],
}

describe('.nexkit/config.json', () => {
  test('declares each sharded check once, with its split’s count and cap, and no fixed shard', () => {
    assert.deepEqual(configProblems(config), [])
  })

  test('each count command prints a whole number from 1 to its max last', () => {
    for (const check of config.checks.filter((c) => c.shards)) {
      const lines = execSync(check.shards.count, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] })
        .split('\n')
        .filter((line) => line.trim())
      assert.match(lines.at(-1), /^\d+$/, check.name)
      const number = Number(lines.at(-1))
      assert.ok(number >= 1 && number <= check.shards.max, `${check.name} printed ${number}`)
    }
  })
})

describe('configProblems', () => {
  test('finds nothing in a config whose sharded checks match their splits', () => {
    assert.deepEqual(configProblems(valid), [])
  })

  test('reports a fixed shard left over', () => {
    const config = {
      checks: [...valid.checks.filter((check) => check.name !== 'smoke'), { name: 'e2e-1', run: 'AXIS_E2E_SHARD=1/3 scripts/e2e.sh' }],
    }
    assert.deepEqual(configProblems(config), ['e2e-1 is a fixed shard; its check has shards.'])
  })

  test('reports a max that is not the split’s cap, since NexKit fails a count above the max', () => {
    const config = { checks: valid.checks.map((check) => (check.name === 'integration' ? sharded('integration', 2) : check)) }
    assert.deepEqual(configProblems(config), [`integration has max 2, not its split's cap of ${maxShards.integration}.`])
  })

  test('reports a sharded check that is missing or runs another count', () => {
    const withoutTest = { checks: valid.checks.filter((check) => check.name !== 'test') }
    assert.deepEqual(configProblems(withoutTest), ['test is listed 0 times, not once.'])
    const otherCount = {
      checks: valid.checks.map((check) =>
        check.name === 'e2e' ? { ...check, shards: { ...check.shards, count: 'echo 3' } } : check,
      ),
    }
    assert.deepEqual(configProblems(otherCount), [
      "e2e counts its shards with 'echo 3', not 'node scripts/lib/e2e-shard.mjs --count'.",
    ])
  })

  test('reports caps that leave no room for two rounds within the job limit', () => {
    const config = {
      checks: [...valid.checks, { name: 'extra-1', run: 'true' }, { name: 'extra-2', run: 'true' }],
    }
    assert.deepEqual(configProblems(config), [
      'A round at its caps runs 12 jobs, so two rounds pass the 20 jobs GitHub runs at once.',
    ])
  })
})
