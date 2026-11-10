import { renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { type FollowJobOptions, useFollowJob } from './useFollowJob'

interface Job {
  state: 'running' | 'succeeded'
}

const running: Job = { state: 'running' }
const succeeded: Job = { state: 'succeeded' }

function options(overrides: Partial<FollowJobOptions<Job>> = {}): FollowJobOptions<Job> {
  return {
    read: async () => running,
    ends: (job) => job.state === 'succeeded',
    interval: 1000,
    onEnd: vi.fn(),
    ...overrides,
  }
}

describe('useFollowJob', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('reads at once, then again each interval', async () => {
    const read = vi.fn(async () => running)
    renderHook(() => useFollowJob('job', options({ read })))

    expect(read).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(999)
    expect(read).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(1)
    expect(read).toHaveBeenCalledTimes(2)
  })

  it('waits one interval before the first read when waitFirst is set', async () => {
    const read = vi.fn(async () => running)
    renderHook(() => useFollowJob('job', options({ read, waitFirst: true })))

    expect(read).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1000)
    expect(read).toHaveBeenCalledTimes(1)
  })

  it('runs onAnswer for each answer that does not end the job', async () => {
    const answers = [running, running, succeeded]
    const read = vi.fn(async () => answers.shift()!)
    const onAnswer = vi.fn()
    renderHook(() => useFollowJob('job', options({ read, onAnswer })))

    await vi.advanceTimersByTimeAsync(2000)
    expect(onAnswer).toHaveBeenCalledTimes(2)
    expect(onAnswer).toHaveBeenNthCalledWith(1, running)
    expect(onAnswer).toHaveBeenNthCalledWith(2, running)
  })

  it('runs onEnd once with the answer that ends the job, then reads no more', async () => {
    const answers = [running, running, succeeded]
    const read = vi.fn(async () => answers.shift()!)
    const onEnd = vi.fn()
    renderHook(() => useFollowJob('job', options({ read, onEnd })))

    await vi.advanceTimersByTimeAsync(2000)
    expect(read).toHaveBeenCalledTimes(3)
    expect(onEnd).toHaveBeenCalledTimes(1)
    expect(onEnd).toHaveBeenCalledWith(succeeded, expect.any(AbortSignal))

    await vi.advanceTimersByTimeAsync(10_000)
    expect(read).toHaveBeenCalledTimes(3)
    expect(onEnd).toHaveBeenCalledTimes(1)
  })

  it('aborts the read in flight on unmount and ignores its answer', async () => {
    let signal: AbortSignal | undefined
    let answer: (job: Job) => void = () => {}
    const read = vi.fn(
      (readSignal: AbortSignal) =>
        new Promise<Job>((resolve) => {
          signal = readSignal
          answer = resolve
        }),
    )
    const onAnswer = vi.fn()
    const { unmount } = renderHook(() => useFollowJob('job', options({ read, onAnswer })))

    unmount()
    expect(signal?.aborted).toBe(true)
    answer(running)
    await vi.advanceTimersByTimeAsync(10_000)
    expect(onAnswer).not.toHaveBeenCalled()
    expect(read).toHaveBeenCalledTimes(1)
  })

  it('reads nothing while the key is null', async () => {
    const read = vi.fn(async () => running)
    renderHook(() => useFollowJob(null, options({ read })))

    await vi.advanceTimersByTimeAsync(10_000)
    expect(read).not.toHaveBeenCalled()
  })

  it('does not restart the reads when rerendered with the same key', () => {
    const read = vi.fn(async () => running)
    const { rerender } = renderHook(({ key }) => useFollowJob(key, options({ read })), {
      initialProps: { key: 'job' },
    })

    expect(read).toHaveBeenCalledTimes(1)
    rerender({ key: 'job' })
    expect(read).toHaveBeenCalledTimes(1)
  })

  it('restarts with a new key and aborts the read of the old key', async () => {
    const signals: AbortSignal[] = []
    const read = vi.fn((signal: AbortSignal) => {
      signals.push(signal)
      return new Promise<Job>(() => {})
    })
    const { rerender } = renderHook(({ key }) => useFollowJob(key, options({ read })), {
      initialProps: { key: 'first' },
    })

    rerender({ key: 'second' })
    expect(signals[0].aborted).toBe(true)
    expect(signals[1].aborted).toBe(false)
    expect(read).toHaveBeenCalledTimes(2)
  })

  it('asks again after a failed read by default', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    let calls = 0
    const read = vi.fn(async (): Promise<Job> => {
      calls += 1
      if (calls === 1) {
        throw new Error('down')
      }
      return succeeded
    })
    const onEnd = vi.fn()
    renderHook(() => useFollowJob('job', options({ read, onEnd })))

    await vi.advanceTimersByTimeAsync(1000)
    expect(read).toHaveBeenCalledTimes(2)
    expect(onEnd).toHaveBeenCalledTimes(1)
    expect(warn).toHaveBeenCalledTimes(1)
  })

  it('stops when onError returns stop', async () => {
    const read = vi.fn(async (): Promise<Job> => {
      throw new Error('down')
    })
    const onError = vi.fn(() => 'stop' as const)
    renderHook(() => useFollowJob('job', options({ read, onError })))

    await vi.advanceTimersByTimeAsync(10_000)
    expect(read).toHaveBeenCalledTimes(1)
    expect(onError).toHaveBeenCalledWith(expect.any(Error))
  })
})
