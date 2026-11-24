import { ConfigProvider } from 'antd'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { FoldButton } from './FoldButton'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

const texts = {
  'test.show': 'Show {name}',
  'test.hide': 'Hide {name}',
}
const catalogs = [{ texts, fallbackTexts: texts }]
const names = { show: 'test.show', hide: 'test.hide' }

function renderFold(props: Partial<React.ComponentProps<typeof FoldButton>> = {}) {
  const onToggle = vi.fn()
  const result = render(
    <ThemeModeProvider initialMode="light">
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          <FoldButton open={false} onToggle={onToggle} names={names} values={{ name: 'Totals' }} {...props} />
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>,
  )
  return { ...result, onToggle }
}

describe('FoldButton', () => {
  it('points right and sets aria-expanded false while folded, named from the show text', () => {
    const { container } = renderFold()

    const button = screen.getByRole('button', { name: 'Show Totals' })
    expect(button).toHaveAttribute('aria-expanded', 'false')
    expect(container.querySelector<SVGElement>('.anticon-down svg')?.style.transform).toBe('rotate(270deg)')
  })

  it('points down and sets aria-expanded true while open, named from the hide text', () => {
    const { container } = renderFold({ open: true })

    const button = screen.getByRole('button', { name: 'Hide Totals' })
    expect(button).toHaveAttribute('aria-expanded', 'true')
    expect(container.querySelector<SVGElement>('.anticon-down svg')?.style.transform).toBe('')
  })

  it('calls onToggle once on a click', async () => {
    const { onToggle } = renderFold()

    await userEvent.click(screen.getByRole('button', { name: 'Show Totals' }))

    expect(onToggle).toHaveBeenCalledTimes(1)
  })

  it('sets aria-controls to the body it is given', () => {
    renderFold({ controls: 'totals-body' })

    expect(screen.getByRole('button', { name: 'Show Totals' })).toHaveAttribute('aria-controls', 'totals-body')
  })

  it('sets no aria-controls when it is given no body', () => {
    renderFold()

    expect(screen.getByRole('button', { name: 'Show Totals' })).not.toHaveAttribute('aria-controls')
  })
})
