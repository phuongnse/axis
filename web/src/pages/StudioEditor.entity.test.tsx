import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { StudioResourceItem } from '../platform/studio'
import { setAt } from '../platform/jsonPatch'
import {
  loadStudioSchemas,
  resources,
  slow,
  stubResource,
  renderResource,
  sentChanges,
  requests,
  sentPatch,
  pick,
} from '../test/studioEditor'

// The schema is a chunk of its own. Loading it here keeps the first test from racing its first lookup.
beforeAll(loadStudioSchemas)

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe('StudioEditor for an entity', () => {
  const designedItem: StudioResourceItem = { ...resources[1], id: 'a4e6d3a1-77a0-4b0d-8b0c-5c1d6f0a9b01' }
  const designed = {
    id: designedItem.id,
    kind: 'entity',
    name: 'Invoice',
    formatVersion: 1,
    label: { textKey: 'entity.invoice' },
    fields: [
      { name: 'total', type: 'decimal', unique: true, precision: 18, scale: 2 },
      { name: 'code', type: 'text', maxLength: 20 },
      { name: 'note', type: 'text' },
    ],
  }

  function sentFields(fetchMock: ReturnType<typeof vi.fn>): unknown {
    return sentPatch(fetchMock).find((operation) => operation.path === '/fields')!.value
  }

  it('opens in the designer and keeps the JSON tab with the draft', async () => {
    stubResource(designedItem, designed)
    renderResource(designedItem)

    const designer = await screen.findByTestId('studio-entity-designer', {}, slow)
    expect(designer).toBeVisible()
    await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))

    expect(JSON.parse((screen.getByRole('textbox', { name: 'JSON' }) as HTMLTextAreaElement).value)).toEqual(designed)
  })

  it('changes a decimal field to enum: the type changes, precision and scale go and unique stays', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    await pick(await screen.findByTestId('studio-field-/fields/0/type', {}, slow), 'enum')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([
      { op: 'replace', path: '/fields/0/type', value: 'enum' },
      { op: 'remove', path: '/fields/0/precision' },
      { op: 'remove', path: '/fields/0/scale' },
    ])
  })

  it('adds a field', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    await userEvent.click(await screen.findByRole('button', { name: 'Add field' }, slow))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentFields(fetchMock)).toEqual([...designed.fields, { name: '', type: 'text' }])
  })

  it('removes a field and, on Drop data, saves the entity and a dropField migration in one changes call', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    const row = await screen.findByTestId('studio-field-/fields/1', {}, slow)
    await userEvent.click(within(row).getByRole('button', { name: 'Remove field' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(screen.getByTestId('studio-migration-field')).toHaveTextContent('code')
    await userEvent.click(screen.getByRole('button', { name: 'Drop data' }))

    await screen.findByTestId('studio-saved')
    expect(requests(fetchMock, 'PATCH')).toBe(0)
    const [entity, migration] = sentChanges(fetchMock).changes
    expect(entity).toEqual({
      id: designedItem.id,
      version: designedItem.version,
      patch: [{ op: 'replace', path: '/fields', value: [designed.fields[0], designed.fields[2]] }],
    })
    expect(migration).toEqual({
      create: {
        id: expect.any(String),
        kind: 'migration',
        name: expect.stringMatching(/^InvoiceChange\d{14}$/),
        formatVersion: 1,
        entity: 'Invoice',
        changes: [{ type: 'dropField', field: 'code' }],
      },
    })
  })

  it('keeps a removed field when the answer is Keep field and then saves nothing', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    const row = await screen.findByTestId('studio-field-/fields/2', {}, slow)
    await userEvent.click(within(row).getByRole('button', { name: 'Remove field' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await userEvent.click(screen.getByRole('button', { name: 'Keep field' }))

    await waitFor(() => expect(screen.queryByTestId('studio-migration-question')).not.toBeInTheDocument())
    expect(requests(fetchMock, 'PATCH') + requests(fetchMock, 'POST')).toBe(0)
    expect(
      screen.getAllByRole('textbox', { name: 'Field name' }).map((box) => (box as HTMLInputElement).value),
    ).toEqual(['total', 'code', 'note'])
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('saves one patch and no changes call for a field added in the same session and then renamed', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    await userEvent.click(await screen.findByRole('button', { name: 'Add field' }, slow))
    await userEvent.type(within(screen.getByTestId('studio-field-/fields/3/name')).getByRole('textbox'), 'ab')
    await userEvent.clear(within(screen.getByTestId('studio-field-/fields/3/name')).getByRole('textbox'))
    await userEvent.type(within(screen.getByTestId('studio-field-/fields/3/name')).getByRole('textbox'), 'memo')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(screen.queryByTestId('studio-migration-question')).not.toBeInTheDocument()
    expect(requests(fetchMock, 'PATCH')).toBe(1)
    expect(requests(fetchMock, 'POST')).toBe(0)
    expect((sentFields(fetchMock) as { name: string }[]).map((field) => field.name)).toEqual([
      'total',
      'code',
      'note',
      'memo',
    ])
  })

  it('asks Keep data for a renamed field, counts a chain of renames as one and adds renameField', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    const name = within(await screen.findByTestId('studio-field-/fields/1/name', {}, slow)).getByRole('textbox')
    await userEvent.clear(name)
    await userEvent.type(name, 'abc')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(screen.getByTestId('studio-migration-field')).toHaveTextContent('code → abc')
    await userEvent.click(screen.getByRole('button', { name: 'Keep data' }))

    await screen.findByTestId('studio-saved')
    const [, migration] = sentChanges(fetchMock).changes
    expect((migration as { create: { changes: unknown } }).create.changes).toEqual([
      { type: 'renameField', from: 'code', to: 'abc' },
    ])
  })

  function migrationChanges(fetchMock: ReturnType<typeof vi.fn>): unknown {
    const [, migration] = sentChanges(fetchMock).changes
    return (migration as { create: { changes: unknown } }).create.changes
  }

  it('asks for a convert expression for a retyped field and adds convert', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    await pick(await screen.findByTestId('studio-field-/fields/2/type', {}, slow), 'integer')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(screen.getByTestId('studio-migration-field')).toHaveTextContent('note')
    expect(screen.getByRole('button', { name: 'Add convert' })).toBeDisabled()
    await userEvent.type(screen.getByRole('textbox', { name: 'Convert expression' }), 'length(note)')
    await userEvent.click(screen.getByRole('button', { name: 'Add convert' }))

    await screen.findByTestId('studio-saved')
    expect(migrationChanges(fetchMock)).toEqual([{ type: 'convert', field: 'note', value: 'length(note)' }])
  })

  it('asks for a fill expression for a field made required and adds fill', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    const row = await screen.findByTestId('studio-field-/fields/2', {}, slow)
    await userEvent.click(within(row).getByRole('checkbox', { name: 'Required' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await userEvent.type(screen.getByRole('textbox', { name: 'Fill expression' }), "'none'")
    await userEvent.click(screen.getByRole('button', { name: 'Fill nulls' }))

    await screen.findByTestId('studio-saved')
    expect(migrationChanges(fetchMock)).toEqual([{ type: 'fill', field: 'note', value: "'none'" }])
  })

  it('adds nothing for a skipped convert and keeps the draft on Cancel', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    await pick(await screen.findByTestId('studio-field-/fields/2/type', {}, slow), 'integer')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByTestId('studio-migration-question')).not.toBeInTheDocument())
    expect(requests(fetchMock, 'PATCH') + requests(fetchMock, 'POST')).toBe(0)

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await userEvent.click(screen.getByRole('button', { name: 'Skip' }))

    await screen.findByTestId('studio-saved')
    expect(requests(fetchMock, 'PATCH')).toBe(1)
    expect(requests(fetchMock, 'POST')).toBe(0)
  })

  it('warns in the row of a field switched to enum and saves one patch and no changes call', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    await pick(await screen.findByTestId('studio-field-/fields/2/type', {}, slow), 'enum')
    expect(screen.getByTestId('studio-migration-warning-/fields/2')).toBeInTheDocument()
    expect(screen.queryByTestId('studio-migration-warning-/fields/1')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(screen.queryByTestId('studio-migration-question')).not.toBeInTheDocument()
    expect(requests(fetchMock, 'PATCH')).toBe(1)
    expect(requests(fetchMock, 'POST')).toBe(0)
  })

  it('puts convert after renameField and uses the new name', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    const name = within(await screen.findByTestId('studio-field-/fields/2/name', {}, slow)).getByRole('textbox')
    await userEvent.clear(name)
    await userEvent.type(name, 'remark')
    await pick(screen.getByTestId('studio-field-/fields/2/type'), 'integer')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(screen.getByTestId('studio-migration-field')).toHaveTextContent('note → remark')
    await userEvent.click(screen.getByRole('button', { name: 'Keep data' }))
    expect(screen.getByTestId('studio-migration-field')).toHaveTextContent('remark')
    await userEvent.type(screen.getByRole('textbox', { name: 'Convert expression' }), 'length(remark)')
    await userEvent.click(screen.getByRole('button', { name: 'Add convert' }))

    await screen.findByTestId('studio-saved')
    expect(migrationChanges(fetchMock)).toEqual([
      { type: 'renameField', from: 'note', to: 'remark' },
      { type: 'convert', field: 'remark', value: 'length(remark)' },
    ])
  })

  it('asks nothing for a child-collection field', async () => {
    const withCollection = {
      ...designed,
      fields: [...designed.fields, { name: 'lines', type: 'child-collection', target: 'InvoiceLine' }],
    }
    const { fetchMock } = stubResource(designedItem, withCollection)
    renderResource(designedItem)

    const row = await screen.findByTestId('studio-field-/fields/3', {}, slow)
    await userEvent.click(within(row).getByRole('button', { name: 'Remove field' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(requests(fetchMock, 'PATCH')).toBe(1)
    expect(requests(fetchMock, 'POST')).toBe(0)
  })

  it('sends nothing and keeps the draft when the answer to a rename is Cancel', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    const name = within(await screen.findByTestId('studio-field-/fields/2/name', {}, slow)).getByRole('textbox')
    await userEvent.type(name, 's')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(screen.queryByTestId('studio-migration-question')).not.toBeInTheDocument())
    expect(requests(fetchMock, 'PATCH') + requests(fetchMock, 'POST')).toBe(0)
    expect(name).toHaveValue('notes')
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
  })

  it('moves a field up and down', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    const row = await screen.findByTestId('studio-field-/fields/1', {}, slow)
    expect(within(screen.getByTestId('studio-field-/fields/0')).getByRole('button', { name: 'Move up' })).toBeDisabled()
    await userEvent.click(within(row).getByRole('button', { name: 'Move up' }))
    await userEvent.click(
      within(screen.getByTestId('studio-field-/fields/1')).getByRole('button', { name: 'Move down' }),
    )
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    // Fields that swap places in an array of the same length are patched item by item, so apply the patch.
    let moved: unknown = designed
    for (const operation of sentPatch(fetchMock)) {
      moved = setAt(moved, operation.path, operation.op === 'remove' ? undefined : operation.value)
    }
    // The first move puts `code` first, the second moves `total` below `note`.
    expect((moved as typeof designed).fields).toEqual([designed.fields[1], designed.fields[2], designed.fields[0]])
  })
})
