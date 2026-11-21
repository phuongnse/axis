import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router'
import type { StepRun } from './inspection'

/** What {@link useStepRunAddress} gives: the step and run the address names, and how to open and close them. */
export interface StepRunAddress {
  /** The step the address names, or `undefined` when it has none, or names a step the graph does not have. */
  step: string | undefined
  /** The run the address names, else the step's latest run. `undefined` for a step with no run. */
  run: StepRun | undefined
  /** Opens a step's run: the latest, or the one numbered `number`. Each opening adds a history entry. */
  open: (step: string, number?: number) => void
  /** Shuts the panel: removes the step, the run and the panel's tab from the address. */
  close: () => void
  /** The address of a step's run, such as `?step=check&run=1`, for a link to it, keeping the address's other parameters. */
  search: (step: string, number: number) => string
}

/**
 * The step and the run that a process instance's side panel shows, kept in the address as `step`, the step's name, and
 * `run`, the run's number, see docs/reference/process-inspector-widget.md#instance-page. `run` is left out for the
 * step's latest run, and an unknown or missing `run` names the latest. A `step` that is not in `stepNames` is ignored.
 * Opening another run drops `stepTab`, so it opens on its first tab, and so does `close`. The tab itself is kept by
 * `useAddressTab('stepTab', …)`.
 */
export function useStepRunAddress(runs: readonly StepRun[], stepNames: readonly string[]): StepRunAddress {
  const [searchParams, setSearchParams] = useSearchParams()
  const named = searchParams.get('step')
  const step = named !== null && stepNames.includes(named) ? named : undefined
  const number = Number(searchParams.get('run'))
  const run = useMemo(() => {
    const own = runs.filter((candidate) => candidate.step === step)
    return own.find((candidate) => candidate.number === number) ?? own.at(-1)
  }, [runs, step, number])

  const address = useCallback(
    (name: string, wanted?: number): URLSearchParams => {
      const next = new URLSearchParams(searchParams)
      const latest = runs.findLast((candidate) => candidate.step === name)?.number
      next.set('step', name)
      if (wanted === undefined || wanted === latest) {
        next.delete('run')
      } else {
        next.set('run', String(wanted))
      }
      next.delete('stepTab')
      return next
    },
    [runs, searchParams],
  )

  const open = useCallback(
    (name: string, wanted?: number) => setSearchParams(address(name, wanted), { preventScrollReset: true }),
    [address, setSearchParams],
  )
  const close = useCallback(() => {
    const next = new URLSearchParams(searchParams)
    next.delete('step')
    next.delete('run')
    next.delete('stepTab')
    setSearchParams(next, { preventScrollReset: true })
  }, [searchParams, setSearchParams])
  const search = useCallback((name: string, wanted: number) => `?${address(name, wanted).toString()}`, [address])

  return { step, run, open, close, search }
}
