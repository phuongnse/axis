import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Select } from 'antd'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { ChoiceActions } from './ChoiceActions'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

const texts = { 'test.manageInputs': 'Manage saved inputs…' }

function ChoiceSelect({ onChange, onAction }: { onChange: () => void; onAction: () => void }) {
  const [open, setOpen] = useState(true)
  return (
    <Select
      aria-label="Saved input"
      open={open}
      onOpenChange={setOpen}
      virtual={false}
      defaultValue="first"
      onChange={onChange}
      options={[
        { value: 'first', label: 'First input' },
        { value: 'second', label: 'Second input' },
      ]}
      popupRender={(list) => (
        <ChoiceActions
          list={list}
          actions={[{ key: 'manage', labelKey: 'test.manageInputs', onClick: onAction }]}
          close={() => setOpen(false)}
        />
      )}
    />
  )
}

function renderSelect() {
  const onChange = vi.fn()
  const onAction = vi.fn()
  render(
    <TextProvider catalogs={[{ texts, fallbackTexts: texts }]} development={false}>
      <ThemeModeProvider>
        <ChoiceSelect onChange={onChange} onAction={onAction} />
      </ThemeModeProvider>
    </TextProvider>,
  )
  return { onChange, onAction }
}

const follows = (earlier: Element, later: Element) =>
  Boolean(earlier.compareDocumentPosition(later) & Node.DOCUMENT_POSITION_FOLLOWING)

describe('ChoiceActions', () => {
  it('lists the choices, then a separator, then the actions', async () => {
    renderSelect()

    const choices = Array.from(document.querySelectorAll('.ant-select-item-option'))
    const separator = await screen.findByRole('separator')
    const action = await screen.findByRole('menuitem', { name: 'Manage saved inputs…' })

    expect(choices.map((choice) => choice.textContent)).toEqual(['First input', 'Second input'])
    expect(follows(choices[1], separator)).toBe(true)
    expect(follows(separator, action)).toBe(true)
  })

  it('runs a chosen action once and closes the list, with the select keeping its value', async () => {
    const { onChange, onAction } = renderSelect()

    await userEvent.click(await screen.findByRole('menuitem', { name: 'Manage saved inputs…' }))

    expect(onAction).toHaveBeenCalledTimes(1)
    expect(onChange).not.toHaveBeenCalled()
    expect(screen.getByRole('combobox', { name: 'Saved input' }).closest('.ant-select')).toHaveTextContent(
      'First input',
    )
    await waitFor(() =>
      expect(screen.getByRole('combobox', { name: 'Saved input' })).toHaveAttribute('aria-expanded', 'false'),
    )
  })
})
