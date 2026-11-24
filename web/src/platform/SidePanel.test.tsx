import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { stubWindowWidth } from '../test/windowWidth'
import { breakpointTokens, designTokensFor, sizeTokens, spacingTokens } from './designTokens'
import { SidePanel } from './SidePanel'
import { ShellAsideContext } from './shellAsideContext'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

let aside: HTMLDivElement | undefined

/** Renders a panel in the shell's right column, which is `aside`. */
function renderPanel({ onClose, status, line }: { onClose?: () => void; status?: string; line?: string } = {}) {
  aside = document.createElement('div')
  document.body.append(aside)
  return render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <ShellAsideContext.Provider value={{ target: aside, claim: () => () => {} }}>
          {onClose ? (
            <SidePanel
              id="panel"
              title="Problems"
              status={status}
              line={line}
              closeNameKey="shell.studio.problems.close"
              onClose={onClose}
              testId="panel-test"
              closeTestId="panel-close"
            >
              <p>Body text</p>
            </SidePanel>
          ) : (
            <SidePanel id="panel" title="Problems" status={status} line={line} testId="panel-test">
              <p>Body text</p>
            </SidePanel>
          )}
        </ShellAsideContext.Provider>
      </TextProvider>
    </ThemeModeProvider>,
  )
}

beforeEach(() => {
  localStorage.clear()
  window.innerWidth = 1280
})

afterEach(() => {
  aside?.remove()
  aside = undefined
})

describe('SidePanel', () => {
  it('is drawn in the right column, named by its title', () => {
    renderPanel({ onClose: vi.fn() })

    expect(aside).toContainElement(screen.getByRole('complementary', { name: 'Problems' }))
    expect(screen.getByTestId('panel-test')).toHaveAttribute('id', 'panel')
  })

  it('draws its title and close button outside the body, which scrolls on its own and keeps room for a scrollbar', () => {
    renderPanel({ onClose: vi.fn() })

    const body = screen.getByText('Body text').parentElement!
    expect(body).toHaveStyle({ overflowY: 'auto', overscrollBehavior: 'contain', scrollbarGutter: 'stable' })
    expect(body).not.toContainElement(screen.getByRole('heading', { name: 'Problems' }))
    expect(body).not.toContainElement(screen.getByTestId('panel-close'))
  })

  it('runs onClose from the close button', async () => {
    const onClose = vi.fn()
    renderPanel({ onClose })

    await userEvent.click(screen.getByRole('button', { name: 'Close problems' }))

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('has no close button without onClose', () => {
    renderPanel()

    expect(screen.queryByRole('button')).toBeNull()
  })

  it('shows its status beside the title', () => {
    renderPanel({ status: 'Waiting' })

    expect(screen.getByText('Waiting').parentElement).toContainElement(
      screen.getByRole('heading', { name: 'Problems' }),
    )
  })
})

describe('SidePanel line', () => {
  it('shows its line under the title, in the muted colour', () => {
    renderPanel({ line: 'Calls a connector' })

    const line = screen.getByText('Calls a connector')
    expect(line).toHaveStyle({ color: designTokensFor('light').colour.textMuted })
    expect(
      screen.getByRole('heading', { name: 'Problems' }).compareDocumentPosition(line) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy()
  })
})

describe('SidePanel at narrow width', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('takes the width of the window and has no resize handle at 360 px', () => {
    stubWindowWidth(360)
    window.innerWidth = 360

    renderPanel({ onClose: vi.fn() })

    expect(screen.getByTestId('panel-test')).toHaveStyle({ width: '100%' })
    expect(screen.queryByRole('separator')).toBeNull()
  })

  it('keeps the sidePanel width and its handle at 992 px', () => {
    stubWindowWidth(breakpointTokens.narrowWidth)
    window.innerWidth = breakpointTokens.narrowWidth

    renderPanel()

    expect(screen.getByTestId('panel-test')).toHaveStyle({ width: `${sizeTokens.sidePanel}px` })
    expect(screen.getByRole('separator', { name: 'Resize panel' })).toBeInTheDocument()
  })

  it('keeps the width stored by the handle, so the panel returns to it when the window widens', () => {
    localStorage.setItem('axis.sidePanelWidth', '400')
    stubWindowWidth(360)
    window.innerWidth = 360
    const narrow = renderPanel()
    expect(screen.getByTestId('panel-test')).toHaveStyle({ width: '100%' })
    narrow.unmount()
    aside?.remove()

    stubWindowWidth(breakpointTokens.narrowWidth)
    window.innerWidth = breakpointTokens.narrowWidth
    renderPanel()

    expect(screen.getByTestId('panel-test')).toHaveStyle({ width: '400px' })
  })
})

describe('SidePanel width', () => {
  it('opens at the sidePanel width, with a named handle that reports it', () => {
    renderPanel()

    const handle = screen.getByRole('separator', { name: 'Resize panel' })
    expect(screen.getByTestId('panel-test')).toHaveStyle({ width: `${sizeTokens.sidePanel}px` })
    expect(handle).toHaveAttribute('data-resize-handle')
    expect(handle).toHaveAttribute('aria-orientation', 'vertical')
    expect(handle).toHaveAttribute('aria-valuemin', String(sizeTokens.sidePanel))
    expect(handle).toHaveAttribute('aria-valuemax', '640')
    expect(handle).toHaveAttribute('aria-valuenow', String(sizeTokens.sidePanel))
  })

  it('keeps a width set with the arrow keys for a panel mounted afterwards', async () => {
    const first = renderPanel()
    screen.getByRole('separator', { name: 'Resize panel' }).focus()

    await userEvent.keyboard('{ArrowLeft}{ArrowLeft}')

    const widened = sizeTokens.sidePanel + 2 * spacingTokens.md
    expect(screen.getByTestId('panel-test')).toHaveStyle({ width: `${widened}px` })
    first.unmount()
    aside?.remove()

    renderPanel()

    expect(screen.getByTestId('panel-test')).toHaveStyle({ width: `${widened}px` })
    expect(screen.getByRole('separator', { name: 'Resize panel' })).toHaveAttribute('aria-valuenow', String(widened))
  })

  it('narrows with ArrowRight, but never under the sidePanel width', async () => {
    renderPanel()
    screen.getByRole('separator', { name: 'Resize panel' }).focus()

    await userEvent.keyboard('{ArrowLeft}{ArrowRight}{ArrowRight}')

    expect(screen.getByTestId('panel-test')).toHaveStyle({ width: `${sizeTokens.sidePanel}px` })
  })

  it('goes to the most with End, half the window, and back to the least with Home', async () => {
    renderPanel()
    screen.getByRole('separator', { name: 'Resize panel' }).focus()

    await userEvent.keyboard('{End}')
    expect(screen.getByTestId('panel-test')).toHaveStyle({ width: '640px' })

    await userEvent.keyboard('{ArrowLeft}')
    expect(screen.getByTestId('panel-test')).toHaveStyle({ width: '640px' })

    await userEvent.keyboard('{Home}')
    expect(screen.getByTestId('panel-test')).toHaveStyle({ width: `${sizeTokens.sidePanel}px` })
  })

  it('opens at the sidePanel width when the stored width is not a number', () => {
    localStorage.setItem('axis.sidePanelWidth', 'wide')

    renderPanel()

    expect(screen.getByTestId('panel-test')).toHaveStyle({ width: `${sizeTokens.sidePanel}px` })
  })

  it('clamps a stored width to half the window', () => {
    localStorage.setItem('axis.sidePanelWidth', '5000')

    renderPanel()

    expect(screen.getByTestId('panel-test')).toHaveStyle({ width: '640px' })
  })
})
