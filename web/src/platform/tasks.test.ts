import { beforeEach, describe, expect, it, vi } from 'vitest'
import { assignLocation } from './navigation'
import { fetchTask, fetchTasks } from './tasks'

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
