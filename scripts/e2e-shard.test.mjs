import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { describe, test } from 'node:test'
import { estimate, isSerial, junitDurations, main, mergeDurations, readSpecs, split } from './lib/e2e-shard.mjs'

const spec = (name, seconds, serial = false) => ({ path: `specs/${name}.spec.ts`, seconds, serial })

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
