import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { describe, test } from 'node:test'
import {
  controlPlane,
  count,
  filterArgs,
  junitDurations,
  main,
  mergeDurations,
  parseSource,
  readClasses,
  split,
  weigh,
} from './lib/integration-shard.mjs'

const testClass = (name, seconds, collection) => ({ name: `N.${name}`, seconds, collection, tests: 1 })

describe('count', () => {
  const four = [testClass('ATests', 10), testClass('BTests', 10), testClass('CTests', 10), testClass('DTests', 10)]

  test('prints the fewest shards whose longest shard fits the budget', () => {
    // Four 10 s classes take 40 s in one shard and 20 s in each of two.
    assert.equal(count(four, 0, 25, 6).total, 2)
    assert.equal(count(four, 0, 45, 6).total, 1)
  })

  test('prints the cap when no count up to the cap fits the budget', () => {
    assert.equal(count(four, 0, 5, 3).total, 3)
  })

  test('names a class that alone takes longer than the budget, and still counts', () => {
    const classes = [testClass('HeavyTests', 30), testClass('LightTests', 5)]
    assert.deepEqual(count(classes, 0, 20, 6), {
      total: 6,
      overBudget: ['Over budget: N.HeavyTests takes about 30 s, longer than the budget of 20 s.'],
    })
  })

  test('names a collection that alone takes longer than the budget by its classes', () => {
    const classes = [testClass('ATests', 15, 'Scratch'), testClass('ZTests', 15, 'Scratch')]
    assert.deepEqual(count(classes, 0, 20, 6), {
      total: 6,
      overBudget: ['Over budget: collection Scratch (N.ATests, N.ZTests) takes about 30 s, longer than the budget of 20 s.'],
    })
  })

  test('names the control plane suite when its seconds alone take longer than the budget', () => {
    assert.deepEqual(count([testClass('PTests', 5)], 25, 20, 6), {
      total: 6,
      overBudget: [`Over budget: ${controlPlane} takes about 25 s, longer than the budget of 20 s.`],
    })
  })
})

describe('parseSource', () => {
  test('finds the test classes of a file with their namespace, collection and number of tests', () => {
    const source = `namespace Axis.Integration.Tests;

[Collection(AdminCellRestoreCheckTests.ScratchTenantCollection)]
public sealed class RemovalTests(Fixture fixture)
{
    [Fact]
    public async Task One() { }

    [Theory]
    [InlineData(1)]
    public void Two(int value) { }
}

internal sealed class Helper { }
`
    assert.deepEqual(parseSource(source), [
      { name: 'Axis.Integration.Tests.RemovalTests', collection: 'ScratchTenantCollection', tests: 2 },
    ])
  })

  test('finds nothing in a file without tests', () => {
    assert.deepEqual(parseSource('namespace N;\npublic sealed class FixtureTests { }\n'), [])
  })
})

describe('split', () => {
  test('puts every class in exactly one shard, the same way on every call', () => {
    const classes = Array.from({ length: 20 }, (_, index) => testClass(`C${index}Tests`, (index * 7) % 13))
    const shards = split(classes, 3)

    assert.deepEqual(shards.flat().sort(), classes.map((c) => c.name).sort())
    assert.deepEqual(split([...classes].reverse(), 3), shards)
  })

  test('gives the heaviest class a shard of its own and balances the rest', () => {
    const classes = [testClass('ScenarioRunnerTests', 300), ...Array.from({ length: 30 }, (_, i) => testClass(`P${i}Tests`, 20))]
    const shards = split(classes, 3)

    assert.deepEqual(shards[0], ['N.ScenarioRunnerTests'])
    assert.deepEqual(shards.slice(1).map((shard) => shard.length), [15, 15])
  })

  test('gives shard 1 fewer classes when it starts with the control plane seconds', () => {
    const classes = Array.from({ length: 30 }, (_, i) => testClass(`P${i}Tests`, 20))
    const shards = split(classes, 3, 200)
    const seconds = shards.map((shard, index) => shard.length * 20 + (index === 0 ? 200 : 0))

    assert.ok(shards[0].length < shards[1].length && shards[0].length < shards[2].length, JSON.stringify(shards))
    assert.ok(Math.max(...seconds) - Math.min(...seconds) <= 20, JSON.stringify(seconds))
  })

  test('keeps the classes of one collection in one shard', () => {
    const classes = [
      testClass('ATests', 10, 'Scratch'),
      testClass('BTests', 10),
      testClass('CTests', 10),
      testClass('ZTests', 10, 'Scratch'),
    ]
    const shards = split(classes, 2)

    assert.ok(shards.some((shard) => shard.includes('N.ATests') && shard.includes('N.ZTests')), JSON.stringify(shards))
  })
})

