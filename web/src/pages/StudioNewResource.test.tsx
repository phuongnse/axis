import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { designTokensFor } from '../platform/designTokens'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { formItem } from '../test/antd'
import { withAntdStyles } from '../test/antdStyles'
import { StudioNewResource } from './StudioNewResource'

function renderButton() {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioNewResource application="App" resources={[]} onCreated={vi.fn()} />
      </TextProvider>
    </ThemeModeProvider>,
  )
}

describe('the New resource dialog', () => {
  withAntdStyles()

  it('spaces its kind and name fields spacing-lg apart, with no space below each field', async () => {
    renderButton()
    await userEvent.click(screen.getByRole('button', { name: 'New resource…' }))
    await screen.findByRole('dialog')

    const kind = formItem(screen.getByRole('combobox', { name: 'Kind' }))
    const name = formItem(screen.getByRole('textbox', { name: 'Name' }))
    const { spacing } = designTokensFor('light')

    expect(kind.parentElement).toBe(name.parentElement)
    expect(kind.parentElement).toHaveStyle({ gap: `${spacing.lg}px` })
    expect(getComputedStyle(kind).getPropertyValue('--ant-form-item-margin-bottom')).toBe('0px')
    expect(getComputedStyle(name).getPropertyValue('--ant-form-item-margin-bottom')).toBe('0px')
  })
})
