import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { describe, test } from 'node:test'
import { count, estimate, junitDurations, main, readFiles, shardFiles, split } from './lib/test-shard.mjs'
import { mergeDurations } from './lib/shards.mjs'

const file = (name, seconds) => ({ path: name, seconds })
const names = (files) => files.map((f) => f.path).sort()

const files = [
  file('a.test.ts', 50),
  file('b.test.ts', 40),
  file('c.test.ts', 30),
  file('d.test.ts', 30),
  file('e.test.ts', 20),
  file('f.test.ts', 20),
  file('g.test.ts', 10),
  file('h.test.ts', 5),
  file('i.test.ts', 5),
]

describe('split and shardFiles', () => {
  test('puts every file in exactly one shard, the same way on every call and in any input order', () => {
    const shards = split(files, 3, 4)
    assert.deepEqual(shards.flat().sort(), names(files))
    assert.deepEqual(split(files, 3, 4), shards)
    assert.deepEqual(split([...files].reverse(), 3, 4), shards)
  })

  test('gives shard 1 of several no file, and shards 2 and 3 every file between them', () => {
    assert.deepEqual(shardFiles(files, 1, 3, 4), [])
    const two = shardFiles(files, 2, 3, 4)
    const three = shardFiles(files, 3, 3, 4)
    assert.deepEqual([...two, ...three].sort(), names(files))
    assert.deepEqual(
      two.filter((name) => three.includes(name)),
      [],
    )
  })

  test('gives a shard of 1 of 1 every file', () => {
    assert.deepEqual(shardFiles(files, 1, 1, 4), names(files))
  })
})

describe('estimate and count', () => {
  test('a shard takes the longer of its seconds shared over the workers and its longest file', () => {
    assert.equal(estimate([file('a', 40), file('b', 20)], 4), 40)
    assert.equal(
      estimate(
        Array.from({ length: 8 }, (_, i) => file(`f${i}`, 10)),
        4,
      ),
      20,
    )
    assert.equal(estimate([], 4), 0)
  })

  const eight = Array.from({ length: 8 }, (_, i) => file(`f${i}.test.ts`, 100))

  test('prints the fewest shards that fit the budget, counting shard 1 and never less than 2', () => {
    assert.deepEqual(count(eight, 1, 250), { total: 5, overBudget: [] })
    assert.deepEqual(count(eight, 1, 1000), { total: 2, overBudget: [] })
  })

  test('prints the cap when no count up to it fits the budget', () => {
    assert.deepEqual(count(eight, 1, 150), { total: 6, overBudget: [] })
  })

  test('names each file that alone takes longer than the budget, and still counts', () => {
    const specs = [file('big.test.ts', 1000), file('small.test.ts', 10)]
    assert.deepEqual(count(specs, 4, 900), {
      total: 6,
      overBudget: ['Over budget: big.test.ts takes about 1000 s, longer than the budget of 900 s.'],
    })
  })
})

