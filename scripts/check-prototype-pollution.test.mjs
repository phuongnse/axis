import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, before, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { checkPrototypePollution, exceptions, findPrototypePollution } from './check-prototype-pollution.mjs'

const script = fileURLToPath(new URL('./check-prototype-pollution.mjs', import.meta.url))
const repositoryRoot = fileURLToPath(new URL('..', import.meta.url))

let dirtyRoot
let cleanRoot

before(() => {
  dirtyRoot = mkdtempSync(path.join(tmpdir(), 'check-prototype-pollution-'))
  mkdirSync(path.join(dirtyRoot, 'web/src/test'), { recursive: true })
  mkdirSync(path.join(dirtyRoot, 'web/node_modules/x'), { recursive: true })
  writeFileSync(
    path.join(dirtyRoot, 'web/src/a.ts'),
    ['export function put(target, key, value) {', '  target[key] = value', '}'].join('\n'),
  )
  writeFileSync(path.join(dirtyRoot, 'web/src/a.test.ts'), 'target[key] = value\n')
  writeFileSync(path.join(dirtyRoot, 'web/src/test/setup.ts'), 'Range.prototype.getClientRects = () => []\n')
  writeFileSync(path.join(dirtyRoot, 'web/node_modules/x/b.js'), 'target[key] = value\n')

  cleanRoot = mkdtempSync(path.join(tmpdir(), 'check-prototype-pollution-'))
  mkdirSync(path.join(cleanRoot, 'web/src'), { recursive: true })
  writeFileSync(path.join(cleanRoot, 'web/src/a.ts'), 'export const put = (key, value) => ({ [key]: value })\n')
})

after(() => {
  rmSync(dirtyRoot, { recursive: true, force: true })
  rmSync(cleanRoot, { recursive: true, force: true })
})

function runCli(args) {
  return spawnSync(process.execPath, [script, ...args], { encoding: 'utf8' })
}

function names(source) {
  return findPrototypePollution(source).map(({ line, name }) => `${line} ${name}`)
}

describe('findPrototypePollution', () => {
  test('reports each refused pattern', () => {
    assert.deepEqual(names("const key = '__proto__'"), ['1 __proto__'])
    assert.deepEqual(names('value.__proto__.polluted = 1'), ['1 __proto__'])
    assert.deepEqual(names('Foo.prototype.bar = baz'), ['1 prototype write'])
    assert.deepEqual(names('Foo.prototype.bar ??= baz'), ['1 prototype write'])
    assert.deepEqual(names('Foo.prototype[name] = baz'), ['1 prototype write'])
    assert.deepEqual(names("Object.defineProperty(Foo.prototype, 'bar', {})"), ['1 defineProperty on a prototype'])
    assert.deepEqual(names('Object.setPrototypeOf(target, source)'), ['1 setPrototypeOf'])
    assert.deepEqual(names('Object.assign(target, source)'), ['1 Object.assign'])
    assert.deepEqual(names('target[key] = value'), ['1 computed key write'])
    assert.deepEqual(names('target[key] ??= value'), ['1 computed key write'])
    assert.deepEqual(names('target[key] ||= value'), ['1 computed key write'])
    assert.deepEqual(names('target.items[row.id] = value'), ['1 computed key write'])
    assert.deepEqual(names('lookup()[key] = value'), ['1 computed key write'])
    assert.deepEqual(names('grid[row][column] = value'), ['1 computed key write'])
    assert.deepEqual(names('target[keys[0]] = value'), ['1 computed key write'])
    assert.deepEqual(names('target[`${a}`] = value'), ['1 computed key write'])
  })

  test('reports a write into a prototype once', () => {
    assert.deepEqual(names('Foo.prototype[name] = baz'), ['1 prototype write'])
  })

  test('reports the line of each use', () => {
    assert.deepEqual(names(['const a = 1', 'a[key] = 2', '', 'Object.assign(a, b)'].join('\n')), [
      '2 computed key write',
      '4 Object.assign',
    ])
  })

  test('passes a write to a number or string literal key', () => {
    assert.deepEqual(names('bytes[6] = x'), [])
    assert.deepEqual(names('bytes[ 15 ] ??= x'), [])
    assert.deepEqual(names("target['name'] = x"), [])
    assert.deepEqual(names("const run = (steps: Steps['items'] = []) => steps"), [])
  })

  test('passes spread and computed keys in an object literal', () => {
    assert.deepEqual(names('const next = { ...current, [key]: value }'), [])
    assert.deepEqual(names('const next = { [key]: value }'), [])
    assert.deepEqual(names('const copy = { ...a, ...b }'), [])
  })

  test('passes destructuring, reads and comparisons', () => {
    assert.deepEqual(names('const [x, setX] = useState()'), [])
    assert.deepEqual(names('const [a, b] = pair'), [])
    assert.deepEqual(names('let [a, b] = pair'), [])
    assert.deepEqual(names('for (const [key, value] of entries) {}'), [])
    assert.deepEqual(names('const value = target[key]'), [])
    assert.deepEqual(names('if (target[key] === value) {}'), [])
    assert.deepEqual(names('if (target[key] == value) {}'), [])
    assert.deepEqual(names('const read = (key) => target[key]'), [])
    assert.deepEqual(names('const values: string[] = []'), [])
    assert.deepEqual(names('const map = new Map(); map.set(key, value)'), [])
    assert.deepEqual(names('const x = items.map((item) => [item.id]) '), [])
  })

  test('passes code that leaves a prototype alone', () => {
    assert.deepEqual(names('const proto = Foo.prototype'), [])
    assert.deepEqual(names('if (Foo.prototype.bar === baz) {}'), [])
    assert.deepEqual(names('Object.fromEntries(entries)'), [])
    assert.deepEqual(names('Object.keys(target)'), [])
  })
})

describe('checkPrototypePollution', () => {
  test('names the file and line of a file that is open to pollution and skips tests and node_modules', () => {
    const { problems, fileCount } = checkPrototypePollution(dirtyRoot, [])
    assert.deepEqual(problems, [{ file: 'web/src/a.ts', line: 2, name: 'computed key write' }])
    assert.equal(fileCount, 1)
  })

  test('skips a file that is allowed', () => {
    const allowed = [{ file: 'web/src/a.ts', reason: 'a test' }]
    assert.deepEqual(checkPrototypePollution(dirtyRoot, allowed).problems, [])
  })

  test('keeps every exception named with its reason', () => {
    assert.ok(exceptions.length > 0)
    for (const exception of exceptions) {
      assert.match(exception.file, /^web\/src\//)
      assert.ok(exception.reason.length > 0)
    }
  })
})

describe('command line', () => {
  test('exits 1 and names the file and line of a file that is open to pollution', () => {
    const result = runCli(['--root', dirtyRoot])
    assert.equal(result.status, 1)
    assert.match(result.stderr, /^web\/src\/a\.ts:2: computed key write is open to prototype pollution\./)
  })

  test('exits 0 on a root that is not open to pollution', () => {
    const result = runCli(['--root', cleanRoot])
    assert.equal(result.status, 0)
    assert.match(result.stdout, /Checked 1 files\./)
  })

  test('passes on the SPA of this repository', () => {
    const result = runCli(['--root', repositoryRoot])
    assert.equal(result.status, 0, result.stderr)
  })
})
