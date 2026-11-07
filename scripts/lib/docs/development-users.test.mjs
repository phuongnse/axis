import assert from 'node:assert/strict'
import { readFileSync, rmSync } from 'node:fs'
import path from 'node:path'
import { beforeEach, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { checkDevelopmentUsers, operatorRealmFile, realmFile } from './development-users.mjs'
import { useTempRoot } from './testing.mjs'

const repositoryRoot = fileURLToPath(new URL('../../..', import.meta.url))
const fixture = useTempRoot()
const { write } = fixture
const messages = () => fixture.messages(checkDevelopmentUsers)

describe('development users', () => {
  const security = 'docs/reference/security.md'
  const userRow = (user, roles) => `| \`${user}\` | ${roles.map((role) => `\`${role}\``).join(', ')} | Samples |`
  const realmUser = (username, roles, id = username) => ({ id, username, enabled: true, clientRoles: { axis: roles } })
  const serviceAccount = { id: 'x', username: 'service-account-x', serviceAccountClientId: 'x', clientRoles: { x: ['x'] } }
  const operatorUser = (username, roles, id = username) => ({ id, username, enabled: true, realmRoles: roles })
  const lineOf = (text, needle) => text.split('\n').findIndex((line) => line.includes(needle)) + 1

  const writeSecurity = (...rows) =>
    write(security, ['# Security', '', '### Development users', '', 'Intro line.', '', '| User | Roles | Used by |', '| --- | --- | --- |', ...rows].join('\n') + '\n')
  const writeRealm = (...users) => {
    const text = JSON.stringify({ realm: 'axis', users: [...users, serviceAccount] }, null, 2) + '\n'
    write(realmFile, text)
    return text
  }

  const writeOperatorRealm = (...users) => {
    const text = JSON.stringify({ realm: 'axis-operators', users }, null, 2) + '\n'
    write(operatorRealmFile, text)
    return text
  }

  beforeEach(() => {
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
    rmSync(path.join(fixture.root(), realmFile))

    assert.deepEqual(messages(), [])
  })

  describe('with the operator realm', () => {
    beforeEach(() => {
      writeSecurity(
        userRow('requester', ['employee', 'requester']),
        userRow('manager', ['department-manager']),
        userRow('cell-operator', ['cell-operator']),
      )
      writeOperatorRealm(operatorUser('cell-operator', ['cell-operator']))
    })

    test('passes on a matching operator realm user', () => {
      assert.deepEqual(messages(), [])
    })

    test('reports an operator realm user who is not in the table, on the username line', () => {
      const text = writeOperatorRealm(operatorUser('cell-operator', ['cell-operator']), operatorUser('night-operator', ['cell-operator']))

      assert.deepEqual(messages(), [
        `${operatorRealmFile}:${lineOf(text, '"username": "night-operator"')}: night-operator is in ${operatorRealmFile} but not in the development users table of ${security}`,
      ])
    })

    test('reports a table user who is in no realm file, naming both files', () => {
      writeSecurity(
        userRow('requester', ['employee', 'requester']),
        userRow('manager', ['department-manager']),
        userRow('cell-operator', ['cell-operator']),
        userRow('ghost', ['cell-operator']),
      )

      assert.deepEqual(messages(), [`${security}:12: ghost is in the development users table but not in ${realmFile} or ${operatorRealmFile}`])
    })

    test('reports a realm role mismatch, naming the operator realm file', () => {
      writeOperatorRealm(operatorUser('cell-operator', ['cell-operator', 'auditor']))

      assert.deepEqual(messages(), [
        `${security}:11: cell-operator has the roles cell-operator in the table and auditor, cell-operator in ${operatorRealmFile}`,
      ])
    })

    test('reads the realm roles, not the axis client roles, of an operator realm user', () => {
      writeOperatorRealm({ ...operatorUser('cell-operator', []), clientRoles: { axis: ['cell-operator'] } })

      assert.deepEqual(messages(), [
        `${security}:11: cell-operator has the roles cell-operator in the table and none in ${operatorRealmFile}`,
      ])
    })

    test('reports an id that differs from the username, on the operator realm file', () => {
      const text = writeOperatorRealm(operatorUser('cell-operator', ['cell-operator'], 'op-1'))

      assert.deepEqual(messages(), [
        `${operatorRealmFile}:${lineOf(text, '"username": "cell-operator"')}: cell-operator has the id 'op-1': a development user's id is its username`,
      ])
    })

    test('reports a username that both realm files hold', () => {
      writeRealm(realmUser('requester', ['employee', 'requester']), realmUser('manager', ['department-manager']), realmUser('cell-operator', ['cell-operator']))
      const text = readFileSync(path.join(fixture.root(), operatorRealmFile), 'utf8')

      assert.deepEqual(messages(), [
        `${operatorRealmFile}:${lineOf(text, '"username": "cell-operator"')}: cell-operator is in ${realmFile} too: a username is in one development realm only`,
      ])
    })

    test('skips only the operator realm file when it is missing', () => {
      rmSync(path.join(fixture.root(), operatorRealmFile))

      assert.deepEqual(messages(), [`${security}:11: cell-operator is in the development users table but not in ${realmFile}`])
    })

    test('checks the operator realm file alone when the axis realm file is missing', () => {
      rmSync(path.join(fixture.root(), realmFile))

      assert.deepEqual(messages(), [
        `${security}:9: requester is in the development users table but not in ${operatorRealmFile}`,
        `${security}:10: manager is in the development users table but not in ${operatorRealmFile}`,
      ])
    })
  })
})

describe('the repository development users', () => {
  test('finds no problem in the development users table or the development realm', () => {
    const found = []
    checkDevelopmentUsers(repositoryRoot, (file, line, message) => found.push(`${file}:${line}: ${message}`))

    assert.deepEqual(found, [])
  })
})
