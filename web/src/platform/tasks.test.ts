import { describe, expect, it, vi } from 'vitest'
import { assignLocation } from './navigation'
import { completeTask, fetchTask, fetchTasks, signInRequired } from './tasks'

vi.mock('./navigation')

describe('task API calls', () => {
  it('do not start sign-in on a 401, because the task API still reads only the test user', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify({ title: 'Sign in to work on tasks.' }), { status: 401 })),
    )

    expect(await fetchTasks('/api/tasks', '')).toBe(signInRequired)
    expect(await fetchTask('/api/tasks', 'task-1')).toBe(signInRequired)
    await expect(completeTask('/api/tasks', 'task-1', '{}')).rejects.toThrow('status 401')
    expect(assignLocation).not.toHaveBeenCalled()
  })
})
