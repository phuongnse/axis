import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { SaveBar } from '../pages/SaveBar'
import { ActionBar } from './ActionBar'
import { AxisButton } from './AxisButton'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

const texts = {
  ...platformEnglish,
  'test.validate': 'Validate',
  'test.preview': 'Preview',
}

function renderIn(ui: ReactNode) {
  return render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts, fallbackTexts: texts }]} development={false}>
        {ui}
      </TextProvider>
    </ThemeModeProvider>,
  )
}

// jsdom has no layout, so the widths are faked: the bar's row is `box` wide, and its content `content` wide.
function fakeWidths(box: number, content: number) {
  const onRow = (element: HTMLElement, width: number) => (element.hasAttribute('data-fold-row') ? width : 0)
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockImplementation(function (this: HTMLElement) {
    return onRow(this, box)
  })
  vi.spyOn(HTMLElement.prototype, 'scrollWidth', 'get').mockImplementation(function (this: HTMLElement) {
    return onRow(this, Math.max(box, content))
  })
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
}

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('ActionBar', () => {
  const renderBar = () =>
    renderIn(
      <ActionBar
        start={<span>Start</span>}
        fold={[
          { key: 'validate', labelKey: 'test.validate', onClick: () => {} },
          { key: 'preview', labelKey: 'test.preview', disabled: true, onClick: () => {} },
        ]}
        end={<AxisButton role="finish">Publish…</AxisButton>}
      />,
    )

  it('shows the named actions as buttons, in order, before the end, while they fit', () => {
    fakeWidths(500, 400)
    renderBar()

    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual([
      'Validate',
      'Preview',
      'Publish…',
    ])
    expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument()
  })

  it('folds the named actions, in order, into the More actions menu when it is narrow, each keeping its disabled state', async () => {
    fakeWidths(300, 400)
    renderBar()

    expect(screen.queryByRole('button', { name: 'Validate' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'More actions' }))
    const entries = await screen.findAllByRole('menuitem')

    expect(entries.map((entry) => entry.textContent)).toEqual(['Validate', 'Preview'])
    expect(entries[0]).not.toHaveAttribute('aria-disabled', 'true')
    expect(entries[1]).toHaveAttribute('aria-disabled', 'true')
    expect(screen.getByRole('button', { name: 'Publish…' })).toBeInTheDocument()
  })
})

describe('a docked ActionBar', () => {
  it('stays at the foot of the window across the nearest main and leaves its height in the page', () => {
    renderIn(
      <main>
        <ActionBar docked data-testid="docked-bar" start={<span>Start</span>} />
      </main>,
    )
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ left: 240, width: 800 } as DOMRect)
    window.dispatchEvent(new Event('resize'))

    const wrapper = screen.getByTestId('docked-bar')
    expect(wrapper).toHaveStyle({ position: 'fixed', bottom: '0px' })
    return waitFor(() => expect(wrapper).toHaveStyle({ left: '240px', width: '800px' }))
  })

  it('is drawn in the flow, with no fixed wrapper, unless it is docked', () => {
    renderIn(<ActionBar data-testid="plain-bar" start={<span>Start</span>} />)

    expect(screen.queryByTestId('plain-bar')).toBeNull()
    expect(screen.getByText('Start').closest('[data-action-bar]')?.parentElement?.style.position).not.toBe('fixed')
  })
})

describe('SaveBar in an ActionBar', () => {
  const renderSaveBar = (busy: boolean) =>
    renderIn(
      <main>
        <SaveBar
          unsaved
          undoable
          redoable={false}
          saveable
          busy={busy}
          onUndo={vi.fn()}
          onRedo={vi.fn()}
          onDiscard={vi.fn()}
          onSave={vi.fn()}
        />
      </main>,
    )

  it('draws its Save button inside an ActionBar, which is inside the save bar', () => {
    renderSaveBar(false)

    const bar = screen.getByRole('button', { name: 'Save' }).closest('[data-action-bar]')

    expect(bar).not.toBeNull()
    expect(screen.getByTestId('studio-save-bar')).toContainElement(bar as HTMLElement)
  })

  it('folds Discard into a disabled More actions entry while a save runs, and keeps Save a button', async () => {
    fakeWidths(300, 400)
    renderSaveBar(true)

    expect(screen.queryByRole('button', { name: 'Discard' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'More actions' }))

    expect(await screen.findByRole('menuitem', { name: 'Discard' })).toHaveAttribute('aria-disabled', 'true')
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
  })
})
