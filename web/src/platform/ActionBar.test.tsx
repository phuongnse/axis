import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { ActionBar } from './ActionBar'
import { AxisButton } from './AxisButton'
import { SaveBar } from './SaveBar'
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

describe('ActionBar with pane', () => {
  const barOf = (button: HTMLElement) => button.closest<HTMLElement>('[data-action-bar]')!

  it('draws a main action as an outlined button at the pane inset', () => {
    renderIn(<ActionBar pane end={<AxisButton role="main">Run</AxisButton>} />)

    const button = screen.getByRole('button', { name: 'Run' })
    expect(button).toHaveClass('ant-btn-variant-outlined')
    expect(button).not.toHaveClass('ant-btn-variant-text')
    expect(barOf(button).getAttribute('style')).toContain('padding-inline: 16px')
  })

  it('draws a main action as an outlined button at the page inset too, as it keeps its border', () => {
    renderIn(<ActionBar end={<AxisButton role="main">Run</AxisButton>} />)

    const button = screen.getByRole('button', { name: 'Run' })
    expect(button).toHaveClass('ant-btn-variant-outlined')
    expect(button).not.toHaveClass('ant-btn-variant-text')
    expect(barOf(button).getAttribute('style')).toContain('padding-inline: 24px')
  })

  it('keeps the outline of the start and the fold actions too', () => {
    fakeWidths(500, 400)
    renderIn(
      <ActionBar
        pane
        start={<AxisButton role="secondary">Start</AxisButton>}
        fold={[{ key: 'validate', labelKey: 'test.validate', onClick: () => {} }]}
      />,
    )

    expect(screen.getByRole('button', { name: 'Start' })).not.toHaveClass('ant-btn-variant-text')
    expect(screen.getByRole('button', { name: 'Validate' })).not.toHaveClass('ant-btn-variant-text')
  })
})

describe('SaveBar in an ActionBar', () => {
  const renderSaveBar = (busy: boolean) =>
    renderIn(
      <main>
        <SaveBar
          unsaved
          history={{ undoable: true, redoable: false, onUndo: vi.fn(), onRedo: vi.fn() }}
          saveable
          busy={busy}
          onDiscard={vi.fn()}
          onSave={vi.fn()}
          data-testid="studio-save-bar"
        />
      </main>,
    )

  it('draws its Save button inside an ActionBar, which is the save bar', () => {
    renderSaveBar(false)

    const bar = screen.getByRole('button', { name: 'Save' }).closest('[data-action-bar]')

    expect(bar).not.toBeNull()
    expect(screen.getByTestId('studio-save-bar')).toHaveAttribute('data-action-bar')
  })

  it('keeps Discard a disabled button beside Save at narrow width, as one action to fold does not fold', () => {
    fakeWidths(300, 400)
    renderSaveBar(true)

    expect(screen.getByRole('button', { name: 'Discard' })).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
  })
})
