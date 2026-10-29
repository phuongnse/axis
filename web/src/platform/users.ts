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
