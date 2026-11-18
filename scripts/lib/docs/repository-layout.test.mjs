import assert from 'node:assert/strict'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { checkRepositoryLayout } from './repository-layout.mjs'
import { relativePath } from './helpers.mjs'
import { useTempRoot } from './testing.mjs'

const repositoryRoot = fileURLToPath(new URL('../../..', import.meta.url))
const fixture = useTempRoot()
const { write } = fixture
const messages = () => fixture.messages(checkRepositoryLayout)

const layoutDoc = 'docs/repository-layout.md'
const solution = 'Axis.slnx'
const lineOf = (text, needle) => text.split('\n').findIndex((line) => line.includes(needle)) + 1

/** Writes the layout doc with rows in its text block, and returns the text. The doc lives in docs/, so the tree always lists it. */
function writeTree(...rows) {
  const text = ['# Repository layout', '', '## Repository layout', '', 'Intro line.', '', '```text', 'docs/', ...rows, '```', ''].join('\n')
  write(layoutDoc, text)
  return text
}

/** Writes Axis.slnx with one project line for each path, and returns the text. */
function writeSolution(...projects) {
  const text = ['<Solution>', '  <Folder Name="/src/">', ...projects.map((project) => `    <Project Path="${project}" />`), '  </Folder>', '</Solution>', ''].join('\n')
  write(solution, text)
  return text
}

/** Creates a folder, with one file in it so that it exists on disk. */
const folder = (name) => write(`${name}/.keep`, '')

describe('repository layout tree', () => {
  const inStep = () => {
    writeSolution('src/Axis.Core/Axis.Core.csproj', 'src/Axis.Server/Axis.Server.csproj')
    folder('src/Axis.Core')
    folder('src/Axis.Server')
    return writeTree('src/', '  Axis.Core/', '  Axis.Server/')
  }

  test('passes when the tree lists the folders on disk and the projects of Axis.slnx', () => {
    inStep()

    assert.deepEqual(messages(), [])
  })

  test('reports a folder on disk that the tree lacks, on the line of its parent entry', () => {
    const text = inStep()
    folder('src/Axis.Extra')

    assert.deepEqual(messages(), [
      `${layoutDoc}:${lineOf(text, 'src/')}: src/Axis.Extra/ is a folder but is not in the repository layout tree of ${layoutDoc}`,
    ])
  })

  test('reports a top-level folder on disk that the tree lacks, on the fence line', () => {
    const text = inStep()
    folder('scripts')

    assert.deepEqual(messages(), [
      `${layoutDoc}:${lineOf(text, '```text')}: scripts/ is a folder but is not in the repository layout tree of ${layoutDoc}`,
    ])
  })

  test('reports a tree entry with no folder, on its line', () => {
    writeSolution('src/Axis.Core/Axis.Core.csproj', 'src/Axis.Server/Axis.Server.csproj')
    folder('src/Axis.Core')
    folder('src/Axis.Server')
    const text = writeTree('src/', '  Axis.Core/', '  Axis.Server/', '  Axis.Ghost/')

    assert.deepEqual(messages(), [
      `${layoutDoc}:${lineOf(text, 'Axis.Ghost/')}: src/Axis.Ghost/ is in the repository layout tree but is not a folder`,
    ])
  })

  test('reports an Axis.slnx project whose folder the tree does not name, on the project line', () => {
    inStep()
    const text = writeSolution('src/Axis.Core/Axis.Core.csproj', 'src/Axis.Server/Axis.Server.csproj', 'src/Axis.Ghost/Axis.Ghost.csproj')

    assert.deepEqual(messages(), [
      `${solution}:${lineOf(text, 'Axis.Ghost.csproj')}: src/Axis.Ghost/ holds a project of Axis.slnx but is not in the repository layout tree of ${layoutDoc}`,
    ])
  })

  test('does not report a hidden folder or a folder that .gitignore lists', () => {
    writeSolution()
    write('.gitignore', ['# Build output', 'artifacts/', 'bin/', 'src/private/', '*.log', ''].join('\n'))
    folder('.github')
    folder('artifacts')
    folder('src/bin')
    folder('src/private')
    folder('src/.cache')
    writeTree('src/')

    assert.deepEqual(messages(), [])
  })

  test('does not report folders inside a leaf entry', () => {
    inStep()
    folder('src/Axis.Core/Models')
    folder('src/Axis.Server/wwwroot')

    assert.deepEqual(messages(), [])
  })

  test('expands the middle folder of an entry with several segments', () => {
    writeSolution()
    folder('controlplane/src/Axis.ControlPlane')
    folder('controlplane/src/Other')
    const text = writeTree('controlplane/', '  src/Axis.ControlPlane/')

    assert.deepEqual(messages(), [
      `${layoutDoc}:${lineOf(text, 'src/Axis.ControlPlane/')}: controlplane/src/Other/ is a folder but is not in the repository layout tree of ${layoutDoc}`,
    ])
  })

  test('lets a <Name> segment cover every folder that matches it, and reports an entry that matches none', () => {
    writeSolution()
    folder('tests/Axis.Agent.Tests')
    folder('tests/Axis.Data.Tests')
    const text = writeTree('tests/', '  Axis.<Module>.Tests/', '  Axis.<Module>.Gone/')

    assert.deepEqual(messages(), [
      `${layoutDoc}:${lineOf(text, 'Axis.<Module>.Gone/')}: tests/Axis.<Module>.Gone/ is in the repository layout tree but is not a folder`,
    ])
  })

  test('reports a folder on disk that no placeholder covers', () => {
    writeSolution()
    folder('tests/Axis.Agent.Tests')
    folder('tests/smoke')
    const text = writeTree('tests/', '  Axis.<Module>.Tests/')

    assert.deepEqual(messages(), [
      `${layoutDoc}:${lineOf(text, 'tests/')}: tests/smoke/ is a folder but is not in the repository layout tree of ${layoutDoc}`,
    ])
  })

  test('skips the check when Axis.slnx is absent', () => {
    folder('extra')
    writeTree('src/')

    assert.deepEqual(messages(), [])
  })
})

describe('repository layout tree when the doc has no tree', () => {
  test('reports the missing tree on the first line of the layout doc', () => {
    writeSolution()
    write('docs/other.md', '# Other\n')

    assert.deepEqual(messages(), [
      `${layoutDoc}:1: needs a text block under a Repository layout heading that names the top-level folders and the projects in them`,
    ])
  })
})

describe('the repository layout tree of the repository', () => {
  test('matches the folders and the projects of the repository', () => {
    const found = []
    checkRepositoryLayout(repositoryRoot, (file, line, message) => found.push(`${relativePath(repositoryRoot, file)}:${line}: ${message}`))

    assert.deepEqual(found, [])
  })
})
