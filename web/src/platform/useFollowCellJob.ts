import { fetchCellJob, type CellJob } from './cell'
import { useFollowJob } from './useFollowJob'

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
  useFollowJob(jobId, {
    read: (signal) => fetchCellJob(jobId!, signal),
    ends: (result) => result.kind !== 'ok' || (result.value.state !== 'queued' && result.value.state !== 'running'),
    interval: 2000,
    onAnswer: (result) => {
      if (result.kind === 'ok') {
        handlers.onJob(result.value)
      }
    },
    onEnd: (result) => {
      if (result.kind !== 'ok') {
        handlers.onRefused(result.kind)
        return
      }
      handlers.onJob(result.value)
      handlers.onEnd(result.value)
    },
    onError: (error) => {
      console.warn('Reading the cell job failed', error)
      return 'retry'
    },
  })
}
