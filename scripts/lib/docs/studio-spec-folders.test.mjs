import assert from 'node:assert/strict'
import { rmSync } from 'node:fs'
import path from 'node:path'
import { beforeEach, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { checkStudioSpecFolders } from './studio-spec-folders.mjs'
import { useTempRoot } from './testing.mjs'

const repositoryRoot = fileURLToPath(new URL('../../..', import.meta.url))
const fixture = useTempRoot()
const { write } = fixture
const messages = () => fixture.messages(checkStudioSpecFolders)

describe('Studio spec folders table', () => {
  const testing = 'docs/testing.md'
  const specFile = (name) => `tests/e2e/specs/${name}.spec.ts`
  const tableRow = (specs, folder = 'A temporary copy.') => `| ${specs} | ${folder} |`
  const lineOf = (text, needle) => text.split('\n').findIndex((line) => line.includes(needle)) + 1

  const writeTable = (...rows) => {
    const text = ['# Testing', '', 'Intro line.', '', '| Specs | Folder |', '| --- | --- |', ...rows].join('\n') + '\n'
    write(testing, text)
    return text
  }

  const writeSpec = (name, ...body) => {
    write(specFile(name), ["import { startServer } from '../activate'", '', ...body].join('\n') + '\n')
  }

  beforeEach(() => {
    writeTable(tableRow('`studio`'))
    writeSpec('studio', 'const server = await startServer({', '  Studio__Folders__0: copy,', '})')
  })

  test('passes when the table lists the specs that start a server over a Studio folder', () => {
    assert.deepEqual(messages(), [])
  })

  test('reports a spec that sets a Studio folder but is not in the table, on the spec line', () => {
    writeSpec('studio-history', 'const server = await startServer({', '  Studio__Folders__0: copy,', '  Studio__RevisionsPath: revisions,', '})')

    assert.deepEqual(messages(), [
      `${specFile('studio-history')}:4: studio-history starts a server over its own Studio folder but is not in the Studio spec folders table of ${testing}`,
    ])
  })

  test('reports a spec that sets Studio__ApplicationsPath but is not in the table, on the spec line', () => {
    writeSpec('studio-page-dashboard', 'const server = await startServer({ Studio__ApplicationsPath: applications })')

    assert.deepEqual(messages(), [
      `${specFile('studio-page-dashboard')}:3: studio-page-dashboard starts a server over its own Studio folder but is not in the Studio spec folders table of ${testing}`,
    ])
  })

  test('reports a table row whose spec starts no server over a Studio folder, on the row line', () => {
    const text = writeTable(tableRow('`studio`'), tableRow('`ghost`'))

    assert.deepEqual(messages(), [
      `${testing}:${lineOf(text, '`ghost`')}: ghost is in the Studio spec folders table but starts no server over its own Studio folder`,
    ])
  })

  test('reports a table row whose spec has no file, on the row line', () => {
    const text = writeTable(tableRow('`studio`, `studio-tests`'))

    assert.deepEqual(messages(), [
      `${testing}:${lineOf(text, '`studio`')}: studio-tests is in the Studio spec folders table but starts no server over its own Studio folder`,
    ])
  })

  test('reads only the Specs column, so a name in the Folder column is not a spec', () => {
    writeTable(tableRow('`studio`', 'A copy, as `studio-new-resource` is.'))

    assert.deepEqual(messages(), [])
  })

  test('ignores a spec that starts a server without a Studio folder', () => {
    writeSpec('studio-journey', 'const server = await startServer({ ActivateOnStartup__0: e2eApp })')

    assert.deepEqual(messages(), [])
  })

  test('ignores a spec that names a Studio folder setting without starting a server', () => {
    writeSpec('studio-notes', "const setting = 'Studio__Folders__0'")

    assert.deepEqual(messages(), [])
  })

  test('reports a missing table on line 1 of the testing doc', () => {
    write(testing, '# Testing\n\nNo table.\n')

    assert.deepEqual(messages(), [
      `${testing}:1: needs a table with Specs and Folder columns that lists the specs that start a server over their own Studio folder`,
    ])
  })

  test('skips the check when the specs folder is missing', () => {
    rmSync(path.join(fixture.root(), 'tests'), { recursive: true, force: true })

    assert.deepEqual(messages(), [])
  })
})

describe('the repository Studio spec folders', () => {
  test('finds no problem in the Studio spec folders table or the specs', () => {
    const found = []
    checkStudioSpecFolders(repositoryRoot, (file, line, message) => found.push(`${file}:${line}: ${message}`))

    assert.deepEqual(found, [])
  })
})
