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
  'shell.value.showAs': 'Show as',
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
  it('shows a JSON body as a table with a Table and JSON switch', () => {
    const { container } = renderBody('{"title":"Paper","lines":[{"quantity":2}]}')

    expect(screen.getByRole('cell', { name: 'lines[0].quantity' })).toBeInTheDocument()
    expect(container.querySelector('pre')).toBeNull()
    expect(screen.getByRole('radiogroup', { name: 'Show as' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Table' })).toBeChecked()
  })

  it('shows the body as indented JSON in a code block when the person picks JSON', async () => {
    const { container } = renderBody('{"title":"Paper"}')

    await userEvent.click(screen.getByText('JSON'))

    expect(container.querySelector('pre')).toHaveTextContent('"title": "Paper"')
    expect(container.querySelector('pre')?.textContent).toBe('{\n  "title": "Paper"\n}')
    expect(screen.queryByRole('cell')).not.toBeInTheDocument()
  })

  it('shows text that is not JSON as it is in a code block, with no switch', () => {
    const { container } = renderBody('Bad gateway')

    expect(screen.getByTestId('body').tagName).toBe('PRE')
    expect(container.querySelector('pre')).toHaveTextContent('Bad gateway')
    expect(screen.queryByRole('radiogroup')).not.toBeInTheDocument()
  })

  it('shows a JSON scalar in a code block, as a table needs names', () => {
    const { container } = renderBody('"just text"')

    expect(container.querySelector('pre')).toHaveTextContent('"just text"')
    expect(screen.queryByRole('radiogroup')).not.toBeInTheDocument()
  })

  it('keeps the digits of a long number in the table', () => {
    renderBody('{"id":12345678901234567890}')

    expect(screen.getByRole('cell', { name: '12345678901234567890' })).toBeInTheDocument()
  })
})
