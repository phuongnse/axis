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
