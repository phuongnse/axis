import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, before, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { baselineFile, compare, countUses, findUses } from './check-shared-widgets.mjs'

const script = fileURLToPath(new URL('./check-shared-widgets.mjs', import.meta.url))
const repositoryRoot = fileURLToPath(new URL('..', import.meta.url))

let root

before(() => {
  root = mkdtempSync(path.join(tmpdir(), 'check-shared-widgets-'))
  mkdirSync(path.join(root, 'web/src/platform'), { recursive: true })
  mkdirSync(path.join(root, 'scripts'), { recursive: true })
  writeFileSync(
    path.join(root, 'web/src/a.tsx'),
    ['export const A = () => (', '  <Button onClick={f}>Go</Button>', ')'].join('\n'),
  )
  writeFileSync(path.join(root, 'web/src/platform/AxisButton.tsx'), 'export const B = () => <Button type="text" />\n')
  writeFileSync(path.join(root, 'web/src/a.test.tsx'), 'export const T = () => <Button />\n')
})

after(() => rmSync(root, { recursive: true, force: true }))

function runCli(args) {
  return spawnSync(process.execPath, [script, ...args], { encoding: 'utf8' })
}

const uses = (source, file = 'web/src/x.tsx') => findUses(source, file).map(({ line, rule }) => `${line} ${rule}`)

describe('findUses', () => {
  test('reports an Ant Design button, table and preset tag colour', () => {
    assert.deepEqual(uses('<Button type="text">x</Button>'), ['1 button'])
    assert.deepEqual(uses(`import { Table } from 'antd'`), ['1 table'])
    assert.deepEqual(uses('<Tag color="blue">x</Tag>'), ['1 tag'])
  })

  test('passes the shared widgets, other names and a state colour', () => {
    assert.deepEqual(uses('<AxisButton role="main" />'), [])
    assert.deepEqual(uses('<AxisTable columns={c} />'), [])
    assert.deepEqual(uses('<ButtonGroup />'), [])
    assert.deepEqual(uses('<Tag style={{ color: colour.text }}>x</Tag>'), [])
  })

  test('reports each status preset and each -inverse colour name as one tag', () => {
    for (const name of ['success', 'error', 'warning', 'processing', 'default', 'blue-inverse']) {
      assert.deepEqual(uses(`<Tag color="${name}">x</Tag>`), ['1 tag'], name)
    }
  })

  test('reports a preset inside a colour expression once, and passes a colour token', () => {
    assert.deepEqual(uses(`<Tag color={failed ? 'error' : 'success'}>x</Tag>`), ['1 tag'])
    assert.deepEqual(uses(`<Tag color={diagnostic.severity === 'error' ? 'error' : 'warning'}>x</Tag>`), ['1 tag'])
    assert.deepEqual(uses('<Tag color={colour.dangerText}>x</Tag>'), [])
    assert.deepEqual(uses('<Tag color={statusColors[value]}>x</Tag>'), [])
  })

  test('reports a tag whose colour is on a later line at the line of the tag', () => {
    assert.deepEqual(uses(['<Tag', '  key={a}', '  color="warning"', '>x</Tag>'].join('\n')), ['1 tag'])
  })

  test('reports the line where a button starts', () => {
    assert.deepEqual(uses(['<div>', '  <Button type="text"', '    onClick={f}', '  />', '</div>'].join('\n')), ['2 button'])
  })

  test('reports a Table import from antd, also among other names and as an alias', () => {
    assert.deepEqual(uses(`import { Flex, Table, Typography } from "antd"`), ['1 table'])
    assert.deepEqual(uses(`import { Table as AntTable } from 'antd'`), ['1 table'])
    assert.deepEqual(uses(`import { Button, Table, type TableProps } from 'antd'`), ['1 table'])
  })

  test('reports a multi-line antd import at the line of the Table specifier', () => {
    const source = ['import {', '  Button,', '  Table,', '  Tag,', `} from 'antd'`, '', 'export const A = 1'].join('\n')
    assert.deepEqual(uses(source), ['3 table'])
  })

  test('passes the types of the table, the shared table and a Table of another module', () => {
    assert.deepEqual(uses(`import { type TableProps } from 'antd'`), [])
    assert.deepEqual(uses(`import { Flex, type TableColumnsType, type TableColumnType } from 'antd'`), [])
    assert.deepEqual(uses(`import { AxisTable } from './AxisTable'`), [])
    assert.deepEqual(uses(`import { Table } from './Table'`), [])
    assert.deepEqual(uses(`import { Flex } from 'antd'\nimport { Table } from 'other'`), [])
  })

  test('lets the shared widget itself use the Ant Design one', () => {
    assert.deepEqual(uses('<Button />', 'web/src/platform/AxisButton.tsx'), [])
    assert.deepEqual(uses(`import { Table } from 'antd'`, 'web/src/platform/AxisTable.tsx'), [])
  })
})

