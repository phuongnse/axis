import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { TextTable, type TextColumn } from './TextTable'

const columns: TextColumn[] = [
  { id: 'en-id', locale: 'en', texts: { greeting: 'Hello', farewell: 'Goodbye' }, editable: true },
  { id: 'vi-id', locale: 'vi', texts: { greeting: 'Xin chào', farewell: 'Tạm biệt' }, editable: false },
]

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

type Add = (key: string, texts: Record<string, string>) => Promise<'added' | 'invalid' | 'failed'>

function renderTable(data: TextColumn[] = columns, onChange = vi.fn(), onAdd = vi.fn<Add>(async () => 'added')) {
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <TextTable columns={data} onChange={onChange} onAdd={onAdd} />
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>,
  )
  return { onChange, onAdd }
}

function shownKeys() {
  return screen.queryAllByTestId('studio-text-row').map((row) => row.getAttribute('data-key'))
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('TextTable in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('renders inside the theme root with its headers and cells', () => {
    renderTable()

    const table = screen.getByTestId('studio-text-table')
    expect(table.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(table).toBeVisible()
    for (const name of ['Key', 'en', 'vi']) {
      expect(within(table).getByRole('columnheader', { name })).toBeVisible()
    }
    expect(shownKeys()).toEqual(['farewell', 'greeting'])
    const greeting = screen.getAllByTestId('studio-text-row')[1]
    expect(within(greeting).getByRole('textbox', { name: 'en' })).toHaveValue('Hello')
    expect(within(greeting).getByText('Xin chào')).toBeVisible()
  })
})

describe('TextTable', () => {
  it('filters the rows by key or by the text of any locale, ignoring letter case', async () => {
    renderTable()
    const search = screen.getByRole('searchbox', { name: 'Search keys and texts' })

    await userEvent.type(search, 'XIN')
    expect(shownKeys()).toEqual(['greeting'])

    await userEvent.clear(search)
    await userEvent.type(search, 'FARE')
    expect(shownKeys()).toEqual(['farewell'])

    await userEvent.clear(search)
    await userEvent.type(search, 'goodBYE')
    expect(shownKeys()).toEqual(['farewell'])
  })

  it('edits the open column and sets an empty string when a cell is cleared', async () => {
    const { onChange } = renderTable()
    const row = screen.getAllByTestId('studio-text-row')[1]
    const cell = within(row).getByRole('textbox', { name: 'en' })

    await userEvent.type(cell, '!')
    expect(onChange).toHaveBeenLastCalledWith('/texts/greeting', 'Hello!')
    await userEvent.clear(cell)
    expect(onChange).toHaveBeenLastCalledWith('/texts/greeting', '')
    expect(within(row).queryByRole('textbox', { name: 'vi' })).not.toBeInTheDocument()
  })

  it('marks a key that a column lacks as missing, and an editable cell adds it when typed in', async () => {
    const { onChange } = renderTable([
      { id: 'en-id', locale: 'en', texts: { greeting: 'Hello' }, editable: true },
      { id: 'vi-id', locale: 'vi', texts: { greeting: 'Xin chào', 'a/b~c': 'x' }, editable: false },
    ])
    const row = screen.getAllByTestId('studio-text-row')[0]

    expect(within(row).getByText('Missing')).toBeVisible()
    await userEvent.type(within(row).getByRole('textbox', { name: 'en' }), 'y')
    expect(onChange).toHaveBeenLastCalledWith('/texts/a~1b~0c', 'y')
  })

  it('adds a key with a text for every locale and clears the inputs', async () => {
    const { onAdd } = renderTable()
    const add = screen.getByTestId('studio-text-add')

    await userEvent.type(within(add).getByRole('textbox', { name: 'New key' }), ' hi ')
    await userEvent.type(within(add).getByRole('textbox', { name: 'en' }), 'Hi')
    await userEvent.click(within(add).getByRole('button', { name: 'Add key' }))

    expect(onAdd).toHaveBeenCalledWith('hi', { en: 'Hi', vi: '' })
    expect(within(add).getByRole('textbox', { name: 'New key' })).toHaveValue('')
    expect(within(add).getByRole('textbox', { name: 'en' })).toHaveValue('')
  })

  it('refuses a key that exists, an empty key and a key with a slash, and calls nothing', async () => {
    const { onAdd } = renderTable()
    const add = screen.getByTestId('studio-text-add')
    const key = within(add).getByRole('textbox', { name: 'New key' })
    const button = within(add).getByRole('button', { name: 'Add key' })

    await userEvent.type(key, 'greeting')
    await userEvent.click(button)
    expect(within(add).getByText('This key exists.')).toBeVisible()

    await userEvent.clear(key)
    await userEvent.click(button)
    expect(within(add).getByText("Enter a key without '/'.")).toBeVisible()

    await userEvent.type(key, 'a/b')
    await userEvent.click(button)
    expect(within(add).getByText("Enter a key without '/'.")).toBeVisible()
    expect(onAdd).not.toHaveBeenCalled()
  })

  it('keeps the inputs and shows a failure when the key could not be added', async () => {
    const { onAdd } = renderTable(
      columns,
      vi.fn(),
      vi.fn<Add>(async () => 'failed'),
    )
    const add = screen.getByTestId('studio-text-add')

    await userEvent.type(within(add).getByRole('textbox', { name: 'New key' }), 'hi')
    await userEvent.click(within(add).getByRole('button', { name: 'Add key' }))

    expect(onAdd).toHaveBeenCalledTimes(1)
    expect(await within(add).findByText('The key could not be added.')).toBeVisible()
    expect(within(add).getByRole('textbox', { name: 'New key' })).toHaveValue('hi')
  })
})
