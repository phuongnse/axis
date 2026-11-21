import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { LayerMarkTag, RemovedRows, type DesignerLayer } from './LayerMarkViews'

function renderMarks(layer: DesignerLayer, removed = false) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        {removed ? (
          <RemovedRows layer={layer} list="/grants" />
        ) : (
          <LayerMarkTag layer={layer} list="/grants" index={0} name="a" />
        )}
      </TextProvider>
    </ThemeModeProvider>,
  )
}

describe('LayerMarkViews', () => {
  it('draws Reset to module of an added element as a light text button that resets it', async () => {
    const onReset = vi.fn()
    renderMarks({ elements: [{ list: '/grants', key: 'a', mark: 'added' }], disabled: false, onReset })

    const reset = screen.getByRole('button', { name: 'Reset to module a' })
    expect(reset).toHaveClass('ant-btn-variant-text')
    expect(reset).toBeEnabled()

    await userEvent.click(reset)
    expect(onReset).toHaveBeenCalledWith('/grants', 'a')
  })

  it('turns Reset to module off while the layer is disabled', () => {
    renderMarks({ elements: [{ list: '/grants', key: 'a', mark: 'added' }], disabled: true, onReset: vi.fn() })

    expect(screen.getByRole('button', { name: 'Reset to module a' })).toBeDisabled()
  })

  it('draws Reset to module of a removed element as a light text button', async () => {
    const onReset = vi.fn()
    renderMarks({ elements: [{ list: '/grants', key: 'a', mark: 'removed' }], disabled: false, onReset }, true)

    const reset = screen.getByRole('button', { name: 'Reset to module a' })
    expect(reset).toHaveClass('ant-btn-variant-text')

    await userEvent.click(reset)
    expect(onReset).toHaveBeenCalledWith('/grants', 'a')
  })
})
