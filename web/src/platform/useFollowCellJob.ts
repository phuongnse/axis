import { useEffect, useRef } from 'react'
import { fetchCellJob, type CellJob } from './cell'

/** How long a view waits between two checks of a queued or running job. */
const pollDelay = 2000

/** What a view does with the job that it follows. */
export interface FollowCellJobHandlers {
  /** Runs with each read of the job. */
  onJob: (job: CellJob) => void
  /** Runs once, when the job has succeeded or failed. */
  onEnd: (job: CellJob) => void
  /** Runs when the console refuses a read. The following then stops. */
  onRefused: (refusal: 'unauthorized' | 'forbidden') => void
}

/**
 * Follows the job `jobId` of the cell console: it reads the job at once and then every 2 seconds, retries a failed
 * read and stops when the job has succeeded or failed. `null` follows nothing. Giving the same id again does not restart
 * the reads, and a view stops following by giving `null` or by unmounting.
 */
export function useFollowCellJob(jobId: string | null, handlers: FollowCellJobHandlers) {
  // The reads use the newest handlers without restarting.
  const latest = useRef(handlers)
  useEffect(() => {
    latest.current = handlers
  })

  useEffect(() => {
    if (!jobId) {
      return
    }
    const controller = new AbortController()
    let timer: ReturnType<typeof setTimeout> | undefined
    async function check(id: string) {
      let next: CellJob | undefined
      try {
        const result = await fetchCellJob(id, controller.signal)
        if (controller.signal.aborted) {
          return
        }
        if (result.kind !== 'ok') {
          latest.current.onRefused(result.kind)
          return
        }
        next = result.value
        latest.current.onJob(next)
      } catch (error: unknown) {
        if (controller.signal.aborted) {
          return
        }
        console.warn('Reading the cell job failed', error)
      }
      // A failed read tries again, like a running job.
      if (!next || next.state === 'queued' || next.state === 'running') {
        timer = setTimeout(() => void check(id), pollDelay)
      } else {
        latest.current.onEnd(next)
      }
    }
    void check(jobId)
    return () => {
      controller.abort()
      clearTimeout(timer)
    }
  }, [jobId])
}