describe('compare', () => {
  test('fails a use above the baseline and a baseline above the use', () => {
    assert.equal(compare({ 'a.tsx': { button: 2 } }, { 'a.tsx': { button: 1 } }).length, 1)
    assert.equal(compare({ 'a.tsx': { button: 1 } }, { 'a.tsx': { button: 2 } }).length, 1)
    assert.equal(compare({ 'b.tsx': { button: 1 } }, {}).length, 1)
  })

  test('passes the counts the baseline holds', () => {
    assert.deepEqual(compare({ 'a.tsx': { button: 1 } }, { 'a.tsx': { button: 1 } }), [])
  })

  test('fails a table use that the baseline allows, because the table rule has no baseline', () => {
    const problems = compare({ 'a.tsx': { table: 1 } }, { 'a.tsx': { table: 1 } })
    assert.equal(problems.length, 1)
    assert.match(problems[0].message, /1 use\(s\) of the table rule, 0 allowed/)
  })
})

describe('countUses', () => {
  test('counts the files of web/src, not the tests and not the shared widget', () => {
    assert.deepEqual(countUses(root), { 'web/src/a.tsx': { button: 1 } })
  })
})

describe('command line', () => {
  test('exits 1 on a use that the baseline does not hold', () => {
    const result = runCli(['--root', root])
    assert.equal(result.status, 1)
    assert.match(result.stderr, /^web\/src\/a\.tsx: 1 use\(s\) of the button rule, 0 allowed\. Use AxisButton/)
  })

  test('writes the baseline, then passes', () => {
    assert.equal(runCli(['--root', root, '--write-baseline']).status, 0)
    assert.equal(runCli(['--root', root]).status, 0)
  })

  test('exits 1 on a Table import from antd that the baseline allows', () => {
    const strictRoot = mkdtempSync(path.join(tmpdir(), 'check-shared-widgets-strict-'))
    try {
      mkdirSync(path.join(strictRoot, 'web/src'), { recursive: true })
      mkdirSync(path.join(strictRoot, 'scripts'), { recursive: true })
      writeFileSync(path.join(strictRoot, 'web/src/t.tsx'), `import { Table } from 'antd'\nexport const T = Table\n`)
      writeFileSync(path.join(strictRoot, baselineFile), JSON.stringify({ 'web/src/t.tsx': { table: 1 } }))
      const result = runCli(['--root', strictRoot])
      assert.equal(result.status, 1)
      assert.match(result.stderr, /^web\/src\/t\.tsx: 1 use\(s\) of the table rule, 0 allowed\. Import Table from antd only in AxisTable\.tsx/)
    } finally {
      rmSync(strictRoot, { recursive: true, force: true })
    }
  })

  test('leaves the table rule out of the baseline it writes', () => {
    const strictRoot = mkdtempSync(path.join(tmpdir(), 'check-shared-widgets-write-'))
    try {
      mkdirSync(path.join(strictRoot, 'web/src'), { recursive: true })
      mkdirSync(path.join(strictRoot, 'scripts'), { recursive: true })
      writeFileSync(path.join(strictRoot, 'web/src/t.tsx'), `import { Table } from 'antd'\nexport const T = Table\n`)
      assert.equal(runCli(['--root', strictRoot, '--write-baseline']).status, 0)
      assert.equal(readFileSync(path.join(strictRoot, baselineFile), 'utf8').trim(), '{}')
    } finally {
      rmSync(strictRoot, { recursive: true, force: true })
    }
  })

  test('passes on the SPA of this repository', () => {
    const result = runCli(['--root', repositoryRoot])
    assert.equal(result.status, 0, result.stderr)
    assert.ok(baselineFile.endsWith('.json'))
  })
})
