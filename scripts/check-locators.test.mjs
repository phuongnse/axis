import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, before, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { findDialogLookups, findLooseLocators } from './check-locators.mjs'

const script = fileURLToPath(new URL('./check-locators.mjs', import.meta.url))
const repositoryRoot = fileURLToPath(new URL('..', import.meta.url))

let root

before(() => {
  root = mkdtempSync(path.join(tmpdir(), 'check-locators-'))
  mkdirSync(path.join(root, 'tests/e2e/specs'), { recursive: true })
  mkdirSync(path.join(root, 'tests/e2e/node_modules/x'), { recursive: true })
  writeFileSync(
    path.join(root, 'tests/e2e/specs/a.spec.ts'),
    ['test("a", async ({ page }) => {', "  await page.getByRole('button', { name: 'Save' }).click()", '})'].join('\n'),
  )
  writeFileSync(
    path.join(root, 'tests/e2e/node_modules/x/b.ts'),
    "page.getByRole('button', { name: 'Installed' })\n",
  )
  writeFileSync(path.join(root, 'tests/e2e/specs/c.spec.ts'), "await page.getByRole('dialog', { name: 'X', exact: true })\n")
  writeFileSync(path.join(root, 'tests/e2e/specs/d.spec.ts'), 'const dialog = await waitForDialog(page)\n')
  writeFileSync(path.join(root, 'tests/e2e/helper.ts'), "await page.getByRole('dialog', { name: 'X', exact: true })\n")
})

after(() => rmSync(root, { recursive: true, force: true }))

function runCli(args) {
  return spawnSync(process.execPath, [script, ...args], { encoding: 'utf8' })
}

function loose(source) {
  return findLooseLocators(source).map(({ line, method, reason }) => `${line} ${method} ${reason}`)
}

describe('findLooseLocators', () => {
  test('reports a string name without exact: true', () => {
    assert.deepEqual(loose("page.getByRole('button', { name: 'Save' })"), ['1 getByRole name without exact: true'])
    assert.deepEqual(loose("page.getByRole('button', { name: 'Save', exact: true })"), [])
    assert.deepEqual(loose("page.getByRole('button', { name: 'Save', exact: false })"), [
      '1 getByRole name without exact: true',
    ])
    assert.deepEqual(loose("page.getByRole('button', { name: 'Save', exact: flag })"), [
      '1 getByRole name without exact: true',
    ])
  })

  test('reads a multi-line call and reports the line of the method', () => {
    const multiLine = ["await page", "  .getByRole('button', {", "    name: 'Save',", '  })'].join('\n')
    assert.deepEqual(loose(multiLine), ['2 getByRole name without exact: true'])
    const withExact = ["await page", "  .getByRole('button', {", "    name: 'Save',", '    exact: true,', '  })'].join('\n')
    assert.deepEqual(loose(withExact), [])
  })

  test('reads the name shorthand as a string', () => {
    assert.deepEqual(loose('page.getByRole("button", { name })'), ['1 getByRole name without exact: true'])
    assert.deepEqual(loose('page.getByRole("button", { name, exact: true })'), [])
  })

  test('reports a template literal name and passes it with exact: true', () => {
    assert.deepEqual(loose('page.getByRole("row", { name: `Row ${id}` })'), ['1 getByRole name without exact: true'])
    assert.deepEqual(loose('page.getByRole("row", { name: `Row ${id}`, exact: true })'), [])
  })

  test('keeps the commas and parentheses inside a template expression out of the split', () => {
    assert.deepEqual(loose('page.getByRole("row", { name: `Row ${f(a, b)}`, exact: true })'), [])
    assert.deepEqual(loose('page.getByText(`Row ${f(a, b)}`)'), ['1 getByText text without exact: true'])
  })

  test('accepts regular expressions and new RegExp for getByRole and getByText', () => {
    assert.deepEqual(loose('page.getByRole("button", { name: /Save/i })'), [])
    assert.deepEqual(loose("page.getByRole('row', { name: new RegExp(title, 'i') })"), [])
    assert.deepEqual(loose('page.getByText(/Save/i)'), [])
    assert.deepEqual(loose("page.getByText(new RegExp(title, 'i'))"), [])
    assert.deepEqual(loose('page.getByLabel(fieldLabel(label))'), [])
    assert.deepEqual(loose('page.getByLabel(label)'), ['1 getByLabel text without exact: true'])
  })

  test('does not treat a regular expression with a quote or a slash as code', () => {
    assert.deepEqual(loose("const r = /don't/; page.getByRole('button', { name: r })"), [
      '1 getByRole name without exact: true',
    ])
    assert.deepEqual(loose("page.getByRole('button', { name: /a[/']b/ })"), [])
  })

  test('reads a regular expression after an arrow as a pattern', () => {
    assert.deepEqual(loose("page.getByText(items.find((item) => /[)]/.test(item)), { exact: true })"), [])
  })

  test('passes getByRole with no name and reports getByText and getByLabel without exact', () => {
    assert.deepEqual(loose("page.getByRole('row')"), [])
    assert.deepEqual(loose("page.getByRole('row', { exact: true })"), [])
    assert.deepEqual(loose("page.getByText('x')"), ['1 getByText text without exact: true'])
    assert.deepEqual(loose('page.getByLabel(label)'), ['1 getByLabel text without exact: true'])
    assert.deepEqual(loose("page.getByTitle('x', { exact: true })"), [])
    assert.deepEqual(loose("page.getByLabel('x', { exact: false })"), ['1 getByLabel text without exact: true'])
  })

  test('reports an options argument that is not an object literal for getByRole', () => {
    assert.deepEqual(loose("page.getByRole('button', options)"), ['1 getByRole options must be an object literal'])
  })

  test('ignores calls inside strings and comments', () => {
    assert.deepEqual(loose("const s = \"page.getByText('x')\"; // page.getByText('y')"), [])
    assert.deepEqual(loose("/* page.getByRole('button', { name: 'z' }) */ const t = `getByLabel('w')`"), [])
  })

  test('does not match a method of another name or a property access', () => {
    assert.deepEqual(loose("page.getByRoleX('x')"), [])
    assert.deepEqual(loose("page.getByPlaceholder('x')"), [])
  })

  test('reports a call that is not closed', () => {
    assert.deepEqual(loose("page.getByText('x'"), ['1 getByText call is not closed'])
  })
})

