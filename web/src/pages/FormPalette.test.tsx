import { DndContext, PointerSensor, useSensor, useSensors } from '@dnd-kit/core'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useEffect, useState } from 'react'
import { describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { setAt } from '../platform/jsonPatch'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { TextProvider } from '../platform/TextProvider'
import { FormPalette } from './FormPalette'
import { FormProperties } from './FormProperties'

function nameField(name: string, type: string) {
  return { name, type, target: null, values: null }
}

const names = {
  locales: ['en'],
  entities: [
    {
      id: 'e1',
      name: 'Ticket',
      module: null,
      fields: [
        nameField('title', 'text'),
        nameField('Status', 'enum'),
        nameField('notes', 'text'),
        nameField('lines', 'child-collection'),
      ],
    },
  ],
} as unknown as StudioNames

const initial: Record<string, unknown> = {
  kind: 'form',
  entity: 'ticket',
  sections: [
    { name: 'problem', fields: [{ field: 'TITLE' }] },
    { name: 'details', fields: [{ field: 'status' }] },
  ],
}

let latest: Record<string, unknown> = initial

function Harness({ start = null, withProperties = false }: { start?: string | null; withProperties?: boolean }) {
  const [draft, setDraft] = useState(initial)
  const [selected, setSelected] = useState<string | null>(start)
  const [announcement, setAnnouncement] = useState('')
  useEffect(() => {
    latest = draft
  })
  // As the form designer has it: only the pointer drags, so Enter and Space reach the button.
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }))
  const onChange = (pointer: string, value: unknown) =>
    setDraft((current) => setAt(current, pointer, value) as typeof current)
  return (
    <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
      <StudioNamesContext.Provider value={names}>
        <DndContext sensors={sensors}>
          <FormPalette
            draft={draft}
            selected={selected}
            announcement={announcement}
            onAnnounce={setAnnouncement}
            onSelect={setSelected}
            onChange={onChange}
          />
          {withProperties && (
            <FormProperties
              draft={draft}
              selected={selected}
              entityFields={[]}
              onSelect={setSelected}
              onAnnounce={setAnnouncement}
              onChange={onChange}
            />
          )}
        </DndContext>
      </StudioNamesContext.Provider>
    </TextProvider>
  )
}

function paletteButtons(): string[] {
  return screen
    .getAllByRole('button')
    .map((button) => button.getAttribute('data-testid'))
    .filter((id): id is string => id?.startsWith('studio-form-palette-') === true)
    .map((id) => id.slice('studio-form-palette-'.length))
}

describe('FormPalette', () => {
  it('lists the entity fields that no section lists, whatever their case, child collections included', () => {
    render(<Harness />)
    expect(paletteButtons()).toEqual(['notes', 'lines'])
    expect(screen.getByTestId('studio-form-palette-lines')).toHaveTextContent('child-collection')
  })

  it('inserts a field into the selected section on Enter and says where it landed', async () => {
    render(<Harness start="/sections/1" />)
    screen.getByTestId('studio-form-palette-notes').focus()
    await userEvent.keyboard('{Enter}')

    const sections = latest.sections as { fields: unknown[] }[]
    expect(sections[1].fields.at(-1)).toEqual({ field: 'notes' })
    expect(sections[0].fields).toHaveLength(1)
    expect(screen.queryByTestId('studio-form-palette-notes')).toBeNull()
    expect(screen.getByTestId('studio-form-palette-announcement')).toHaveTextContent(
      'Inserted notes · details · position 2',
    )
  })

  it('inserts after the selected field', async () => {
    render(<Harness start="/sections/0/fields/0" />)
    await userEvent.click(screen.getByTestId('studio-form-palette-lines'))
    expect((latest.sections as { fields: unknown[] }[])[0].fields).toEqual([{ field: 'TITLE' }, { field: 'lines' }])
  })

  it('says every field is placed when none is left', async () => {
    render(<Harness />)
    await userEvent.click(screen.getByTestId('studio-form-palette-notes'))
    await userEvent.click(screen.getByTestId('studio-form-palette-lines'))
    expect(screen.getByText('Every field of the entity is on the form.')).toBeInTheDocument()
  })

  it('adds a section, selects it and says so', async () => {
    render(<Harness start="/sections/0" />)
    await userEvent.click(screen.getByTestId('studio-form-add-section'))

    const sections = latest.sections as { name: string; fields: unknown[] }[]
    expect(sections).toHaveLength(3)
    expect(sections[2]).toMatchObject({ name: 'section1', fields: [] })
    expect(latest.steps).toBeUndefined()
    expect(screen.getByTestId('studio-form-palette-announcement')).toHaveTextContent('Added section1')

    await userEvent.click(screen.getByTestId('studio-form-palette-notes'))
    expect((latest.sections as { fields: unknown[] }[])[2].fields).toEqual([{ field: 'notes' }])
  })

  it('lists a field again once the property panel removes it from its section', async () => {
    render(<Harness start="/sections/1/fields/0" withProperties />)
    expect(paletteButtons()).not.toContain('Status')

    await userEvent.click(screen.getByRole('button', { name: 'Remove field' }))

    expect(paletteButtons()).toContain('Status')
    expect(screen.getByTestId('studio-form-palette-announcement')).toHaveTextContent('Removed status · details')
  })

  it('adds the first step with every section, then an empty one', async () => {
    render(<Harness />)
    await userEvent.click(screen.getByTestId('studio-form-add-step'))
    expect((latest.steps as { sections: string[] }[]).map((step) => step.sections)).toEqual([['problem', 'details']])

    await userEvent.click(screen.getByTestId('studio-form-add-step'))
    expect((latest.steps as { sections: string[] }[]).map((step) => step.sections)).toEqual([
      ['problem', 'details'],
      [],
    ])
  })
})
