import assert from 'node:assert/strict'
import { mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { budgetSeconds, maxShards } from './lib/shards.mjs'
import { count, junitDurations, main, readSuites, split, suites } from './lib/smoke-shard.mjs'

const real = { 'dev-script': 360, compose: 150, images: 150, onprem: 180, 'ai-eval': 120 }
const list = (durations) => Object.entries(durations).map(([name, seconds]) => ({ name, seconds }))

// Runs the test with a durations file holding `durations`, and removes the folder after it.
function withDurations(durations, run) {
  const dir = mkdtempSync(path.join(tmpdir(), 'smoke-shard-'))
  try {
    const file = path.join(dir, 'smoke-durations.json')
    writeFileSync(file, JSON.stringify(durations))
    return run(file, dir)
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

function writeJunit(dir, name, body) {
  const file = path.join(dir, `${name}.xml`)
  writeFileSync(
    file,
    `<?xml version="1.0" encoding="utf-8"?>\n<testsuites>\n${body}\n<!-- tests 3 -->\n</testsuites>\n`,
  )
  return file
}

describe('suites', () => {
  test('are the suite names of scripts/smoke.sh, in its order', () => {
    const script = readFileSync(fileURLToPath(new URL('./smoke.sh', import.meta.url)), 'utf8')
    const names = [...script.matchAll(/^\s*run_suite_if_affected (\S+)/gm)].map((match) => match[1])
    assert.deepEqual(suites, names)
  })
})

describe('the smoke tests table of docs/development.md', () => {
  test('lists the suites, and no other test', () => {
    const docs = fileURLToPath(new URL('../docs/', import.meta.url))
    const tables = readdirSync(docs, { recursive: true, withFileTypes: true })
      .filter((entry) => entry.isFile() && entry.name.endsWith('.md'))
      .map((entry) => path.join(entry.parentPath, entry.name))
      .filter((file) => /^\| Test +\| What it does +\|$/m.test(readFileSync(file, 'utf8')))
    assert.equal(tables.length, 1, `one page has the smoke tests table: ${tables.join(', ')}`)
    const text = readFileSync(tables[0], 'utf8').split('\n')
    const names = text
      .slice(text.findIndex((line) => /^\| Test +\| What it does +\|$/.test(line)) + 2)
      .filter((line, index, lines) => lines.slice(0, index + 1).every((row) => row.startsWith('|')))
      .map((line) => /^\| `([^`]+)`/.exec(line)?.[1])
    assert.deepEqual(names, suites)
  })
})

describe('split', () => {
  test('puts every suite in exactly one shard for 1 to 6 shards, the same way on every call and in any input order', () => {
    const suitesList = list(real)
    for (let total = 1; total <= 6; total++) {
      const shards = split(suitesList, total)
      assert.equal(shards.length, total)
      assert.deepEqual(shards.flat().sort(), [...suites].sort(), `${total} shards`)
      assert.deepEqual(split(suitesList, total), shards)
      assert.deepEqual(split([...suitesList].reverse(), total), shards)
    }
  })

  test('gives the heaviest suite a shard of its own, then the next heaviest to the lightest shard', () => {
    assert.deepEqual(split(list(real), 3), [['dev-script'], ['ai-eval', 'onprem'], ['compose', 'images']])
    assert.deepEqual(split(list(real), 4), [['dev-script'], ['onprem'], ['ai-eval', 'compose'], ['images']])
  })

  test('keeps every suite in one shard when there is one shard', () => {
    assert.deepEqual(split(list(real), 1), [[...suites].sort()])
  })
})

describe('count', () => {
  test('prints 1 when the five suites, 960 s in all, fit the budget', () => {
    assert.deepEqual(count(list(real), 1000), { total: 1, overBudget: [] })
  })

  test('prints the fewest shards whose longest shard fits a smaller budget', () => {
    // One shard takes 960 s and two take 480 s, while three take at most 360 s.
    assert.deepEqual(count(list(real), 400), { total: 3, overBudget: [] })
  })

  test('prints the cap, and an Over budget line for the suite that alone takes longer', () => {
    assert.deepEqual(count(list(real), 300, 6), {
      total: 6,
      overBudget: ['Over budget: dev-script takes about 360 s, longer than the budget of 300 s.'],
    })
  })
})

describe('readSuites', () => {
  test('weighs a suite without an entry as the median of the known ones', () => {
    withDurations({ 'dev-script': 360, compose: 150, onprem: 180 }, (file) => {
      assert.deepEqual(readSuites(file), [
        { name: 'dev-script', seconds: 360 },
        { name: 'compose', seconds: 150 },
        { name: 'images', seconds: 180 },
        { name: 'onprem', seconds: 180 },
        { name: 'ai-eval', seconds: 180 },
      ])
    })
  })

  test('weighs every suite 1 second when none is known', () => {
    withDurations({}, (file) => {
      assert.deepEqual(
        readSuites(file).map((suite) => suite.seconds),
        [1, 1, 1, 1, 1],
      )
    })
  })
})

describe('junitDurations', () => {
  test('sums only the top-level elements, so a describe block counts once with its nested tests', () => {
    withDurations({}, (_file, dir) => {
      const junit = writeJunit(
        dir,
        'dev-script',
        `<testsuite name="outer" time="2.5" tests="2">
\t<testcase name="inner one" time="1.0" classname="test"/>
\t<testcase name="inner two" time="1.0" classname="test"></testcase>
</testsuite>
<testcase name="top" time="0.4" classname="test"><failure message="boom"/></testcase>`,
      )
      assert.deepEqual(junitDurations([junit]), { 'dev-script': 3 })
    })
  })
})

describe('main', () => {
  test('--count prints only the number, and warns about a suite over the budget', () => {
    const big = budgetSeconds + 100
    withDurations({ ...real, 'dev-script': big }, (file) => {
      const warnings = []
      assert.equal(
        main(['--count'], file, (line) => warnings.push(line)),
        String(maxShards.smoke),
      )
      assert.deepEqual(warnings, [`Over budget: dev-script takes about ${big} s, longer than the budget of ${budgetSeconds} s.`])
    })
  })

  test('--count prints 2 for the real numbers, whose 960 s pass the budget while two shards take 480 s, and no warning', () => {
    withDurations(real, (file) => {
      const warnings = []
      assert.equal(
        main(['--count'], file, (line) => warnings.push(line)),
        '2',
      )
      assert.deepEqual(warnings, [])
    })
  })

  test('K/N prints the suites of shard K, one per line', () => {
    withDurations(real, (file) => {
      assert.equal(main(['1/3'], file), 'dev-script')
      assert.equal(main(['2/3'], file), 'ai-eval\nonprem')
      assert.equal(main(['3/3'], file), 'compose\nimages')
    })
  })

  test('a shard with no suite throws, naming the fault', () => {
    withDurations(real, (file) => {
      assert.throws(() => main(['6/6'], file), /Shard 6\/6 has no smoke suite\. Use fewer shards\./)
    })
  })

  test('refuses a malformed shard', () => {
    withDurations(real, (file) => {
      for (const shard of ['4/3', '0/3', 'abc']) {
        assert.throws(() => main([shard], file), new RegExp(`A shard such as 1/3, not '${shard}'`))
      }
    })
  })

  test('--durations rewrites the suites measured from JUnit files and keeps the others', () => {
    withDurations(real, (file, dir) => {
      const junit = writeJunit(dir, 'dev-script', '<testcase name="top" time="400.2" classname="test"/>')
      assert.equal(main(['--durations', junit], file), `1 suites measured, 5 kept in ${file}.`)
      assert.deepEqual(JSON.parse(readFileSync(file, 'utf8')), {
        compose: 150,
        'dev-script': 400,
        images: 150,
        onprem: 180,
        'ai-eval': 120,
      })
    })
  })
})
