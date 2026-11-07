import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, readdirSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { afterEach, beforeEach, describe, test } from 'node:test'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { rules } from './check-docs.mjs'

const script = fileURLToPath(new URL('./check-docs.mjs', import.meta.url))
const repositoryRoot = fileURLToPath(new URL('..', import.meta.url))
const rulesFolder = fileURLToPath(new URL('./lib/docs/', import.meta.url))

let root

function write(file, text) {
  mkdirSync(path.dirname(path.join(root, file)), { recursive: true })
  writeFileSync(path.join(root, file), text)
}

const git = (...args) => spawnSync('git', args, { cwd: root, encoding: 'utf8' })

const doc = (n) => '# Guide\n' + 'line\n'.repeat(n - 1)

const index = (...rows) =>
  ['# Decisions', '', '| Decision | Title | Status |', '| --- | --- | --- |', ...rows].join('\n') + '\n'

beforeEach(() => {
  root = mkdtempSync(path.join(tmpdir(), 'check-docs-'))
  write('docs/README.md', '# Documentation\n\n| [Decisions](decisions.md) | [Guide](reference/guide.md) |\n')
  write('docs/reference/guide.md', '# Guide\n')
  write('docs/decisions/d1-files-first.md', '# D1. Files first\n\nStatus: **Agreed**\n\nText.\n')
  write('docs/decisions/d2-typed-expressions.md', '# D2. Typed expressions\n\nStatus: **Proposed**\n')
  write(
    'docs/decisions.md',
    index(
      '| [D1](decisions/d1-files-first.md) | Files first | **Agreed** |',
      '| [D2](decisions/d2-typed-expressions.md) | Typed expressions | **Proposed** |',
    ),
  )
  write('AGENTS.md', '# Agents\n')
})

afterEach(() => rmSync(root, { recursive: true, force: true }))

describe('rules', () => {
  test('has one module under scripts/lib/docs/ for each rule, and no other', async () => {
    const modules = readdirSync(rulesFolder).filter(
      (name) => name.endsWith('.mjs') && !name.endsWith('.test.mjs') && name !== 'helpers.mjs' && name !== 'testing.mjs',
    )

    assert.equal(rules.length, modules.length)
    for (const name of modules) {
      const exported = await import(pathToFileURL(path.join(rulesFolder, name)).href)
      const functions = Object.values(exported).filter((value) => typeof value === 'function')
      assert.equal(functions.length, 1, `${name} exports one function`)
      assert.ok(rules.includes(functions[0]), `${name} is in the rules of scripts/check-docs.mjs`)
    }
  })
})

describe('CLI', () => {
  test('prints each problem with file and line and exits 1', () => {
    write('docs/reference/orphan.md', '# Orphan\n')

    const result = spawnSync(process.execPath, [script, '--root', root], {
      encoding: 'utf8',
      env: { ...process.env, NEXKIT_BASE_SHA: '' },
    })

    assert.equal(result.status, 1)
    assert.equal(result.stderr, 'docs/README.md:1: does not list docs/reference/orphan.md\n')
  })

  test('fails a committed doc that a change grows past 900 lines', () => {
    write('docs/reference/guide.md', doc(880))
    git('init', '-q')
    git('add', '-A')
    git('-c', 'user.name=t', '-c', 'user.email=t@t', 'commit', '-qm', 'base')
    const base = git('rev-parse', 'HEAD').stdout.trim()
    write('docs/reference/guide.md', doc(910))

    const result = spawnSync(process.execPath, [script, '--root', root], {
      encoding: 'utf8',
      env: { ...process.env, NEXKIT_BASE_SHA: base },
    })

    assert.equal(result.status, 1)
    assert.match(result.stderr, /^docs\/reference\/guide\.md:901: 910 lines, up from 880/)
  })

  test('passes on the repository', () => {
    const result = spawnSync(process.execPath, [script, '--root', repositoryRoot], { encoding: 'utf8' })

    assert.equal(result.stderr, '')
    assert.equal(result.status, 0)
  })
})