describe('findDialogLookups', () => {
  test('finds a dialog looked up with single, double or backtick quotes', () => {
    assert.deepEqual(findDialogLookups("page.getByRole('dialog')"), [1])
    assert.deepEqual(findDialogLookups('page.getByRole("dialog")'), [1])
    assert.deepEqual(findDialogLookups('page.getByRole(`dialog`)'), [1])
  })

  test('finds a dialog looked up through a chain or a name, on the line of the call', () => {
    const chained = ["await page", "  .getByTestId('x')", "  .getByRole('dialog', { name: 'X', exact: true })"].join('\n')
    assert.deepEqual(findDialogLookups(chained), [3])
    assert.deepEqual(findDialogLookups("await page\n  .getByRole(\n    'dialog')"), [2])
  })

  test('ignores other roles, and a dialog named in a string or a comment', () => {
    assert.deepEqual(findDialogLookups("page.getByRole('button', { name: 'dialog', exact: true })"), [])
    assert.deepEqual(findDialogLookups("// page.getByRole('dialog')\nconst s = \"getByRole('dialog')\""), [])
  })
})

describe('CLI', () => {
  test('reports a loose call and a dialog lookup in a spec, with its file and line, and skips installed packages', () => {
    const result = runCli(['--root', root])

    assert.equal(result.status, 1)
    assert.match(result.stderr, /^tests\/e2e\/specs\/a\.spec\.ts:2: getByRole name without exact: true$/m)
    assert.match(
      result.stderr,
      /^tests\/e2e\/specs\/c\.spec\.ts:1: getByRole dialog without waitForDialog or confirmDialog$/m,
    )
    assert.equal(result.stderr.trim().split('\n').length, 2)
    assert.doesNotMatch(result.stderr, /node_modules|Installed/)
  })

  test('does not report a dialog lookup outside the specs folder or a spec that uses waitForDialog', () => {
    const result = runCli(['--root', root])

    assert.doesNotMatch(result.stderr, /helper\.ts|d\.spec\.ts/)
  })

  test('passes on the repository', () => {
    const result = runCli(['--root', repositoryRoot])

    assert.equal(result.stderr, '')
    assert.equal(result.status, 0)
    assert.match(result.stdout, /^Checked \d+ locator calls in \d+ files\.$/m)
  })
})
