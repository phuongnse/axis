import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { describe, test } from 'node:test'
import {
  count,
  estimate,
  isSerial,
  junitDurations,
  main,
  readSpecs,
  split,
} from './lib/e2e-shard.mjs'
import { mergeDurations } from './lib/shards.mjs'

const spec = (name, seconds, serial = false) => ({ path: `specs/${name}.spec.ts`, seconds, serial })

describe('count', () => {
  const four = [spec('a', 100), spec('b', 100), spec('c', 100), spec('d', 100)]

  test('prints the fewest shards whose longest estimated shard fits the budget', () => {
    // Four 100 s specs take 200 s on two workers, and two shards of two specs take 100 s each.
    assert.deepEqual(count(four, 2, 150, 6), { total: 2, overBudget: [] })
    assert.deepEqual(count(four, 2, 250, 6), { total: 1, overBudget: [] })
  })

  test('prints the cap when no count up to the cap fits the budget', () => {
    // Three shards still leave one with two 100 s specs, which runs 100 s on two workers, past the 50 s budget.
    assert.deepEqual(count(four, 2, 50, 3), { total: 3, overBudget: [] })
  })

  test('names each spec that alone takes longer than the budget, and still counts', () => {
    const specs = [spec('big', 1000, true), spec('small', 10)]
    assert.deepEqual(count(specs, 2, 900, 6), {
      total: 6,
      overBudget: ['Over budget: specs/big.spec.ts takes about 1000 s, longer than the budget of 900 s.'],
    })
  })

  test('counts a parallel spec by its seconds on the workers', () => {
    // 1600 s shares two workers to 800 s, so it fits the 900 s budget.
    assert.deepEqual(count([spec('wide', 1600)], 2, 900, 6), { total: 1, overBudget: [] })
  })
})

describe('estimate', () => {
  test('shares the workers between independent tests', () => {
    assert.equal(estimate([spec('a', 60), spec('b', 40)], 2), 50)
  })

  test('starts a serial spec once the specs before it in file order have shared the workers', () => {
    // a runs 60 s on two workers, so the serial z starts at 30 s and ends at 330 s.
    assert.equal(estimate([spec('z', 300, true), spec('a', 60)], 2), 330)
    assert.equal(estimate([spec('a', 300, true), spec('z', 60)], 2), 300)
  })
})

describe('split', () => {
  test('puts every spec in exactly one shard, the same way on every call', () => {
    const specs = Array.from({ length: 20 }, (_, index) => spec(`s${index}`, (index * 7) % 13, index % 5 === 0))
    const shards = split(specs, 3, 2)

    assert.deepEqual(shards.flat().sort(), specs.map((s) => s.path).sort())
    assert.deepEqual(split([...specs].reverse(), 3, 2), shards)
  })

  test('gives a long serial journey a shard of its own instead of adding it to the busiest one', () => {
    const specs = [
      spec('service-desk-journey', 396, true),
      spec('asset-management-journey', 253, true),
      spec('purchase-request-release', 145, true),
      ...Array.from({ length: 30 }, (_, index) => spec(`page-${index}`, 20)),
    ]
    const shards = split(specs, 3, 2)

    assert.deepEqual(shards[0], ['specs/service-desk-journey.spec.ts'])
    const ends = shards.map((paths) =>
      estimate(
        specs.filter((s) => paths.includes(s.path)),
        2,
      ),
    )
    assert.ok(Math.max(...ends) <= 400, ends.join(' '))
  })
})

