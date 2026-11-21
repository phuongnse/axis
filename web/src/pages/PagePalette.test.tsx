import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { PagePalette } from './PagePalette'

function renderPalette(value: Record<string, unknown>) {
  const onChange = vi.fn()
  const onSelect = vi.fn()
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <PagePalette
          value={value}
          selected="/widgets/0"
          announcement=""
          onAnnounce={vi.fn()}
          onSelect={onSelect}
          onChange={onChange}
        />
      </TextProvider>
    </ThemeModeProvider>,
  )
  return { onChange, onSelect }
}

describe('PagePalette', () => {
  it('draws each widget type as a secondary button', () => {
    renderPalette({ widgets: [{ type: 'taskInbox' }] })

    expect(screen.getByTestId('studio-palette-table')).toHaveClass('ant-btn-variant-outlined')
    expect(screen.getByTestId('studio-palette-table')).not.toHaveClass('ant-btn-variant-text')
  })

  it('inserts a type after the selected widget and selects it, on a click', async () => {
    const { onChange, onSelect } = renderPalette({ widgets: [{ type: 'taskInbox' }] })

    await userEvent.click(screen.getByRole('button', { name: 'table' }))

    expect(onChange).toHaveBeenLastCalledWith('/widgets', [
      { type: 'taskInbox' },
      expect.objectContaining({ type: 'table' }),
    ])
    expect(onSelect).toHaveBeenLastCalledWith('/widgets/1')
  })
})
