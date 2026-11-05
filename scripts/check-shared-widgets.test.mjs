import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
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
    assert.deepEqual(uses('<Table<Row> rowKey="id" />'), ['1 table'])
    assert.deepEqual(uses('<Tag color="blue">x</Tag>'), ['1 tag'])
  })

  test('passes the shared widgets, other names and a state colour', () => {
    assert.deepEqual(uses('<AxisButton role="main" />'), [])
    assert.deepEqual(uses('<AxisTable columns={c} />'), [])
    assert.deepEqual(uses('<ButtonGroup />'), [])
    assert.deepEqual(uses('<TableFilter />'), [])
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

  test('reports the line where a button and a table start', () => {
    assert.deepEqual(uses(['<div>', '  <Button type="text"', '    onClick={f}', '  />', '</div>'].join('\n')), ['2 button'])
    assert.deepEqual(uses(['<div>', '  <Table<Row> rowKey="id"', '  />', '</div>'].join('\n')), ['2 table'])
  })

  test('lets the shared widget itself use the Ant Design one', () => {
    assert.deepEqual(uses('<Button />', 'web/src/platform/AxisButton.tsx'), [])
    assert.deepEqual(uses('<Table />', 'web/src/platform/AxisTable.tsx'), [])
  })
})

describe('compare', () => {
  test('fails a use above the baseline and a baseline above the use', () => {
    assert.equal(compare({ 'a.tsx': { button: 2 } }, { 'a.tsx': { button: 1 } }).length, 1)
    assert.equal(compare({ 'a.tsx': { button: 1 } }, { 'a.tsx': { button: 2 } }).length, 1)
    assert.equal(compare({ 'b.tsx': { table: 1 } }, {}).length, 1)
  })

  test('passes the counts the baseline holds', () => {
    assert.deepEqual(compare({ 'a.tsx': { button: 1 } }, { 'a.tsx': { button: 1 } }), [])
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

  test('passes on the SPA of this repository', () => {
    const result = runCli(['--root', repositoryRoot])
    assert.equal(result.status, 0, result.stderr)
    assert.ok(baselineFile.endsWith('.json'))
  })
})
