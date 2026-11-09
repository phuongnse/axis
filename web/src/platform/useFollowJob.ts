import { useEffect, useRef } from 'react'

/** What a screen gives the hook to ask for a job's state until the job ends. */
export interface FollowJobOptions<T> {
  /** Reads the job once. */
  read: (signal: AbortSignal) => Promise<T>
  /** Whether the answer ends the following. */
  ends: (answer: T) => boolean
  /** Milliseconds between two reads. */
  interval: number
  /** Waits one interval before the first read. */
  waitFirst?: boolean
  /** Runs with each answer that does not end the following. */
  onAnswer?: (answer: T) => void
  /** Runs once with the answer that ends the following. */
  onEnd: (answer: T, signal: AbortSignal) => void | Promise<void>
  /** Runs when a read or a handler throws. 'stop' ends the following, anything else asks again. */
  onError?: (error: unknown) => 'retry' | 'stop'
}

/**
 * Asks for a job's state at once, then every `interval` until an answer ends it, and then runs `onEnd` once. A failed
 * read asks again unless `onError` stops it. A `null` key follows nothing. Giving the same key again does not restart
 * the reads, and unmounting or changing the key aborts the read in flight.
 */
export function useFollowJob<T>(key: string | null, options: FollowJobOptions<T>): void {
  // The reads use the newest options without restarting.
  const latest = useRef(options)
  useEffect(() => {
    latest.current = options
  })

  useEffect(() => {
    if (key === null) {
      return
    }
    const controller = new AbortController()
    let timer: ReturnType<typeof setTimeout> | undefined

    function schedule() {
      timer = setTimeout(() => void check(), latest.current.interval)
    }

    async function check() {
      const { read, ends, onAnswer, onEnd, onError } = latest.current
      try {
        const answer = await read(controller.signal)
        if (controller.signal.aborted) {
          return
        }
        if (!ends(answer)) {
          onAnswer?.(answer)
          schedule()
          return
        }
        await onEnd(answer, controller.signal)
      } catch (error: unknown) {
        if (controller.signal.aborted) {
          return
        }
        if (onError === undefined) {
          console.warn('Reading the job failed', error)
          schedule()
        } else if (onError(error) === 'retry') {
          schedule()
        }
      }
    }

    if (latest.current.waitFirst) {
      schedule()
    } else {
      void check()
    }
    return () => {
      controller.abort()
      clearTimeout(timer)
    }
  }, [key])
}
