// Lints fixture files with the real oxlint binary, so the tests cover the plugin's loading and the disable comments as well as its rules.
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { afterAll, describe, expect, it } from 'vitest'

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const oxlint = path.join(webRoot, 'node_modules/.bin/oxlint')
const plugin = path.join(webRoot, 'lint/axis-plugin.mjs')
const folder = mkdtempSync(path.join(tmpdir(), 'axis-plugin-'))
let count = 0

afterAll(() => rmSync(folder, { recursive: true, force: true }))

/** Lints the source as a fixture and returns the reports as `rule:line`. */
function lint(source, { rules, config, extension = 'tsx', args = [] }) {
  const id = count++
  const file = path.join(folder, `fixture-${id}.${extension}`)
  const configFile = path.join(folder, `config-${id}.json`)
  writeFileSync(file, source)
  writeFileSync(
    configFile,
    JSON.stringify({ ...(config ?? { jsPlugins: [plugin], rules }), categories: { correctness: 'off' } }),
  )
  const result = spawnSync(oxlint, ['-c', configFile, '--format', 'json', ...args, file], { encoding: 'utf8' })
  const diagnostics = JSON.parse(result.stdout).diagnostics
  return diagnostics.map((diagnostic) => `${diagnostic.code}:${diagnostic.labels[0].span.line}`)
}

describe('axis/no-srcdoc', () => {
  const rules = { 'axis/no-srcdoc': 'error' }

  it('reports an iframe srcdoc', () => {
    expect(lint('const a = <iframe srcDoc={html} />\n', { rules })).toEqual(['axis(no-srcdoc):1'])
  })

  it('allows an iframe src', () => {
    expect(lint('const a = <iframe src={url} />\n', { rules })).toEqual([])
  })

  it('does not report a comment or a string that names srcdoc', () => {
    expect(lint('// <iframe srcDoc={html} />\nconst a = "srcDoc"\n', { rules })).toEqual([])
  })
})

describe('axis/no-computed-key-write', () => {
  const rules = { 'axis/no-computed-key-write': 'error' }

  it('reports a write to a computed key, with any assignment operator', () => {
    const source = 'obj[key] = 1\nobj[key] ??= 1\nobj[key] ||= 1\nobj[a.b] += 1\n'
    expect(lint(source, { rules, extension: 'ts' })).toEqual([
      'axis(no-computed-key-write):1',
      'axis(no-computed-key-write):2',
      'axis(no-computed-key-write):3',
      'axis(no-computed-key-write):4',
    ])
  })

  it('allows a literal key, an object literal, a spread, a destructuring and a read', () => {
    const source =
      "obj['a'] = 1\nobj[0] = 1\nconst b = { [key]: 1, ...c }\nconst { [key]: d } = e\nconst f = obj[key]\n"
    expect(lint(source, { rules, extension: 'ts' })).toEqual([])
  })

  it('does not report a write into a prototype, which no-prototype-write reports', () => {
    expect(lint('Foo.prototype[name] = 1\n', { rules, extension: 'ts' })).toEqual([])
  })

  it('does not report a comment that names a computed write', () => {
    expect(lint('// obj[key] = value\nconst a = "obj[key] = value"\n', { rules, extension: 'ts' })).toEqual([])
  })
})

describe('axis/no-prototype-write', () => {
  const rules = { 'axis/no-prototype-write': 'error' }

  it('reports a write into a prototype, a defineProperty on one and a __proto__ key', () => {
    const source = [
      'Foo.prototype.bar = 1',
      'Foo.prototype[name] = 1',
      "Object.defineProperty(Foo.prototype, 'bar', {})",
      "Reflect.defineProperty(a.b.prototype, 'bar', {})",
      'const a = { __proto__: null }',
      "const b = { '__proto__': null }",
      '',
    ].join('\n')
    expect(lint(source, { rules, extension: 'ts' })).toEqual([
      'axis(no-prototype-write):1',
      'axis(no-prototype-write):2',
      'axis(no-prototype-write):3',
      'axis(no-prototype-write):4',
      'axis(no-prototype-write):5',
      'axis(no-prototype-write):6',
    ])
  })

  it('allows a read of a prototype, a defineProperty on an instance and a computed key', () => {
    const source = "const a = Foo.prototype.bar\nObject.defineProperty(foo, 'bar', {})\nconst b = { [key]: 1 }\n"
    expect(lint(source, { rules, extension: 'ts' })).toEqual([])
  })

  it('does not report a comment that names a prototype write', () => {
    expect(lint('// Foo.prototype.bar = 1 and { __proto__: null }\n', { rules, extension: 'ts' })).toEqual([])
  })
})

