import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { BodyView } from './BodyView'
import { TextProvider } from './TextProvider'

const texts = {
  'shell.value.empty': 'Empty',
  'shell.value.hidden': 'Hidden',
  'shell.value.field': '1 field',
  'shell.value.fields': '{count} fields',
  'shell.value.item': '1 item',
  'shell.value.items': '{count} items',
  'shell.value.table': 'Table',
  'shell.value.json': 'JSON',
  'shell.empty.noData': 'No data yet.',
}

function renderBody(body: string) {
  return render(
    <TextProvider catalogs={[{ texts, fallbackTexts: texts }]} development={false}>
      <BodyView body={body} testId="body" />
    </TextProvider>,
  )
}

describe('BodyView', () => {
  it('shows a JSON body as a table, with Table and JSON as line tabs above it', () => {
    const { container } = renderBody('{"title":"Paper","lines":[{"quantity":2}]}')

    expect(screen.getByTestId('body')).toContainElement(screen.getByRole('tablist'))
    expect(screen.getByRole('tab', { name: 'Table' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: 'JSON' })).toHaveAttribute('aria-selected', 'false')
    expect(screen.getByRole('cell', { name: 'lines[0].quantity' })).toBeInTheDocument()
    expect(container.querySelector('pre')).toBeNull()
    expect(screen.queryByRole('radiogroup')).not.toBeInTheDocument()
  })

  it('switches between the table and the indented JSON both ways, with the chosen tab marked', async () => {
    const { container } = renderBody('{"title":"Paper"}')

    await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))

    expect(screen.getByRole('tab', { name: 'JSON' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: 'Table' })).toHaveAttribute('aria-selected', 'false')
    expect(container.querySelector('pre')?.textContent).toBe('{\n  "title": "Paper"\n}')
    expect(screen.queryByRole('cell')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('tab', { name: 'Table' }))

    expect(screen.getByRole('tab', { name: 'Table' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: 'JSON' })).toHaveAttribute('aria-selected', 'false')
    expect(screen.getByRole('cell', { name: 'title' })).toBeInTheDocument()
    expect(container.querySelector('pre')).toBeNull()
  })

  it('shows text that is not JSON as it is in a code block, with no tabs', () => {
    const { container } = renderBody('Bad gateway')

    expect(screen.getByTestId('body').tagName).toBe('PRE')
    expect(container.querySelector('pre')).toHaveTextContent('Bad gateway')
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
  })

  it('shows a JSON scalar in a code block, as a table needs names', () => {
    const { container } = renderBody('"just text"')

    expect(container.querySelector('pre')).toHaveTextContent('"just text"')
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
  })

  it('shows a JSON number in a code block, as a table needs names', () => {
    const { container } = renderBody('42')

    expect(container.querySelector('pre')).toHaveTextContent('42')
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('keeps the digits of a long number in the table', () => {
    renderBody('{"id":12345678901234567890}')

    expect(screen.getByRole('cell', { name: '12345678901234567890' })).toBeInTheDocument()
  })
})