describe('readFiles and main', () => {
  const fixture = (durations, paths) => {
    const dir = mkdtempSync(path.join(tmpdir(), 'test-shard-'))
    writeFileSync(path.join(dir, 'vitest-durations.json'), JSON.stringify(durations))
    for (const name of paths) {
      mkdirSync(path.dirname(path.join(dir, name)), { recursive: true })
      writeFileSync(path.join(dir, name), '')
    }
    return dir
  }

  test('weighs a file without a duration as the median, finds nested files and skips node_modules', () => {
    const dir = fixture({ 'src/a.test.ts': 10, 'src/z.test.ts': 30 }, [
      'src/a.test.ts',
      'src/z.test.ts',
      'src/nested/deep/new.spec.tsx',
      'src/helper.ts',
      'node_modules/pkg/x.test.ts',
    ])
    try {
      assert.deepEqual(readFiles(dir), [
        { path: 'src/a.test.ts', seconds: 10 },
        { path: 'src/nested/deep/new.spec.tsx', seconds: 20 },
        { path: 'src/z.test.ts', seconds: 30 },
      ])
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })

  test('weighs a file 1 second when no duration is known', () => {
    const dir = fixture({}, ['src/a.test.ts'])
    try {
      assert.deepEqual(readFiles(dir), [{ path: 'src/a.test.ts', seconds: 1 }])
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })

  test('prints the files of a shard relative to the folder, and nothing for shard 1 of several', () => {
    const dir = fixture({ 'src/a.test.ts': 10, 'src/b.test.ts': 20 }, ['src/a.test.ts', 'src/b.test.ts'])
    try {
      assert.equal(main(['1/3'], dir), '')
      assert.equal(main(['2/3'], dir), 'src/b.test.ts')
      assert.equal(main(['3/3'], dir), 'src/a.test.ts')
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })

  test('--count prints the count alone and warns about each file over the budget', () => {
    const dir = fixture({ 'big.test.ts': 1000, 'small.test.ts': 10 }, ['big.test.ts', 'small.test.ts'])
    const warnings = []
    try {
      assert.equal(
        main(['--count'], dir, (line) => warnings.push(line)),
        '6',
      )
      assert.deepEqual(warnings, ['Over budget: big.test.ts takes about 1000 s, longer than the budget of 900 s.'])
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })

  test('--durations rewrites the durations from JUnit files, keeping unmeasured files and dropping gone ones', () => {
    const dir = fixture({ 'src/kept.test.ts': 5, 'src/measured.test.ts': 9, 'src/gone.test.ts': 7 }, [
      'src/kept.test.ts',
      'src/measured.test.ts',
      'src/new.test.ts',
    ])
    const junit = path.join(dir, 'web.xml')
    writeFileSync(
      junit,
      '<testsuites name="vitest tests" time="12"><testsuite name="src/measured.test.ts" time="3.2"></testsuite>' +
        '<testsuite name="src/new.test.ts" time="4.4"></testsuite></testsuites>',
    )
    try {
      assert.equal(
        main(['--durations', junit], dir),
        `2 files measured, 3 kept in ${path.join(dir, 'vitest-durations.json')}.`,
      )
      assert.deepEqual(JSON.parse(readFileSync(path.join(dir, 'vitest-durations.json'), 'utf8')), {
        'src/kept.test.ts': 5,
        'src/measured.test.ts': 3,
        'src/new.test.ts': 4,
      })
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })

  test('refuses a malformed shard', () => {
    const dir = fixture({}, [])
    try {
      for (const shard of ['4/3', '0/3', 'abc']) assert.throws(() => main([shard], dir), /A shard such as 1\/3/)
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })
})

describe('junitDurations and mergeDurations', () => {
  test('reads each file suite by its name, and not the root suite of the run', () => {
    const text =
      '<testsuites name="vitest tests" time="99">' +
      '<testsuite name="src/a.test.ts" hostname="runner" time="1.4"></testsuite>' +
      '<testsuite name="src/b.test.ts" time="2.6"></testsuite></testsuites>'
    assert.deepEqual(junitDurations([text]), { 'src/a.test.ts': 1, 'src/b.test.ts': 3 })
  })

  test('keeps a name that was not measured and drops one that no longer exists', () => {
    assert.deepEqual(
      mergeDurations({ 'src/a.test.ts': 1, 'src/old.test.ts': 2 }, { 'src/b.test.ts': 3 }, [
        'src/b.test.ts',
        'src/a.test.ts',
      ]),
      { 'src/a.test.ts': 1, 'src/b.test.ts': 3 },
    )
  })
})

describe('the real web folder', () => {
  test('no Vitest file path is a substring of another, so each filter picks out one file', () => {
    const paths = readFiles().map((testFile) => testFile.path)
    assert.ok(paths.length > 100)
    for (const a of paths) {
      for (const b of paths) if (a !== b) assert.ok(!b.includes(a), `${a} is a substring of ${b}`)
    }
  })

  test('the two Vitest shards give every file of web/ between them, and each file once', () => {
    const second = main(['2/3']).split('\n')
    const third = main(['3/3']).split('\n')
    assert.deepEqual([...second, ...third].sort(), names(readFiles()))
  })
})
