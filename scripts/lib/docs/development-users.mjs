// - docs/reference/security.md, when it exists with samples/keycloak/axis-realm.json or axis-operators-realm.json,
//   has a '### Development users' table that lists the same users as the users of the files that exist, except
//   service accounts. Each user has the same roles in the table as in the realm: the axis roles of the axis realm,
//   the realm roles of the operator realm. No username is in both files, and each realm user's id is its username.
import { existsSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { cellSeparator, lines } from './helpers.mjs'

export const realmFile = 'samples/keycloak/axis-realm.json'
export const operatorRealmFile = 'samples/keycloak/axis-operators-realm.json'
const securityFile = 'docs/reference/security.md'
const developmentUsersHeading = '### Development users'

/** Checks the development users table of the security reference against the users of the development realms. */
export function checkDevelopmentUsers(root, report) {
  const security = path.join(root, securityFile)
  const realms = [
    { file: realmFile, roles: (user) => user.clientRoles?.axis ?? [] },
    { file: operatorRealmFile, roles: (user) => user.realmRoles ?? [] },
  ].filter(({ file }) => existsSync(path.join(root, file)))
  if (!existsSync(security) || realms.length === 0) return

  const realmNames = realms.map(({ file }) => file).join(' or ')
  const text = lines(security)
  const heading = text.findIndex((line) => line === developmentUsersHeading)
  const header = heading < 0 ? -1 : text.findIndex((line, i) => i > heading && line.startsWith('|'))
  const columns = header < 0 ? [] : text[header].split(cellSeparator).map((cell) => cell.trim())
  const userColumn = columns.indexOf('User')
  const rolesColumn = columns.indexOf('Roles')
  if (userColumn < 0 || rolesColumn < 0) {
    report(security, 1, `needs a ${developmentUsersHeading} section with a User and Roles table, checked against ${realmNames}`)
    return
  }

  const table = new Map()
  for (let i = header + 1; i < text.length && text[i].startsWith('|'); i++) {
    const cells = text[i].split(cellSeparator)
    const user = /`([^`]+)`/.exec(cells[userColumn] ?? '')?.[1]
    if (!user) continue
    const roles = [...(cells[rolesColumn] ?? '').matchAll(/`([^`]+)`/g)].map((match) => match[1])
    table.set(user, { line: i + 1, roles })
  }

  const realmUsers = new Map()
  for (const { file, roles } of realms) {
    const realmPath = path.join(root, file)
    const realmLines = lines(realmPath)
    const realmLine = (username) => {
      const index = realmLines.findIndex((line) => line.includes(`"username": "${username}"`))
      return index < 0 ? 1 : index + 1
    }
    const users = (JSON.parse(readFileSync(realmPath, 'utf8')).users ?? []).filter((user) => !user.serviceAccountClientId)
    for (const user of users) {
      const line = realmLine(user.username)
      if (realmUsers.has(user.username)) {
        report(realmPath, line, `${user.username} is in ${realmUsers.get(user.username).file} too: a username is in one development realm only`)
        continue
      }
      realmUsers.set(user.username, { file, roles: roles(user) })
      if (!table.has(user.username)) {
        report(realmPath, line, `${user.username} is in ${file} but not in the development users table of ${securityFile}`)
      }
      if (user.id !== user.username) report(realmPath, line, `${user.username} has the id '${user.id}': a development user's id is its username`)
    }
  }

  for (const [user, { line, roles }] of table) {
    const realmUser = realmUsers.get(user)
    if (!realmUser) {
      report(security, line, `${user} is in the development users table but not in ${realmNames}`)
      continue
    }
    const tableRoles = [...roles].sort().join(', ')
    const realmRoles = [...realmUser.roles].sort().join(', ')
    if (tableRoles !== realmRoles) {
      report(security, line, `${user} has the roles ${tableRoles || 'none'} in the table and ${realmRoles || 'none'} in ${realmUser.file}`)
    }
  }
}
