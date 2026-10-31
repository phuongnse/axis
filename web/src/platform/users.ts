import { apiFetch } from './api'

/** One user of the tenant, as served by `GET /api/users`. */
export interface UserListItem {
  id: string
  displayName: string
  email: string | null
  /** The alias of the user's first federated identity, or `null` for an account. */
  provider: string | null
  status: 'invited' | 'active' | 'disabled'
  /** When the user last signed in, as an ISO 8601 time, or `null` without a sign-in. */
  lastSignedInAt: string | null
}

/** One page of the tenant's users. */
export interface UserList {
  items: UserListItem[]
  /** How many users match the search, over all pages. */
  totalCount: number
}

/** The answer of the list route: the page, or the refusal that the page shows. */
export type UserListResult = { kind: 'ok'; list: UserList } | { kind: 'forbidden' } | { kind: 'notFound' }

/** Loads one page of the tenant's users. A 401 shows the sign-in prompt. */
export async function fetchUsers(
  query: { search: string; page: number; pageSize: number },
  signal?: AbortSignal,
): Promise<UserListResult> {
  const parameters = new URLSearchParams()
  if (query.search) {
    parameters.set('search', query.search)
  }
  parameters.set('page', String(query.page))
  parameters.set('pageSize', String(query.pageSize))
  const response = await apiFetch(`/api/users?${parameters}`, { signal })
  if (response.status === 403) {
    return { kind: 'forbidden' }
  }
  if (response.status === 404) {
    return { kind: 'notFound' }
  }
  if (!response.ok) {
    throw new Error(`Loading the users failed with status ${response.status}.`)
  }
  return { kind: 'ok', list: (await response.json()) as UserList }
}

/** A role given to a person. `applicationId` is `null` for a platform role. `declared` is false when the active release no longer declares the role. */
export interface GivenRole {
  applicationId: string | null
  role: string
  declared: boolean
}

/** An active application with the roles that its active release declares. */
export interface GivableApplication {
  id: string
  name: string
  roles: string[]
}

/** A person's given roles and the roles that can be given, as served by `GET /api/users/{id}/roles`. */
export interface UserRoles {
  given: GivenRole[]
  applications: GivableApplication[]
  platformRoles: string[]
}

/** The answer of the roles route: the roles, or the refusal that the panel shows. */
export type UserRolesResult = { kind: 'ok'; roles: UserRoles } | { kind: 'forbidden' } | { kind: 'notFound' }

/** Loads a person's given roles and the roles that can be given. */
export async function fetchUserRoles(id: string, signal?: AbortSignal): Promise<UserRolesResult> {
  const response = await apiFetch(`/api/users/${encodeURIComponent(id)}/roles`, { signal })
  if (response.status === 403) {
    return { kind: 'forbidden' }
  }
  if (response.status === 404) {
    return { kind: 'notFound' }
  }
  if (!response.ok) {
    throw new Error(`Loading the roles failed with status ${response.status}.`)
  }
  return { kind: 'ok', roles: (await response.json()) as UserRoles }
}

/** The role to give or remove: an application role has `applicationId`, a platform role names its kind. */
export interface RoleChange {
  applicationId?: string
  role: string
}

/**
 * Gives or removes a role. `forbidden` is a platform role that needs a recent sign-in, and `conflict` is an
 * administrator removing their own administrator role.
 */
export async function changeUserRole(
  id: string,
  give: boolean,
  change: RoleChange,
): Promise<'ok' | 'forbidden' | 'conflict'> {
  const response = await apiFetch(`/api/users/${encodeURIComponent(id)}/${give ? 'give-role' : 'remove-role'}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(change),
  })
  if (response.status === 403) {
    return 'forbidden'
  }
  if (response.status === 409) {
    return 'conflict'
  }
  if (!response.ok) {
    throw new Error(`Changing the role failed with status ${response.status}.`)
  }
  return 'ok'
}

/** A person to invite. The `roles` are given at once, each as in the give-role route. */
export interface Invitation {
  email: string
  displayName: string
  roles: RoleChange[]
}

/** Invites a person. `forbidden` is a platform role that needs a recent sign-in, and `conflict` is an account that already uses the email. */
export async function inviteUser(invitation: Invitation): Promise<'ok' | 'forbidden' | 'conflict'> {
  const response = await apiFetch('/api/users/invite', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(invitation),
  })
  if (response.status === 403) {
    return 'forbidden'
  }
  if (response.status === 409) {
    return 'conflict'
  }
  if (!response.ok) {
    throw new Error(`Inviting the person failed with status ${response.status}.`)
  }
  return 'ok'
}

/** Sends an invitation again. `forbidden` is a caller that is not an administrator, and `conflict` is a person who cannot get a new invitation. */
export async function resendInvitation(id: string): Promise<'ok' | 'forbidden' | 'conflict'> {
  const response = await apiFetch(`/api/users/${encodeURIComponent(id)}/resend-invitation`, { method: 'POST' })
  if (response.status === 403) {
    return 'forbidden'
  }
  if (response.status === 409) {
    return 'conflict'
  }
  if (!response.ok) {
    throw new Error(`Resending the invitation failed with status ${response.status}.`)
  }
  return 'ok'
}

/** A command on a person, named as its route `POST /api/users/{id}/{command}`. */
export type UserCommand = 'disable' | 'enable' | 'reset-authenticator'

/** Runs a command on a person. `forbidden` is, for disable, a login that is not recent, and `conflict` is disabling oneself. */
export async function runUserCommand(id: string, command: UserCommand): Promise<'ok' | 'forbidden' | 'conflict'> {
  const response = await apiFetch(`/api/users/${encodeURIComponent(id)}/${command}`, { method: 'POST' })
  if (response.status === 403) {
    return 'forbidden'
  }
  if (response.status === 409) {
    return 'conflict'
  }
  if (!response.ok) {
    throw new Error(`Running ${command} failed with status ${response.status}.`)
  }
  return 'ok'
}
