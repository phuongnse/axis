import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import path from 'node:path'
import { beforeEach, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { checkThreatTables } from './threat-tables.mjs'
import { useTempRoot } from './testing.mjs'

const repositoryRoot = fileURLToPath(new URL('../../..', import.meta.url))
const fixture = useTempRoot()
const { write } = fixture
const messages = () => fixture.messages(checkThreatTables)

const threatHeader = '| Id | Threat | Boundary | Category | Rating | Status | Evidence |'
const threatSeparator = '| --- | --- | --- | --- | --- | --- | --- |'

describe('threat model table', () => {
  const page = 'docs/reference/threat-model.md'
  const row = (id, { status = '**Gap**', rating = 'High', evidence = '[#1](https://github.com/o/r/issues/1).' } = {}) =>
    `| ${id} | Text \\| more. | Server | STRIDE: Spoofing | ${rating} | ${status} | ${evidence} |`
  const otherTable = ['| Id | Requirement | Status | Evidence |', '| --- | --- | --- | --- |', '| X1 | Not a threat row | **Done** | |']
  const lineOf = (text, needle) => text.split('\n').findIndex((line) => line.includes(needle)) + 1
  const linesOf = (text, needle) => text.split('\n').flatMap((line, i) => (line.includes(needle) ? [i + 1] : []))

  const writePage = ({
    product = [row('P1', { status: '**Mitigated**', evidence: '`FooTests`.' }), row('P2'), row('P3', { status: '**Delegated**', evidence: 'The identity provider.' })],
    installation = [row('I1', { status: '**Accepted**', rating: 'Low', evidence: 'Accepted by the owner on 2026-10-29: reason.' })],
  } = {}) => {
    const text = [
      '# Threat model',
      '',
      '## The product',
      '',
      'What the product covers.',
      '',
      threatHeader,
      threatSeparator,
      ...product,
      '',
      '## The installation',
      '',
      'What the installation covers.',
      '',
      threatHeader,
      threatSeparator,
      ...installation,
      '',
      '## Other tables',
      '',
      ...otherTable,
      '',
    ].join('\n')
    write(page, text)
    return text
  }

  beforeEach(() => {
    write('tests/X/FooTests.cs', 'public sealed class FooTests\n{\n}\n')
    writePage()
  })

  test('passes on one valid row of each status and ignores a table with another header', () => {
    assert.deepEqual(messages(), [])
  })

  test('reports a status that is not one of the four, in bold', () => {
    const text = writePage({ product: [row('P1', { status: '**Done**' })] })

    assert.deepEqual(messages(), [
      `${page}:${lineOf(text, '| P1 |')}: P1 status '**Done**' is not one of **Mitigated**, **Gap**, **Delegated**, **Accepted**`,
    ])
  })

  test('reports a row with no rating', () => {
    const text = writePage({ product: [row('P1', { status: '**Mitigated**', evidence: '`FooTests`.' }), row('P2', { rating: '' })] })

    assert.deepEqual(messages(), [`${page}:${lineOf(text, '| P2 |')}: P2 has no rating: one of Low, Medium, High, Critical`])
  })

  test('reports a rating that is not one of the four', () => {
    const text = writePage({ product: [row('P1', { status: '**Mitigated**', evidence: '`FooTests`.' }), row('P2', { rating: 'Severe' })] })

    assert.deepEqual(messages(), [`${page}:${lineOf(text, '| P2 |')}: P2 rating 'Severe' is not one of Low, Medium, High, Critical`])
  })

  test('reports a row without evidence', () => {
    const text = writePage({ product: [row('P1', { status: '**Mitigated**', evidence: '`FooTests`.' }), row('P2', { evidence: '' })] })

    assert.deepEqual(messages(), [`${page}:${lineOf(text, '| P2 |')}: P2 has no evidence`])
  })

  test('reports an id that another row already uses, on the line of the first', () => {
    const text = writePage({ installation: [row('P1', { status: '**Accepted**', rating: 'Low', evidence: 'Accepted by the owner on 2026-10-29: reason.' })] })
    const [first, second] = linesOf(text, '| P1 |')

    assert.deepEqual(messages(), [`${page}:${second}: P1 has a row already, on line ${first}`])
  })

  describe('in more than one file', () => {
    const scopeFile = 'docs/reference/threat-model-installation.md'
    const writeScope = (rows) => {
      const text = ['# Threat model: the installation', '', '## The installation', '', threatHeader, threatSeparator, ...rows, ''].join('\n')
      write(scopeFile, text)
      return text
    }

    test('reports an id that a row in another file already uses, naming that file and line', () => {
      // The files are checked in path order, so the installation file comes first and threat-model.md has the repeat.
      const first = writeScope([row('P1')])
      const second = writePage()

      assert.deepEqual(messages(), [
        `${page}:${lineOf(second, '| P1 |')}: P1 has a row already, in ${scopeFile} on line ${lineOf(first, '| P1 |')}`,
      ])
    })

    test('reports a bad row in a file other than threat-model.md, on that file and line', () => {
      const text = writeScope([row('I10'), row('I11', { status: '**Done**' })])

      assert.deepEqual(messages(), [
        `${scopeFile}:${lineOf(text, '| I11 |')}: I11 status '**Done**' is not one of **Mitigated**, **Gap**, **Delegated**, **Accepted**`,
      ])
    })

    test('finds a threat table in a file that no list of names holds', () => {
      write('docs/reference/guide.md', ['# Guide', '', threatHeader, threatSeparator, row('G1', { rating: 'Severe' }), ''].join('\n'))

      assert.deepEqual(messages(), [`docs/reference/guide.md:5: G1 rating 'Severe' is not one of Low, Medium, High, Critical`])
    })
  })

  test('reports a Mitigated row that names a test class no .cs file declares', () => {
    const text = writePage({ product: [row('P1', { status: '**Mitigated**', evidence: '`FooTests` and `MissingTests`.' }), row('P2')] })

    assert.deepEqual(messages(), [
      `${page}:${lineOf(text, '| P1 |')}: P1 names the test class MissingTests, which no .cs file under tests/, controlplane/tests/ or samples/ declares`,
    ])
  })

  test('accepts a Mitigated row that names a test class declared under controlplane/tests', () => {
    write('controlplane/tests/X/CellOnlyTests.cs', 'public sealed class CellOnlyTests\n{\n}\n')
    writePage({ product: [row('P1', { status: '**Mitigated**', evidence: '`CellOnlyTests`.' }), row('P2')] })

    assert.deepEqual(messages(), [])
  })

  test('reports a Gap row that links no issue', () => {
    const text = writePage({ product: [row('P1', { status: '**Mitigated**', evidence: '`FooTests`.' }), row('P2', { evidence: 'Planned for M18.' })] })

    assert.deepEqual(messages(), [`${page}:${lineOf(text, '| P2 |')}: P2 is a Gap and links no issue: link the GitHub issue that closes it`])
  })

  test('reports an Accepted row that gives no date', () => {
    const text = writePage({ installation: [row('I1', { status: '**Accepted**', rating: 'Low', evidence: 'Accepted by the owner: reason.' })] })

    assert.deepEqual(messages(), [
      `${page}:${lineOf(text, '| I1 |')}: I1 is Accepted and gives no date: give the date the owner accepted it, as YYYY-MM-DD`,
    ])
  })
})

describe('the repository threat model', () => {
  const scopes = [
    ['threat-model-product.md', '## The product'],
    ['threat-model-installation.md', '## The installation'],
    ['threat-model-build.md', '## The way Axis is built'],
    ['threat-model-control-plane.md', '## The control plane'],
  ]

  test('has a threat table under each scope section, in a file of its own', () => {
    for (const [file, heading] of scopes) {
      const text = readFileSync(path.join(repositoryRoot, 'docs/reference', file), 'utf8').split('\n')
      assert.ok(text.includes(heading), `${file} has the section ${heading}`)
      assert.ok(text.includes(threatHeader), `${file} has the threat table`)
    }
  })

  test('finds no problem in any threat model file', () => {
    const found = []
    checkThreatTables(repositoryRoot, (file, line, message) => {
      if (file.startsWith(path.join(repositoryRoot, 'docs/reference/threat-model'))) found.push(`${file}:${line}: ${message}`)
    })

    assert.deepEqual(found, [])
  })
})
