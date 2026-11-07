import assert from 'node:assert/strict'
import { readFileSync, rmSync } from 'node:fs'
import path from 'node:path'
import { beforeEach, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { asvsListFile, asvsStatuses, checkAsvs } from './asvs.mjs'
import { useTempRoot } from './testing.mjs'

const repositoryRoot = fileURLToPath(new URL('../../..', import.meta.url))
const fixture = useTempRoot()
const { write } = fixture
const messages = () => fixture.messages(checkAsvs)

describe('ASVS table', () => {
  const header = ['## Requirements', '', '| Id | Requirement | Status | Evidence |', '| --- | --- | --- | --- |']
  const row = (id, status = '**Gap**', evidence = '[#1](https://github.com/o/r/issues/1).') => `| \`${id}\` | Text \\| more. | ${status} | ${evidence} |`
  const table = (...rows) => ['# ASVS', '', ...header, ...rows].join('\n') + '\n'

  beforeEach(() => {
    write(
      asvsListFile,
      JSON.stringify({
        requirements: [
          { id: 'V1.1.1', level: 2 },
          { id: 'V1.2.1', level: 1 },
          { id: 'V9.1.1', level: 1 },
        ],
      }),
    )
    write('tests/X/FooTests.cs', 'public sealed class FooTests\n{\n}\n')
    write('tests/X/obj/BarTests.cs', 'public sealed class BarTests\n{\n}\n')
    write('docs/reference/asvs.md', table(row('V1.1.1'), row('V1.2.1', '**Met**', '`FooTests`, [doc](guide.md).'), row('V9.1.1')))
  })

  test('passes on a valid table', () => {
    assert.deepEqual(messages(), [])
  })

  test('passes on every status', () => {
    write(
      'docs/reference/asvs.md',
      table(row('V1.1.1', `**${asvsStatuses[2]}**`), row('V1.2.1', `**${asvsStatuses[3]}**`, 'No such thing.'), row('V9.1.1')),
    )

    assert.deepEqual(messages(), [])
  })

  test('reports a repeated id with the line of the first row', () => {
    write('docs/reference/asvs.md', table(row('V1.1.1'), row('V1.2.1'), row('V9.1.1'), row('V1.1.1')))

    assert.deepEqual(messages(), ['docs/reference/asvs.md:10: V1.1.1 has a row already, on line 7'])
  })

  test('reports an id that is not in the list', () => {
    write('docs/reference/asvs.md', table(row('V1.1.1'), row('V1.2.1'), row('V9.1.1'), row('V1.9.9')))

    assert.deepEqual(messages(), [`docs/reference/asvs.md:10: V1.9.9 is not a Level 1 or Level 2 id in ${asvsListFile}`])
  })

  test('reports a status that is not in the list', () => {
    write('docs/reference/asvs.md', table(row('V1.1.1', '**Done**'), row('V1.2.1', 'Gap'), row('V9.1.1')))

    const found = messages()
    assert.equal(found.length, 2)
    assert.match(found[0], /^docs\/reference\/asvs\.md:7: V1\.1\.1 status '\*\*Done\*\*' is not one of \*\*Met\*\*, \*\*Gap\*\*/)
    assert.match(found[1], /^docs\/reference\/asvs\.md:8: V1\.2\.1 status 'Gap' is not one of/)
  })

  test('reports a row without evidence', () => {
    write('docs/reference/asvs.md', table(row('V1.1.1', '**Gap**', ''), row('V1.2.1'), row('V9.1.1')))

    assert.deepEqual(messages(), ['docs/reference/asvs.md:7: V1.1.1 has no evidence'])
  })

  test('reports every listed id that has no row, whatever its chapter', () => {
    write('docs/reference/asvs.md', table(row('V1.1.1')))

    assert.deepEqual(messages(), [
      'docs/reference/asvs.md:3: V1.2.1 (Level 1) has no row in the requirements table',
      'docs/reference/asvs.md:3: V9.1.1 (Level 1) has no row in the requirements table',
    ])
  })

  test('reports a listed id of chapter V9 or later that has no row', () => {
    write('docs/reference/asvs.md', table(row('V1.1.1'), row('V1.2.1')))

    assert.deepEqual(messages(), ['docs/reference/asvs.md:3: V9.1.1 (Level 1) has no row in the requirements table'])
  })

  test('passes when every listed id has a row', () => {
    write('docs/reference/asvs.md', table(row('V1.1.1'), row('V1.2.1'), row('V9.1.1')))

    assert.deepEqual(messages(), [])
  })

  test('reports a Gap row that links no issue', () => {
    write('docs/reference/asvs.md', table(row('V1.1.1', '**Gap**', 'Planned for M16.'), row('V1.2.1'), row('V9.1.1')))

    assert.deepEqual(messages(), ['docs/reference/asvs.md:7: V1.1.1 is a Gap and links no issue: link the GitHub issue that closes it'])
  })

  test('does not take a bare issue number or another link for an issue link', () => {
    write(
      'docs/reference/asvs.md',
      table(row('V1.1.1', '**Gap**', 'See #12.'), row('V1.2.1', '**Gap**', '[doc](guide.md).'), row('V9.1.1')),
    )

    assert.deepEqual(messages(), [
      'docs/reference/asvs.md:7: V1.1.1 is a Gap and links no issue: link the GitHub issue that closes it',
      'docs/reference/asvs.md:8: V1.2.1 is a Gap and links no issue: link the GitHub issue that closes it',
    ])
  })

  test('passes a Gap row once it links an issue', () => {
    write(
      'docs/reference/asvs.md',
      table(row('V1.1.1', '**Gap**', 'Planned in [#12](https://github.com/phuongnse/axis/issues/12).'), row('V1.2.1'), row('V9.1.1')),
    )

    assert.deepEqual(messages(), [])
  })

  test('needs no issue link on a Met, Delegated or Not applicable row', () => {
    write(
      'docs/reference/asvs.md',
      table(
        row('V1.1.1', '**Met**', '`FooTests`.'),
        row('V1.2.1', `**${asvsStatuses[2]}**`, 'The identity provider.'),
        row('V9.1.1', `**${asvsStatuses[3]}**`, 'No such thing.'),
      ),
    )

    assert.deepEqual(messages(), [])
  })

  test('reports a Met row that names a test class that no .cs file declares', () => {
    write('docs/reference/asvs.md', table(row('V1.1.1', '**Met**', '`FooTests` and `MissingTests`.'), row('V1.2.1'), row('V9.1.1')))

    assert.deepEqual(messages(), [
      'docs/reference/asvs.md:7: V1.1.1 names the test class MissingTests, which no .cs file under tests/, controlplane/tests/ or samples/ declares',
    ])
  })

  test('finds a test class under samples/, and ignores one under obj/', () => {
    write('samples/apps/Y/BazTests.cs', 'public partial class BazTests\n{\n}\n')
    write('docs/reference/asvs.md', table(row('V1.1.1', '**Met**', '`BazTests`.'), row('V1.2.1', '**Met**', '`BarTests`.'), row('V9.1.1')))

    assert.deepEqual(messages(), [
      'docs/reference/asvs.md:8: V1.2.1 names the test class BarTests, which no .cs file under tests/, controlplane/tests/ or samples/ declares',
    ])
  })

  test('does not look for a test class in a Gap row', () => {
    write('docs/reference/asvs.md', table(row('V1.1.1', '**Gap**', 'Planned with `MissingTests`, [#1](https://github.com/o/r/issues/1).'), row('V1.2.1'), row('V9.1.1')))

    assert.deepEqual(messages(), [])
  })

  test('reports the page when the list is missing', () => {
    rmSync(path.join(fixture.root(), asvsListFile))

    assert.deepEqual(messages(), [`docs/reference/asvs.md:1: missing ${asvsListFile}: the requirements table is checked against it`])
  })
})

describe('the repository ASVS table', () => {
  const list = JSON.parse(readFileSync(path.join(repositoryRoot, asvsListFile), 'utf8'))
  const rowIds = readFileSync(path.join(repositoryRoot, 'docs/reference/asvs.md'), 'utf8')
    .split('\n')
    .map((line) => /^\| `(V\d+\.\d+\.\d+)` \|/.exec(line)?.[1])
    .filter(Boolean)

  test('names its source and holds only Level 1 and Level 2 ids', () => {
    assert.match(list.source, /^https:\/\/raw\.githubusercontent\.com\/OWASP\/ASVS\/v5\.0\.0\//)
    assert.ok(list.requirements.length > 0)
    for (const { id, level } of list.requirements) {
      assert.match(id, /^V\d+\.\d+\.\d+$/)
      assert.ok(level === 1 || level === 2, `${id} has level ${level}`)
    }
  })

  test('has exactly one row for each listed id', () => {
    assert.ok(list.requirements.length > 0)
    for (const { id } of list.requirements) assert.equal(rowIds.filter((rowId) => rowId === id).length, 1, `${id} has one row`)
  })

  test('finds no problem in the ASVS table', () => {
    const found = []
    checkAsvs(repositoryRoot, (file, line, message) => found.push(`${file}:${line}: ${message}`))

    assert.deepEqual(found, [])
  })
})