describe('weigh', () => {
  test('uses measured seconds, else the tests times the median seconds per test', () => {
    const classes = [
      { name: 'A', tests: 2 },
      { name: 'B', tests: 4 },
      { name: 'C', tests: 3 },
    ]
    const weighed = weigh(classes, { A: 10, B: 40 })

    // A takes 5 s per test and B 10 s, so C weighs 3 × 7.5 s.
    assert.deepEqual(
      weighed.map((c) => c.seconds),
      [10, 40, 22.5],
    )
  })

  test('weighs each test one second when nothing was measured', () => {
    assert.deepEqual(
      weigh([{ name: 'A', tests: 3 }], {}).map((c) => c.seconds),
      [3],
    )
  })
})

describe('filterArgs', () => {
  const shards = [['N.ATests'], ['N.BTests', 'N.CTests'], ['N.DTests']]

  test('runs its own classes in every shard but the last', () => {
    assert.deepEqual(filterArgs(shards, 2), ['--filter-class', 'N.BTests', '--filter-class', 'N.CTests'])
  })

  test('runs every class but the other shards’ and the performance tests in the last shard', () => {
    assert.deepEqual(filterArgs(shards, 3), [
      '--filter-not-class',
      '*PerformanceTests',
      '--filter-not-class',
      'N.ATests',
      '--filter-not-class',
      'N.BTests',
      '--filter-not-class',
      'N.CTests',
    ])
  })

  test('refuses a shard without a class, whose empty filter would run every test', () => {
    assert.throws(() => filterArgs([['N.ATests'], []], 2), /Shard 2\/2 has no test class/)
  })
})

describe('durations', () => {
  test('sums the seconds of each class in JUnit files', () => {
    const junit = `<testsuites><testsuite name="t" tests="3">
<testcase classname="N.ATests" name="one" time="1.4" />
<testcase classname="N.ATests" name="two" time="2.2"></testcase>
<testcase classname="N.BTests" name="three" time="5" />
</testsuite></testsuites>`
    assert.deepEqual(junitDurations([junit]), { 'N.ATests': 4, 'N.BTests': 5 })
  })

  test('folds the control plane classes into one entry', () => {
    const junit = `<testsuites><testsuite name="t" tests="3">
<testcase classname="Axis.ControlPlane.Integration.Tests.XTests" name="one" time="1.4" />
<testcase classname="Axis.ControlPlane.Integration.Tests.YTests" name="two" time="2.2" />
<testcase classname="Axis.ControlPlane.Integration.Tests.YTests" name="three" time="5" />
</testsuite></testsuites>`
    assert.deepEqual(junitDurations([junit]), { 'Axis.ControlPlane.Integration.Tests': 9 })
  })

  test('keeps known classes that were not measured and drops classes that no longer exist', () => {
    assert.deepEqual(mergeDurations({ A: 1, B: 2, Gone: 3 }, { A: 10 }, ['A', 'B']), { A: 10, B: 2 })
  })
})

