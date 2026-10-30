import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { StudioNames } from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { chooseOption, clearSelect, openDropdown, optionTexts } from '../test/antd'
import { ExpressionCheckContext } from './expressionCheckContext'
import { EndpointEvent } from './EndpointEvent'

const events: StudioNames['events'] = [
  {
    id: 'ev1',
    name: 'PaymentReceived',
    entity: 'PurchaseRequest',
    fields: [
      { name: 'amount', type: 'decimal' },
      { name: 'reference', type: 'text' },
      { name: 'note', type: 'text' },
    ],
  },
  { id: 'ev2', name: 'PaymentFailed', entity: 'PurchaseRequest', fields: [{ name: 'reason', type: 'text' }] },
]

function renderEvent(data: unknown, { names = true, onChange = vi.fn() } = {}) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <ExpressionCheckContext.Provider
          value={{ application: 'Shop', resourceId: 'bc241668', kind: 'endpoint', draft: {}, names: null }}
        >
          <EndpointEvent events={names ? events : undefined} value={data} onChange={onChange} />
        </ExpressionCheckContext.Provider>
      </TextProvider>
    </ThemeModeProvider>,
  )
  return onChange
}

beforeEach(() =>
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify({ type: 'text', diagnostics: [] }), { status: 200 })),
  ),
)

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('EndpointEvent', () => {
  it.each([['x'], [['x']]])('points to the JSON view for an event value %j that is not an object', (data) => {
    renderEvent(data)

    expect(screen.getByTestId('studio-field-/event')).toBeVisible()
    expect(screen.getByTestId('studio-json-only')).toBeVisible()
    expect(screen.queryByRole('combobox', { name: 'Event' })).toBeNull()
    expect(screen.queryByTestId('studio-field-/event/fields')).toBeNull()
  })

  it('adds the event with empty fields on a first pick', async () => {
    const onChange = renderEvent(undefined)

    await userEvent.click(screen.getByRole('combobox', { name: 'Event' }))
    await chooseOption('PaymentReceived')

    expect(onChange).toHaveBeenLastCalledWith('/event', { name: 'PaymentReceived', fields: {} })
  })

  it('types the event name while the events are unknown', async () => {
    const onChange = renderEvent(undefined, { names: false })

    await userEvent.type(screen.getByRole('textbox', { name: 'Event' }), 'P')

    expect(onChange).toHaveBeenLastCalledWith('/event', { name: 'P', fields: {} })
  })

  it('removes the whole event when the picker is cleared', async () => {
    const onChange = renderEvent({ name: 'PaymentReceived', fields: { amount: 'a' } })

    await clearSelect(screen.getByTestId('studio-field-/event/name'))

    expect(onChange).toHaveBeenLastCalledWith('/event', undefined)
  })

  it('keeps the fields when another event is picked', async () => {
    const onChange = renderEvent({ name: 'PaymentReceived', fields: { amount: 'a' } })

    await userEvent.click(screen.getByRole('combobox', { name: 'Event' }))
    await chooseOption('PaymentFailed')

    expect(onChange).toHaveBeenLastCalledWith('/event/name', 'PaymentFailed')
  })

  it('shows a row per payload field of the picked event', async () => {
    renderEvent({ name: 'PaymentReceived', fields: { amount: 'a' } })
    await screen.findAllByTestId('studio-expression-type')

    const row = screen.getByTestId('studio-field-/event/fields/amount')
    expect(within(row).getByRole('combobox', { name: 'Event field' })).toBeVisible()
    expect(within(row).getByRole('textbox', { name: 'Expression' })).toBeVisible()
    expect(within(row).getByRole('button', { name: 'Remove' })).toBeVisible()
  })

  it('lists the payload fields that have no row as the add choices', async () => {
    renderEvent({ name: 'PaymentReceived', fields: { amount: 'a' } })

    await userEvent.click(screen.getByRole('combobox', { name: 'Add mapping Event fields' }))
    await openDropdown()

    expect(optionTexts()).toEqual(['reference', 'note'])
  })

  it('adds a row for a payload field with an empty expression', async () => {
    const onChange = renderEvent({ name: 'PaymentReceived', fields: { amount: 'a' } })

    await userEvent.click(screen.getByRole('combobox', { name: 'Add mapping Event fields' }))
    await chooseOption('reference')

    expect(onChange).toHaveBeenLastCalledWith('/event/fields/reference', '')
  })

  it('renames a payload field in place, keeping the order of the others', async () => {
    const onChange = renderEvent({ name: 'PaymentReceived', fields: { amount: 'a', reference: 'r' } })

    const row = screen.getByTestId('studio-field-/event/fields/amount')
    await userEvent.click(within(row).getByRole('combobox', { name: 'Event field' }))
    await chooseOption('note')

    expect(onChange).toHaveBeenLastCalledWith('/event/fields', { note: 'a', reference: 'r' })
    expect(Object.keys(onChange.mock.calls[0][1] as object)).toEqual(['note', 'reference'])
  })

  it('removes a row by its payload field', async () => {
    const onChange = renderEvent({ name: 'PaymentReceived', fields: { amount: 'a' } })

    await userEvent.click(
      within(screen.getByTestId('studio-field-/event/fields/amount')).getByRole('button', { name: 'Remove' }),
    )

    expect(onChange).toHaveBeenLastCalledWith('/event/fields/amount', undefined)
  })
})
