import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { setAt } from '../platform/jsonPatch'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { FormProperties } from './FormProperties'

type Content = Record<string, unknown>

/** Keeps the draft in state, like the editor does, and exposes it as JSON. */
function Harness({ initial, selected }: { initial: Content; selected: string }) {
  const [draft, setDraft] = useState(initial)
  const [announcement, setAnnouncement] = useState('')
  const [current, setCurrent] = useState(selected)
  return (
    <>
      <FormProperties
        draft={draft}
        selected={current}
        entityFields={[]}
        onSelect={setCurrent}
        onAnnounce={setAnnouncement}
        onChange={(pointer, next) => setDraft((previous) => setAt(previous, pointer, next) as Content)}
      />
      <output data-testid="draft">{JSON.stringify(draft)}</output>
      <output data-testid="selected">{current}</output>
      <output data-testid="announcement">{announcement}</output>
    </>
  )
}

function renderHarness(initial: Content, selected: string) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <Harness initial={initial} selected={selected} />
      </TextProvider>
    </ThemeModeProvider>,
  )
}

const stepped = {
  sections: [
    { name: 'a', fields: [] },
    { name: 'b', fields: [] },
  ],
  steps: [
    { title: { textKey: 'steps.one' }, sections: ['a', 'b'] },
    { title: { textKey: 'steps.two' }, sections: [] },
  ],
}

describe('FormProperties section', () => {
  it('moves the section to the step that the Move to step picker chooses', async () => {
    renderHarness(stepped, '/sections/0')

    expect(within(screen.getByTestId('studio-design-move-to-step')).getByTitle('steps.one')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('combobox', { name: 'Move to step' }))
    await userEvent.click(await screen.findByText('steps.two', { selector: '.ant-select-item-option-content' }))

    const draft = JSON.parse(screen.getByTestId('draft').textContent ?? '')
    expect(draft.steps[0].sections).toEqual(['b'])
    expect(draft.steps[1].sections).toEqual(['a'])
    expect(draft.sections.map((section: { name: string }) => section.name)).toEqual(['b', 'a'])
    expect(screen.getByTestId('selected')).toHaveTextContent('/sections/1')
    expect(screen.getByTestId('announcement')).toHaveTextContent('Moved a · steps.two · position 1')
  })

  it('has no Move to step picker on a form without steps', () => {
    renderHarness({ sections: [{ name: 'a', fields: [] }] }, '/sections/0')

    expect(screen.getByTestId('studio-design-section')).toBeInTheDocument()
    expect(screen.queryByTestId('studio-design-move-to-step')).toBeNull()
  })

  it('renames the section and the step entry that lists it in one edit', async () => {
    renderHarness(stepped, '/sections/0')

    const box = screen.getByRole('textbox', { name: 'Name' })
    await userEvent.clear(box)
    await userEvent.type(box, 'first{Enter}')

    const draft = JSON.parse(screen.getByTestId('draft').textContent ?? '')
    expect(draft.sections[0].name).toBe('first')
    expect(draft.steps[0].sections).toEqual(['first', 'b'])
  })

  it('renames a section of a form without steps and adds no steps', async () => {
    renderHarness({ sections: [{ name: 'a', fields: [] }] }, '/sections/0')

    const box = screen.getByRole('textbox', { name: 'Name' })
    await userEvent.clear(box)
    await userEvent.type(box, 'first{Enter}')

    const draft = JSON.parse(screen.getByTestId('draft').textContent ?? '')
    expect(draft.sections[0].name).toBe('first')
    expect(draft).not.toHaveProperty('steps')
  })

  it("refuses another section's name in different letter case and says why", async () => {
    renderHarness(stepped, '/sections/0')
    const before = screen.getByTestId('draft').textContent

    const box = screen.getByRole('textbox', { name: 'Name' })
    await userEvent.clear(box)
    await userEvent.type(box, 'B{Enter}')

    expect(screen.getByTestId('draft').textContent).toBe(before)
    expect(screen.getByText('Another section already has this name.')).toBeInTheDocument()
  })

  it('refuses an empty name and says why', async () => {
    renderHarness(stepped, '/sections/0')
    const before = screen.getByTestId('draft').textContent

    await userEvent.clear(screen.getByRole('textbox', { name: 'Name' }))
    await userEvent.tab()

    expect(screen.getByTestId('draft').textContent).toBe(before)
    expect(screen.getByText('A section needs a name.')).toBeInTheDocument()
  })
})

describe('FormProperties field', () => {
  it('removes the field from its section, keeps the others in order and selects the section', async () => {
    renderHarness(
      { sections: [{ name: 'a', fields: [{ field: 'x' }] }, { fields: [{ field: 'p' }, { field: 'q' }] }] },
      '/sections/1/fields/0',
    )

    await userEvent.click(screen.getByRole('button', { name: 'Remove field' }))

    const draft = JSON.parse(screen.getByTestId('draft').textContent ?? '')
    expect(draft.sections[1].fields).toEqual([{ field: 'q' }])
    expect(screen.getByTestId('selected')).toHaveTextContent('/sections/1')
    expect(screen.getByTestId('announcement')).toHaveTextContent('Removed p · Section 2')
  })

  it('removes a field with an empty name and announces its pointer', async () => {
    renderHarness({ sections: [{ title: { textKey: '' }, fields: [{ field: '' }] }] }, '/sections/0/fields/0')

    await userEvent.click(screen.getByRole('button', { name: 'Remove field' }))

    const draft = JSON.parse(screen.getByTestId('draft').textContent ?? '')
    expect(draft.sections[0].fields).toEqual([])
    expect(screen.getByTestId('announcement')).toHaveTextContent('/sections/0/fields/0')
  })
})