describe('main', () => {
  const project = () => {
    const dir = mkdtempSync(path.join(tmpdir(), 'integration-shard-'))
    const write = (file, name, tests) =>
      writeFileSync(
        path.join(dir, file),
        `namespace N;\npublic sealed class ${name}\n{\n${'    [Fact]\n    public void T() { }\n'.repeat(tests)}}\n`,
      )
    write('ATests.cs', 'ATests', 3)
    write('BTests.cs', 'BTests', 1)
    write('SearchPerformanceTests.cs', 'SearchPerformanceTests', 5)
    mkdirSync(path.join(dir, 'obj'))
    write('obj/CopyTests.cs', 'CopyTests', 9)
    writeFileSync(path.join(dir, 'class-durations.json'), '{}\n')
    return dir
  }

  test('leaves out the performance tests and the build output', () => {
    const dir = project()
    try {
      assert.deepEqual(
        readClasses(dir).map((c) => c.name),
        ['N.ATests', 'N.BTests'],
      )
      assert.equal(main(['--classes', '1/2'], dir), 'N.ATests')
      assert.equal(main(['1/2'], dir), '--filter-class\nN.ATests')
      assert.equal(main(['2/2'], dir), '--filter-not-class\n*PerformanceTests\n--filter-not-class\nN.ATests')
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })

  test('--count prints 1 when the suite fits the budget, and no warning', () => {
    const dir = project()
    try {
      const warnings = []
      assert.equal(main(['--count'], dir, (line) => warnings.push(line)), '1')
      assert.deepEqual(warnings, [])
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })

  test('--count prints the count alone and warns about each class over the budget', () => {
    const dir = project()
    try {
      writeFileSync(path.join(dir, 'class-durations.json'), '{ "N.ATests": 1000, "N.BTests": 5 }\n')
      const warnings = []
      assert.equal(main(['--count'], dir, (line) => warnings.push(line)), '6')
      assert.deepEqual(warnings, ['Over budget: N.ATests takes about 1000 s, longer than the budget of 900 s.'])
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })

  test('refuses a malformed shard', () => {
    assert.throws(() => main(['4/3']), /A shard such as 1\/3, not '4\/3'/)
    assert.throws(() => main(['--classes', 'x']), /A shard such as 1\/3/)
  })

  test('rewrites the durations from JUnit files', () => {
    const dir = project()
    try {
      const junit = path.join(dir, 'Axis.Integration.Tests.xml')
      writeFileSync(
        junit,
        '<testcase classname="N.ATests" name="t" time="1" /><testcase classname="N.BTests" name="t" time="7.6" />',
      )
      main(['--durations', junit], dir)
      assert.deepEqual(JSON.parse(readFileSync(path.join(dir, 'class-durations.json'), 'utf8')), {
        'N.ATests': 1,
        'N.BTests': 8,
      })
      assert.equal(main(['--classes', '1/2'], dir), 'N.BTests')
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })

  test('starts shard 1 with the control plane seconds from the durations', () => {
    const dir = project()
    try {
      writeFileSync(path.join(dir, 'class-durations.json'), '{ "N.ATests": 10, "N.BTests": 5, "Axis.ControlPlane.Integration.Tests": 8 }\n')
      assert.equal(main(['--classes', '1/2'], dir), 'N.BTests')
      writeFileSync(path.join(dir, 'class-durations.json'), '{ "N.ATests": 10, "N.BTests": 5 }\n')
      assert.equal(main(['--classes', '1/2'], dir), 'N.ATests')
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })

  test('keeps the control plane entry when the durations are rewritten', () => {
    const dir = project()
    try {
      const junit = path.join(dir, 'Axis.ControlPlane.Integration.Tests.xml')
      writeFileSync(junit, '<testcase classname="Axis.ControlPlane.Integration.Tests.XTests" name="t" time="8.4" />')
      main(['--durations', junit], dir)
      assert.deepEqual(JSON.parse(readFileSync(path.join(dir, 'class-durations.json'), 'utf8')), {
        'Axis.ControlPlane.Integration.Tests': 8,
      })
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })

  test('splits the real test project into three shards that each hold classes', () => {
    for (const shard of ['1/3', '2/3', '3/3']) assert.notEqual(main(['--classes', shard]), '')
  })
})
