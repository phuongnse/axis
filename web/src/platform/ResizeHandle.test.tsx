import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useRef, useState } from 'react'
import { describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { designTokensFor, spacingTokens } from './designTokens'
import { ResizeHandle } from './ResizeHandle'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

const { colour } = designTokensFor('light')
const min = 100
const max = 200
const start = 160

/** A region sized from state, with a handle on one of its rules. */
function Harness({ orientation, edge }: { orientation: 'vertical' | 'horizontal'; edge: 'start' | 'end' }) {
  const [size, setSize] = useState(start)
  const region = useRef<HTMLDivElement>(null)
  const vertical = orientation === 'vertical'
  return (
    <div
      ref={region}
      data-testid="region"
      style={{ position: 'relative', width: vertical ? size : 50, height: vertical ? 50 : size }}
    >
      <ResizeHandle
        nameKey="shell.sidePanel.resize"
        orientation={orientation}
        edge={edge}
        value={size}
        min={min}
        max={max}
        onChange={(next) => setSize(Math.min(max, Math.max(min, next)))}
        region={region}
      />
    </div>
  )
}

function renderHandle(orientation: 'vertical' | 'horizontal', edge: 'start' | 'end' = 'start') {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <Harness orientation={orientation} edge={edge} />
      </TextProvider>
    </ThemeModeProvider>,
  )
  const handle = screen.getByRole('separator', { name: 'Resize panel' })
  handle.focus()
  return handle
}

describe('ResizeHandle on a vertical rule', () => {
  it('is a named separator that reports its size and limits', () => {
    const handle = renderHandle('vertical')

    expect(handle).toHaveAttribute('aria-orientation', 'vertical')
    expect(handle).toHaveAttribute('aria-valuemin', String(min))
    expect(handle).toHaveAttribute('aria-valuemax', String(max))
    expect(handle).toHaveAttribute('aria-valuenow', String(start))
    expect(handle).toHaveStyle({ cursor: 'col-resize' })
  })

  it('widens a start edge with ArrowLeft and narrows it with ArrowRight, by spacing-md', async () => {
    const handle = renderHandle('vertical')

    await userEvent.keyboard('{ArrowLeft}')
    expect(screen.getByTestId('region')).toHaveStyle({ width: `${start + spacingTokens.md}px` })
    expect(handle).toHaveAttribute('aria-valuenow', String(start + spacingTokens.md))

    await userEvent.keyboard('{ArrowRight}{ArrowRight}')
    expect(screen.getByTestId('region')).toHaveStyle({ width: `${start - spacingTokens.md}px` })
    expect(handle).toHaveAttribute('aria-valuenow', String(start - spacingTokens.md))
  })

  it('widens an end edge with ArrowRight', async () => {
    renderHandle('vertical', 'end')

    await userEvent.keyboard('{ArrowRight}')

    expect(screen.getByTestId('region')).toHaveStyle({ width: `${start + spacingTokens.md}px` })
  })

  it('goes to the least with Home and the most with End, and stays within them', async () => {
    renderHandle('vertical')

    await userEvent.keyboard('{End}{ArrowLeft}')
    expect(screen.getByTestId('region')).toHaveStyle({ width: `${max}px` })

    await userEvent.keyboard('{Home}{ArrowRight}')
    expect(screen.getByTestId('region')).toHaveStyle({ width: `${min}px` })
  })

  it('does nothing for the up and down arrows', async () => {
    renderHandle('vertical')

    await userEvent.keyboard('{ArrowUp}{ArrowDown}')

    expect(screen.getByTestId('region')).toHaveStyle({ width: `${start}px` })
  })
})

describe('ResizeHandle on a horizontal rule', () => {
  it('is a named separator with a row cursor', () => {
    const handle = renderHandle('horizontal')

    expect(handle).toHaveAttribute('aria-orientation', 'horizontal')
    expect(handle).toHaveAttribute('aria-valuenow', String(start))
    expect(handle).toHaveStyle({ cursor: 'row-resize' })
  })

  it('heightens a start edge with ArrowUp and lowers it with ArrowDown, by spacing-md', async () => {
    const handle = renderHandle('horizontal')

    await userEvent.keyboard('{ArrowUp}')
    expect(screen.getByTestId('region')).toHaveStyle({ height: `${start + spacingTokens.md}px` })
    expect(handle).toHaveAttribute('aria-valuenow', String(start + spacingTokens.md))

    await userEvent.keyboard('{ArrowDown}{ArrowDown}')
    expect(screen.getByTestId('region')).toHaveStyle({ height: `${start - spacingTokens.md}px` })
  })

  it('heightens an end edge with ArrowDown', async () => {
    renderHandle('horizontal', 'end')

    await userEvent.keyboard('{ArrowDown}')

    expect(screen.getByTestId('region')).toHaveStyle({ height: `${start + spacingTokens.md}px` })
  })

  it('does nothing for the left and right arrows', async () => {
    renderHandle('horizontal')

    await userEvent.keyboard('{ArrowLeft}{ArrowRight}')

    expect(screen.getByTestId('region')).toHaveStyle({ height: `${start}px` })
  })
})

describe('ResizeHandle look', () => {
  it('draws its rule clear and both grip bars in border at rest', () => {
    const handle = renderHandle('vertical')

    expect(handle.querySelector('[data-resize-line]')).toHaveStyle({ background: 'transparent' })
    const bars = handle.querySelectorAll('[data-resize-bar]')
    expect(bars).toHaveLength(2)
    for (const bar of bars) expect(bar).toHaveStyle({ background: colour.border })
  })

  it('turns its rule and both grip bars primary under the pointer, and back', async () => {
    const handle = renderHandle('vertical')

    await userEvent.hover(handle)
    expect(handle.querySelector('[data-resize-line]')).toHaveStyle({ background: colour.primary })
    for (const bar of handle.querySelectorAll('[data-resize-bar]')) {
      expect(bar).toHaveStyle({ background: colour.primary })
    }

    await userEvent.unhover(handle)
    expect(handle.querySelector('[data-resize-line]')).toHaveStyle({ background: 'transparent' })
    for (const bar of handle.querySelectorAll('[data-resize-bar]')) {
      expect(bar).toHaveStyle({ background: colour.border })
    }
  })
})