describe('axis/no-preset-tag-colour', () => {
  const rules = { 'axis/no-preset-tag-colour': 'error' }

  it('reports a preset colour as a string, in an expression and in a condition', () => {
    const source = [
      'const a = <Tag color="blue">a</Tag>',
      "const b = <Tag color={'success'}>b</Tag>",
      "const c = <Tag color={on ? 'red-inverse' : colour.text}>c</Tag>",
      '',
    ].join('\n')
    expect(lint(source, { rules })).toEqual([
      'axis(no-preset-tag-colour):1',
      'axis(no-preset-tag-colour):2',
      'axis(no-preset-tag-colour):3',
    ])
  })

  it('allows a token, a lookup and a hex colour', () => {
    const source = [
      'const a = <Tag color={colour.dangerText}>a</Tag>',
      'const b = <Tag color={map[value]}>b</Tag>',
      'const c = <Tag color="#ff0000">c</Tag>',
      'const d = <Tag>d</Tag>',
      'const e = <Badge color="blue" />',
      '',
    ].join('\n')
    expect(lint(source, { rules })).toEqual([])
  })

  it('does not report a comment that names a preset tag', () => {
    expect(lint('// <Tag color="blue">\nconst a = \'<Tag color="blue">\'\n', { rules })).toEqual([])
  })
})

describe('axis/no-antd-button', () => {
  const rules = { 'axis/no-antd-button': 'error' }

  it('reports each Button imported from antd, also under another name', () => {
    const source = [
      "import { Button, Button as Plain } from 'antd'",
      'const a = <Button>a</Button>',
      'const b = (',
      '  <Plain',
      '    type="link"',
      '  />',
      ')',
      '',
    ].join('\n')
    expect(lint(source, { rules })).toEqual(['axis(no-antd-button):2', 'axis(no-antd-button):4'])
  })

  it('allows AxisButton, ButtonGroup and a Button from elsewhere', () => {
    const source = [
      "import { Button } from './elsewhere'",
      "import { Space } from 'antd'",
      'const a = <Button>a</Button>',
      'const b = <AxisButton role="primary">b</AxisButton>',
      'const c = <ButtonGroup />',
      '',
    ].join('\n')
    expect(lint(source, { rules })).toEqual([])
  })

  it('does not report a comment that names a Button', () => {
    const source = "import { Button } from 'antd'\n// <Button>a</Button>\nconst a = '<Button>'\n"
    expect(lint(source, { rules })).toEqual([])
  })
})

describe('axis/no-antd-alert', () => {
  const rules = { 'axis/no-antd-alert': 'error' }

  it('reports each Alert imported from antd, also under another name', () => {
    const source = [
      "import { Alert, Alert as Note } from 'antd'",
      'const a = <Alert title="a" />',
      'const b = (',
      '  <Note',
      '    type="info"',
      '  />',
      ')',
      '',
    ].join('\n')
    expect(lint(source, { rules })).toEqual(['axis(no-antd-alert):2', 'axis(no-antd-alert):4'])
  })

  it('allows AxisAlert, Space and an Alert from elsewhere', () => {
    const source = [
      "import { Alert } from './elsewhere'",
      "import { Space } from 'antd'",
      'const a = <Alert title="a" />',
      'const b = <AxisAlert kind="info" message="b" />',
      'const c = <Space />',
      '',
    ].join('\n')
    expect(lint(source, { rules })).toEqual([])
  })

  it('does not report a comment that names an Alert', () => {
    const source = "import { Alert } from 'antd'\n// <Alert title=\"a\" />\nconst a = '<Alert>'\n"
    expect(lint(source, { rules })).toEqual([])
  })
})

