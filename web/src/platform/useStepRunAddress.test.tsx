import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it } from 'vitest'
import type { StepRun } from './inspection'
import { useStepRunAddress } from './useStepRunAddress'

const run = (step: string, number: number): StepRun => ({
  step,
  number,
  state: 'done',
  entries: [],
  startedAt: null,
  finishedAt: null,
})
const runs = [run('again', 1), run('check', 1), run('again', 2)]
const stepNames = ['again', 'check', 'done']

function Probe() {
  const address = useStepRunAddress(runs, stepNames)
  const location = useLocation()
  return (
    <>
      <output data-testid="step">{address.step ?? 'none'}</output>
      <output data-testid="run">{address.run ? `${address.run.step} ${address.run.number}` : 'none'}</output>
      <output data-testid="url">{location.pathname + location.search}</output>
      <output data-testid="link">{address.search('check', 1)}</output>
      <button type="button" onClick={() => address.open('again')}>
        open latest
      </button>
      <button type="button" onClick={() => address.open('again', 1)}>
        open first
      </button>
      <button type="button" onClick={() => address.open('again', 2)}>
        open second
      </button>
      <button type="button" onClick={address.close}>
        close
      </button>
    </>
  )
}

function renderProbe(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Probe />
    </MemoryRouter>,
  )
}

describe('useStepRunAddress', () => {
  it('has no step and no run without the parameters', () => {
    renderProbe('/page')

    expect(screen.getByTestId('step')).toHaveTextContent('none')
    expect(screen.getByTestId('run')).toHaveTextContent('none')
  })

  it('names the latest run of the step that the address names', () => {
    renderProbe('/page?step=again')

    expect(screen.getByTestId('step')).toHaveTextContent('again')
    expect(screen.getByTestId('run')).toHaveTextContent('again 2')
  })

  it('names the run that the address numbers, and the latest for a number that is no run', () => {
    renderProbe('/page?step=again&run=1')
    expect(screen.getByTestId('run')).toHaveTextContent('again 1')
  })

  it.each(['9', 'x', ''])('names the latest run for the run %j that is no run', (value) => {
    renderProbe(`/page?step=again&run=${value}`)

    expect(screen.getByTestId('run')).toHaveTextContent('again 2')
  })

  it('ignores a step the graph does not have', () => {
    renderProbe('/page?step=nowhere')
    expect(screen.getByTestId('step')).toHaveTextContent('none')
  })

  it('names a step of the graph that has no run, with no run', () => {
    renderProbe('/page?step=done')

    expect(screen.getByTestId('step')).toHaveTextContent('done')
    expect(screen.getByTestId('run')).toHaveTextContent('none')
  })

  it('leaves run out for the latest run, writes it for another, and keeps the other parameters', async () => {
    renderProbe('/page?tab=history')

    await userEvent.click(screen.getByRole('button', { name: 'open latest' }))
    expect(screen.getByTestId('url')).toHaveTextContent('/page?tab=history&step=again')

    await userEvent.click(screen.getByRole('button', { name: 'open first' }))
    expect(screen.getByTestId('url')).toHaveTextContent('/page?tab=history&step=again&run=1')

    await userEvent.click(screen.getByRole('button', { name: 'open second' }))
    expect(screen.getByTestId('url')).toHaveTextContent('/page?tab=history&step=again')
  })

  it('drops stepTab when another run opens', async () => {
    renderProbe('/page?step=again&stepTab=error')

    await userEvent.click(screen.getByRole('button', { name: 'open first' }))

    expect(screen.getByTestId('url')).toHaveTextContent('/page?step=again&run=1')
  })

  it('removes step, run and stepTab on close, and keeps the other parameters', async () => {
    renderProbe('/page?tab=history&step=again&run=1&stepTab=error')

    await userEvent.click(screen.getByRole('button', { name: 'close' }))

    expect(screen.getByTestId('url')).toHaveTextContent('/page?tab=history')
    expect(screen.getByTestId('step')).toHaveTextContent('none')
  })

  it('gives the address of a run for a link, with no stepTab', () => {
    renderProbe('/page?tab=history&step=again&stepTab=error')

    expect(screen.getByTestId('link')).toHaveTextContent('?tab=history&step=check')
  })
})
