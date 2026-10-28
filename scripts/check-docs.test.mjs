import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { afterEach, beforeEach, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import {
  asvsListFile,
  asvsStatuses,
  checkDocs,
  maxAgentsLines,
  maxDocLines,
  maxSkillLines,
  realmFile,
} from './check-docs.mjs'

const script = fileURLToPath(new URL('./check-docs.mjs', import.meta.url))
const repositoryRoot = fileURLToPath(new URL('..', import.meta.url))

let root

function write(file, text) {
  mkdirSync(path.dirname(path.join(root, file)), { recursive: true })
  writeFileSync(path.join(root, file), text)
}

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

const messages = () => checkDocs(root).map(({ file, line, message }) => `${file}:${line}: ${message}`)

describe('checkDocs', () => {
  test('passes on matching docs', () => {
    assert.deepEqual(messages(), [])
  })

  test('reports a doc that the docs map does not list', () => {
    write('docs/reference/orphan.md', '# Orphan\n')

    assert.deepEqual(messages(), ['docs/README.md:1: does not list docs/reference/orphan.md'])
  })

  test('reports a doc and AGENTS.md over their size limits', () => {
    write('docs/reference/guide.md', '# Guide\n' + 'line\n'.repeat(maxDocLines))
    write('AGENTS.md', 'line\n'.repeat(maxAgentsLines + 1))

    const found = messages()
    assert.equal(found.length, 2)
    assert.match(found[0], /^docs\/reference\/guide\.md:1001: 1001 lines, over the limit of 1000/)
    assert.match(found[1], /^AGENTS\.md:101: 101 lines, over the limit of 100/)
  })

  test('passes on a skill at its size limit', () => {
    write('.claude/skills/runbook/SKILL.md', 'line\n'.repeat(maxSkillLines))

    assert.deepEqual(messages(), [])
  })

  test('reports a skill over its size limit', () => {
    write('.claude/skills/runbook/SKILL.md', 'line\n'.repeat(maxSkillLines + 1))

    assert.deepEqual(messages(), [
      '.claude/skills/runbook/SKILL.md:251: 251 lines, over the limit of 250: link the rules in docs/ instead of restating them',
    ])
  })

  test('reports a decision file without a row and a row without a file', () => {
    write('docs/decisions/d3-new-thing.md', '# D3. New thing\n\nStatus: **Proposed**\n')
    write(
      'docs/decisions.md',
      index(
        '| [D1](decisions/d1-files-first.md) | Files first | **Agreed** |',
        '| [D2](decisions/d2-typed-expressions.md) | Typed expressions | **Proposed** |',
        '| [D4](decisions/d4-missing.md) | Missing | **Agreed** |',
      ),
    )

    assert.deepEqual(messages().sort(), [
      'docs/decisions.md:7: D4 has no file in docs/decisions/',
      'docs/decisions.md:7: row D4 should be D3: decisions are numbered in order with no gaps',
      'docs/decisions/d3-new-thing.md:1: D3 has no row in docs/decisions.md',
    ])
  })

  test('reports a row whose title or status differs from its file', () => {
    write('docs/decisions/d2-typed-expressions.md', '# D2. Typed expressions\n\nStatus: **Agreed**\n')
    write(
      'docs/decisions.md',
      index(
        '| [D1](decisions/d1-files-first.md) | Files come first | **Agreed** |',
        '| [D2](decisions/d2-typed-expressions.md) | Typed expressions | **Proposed** |',
      ),
    )

    assert.deepEqual(messages(), [
      "docs/decisions.md:5: D1 title 'Files come first' differs from the file's 'Files first'",
      "docs/decisions.md:6: D2 status 'Proposed' differs from the file's 'Agreed'",
    ])
  })

  test('reports a decision file with a wrong name, title or status', () => {
    write('docs/decisions/d1-files-first.md', '# D1. Files first\n\nStatus: Agreed\n')
    write('docs/decisions/d2-typed-expressions.md', '# D2. Typed expression language\n\nStatus: **Proposed**\n')
    write('docs/decisions/notes.md', '# Notes\n')

    assert.deepEqual(messages().sort(), [
      "docs/decisions.md:6: D2 title 'Typed expressions' differs from the file's 'Typed expression language'",
      "docs/decisions/d1-files-first.md:3: needs a 'Status: **Agreed**' or 'Status: **Proposed**' line",
      'docs/decisions/d2-typed-expressions.md:1: the file name should end in d2-typed-expression-language.md',
      'docs/decisions/notes.md:1: a decision file is named docs/decisions/d<n>-<slug>.md',
    ])
  })
})

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
    write('docs/README.md', '# Documentation\n\n| [Decisions](decisions.md) | [Guide](reference/guide.md) | [ASVS](reference/asvs.md) |\n')
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
      'docs/reference/asvs.md:7: V1.1.1 names the test class MissingTests, which no .cs file under tests/ or samples/ declares',
    ])
  })

  test('finds a test class under samples/, and ignores one under obj/', () => {
    write('samples/apps/Y/BazTests.cs', 'public partial class BazTests\n{\n}\n')
    write('docs/reference/asvs.md', table(row('V1.1.1', '**Met**', '`BazTests`.'), row('V1.2.1', '**Met**', '`BarTests`.'), row('V9.1.1')))

    assert.deepEqual(messages(), [
      'docs/reference/asvs.md:8: V1.2.1 names the test class BarTests, which no .cs file under tests/ or samples/ declares',
    ])
  })

  test('does not look for a test class in a Gap row', () => {
    write('docs/reference/asvs.md', table(row('V1.1.1', '**Gap**', 'Planned with `MissingTests`, [#1](https://github.com/o/r/issues/1).'), row('V1.2.1'), row('V9.1.1')))

    assert.deepEqual(messages(), [])
  })

  test('reports the page when the list is missing', () => {
    rmSync(path.join(root, asvsListFile))

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
})