describe('readSpecs and main', () => {
  const fixture = (durations, specs) => {
    const dir = mkdtempSync(path.join(tmpdir(), 'e2e-shard-'))
    mkdirSync(path.join(dir, 'specs'))
    writeFileSync(path.join(dir, 'spec-durations.json'), JSON.stringify(durations))
    for (const [name, source] of Object.entries(specs)) writeFileSync(path.join(dir, 'specs', name), source)
    return dir
  }

  test('weighs a spec without a duration as the median, and finds serial specs', () => {
    const dir = fixture(
      { 'specs/a.spec.ts': 10, 'specs/b.spec.ts': 30, 'specs/c.spec.ts': 50 },
      {
        'a.spec.ts': "test.describe.configure({ mode: 'serial' })",
        'b.spec.ts': '',
        'c.spec.ts': '',
        'new.spec.ts': '',
        'sessions.setup.ts': '',
      },
    )
    try {
      const specs = readSpecs(dir)

      assert.deepEqual(
        specs.map((s) => [s.path, s.seconds, s.serial]),
        [
          ['specs/a.spec.ts', 10, true],
          ['specs/b.spec.ts', 30, false],
          ['specs/c.spec.ts', 50, false],
          ['specs/new.spec.ts', 30, false],
        ],
      )
      const printed = ['1/2', '2/2'].flatMap((shard) => main([shard], dir).split('\n'))
      assert.deepEqual(printed.sort(), specs.map((s) => s.path).sort())
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })

  test('--count prints the count alone and warns about each spec over the budget', () => {
    const dir = fixture(
      { 'specs/big.spec.ts': 1000, 'specs/a.spec.ts': 10 },
      { 'big.spec.ts': "test.describe.configure({ mode: 'serial' })", 'a.spec.ts': '' },
    )
    const warnings = []
    try {
      assert.equal(main(['--count'], dir, (line) => warnings.push(line)), '6')
      assert.deepEqual(warnings, [
        'Over budget: specs/big.spec.ts takes about 1000 s, longer than the budget of 900 s.',
      ])
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })

  for (const shard of ['3/2', '0/2', 'x', undefined]) {
    test(`refuses the shard ${shard}`, () => {
      assert.throws(() => main(shard === undefined ? [] : [shard]), /A shard such as 1\/3/)
    })
  }

  test('--durations rewrites the durations from JUnit files', () => {
    const dir = fixture({ 'specs/a.spec.ts': 10, 'specs/gone.spec.ts': 99 }, { 'a.spec.ts': '', 'b.spec.ts': '' })
    const junit = path.join(dir, 'playwright-1.xml')
    writeFileSync(
      junit,
      '<testsuites><testsuite name="b.spec.ts" timestamp="t" hostname="chromium" tests="2" time="12.6">' +
        '</testsuite><testsuite name="sessions.setup.ts" hostname="setup" time="40"></testsuite></testsuites>',
    )
    try {
      main(['--durations', junit], dir)

      assert.deepEqual(JSON.parse(readFileSync(path.join(dir, 'spec-durations.json'), 'utf8')), {
        'specs/a.spec.ts': 10,
        'specs/b.spec.ts': 13,
      })
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })
})

describe('junitDurations and mergeDurations', () => {
  test('reads only chromium suites, by their name and not their hostname', () => {
    const text =
      '<testsuite name="a.spec.ts" hostname="chromium" time="4.4"/>' +
      '<testsuite name="sessions.setup.ts" hostname="setup" time="9"/>'
    assert.deepEqual(junitDurations([text]), { 'specs/a.spec.ts': 4 })
  })

  test('keeps a spec that was not measured and drops one that no longer exists', () => {
    assert.deepEqual(
      mergeDurations({ 'specs/a.spec.ts': 1, 'specs/old.spec.ts': 2 }, { 'specs/b.spec.ts': 3 }, [
        'specs/b.spec.ts',
        'specs/a.spec.ts',
      ]),
      { 'specs/a.spec.ts': 1, 'specs/b.spec.ts': 3 },
    )
  })
})

test('isSerial finds a serial describe', () => {
  assert.equal(isSerial("test.describe.configure({ mode: 'serial' })"), true)
  assert.equal(isSerial("test.describe.configure({ mode: 'parallel' })"), false)
})
