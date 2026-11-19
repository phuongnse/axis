import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { EventTime } from './EventTime'
import { formatValue } from './formatValue'
import { TextProvider } from './TextProvider'

const texts = { 'shell.value.underSecond': '< 1 sec' }
const now = Date.parse('2026-03-20T12:00:00Z')
const minute = 60_000
const day = 24 * 60 * minute

function renderAt(age: number) {
  const value = new Date(now - age).toISOString()
  render(
    <TextProvider catalogs={[{ texts, fallbackTexts: texts }]} development={false}>
      <EventTime value={value} />
    </TextProvider>,
  )
  return value
}

// Only the clock and its interval are faked, so Ant Design's tooltip timers keep running.
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] })
  vi.setSystemTime(now)
})
afterEach(() => {
  vi.useRealTimers()
})

describe('EventTime', () => {
  it('reads a time within a week as how long ago and moves the words on once a minute', () => {
    renderAt(5 * minute)
    expect(screen.getByText(/5 minutes ago/)).toBeInTheDocument()

    act(() => {
      vi.advanceTimersByTime(minute)
    })

    expect(screen.getByText(/6 minutes ago/)).toBeInTheDocument()
  })

  it('reads a time from a week on as its date and time', () => {
    const value = renderAt(8 * day)

    const exact = formatValue({ name: 'time', type: 'date-time' }, value, {}, { locale: 'en', text: (key) => key })
    expect(screen.getByText(exact)).toBeInTheDocument()
    expect(screen.queryByText(/ago/)).not.toBeInTheDocument()
  })

  it('has the exact time as its tooltip and reads it after the words', async () => {
    const value = renderAt(5 * minute)
    const exact = formatValue({ name: 'time', type: 'date-time' }, value, {}, { locale: 'en', text: (key) => key })

    const time = screen.getByText(/5 minutes ago/)
    expect(time).toHaveTextContent(`5 minutes ago ${exact}`)
    expect(time).toHaveAttribute('datetime', value)
    await userEvent.setup({ advanceTimers: (ms) => void vi.advanceTimersByTime(ms) }).hover(time)

    expect(await screen.findByRole('tooltip')).toHaveTextContent(exact)
  })
})