describe('development users', () => {
  const security = 'docs/reference/security.md'
  const userRow = (user, roles) => `| \`${user}\` | ${roles.map((role) => `\`${role}\``).join(', ')} | Samples |`
  const realmUser = (username, roles, id = username) => ({ id, username, enabled: true, clientRoles: { axis: roles } })
  const serviceAccount = { id: 'x', username: 'service-account-x', serviceAccountClientId: 'x', clientRoles: { x: ['x'] } }
  const lineOf = (text, needle) => text.split('\n').findIndex((line) => line.includes(needle)) + 1

  const writeSecurity = (...rows) =>
    write(security, ['# Security', '', '### Development users', '', 'Intro line.', '', '| User | Roles | Used by |', '| --- | --- | --- |', ...rows].join('\n') + '\n')
  const writeRealm = (...users) => {
    const text = JSON.stringify({ realm: 'axis', users: [...users, serviceAccount] }, null, 2) + '\n'
    write(realmFile, text)
    return text
  }

  beforeEach(() => {
    write('docs/README.md', '# Documentation\n\n| [Decisions](decisions.md) | [Guide](reference/guide.md) | [Security](reference/security.md) |\n')
    writeSecurity(userRow('requester', ['employee', 'requester']), userRow('manager', ['department-manager']))
    writeRealm(realmUser('requester', ['employee', 'requester']), realmUser('manager', ['department-manager']))
  })

  test('passes on matching files', () => {
    assert.deepEqual(messages(), [])
  })

  test('passes when the roles are in another order', () => {
    writeSecurity(userRow('requester', ['requester', 'employee']), userRow('manager', ['department-manager']))

    assert.deepEqual(messages(), [])
  })

  test('reports a table user who is not in the realm, on the table row', () => {
    writeSecurity(userRow('requester', ['employee', 'requester']), userRow('manager', ['department-manager']), userRow('manager-2', ['department-manager']))

    assert.deepEqual(messages(), [`${security}:11: manager-2 is in the development users table but not in ${realmFile}`])
  })

  test('reports a realm user who is not in the table, on the username line', () => {
    const text = writeRealm(
      realmUser('requester', ['employee', 'requester']),
      realmUser('manager', ['department-manager']),
      realmUser('finance-reviewer', ['finance-reviewer']),
    )

    assert.deepEqual(messages(), [
      `${realmFile}:${lineOf(text, '"username": "finance-reviewer"')}: finance-reviewer is in ${realmFile} but not in the development users table of ${security}`,
    ])
  })

  test('reports roles that differ, naming both lists on the table row', () => {
    writeSecurity(userRow('requester', ['employee']), userRow('manager', ['department-manager']))

    assert.deepEqual(messages(), [
      `${security}:9: requester has the roles employee in the table and employee, requester in ${realmFile}`,
    ])
  })

  test('reports an id that differs from the username on the username line', () => {
    const text = writeRealm(realmUser('requester', ['employee', 'requester']), realmUser('manager', ['department-manager'], 'manager-id'))

    assert.deepEqual(messages(), [
      `${realmFile}:${lineOf(text, '"username": "manager"')}: manager has the id 'manager-id': a development user's id is its username`,
    ])
  })

  test('reports a missing development users section on line 1', () => {
    write(security, '# Security\n\nNo table.\n')

    assert.deepEqual(messages(), [
      `${security}:1: needs a ### Development users section with a User and Roles table, checked against ${realmFile}`,
    ])
  })

  test('skips the check when the realm file is missing', () => {
    rmSync(path.join(root, realmFile))

    assert.deepEqual(messages(), [])
  })
})

describe('the repository development users', () => {
  test('finds no problem in the development users table or the development realm', () => {
    const found = checkDocs(repositoryRoot).filter(
      ({ file }) => file === 'docs/reference/security.md' || file === realmFile,
    )

    assert.deepEqual(found, [])
  })
})

describe('CLI', () => {
  test('prints each problem with file and line and exits 1', () => {
    write('docs/reference/orphan.md', '# Orphan\n')

    const result = spawnSync(process.execPath, [script, '--root', root], { encoding: 'utf8' })

    assert.equal(result.status, 1)
    assert.equal(result.stderr, 'docs/README.md:1: does not list docs/reference/orphan.md\n')
  })

  test('passes on the repository', () => {
    const result = spawnSync(process.execPath, [script, '--root', repositoryRoot], { encoding: 'utf8' })

    assert.equal(result.stderr, '')
    assert.equal(result.status, 0)
  })
})
