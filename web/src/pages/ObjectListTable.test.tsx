import { render, screen, within } from '@testing-library/react'
import type { ComponentProps } from 'react'
import userEvent from '@testing-library/user-event'
import { Input } from 'antd'
import { describe, expect, it, vi } from 'vitest'
import english from '../../../src/Axis.Presentation/Texts/en.json'
import { designTokensFor } from '../platform/designTokens'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { ObjectListTable, type ObjectListColumn } from './ObjectListTable'

const items = [
  { name: 'budget', version: '^1.0.0' },
  { name: 'leave', version: '^2.0.0' },
]

const columns: ObjectListColumn[] = [
  { key: 'name', title: 'Module', kind: 'freeText' },
  { key: 'version', title: 'Version', kind: 'fixed' },
  { key: 'connectors', title: 'Connectors', kind: 'freeText' },
]

function renderList(onChange = vi.fn(), large = false, extra: Partial<ComponentProps<typeof ObjectListTable>> = {}) {
  render(
    <ThemeModeProvider initialMode="light">
      <TextProvider catalogs={[{ texts: english, fallbackTexts: english }]} development={false}>
        <ObjectListTable
          pointer="/modules"
          items={items}
          columns={columns}
          itemName="module"
          required={['name']}
          large={large}
          newItem={() => ({ name: '', version: '' })}
          onChange={onChange}
          fields={(copy, change) => (
            <>
              {columns.map((column) => (
                <Input
                  key={column.key}
                  aria-label={column.title}
                  value={String(copy[column.key] ?? '')}
                  onChange={(event) => change(`/${column.key}`, event.target.value)}
                />
              ))}
            </>
          )}
          {...extra}
        />
      </TextProvider>
    </ThemeModeProvider>,
  )
  return onChange
}

const bodyRows = () => screen.getAllByRole('row').slice(1)

describe('the list of objects', () => {
  it('adds no row until Done, and Cancel leaves the list as it was', async () => {
    const onChange = renderList()

    await userEvent.click(screen.getByRole('button', { name: 'Add module…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add module' })
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Module' }), 'payroll')

    expect(bodyRows()).toHaveLength(2)
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    expect(onChange).not.toHaveBeenCalled()
    expect(bodyRows()).toHaveLength(2)
  })

  it('writes the list with the new item with one change on Done of Add', async () => {
    const onChange = renderList()

    await userEvent.click(screen.getByRole('button', { name: 'Add module…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add module' })
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Module' }), 'payroll')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Done' }))

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('/modules', [...items, { name: 'payroll', version: '' }])
  })

  it('edits a copy and writes the whole item with one change on Done of Edit', async () => {
    const onChange = renderList()

    await userEvent.click(screen.getByRole('button', { name: 'budget' }))
    const dialog = await screen.findByRole('dialog', { name: 'Edit module' })
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Version' }), '.1')

    expect(onChange).not.toHaveBeenCalled()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Done' }))

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('/modules/0', { name: 'budget', version: '^1.0.0.1' })
  })

  it('turns Done off while a required field of the copy is empty', async () => {
    renderList()

    await userEvent.click(screen.getByRole('button', { name: 'budget' }))
    const dialog = await screen.findByRole('dialog', { name: 'Edit module' })
    const name = within(dialog).getByRole('textbox', { name: 'Module' })
    await userEvent.clear(name)

    expect(within(dialog).getByRole('button', { name: 'Done' })).toBeDisabled()
    await userEvent.type(name, 'budget2')

    expect(within(dialog).getByRole('button', { name: 'Done' })).toBeEnabled()
  })

  it('leaves out a column that is empty on every row, while the dialog keeps its field', async () => {
    renderList()

    expect(screen.getByRole('columnheader', { name: 'Version' })).toBeInTheDocument()
    expect(screen.queryByRole('columnheader', { name: 'Connectors' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'budget' }))
    const dialog = await screen.findByRole('dialog', { name: 'Edit module' })

    expect(within(dialog).getByRole('textbox', { name: 'Connectors' })).toBeInTheDocument()
  })

  it('writes nothing at the item on Remove', async () => {
    const onChange = renderList()

    await userEvent.click(screen.getByRole('button', { name: 'Remove 2' }))

    expect(onChange).toHaveBeenCalledWith('/modules/1', undefined)
  })

  it('opens a large dialog for an item that holds a list or a map', async () => {
    renderList(vi.fn(), true)

    await userEvent.click(screen.getByRole('button', { name: 'Add module…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add module' })

    expect(dialog.closest('.ant-modal')).toHaveStyle({ width: `${designTokensFor('light').size.dialog.large}px` })
  })

  it('calls onDone right after Done writes the item, and onCancel on Cancel, not the other', async () => {
    const calls: string[] = []
    const onChange = vi.fn(() => calls.push('change'))
    renderList(onChange, false, { onDone: () => calls.push('done'), onCancel: () => calls.push('cancel') })

    await userEvent.click(screen.getByRole('button', { name: 'budget' }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }))
    expect(calls).toEqual(['cancel'])

    await userEvent.click(screen.getByRole('button', { name: 'budget' }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Done' }))
    expect(calls).toEqual(['cancel', 'change', 'done'])
  })

  it('draws Add… into the element it is given and leaves the toolbar out', async () => {
    const slot = document.createElement('div')
    document.body.append(slot)
    const onChange = renderList(vi.fn(), false, { addIn: slot })

    const add = within(slot).getByRole('button', { name: 'Add module…' })
    expect(screen.getAllByRole('button', { name: 'Add module…' })).toHaveLength(1)
    await userEvent.click(add)
    expect(await screen.findByRole('dialog', { name: 'Add module' })).toBeVisible()
    expect(onChange).not.toHaveBeenCalled()
    slot.remove()
  })

  it('shows no Add… until the element it is given exists', () => {
    renderList(vi.fn(), false, { addIn: null })

    expect(screen.queryByRole('button', { name: 'Add module…' })).toBeNull()
  })

  it('draws the mark of a row after the link in its first cell', () => {
    renderList(vi.fn(), false, { firstCellMark: (index) => (index === 1 ? <span>Added</span> : null) })

    expect(within(bodyRows()[0]).queryByText('Added')).toBeNull()
    expect(within(bodyRows()[1]).getByText('Added')).toBeVisible()
  })

  it('gives the fields the index of the item to edit', async () => {
    const indexes: number[] = []
    renderList(vi.fn(), false, {
      fields: (_copy, _change, index) => {
        indexes.push(index)
        return null
      },
    })

    await userEvent.click(screen.getByRole('button', { name: 'leave' }))
    await screen.findByRole('dialog', { name: 'Edit module' })
    expect(indexes.at(-1)).toBe(1)
  })

  it('gives the fields the length of the list as the index of an item to add', async () => {
    const indexes: number[] = []
    renderList(vi.fn(), false, {
      fields: (_copy, _change, index) => {
        indexes.push(index)
        return null
      },
    })

    await userEvent.click(screen.getByRole('button', { name: 'Add module…' }))
    await screen.findByRole('dialog', { name: 'Add module' })
    expect(indexes.at(-1)).toBe(2)
  })
})
