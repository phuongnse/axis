import { DndContext } from '@dnd-kit/core'
import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { FormOutline } from './FormOutline'

function renderOutline(draft: Record<string, unknown>) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <DndContext>
          <FormOutline draft={draft} selected={null} onSelect={() => {}} />
        </DndContext>
      </TextProvider>
    </ThemeModeProvider>,
  )
}

describe('FormOutline', () => {
  it('shows a drop area inside a section that has no fields', () => {
    renderOutline({
      sections: [
        { name: 'empty', title: { textKey: 'forms.t.sections.0.title' }, fields: [] },
        { name: 'full', fields: [{ field: 'title' }] },
      ],
    })

    const rows = screen.getAllByTestId('studio-outline-section')
    const empty = within(rows[0]).getByTestId('studio-outline-empty-section')
    expect(rows[0]).toHaveAttribute('data-path', '/sections/0')
    expect(empty).toHaveTextContent('Drop a field here')
    expect(within(rows[1]).queryByTestId('studio-outline-empty-section')).toBeNull()
    expect(screen.getAllByTestId('studio-outline-empty-section')).toHaveLength(1)
  })
})
