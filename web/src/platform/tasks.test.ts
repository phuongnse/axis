import { beforeEach, describe, expect, it, vi } from 'vitest'
import { assignLocation } from './navigation'
import { fetchTask, fetchTasks, reassignTask, searchUsers, usersPath } from './tasks'

vi.mock('./navigation')

beforeEach(() => {
  vi.mocked(assignLocation).mockReset()
  history.pushState(null, '', '/e2e/tasks?pageSize=100')
})

describe('task API calls', () => {
  it('start sign-in on a 401 with the current path and query string, and throw', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () => new Response(JSON.stringify({ title: 'Sign in to read or write this data.' }), { status: 401 }),
      ),
    )

    await expect(fetchTasks('/api/tasks', 'pageSize=100')).rejects.toThrow('status 401')
    await expect(fetchTask('/api/tasks', 'task-1')).rejects.toThrow('status 401')
    expect(assignLocation).toHaveBeenCalledTimes(2)
    expect(assignLocation).toHaveBeenNthCalledWith(1, '/api/auth/sign-in?returnUrl=%2Fe2e%2Ftasks%3FpageSize%3D100')
    expect(assignLocation).toHaveBeenNthCalledWith(2, '/api/auth/sign-in?returnUrl=%2Fe2e%2Ftasks%3FpageSize%3D100')
  })
})

describe('reassign and the user picker', () => {
  it('derives the users address from the tasks address', () => {
    expect(usersPath('/api/apps/PurchasingApp/tasks')).toBe('/api/apps/PurchasingApp/users')
  })

  it('searches users with the encoded search text and throws on a failure', async () => {
    const fetchMock = vi.fn(
      async (_url: string) => new Response(JSON.stringify({ items: [{ id: 'u1', displayName: 'Ann' }] })),
    )
    vi.stubGlobal('fetch', fetchMock)

    await expect(searchUsers('/api/users', 'a b&c')).resolves.toEqual([{ id: 'u1', displayName: 'Ann' }])
    expect(fetchMock.mock.calls[0][0]).toBe('/api/users?search=a%20b%26c')

    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('', { status: 403 })),
    )
    await expect(searchUsers('/api/users', '')).rejects.toThrow('status 403')
  })

  it('returns the problem of a 400, the status of a 403 or 409, and throws otherwise', async () => {
    const body = { role: 'finance', reason: 'Away.' }
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(JSON.stringify({ title: 'Invalid.', errors: { '/role': ['Unknown.'] } }), { status: 400 }),
      ),
    )
    await expect(reassignTask('/api/tasks', 'task-1', body)).resolves.toEqual({
      ok: false,
      problem: { status: 400, title: 'Invalid.', errors: { '/role': ['Unknown.'] } },
    })

    for (const status of [403, 409]) {
      vi.stubGlobal(
        'fetch',
        vi.fn(async () => new Response('{}', { status })),
      )
      await expect(reassignTask('/api/tasks', 'task-1', body)).resolves.toEqual({ ok: false, status })
    }

    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('', { status: 500 })),
    )
    await expect(reassignTask('/api/tasks', 'task-1', body)).rejects.toThrow('status 500')
  })
})