describe('axis/exact-locators', () => {
  const rules = { 'axis/exact-locators': 'error' }
  const reported = (source, options = rules) => lint(source, { rules: options, extension: 'ts' })
  const rule = 'axis(exact-locators)'

  it('reports a string name without exact: true, also with exact: false or a flag', () => {
    const source = [
      "page.getByRole('button', { name: 'Save' })",
      "page.getByRole('button', { name: 'Save', exact: false })",
      "page.getByRole('button', { name: 'Save', exact: flag })",
      "page.getByRole('button', { name: 'Save', exact: true, ...more })",
      'page.getByRole("button", { name })',
      'page.getByRole("row", { name: `Row ${id}` })',
      '',
    ].join('\n')
    expect(reported(source)).toEqual([1, 2, 3, 4, 5, 6].map((line) => `${rule}:${line}`))
  })

  it('reports a string text without exact: true for getByLabel, getByText and getByTitle', () => {
    const source = [
      "page.getByText('x')",
      'page.getByLabel(label)',
      "page.getByLabel('x', { exact: false })",
      "page.getByTitle('x')",
      'page.getByText(`Row ${f(a, b)}`)',
      '',
    ].join('\n')
    expect(reported(source)).toEqual([1, 2, 3, 4, 5].map((line) => `${rule}:${line}`))
  })

  it('reports a multi-line call on the line of the call', () => {
    const source = ['await page', "  .getByRole('button', {", "    name: 'Save',", '  })', ''].join('\n')
    expect(reported(source)).toEqual([`${rule}:2`])
  })

  it('reports options and names it cannot see', () => {
    const source = [
      "function a(options) { page.getByRole('button', options) }",
      "const o = { name: 'Save' }\npage.getByRole('button', o)",
      "const name = 'Save'\npage.getByRole('button', { name })",
      "const t = 'x'\npage.getByText(t, { exact: true, ...more })",
      "page.getByRole('button', { ...more })",
      '',
    ].join('\n')
    expect(reported(source)).toEqual([`${rule}:1`, `${rule}:3`, `${rule}:5`, `${rule}:7`, `${rule}:8`])
  })

  it('allows exact: true, patterns, and getByRole with no name', () => {
    const source = [
      "page.getByRole('button', { name: 'Save', exact: true })",
      'page.getByRole("button", { name: /Save/i })',
      "page.getByRole('row', { name: new RegExp(title, 'i') })",
      'page.getByLabel(fieldLabel(label))',
      "page.getByRole('row')",
      "page.getByRole('row', { exact: true })",
      "page.getByTitle('x', { exact: true })",
      "page.getByText('x', { 'exact': true })",
      'page.getByText(/Save/i)',
      'getByText(new RegExp(title))',
      'page.getByRole("row", { name: `Row ${id}`, exact: true })',
      '',
    ].join('\n')
    expect(reported(source)).toEqual([])
  })

  it('follows a const to a pattern or to options with exact: true', () => {
    const source = [
      "const r = /don't/\npage.getByRole('button', { name: r })",
      "const o = { name: 'Save', exact: true }\npage.getByRole('button', o)",
      "const p = new RegExp(title, 'i')\npage.getByText(p)",
      'function a() {\n  const label = fieldLabel(x)\n  page.getByLabel(label)\n}',
      '',
    ].join('\n')
    expect(reported(source)).toEqual([])
  })

  it('does not report a call named in a comment or a string, or another method', () => {
    const source = [
      "// page.getByRole('button', { name: 'z' })",
      'const s = "page.getByText(\'x\')"',
      "const t = `getByLabel('w')`",
      "page.getByRoleX('x')",
      "page.getByPlaceholder('x')",
      '',
    ].join('\n')
    expect(reported(source)).toEqual([])
  })

  it('reports a dialog lookup with any quotes, and not a button named dialog', () => {
    const source = [
      "page.getByRole('dialog')",
      'page.getByRole("dialog")',
      'page.getByRole(`dialog`)',
      "page.getByRole('dialog', { name: 'X', exact: true })",
      "page.getByRole('button', { name: 'dialog', exact: true })",
      '',
    ].join('\n')
    expect(reported(source)).toEqual([1, 2, 3, 4].map((line) => `${rule}:${line}`))
  })

  it('allows a dialog lookup with allowDialogLookup', () => {
    const source = "page.getByRole('dialog')\npage.getByRole('dialog', { name: 'X', exact: true })\n"
    expect(reported(source, { 'axis/exact-locators': ['error', { allowDialogLookup: true }] })).toEqual([])
  })

  it('reports a dialog lookup under tests/e2e/specs only, with the config of tests/e2e', () => {
    const source = "export const a = (page) => page.getByRole('dialog', { name: 'X', exact: true })\n"
    const copy = path.join(folder, 'e2e')
    mkdirSync(path.join(copy, 'specs'), { recursive: true })
    const configFile = path.join(copy, '.oxlintrc.json')
    // The copy sits outside tests/e2e, so the plugin is named by its path.
    writeFileSync(configFile, JSON.stringify(readConfig(path.join(webRoot, '../tests/e2e/.oxlintrc.json'))))
    writeFileSync(path.join(copy, 'specs/a.spec.ts'), source)
    writeFileSync(path.join(copy, 'helper.ts'), source)
    const run = (file) =>
      JSON.parse(
        spawnSync(oxlint, ['-c', configFile, '--format', 'json', file], { cwd: copy, encoding: 'utf8' }).stdout,
      ).diagnostics.map((diagnostic) => diagnostic.code)
    expect(run('specs/a.spec.ts')).toEqual([rule])
    expect(run('helper.ts')).toEqual([])
  })
})

