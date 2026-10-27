import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, before, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { checkRawHtml, exceptions, findRawHtml } from './check-raw-html.mjs'

const script = fileURLToPath(new URL('./check-raw-html.mjs', import.meta.url))
const repositoryRoot = fileURLToPath(new URL('..', import.meta.url))

let dirtyRoot
let cleanRoot

before(() => {
  dirtyRoot = mkdtempSync(path.join(tmpdir(), 'check-raw-html-'))
  mkdirSync(path.join(dirtyRoot, 'web/src'), { recursive: true })
  mkdirSync(path.join(dirtyRoot, 'web/node_modules/x'), { recursive: true })
  writeFileSync(
    path.join(dirtyRoot, 'web/src/a.tsx'),
    ['export const A = ({ html }) => (', '  <div dangerouslySetInnerHTML={{ __html: html }} />', ')'].join('\n'),
  )
  writeFileSync(path.join(dirtyRoot, 'web/node_modules/x/b.js'), 'el.innerHTML = value\n')

  cleanRoot = mkdtempSync(path.join(tmpdir(), 'check-raw-html-'))
  mkdirSync(path.join(cleanRoot, 'web/src'), { recursive: true })
  mkdirSync(path.join(cleanRoot, 'web/node_modules/x'), { recursive: true })
  writeFileSync(path.join(cleanRoot, 'web/src/a.tsx'), 'export const A = ({ text }) => <div>{text}</div>\n')
  writeFileSync(path.join(cleanRoot, 'web/node_modules/x/b.js'), 'el.innerHTML = value\n')
})

after(() => {
  rmSync(dirtyRoot, { recursive: true, force: true })
  rmSync(cleanRoot, { recursive: true, force: true })
})

function runCli(args) {
  return spawnSync(process.execPath, [script, ...args], { encoding: 'utf8' })
}

function names(source) {
  return findRawHtml(source).map(({ line, name }) => `${line} ${name}`)
}

describe('findRawHtml', () => {
  test('reports each way to write raw HTML', () => {
    assert.deepEqual(names('<div dangerouslySetInnerHTML={{ __html: x }} />'), ['1 dangerouslySetInnerHTML'])
    assert.deepEqual(names('el.innerHTML = x'), ['1 innerHTML'])
    assert.deepEqual(names('el.outerHTML = x'), ['1 outerHTML'])
    assert.deepEqual(names("el.insertAdjacentHTML('beforeend', x)"), ['1 insertAdjacentHTML'])
    assert.deepEqual(names("document.execCommand('insertHTML', false, x)"), ['1 insertHTML'])
    assert.deepEqual(names('range.createContextualFragment(x)'), ['1 createContextualFragment'])
    assert.deepEqual(names('el.setHTMLUnsafe(x)'), ['1 setHTMLUnsafe'])
    assert.deepEqual(names('Document.parseHTMLUnsafe(x)'), ['1 parseHTMLUnsafe'])
    assert.deepEqual(names("new DOMParser().parseFromString(x, 'text/html')"), ['1 DOMParser'])
    assert.deepEqual(names('<iframe srcDoc={x} />'), ['1 srcDoc'])
    assert.deepEqual(names('document.write(x)'), ['1 document.write'])
    assert.deepEqual(names('document . writeln(x)'), ['1 document.writeln'])
  })

  test('reports a name written as a string key', () => {
    assert.deepEqual(names("el['innerHTML'] = x"), ['1 innerHTML'])
  })

  test('reports the line of each use', () => {
    assert.deepEqual(names(['const a = 1', 'el.innerHTML = a', '', 'el.outerHTML = a'].join('\n')), [
      '2 innerHTML',
      '4 outerHTML',
    ])
  })

  test('passes code that shows a value as text', () => {
    assert.deepEqual(names('el.textContent = x'), [])
    assert.deepEqual(names('const node = document.createElement("div")'), [])
    assert.deepEqual(names('document.writeValue(x)'), [])
    assert.deepEqual(names('<div>{text}</div>'), [])
  })
})

describe('checkRawHtml', () => {
  test('names the file and line of a file that writes raw HTML and skips node_modules', () => {
    const { problems, fileCount } = checkRawHtml(dirtyRoot, [])
    assert.deepEqual(problems, [{ file: 'web/src/a.tsx', line: 2, name: 'dangerouslySetInnerHTML' }])
    assert.equal(fileCount, 1)
  })

  test('skips a file that is allowed', () => {
    const allowed = [{ file: 'web/src/a.tsx', reason: 'a test' }]
    assert.deepEqual(checkRawHtml(dirtyRoot, allowed).problems, [])
  })

  test('keeps every exception named with its reason', () => {
    for (const exception of exceptions) {
      assert.match(exception.file, /^web\//)
      assert.ok(exception.reason.length > 0)
    }
  })
})

describe('command line', () => {
  test('exits 1 and names the file and line of a file that writes raw HTML', () => {
    const result = runCli(['--root', dirtyRoot])
    assert.equal(result.status, 1)
    assert.match(result.stderr, /^web\/src\/a\.tsx:2: dangerouslySetInnerHTML writes raw HTML\./)
  })

  test('exits 0 on a root that does not write raw HTML', () => {
    const result = runCli(['--root', cleanRoot])
    assert.equal(result.status, 0)
    assert.match(result.stdout, /Checked 1 files\./)
  })

  test('passes on the SPA of this repository', () => {
    const result = runCli(['--root', repositoryRoot])
    assert.equal(result.status, 0, result.stderr)
  })
})
