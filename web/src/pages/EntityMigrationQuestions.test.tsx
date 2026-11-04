import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { designTokensFor } from '../platform/designTokens'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { EntityMigrationQuestions } from './EntityMigrationQuestions'

describe('the rename question', () => {
  it('draws Cancel and Drop data as text buttons beside a solid Keep data', () => {
    render(
      <ThemeModeProvider initialMode="light">
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <EntityMigrationQuestions
            questions={[{ type: 'rename', from: 'a', to: 'b' }]}
            onDone={vi.fn()}
            onCancel={vi.fn()}
          />
        </TextProvider>
      </ThemeModeProvider>,
    )

    const buttons = ['Cancel', 'Drop data', 'Keep data'].map((name) => screen.getByRole('button', { name }))
    const [cancel, drop, keep] = buttons
    expect(keep).toHaveClass('ant-btn-variant-solid')
    expect(cancel).toHaveClass('ant-btn-variant-text')
    expect(drop).toHaveClass('ant-btn-variant-text')
    expect(drop).toHaveStyle({ color: designTokensFor('light').colour.dangerText })
  })
})