describe('the rules of web/.oxlintrc.json', () => {
  const realConfig = readConfig(path.join(webRoot, '.oxlintrc.json'))

  it('reports a write to innerHTML and not a comment that names it', () => {
    const source = 'const el = document.body\nel.innerHTML = value\n// el.innerHTML = value\nconst a = "innerHTML"\n'
    expect(lint(source, { config: realConfig, extension: 'ts' })).toEqual(['eslint(no-restricted-properties):2'])
  })

  it('reports a restricted property reached through a variable key', () => {
    expect(
      lint('const el = document.body\nel["inner" + "HTML"] = value\n', { config: realConfig, extension: 'ts' }),
    ).toEqual(['axis(no-computed-key-write):2'])
  })

  it('refuses an Alert outside AxisAlert.tsx and allows it inside', () => {
    const copy = path.join(folder, 'real-config')
    mkdirSync(path.join(copy, 'src/platform'), { recursive: true })
    mkdirSync(path.join(copy, 'src/pages'), { recursive: true })
    const configFile = path.join(copy, '.oxlintrc.json')
    const source = 'import { Alert } from \'antd\'\nexport function Page() {\n  return <Alert title="x" />\n}\n'
    writeFileSync(configFile, JSON.stringify(realConfig))
    writeFileSync(path.join(copy, 'src/platform/AxisAlert.tsx'), source)
    writeFileSync(path.join(copy, 'src/pages/Other.tsx'), source)
    const run = (file) =>
      JSON.parse(
        spawnSync(oxlint, ['-c', configFile, '--format', 'json', file], { cwd: copy, encoding: 'utf8' }).stdout,
      ).diagnostics.map((diagnostic) => diagnostic.code)
    expect(run('src/platform/AxisAlert.tsx')).toEqual([])
    expect(run('src/pages/Other.tsx')).toEqual(['axis(no-antd-alert)'])
  })

  it('reports Object.assign, DOMParser and a Table imported from antd', () => {
    const source = "import { Table } from 'antd'\nObject.assign({}, a)\nnew DOMParser()\n"
    expect(lint(source, { config: realConfig, extension: 'ts' }).sort()).toEqual([
      'eslint(no-restricted-globals):3',
      'eslint(no-restricted-imports):1',
      'eslint(no-restricted-properties):2',
    ])
  })
})

describe('disable comments', () => {
  const config = { jsPlugins: [plugin], rules: { 'axis/no-antd-button': 'error' } }
  const args = ['--report-unused-disable-directives']

  it('lets a disable comment with a reason allow its line', () => {
    const source =
      "import { Button } from 'antd'\n// oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton.\nconst a = <Button>a</Button>\n"
    expect(lint(source, { config, args })).toEqual([])
  })

  it('fails a disable comment that its line no longer needs', () => {
    const source =
      '// oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton.\nconst a = <AxisButton>a</AxisButton>\n'
    const reported = lint(source, { config, args })
    expect(reported).toHaveLength(1)
    expect(reported[0]).toMatch(/:1$/)
  })
})

describe('overrides', () => {
  const config = {
    jsPlugins: [plugin],
    rules: { 'axis/no-prototype-write': 'error' },
    overrides: [{ files: ['allowed/**'], rules: { 'axis/no-prototype-write': 'off' } }],
  }

  it('turns off one rule for one file', () => {
    const source = 'Foo.prototype.bar = 1\n'
    const configFile = path.join(folder, 'overrides.json')
    writeFileSync(path.join(folder, 'other.ts'), source)
    writeFileSync(configFile, JSON.stringify(config))
    const run = (file) =>
      JSON.parse(
        spawnSync(oxlint, ['-c', configFile, '--format', 'json', file], { cwd: folder, encoding: 'utf8' }).stdout,
      ).diagnostics
    expect(run('other.ts')).toHaveLength(1)
    mkdirSync(path.join(folder, 'allowed'))
    writeFileSync(path.join(folder, 'allowed/file.ts'), source)
    expect(run('allowed/file.ts')).toHaveLength(0)
  })
})

/** Reads a JSON file that has whole-line // comments, as oxlint does. */
function readConfig(file) {
  const lines = readFileSync(file, 'utf8').split('\n')
  const config = JSON.parse(lines.filter((line) => !line.trim().startsWith('//')).join('\n'))
  // The fixtures live outside web/, so the plugin is named by its path.
  return { ...config, jsPlugins: [plugin] }
}
