import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ComponentProps } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { designTokensFor } from './designTokens'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import { followLink, renderGuarded } from '../test/leaveGuard'
import { SaveBar } from './SaveBar'

type BarOptions = Partial<ComponentProps<typeof SaveBar>> & { undoable?: boolean; redoable?: boolean }

/** The bar's props: with Undo and Redo, which `undoable` and `redoable` set, unless `history` is given. */
function barProps({ undoable = true, redoable = false, ...props }: BarOptions, handlers: Record<string, () => void>) {
  return {
    unsaved: true,
    history: { undoable, redoable, onUndo: handlers.onUndo, onRedo: handlers.onRedo },
    saveable: true,
    busy: false,
    onDiscard: handlers.onDiscard,
    onSave: handlers.onSave,
    'data-testid': 'studio-save-bar',
    saveTestId: 'studio-save',
    ...props,
  }
}

function renderBar(options: BarOptions = {}) {
  const handlers = { onUndo: vi.fn(), onRedo: vi.fn(), onDiscard: vi.fn(), onSave: vi.fn() }
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <main>
          <SaveBar {...barProps(options, handlers)} />
        </main>
      </TextProvider>
    </ThemeModeProvider>,
  )
  return handlers
}

describe('SaveBar', () => {
  describe('leaving unsaved changes', () => {
    function renderGuardedBar(options: BarOptions) {
      const handlers = { onUndo: vi.fn(), onRedo: vi.fn(), onDiscard: vi.fn(), onSave: vi.fn() }
      return renderGuarded(
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <main>
            <SaveBar {...barProps(options, handlers)} />
          </main>
        </TextProvider>,
      )
    }

    it('makes the guard ask while there are unsaved changes', async () => {
      const router = renderGuardedBar({})

      await followLink()

      expect(await screen.findByText('Leave without saving?')).toBeInTheDocument()
      expect(router.state.location.pathname).toBe('/studio')
    })

    it('does not make the guard ask with only a step to redo', async () => {
      const router = renderGuardedBar({ unsaved: false, undoable: false, redoable: true, saveable: false })

      await followLink()

      expect(router.state.location.pathname).toBe('/elsewhere')
      expect(screen.queryByText('Leave without saving?')).not.toBeInTheDocument()
    })
  })

  it('is not drawn when nothing is unsaved and nothing can be redone', () => {
    renderBar({ unsaved: false, undoable: true, saveable: false })

    expect(screen.queryByTestId('studio-save-bar')).not.toBeInTheDocument()
  })

  it('reads "Unsaved changes" with Discard while there is a change to save', () => {
    renderBar()

    const bar = screen.getByTestId('studio-save-bar')
    expect(bar).toHaveTextContent('Unsaved changes')
    expect(within(bar).getByRole('button', { name: 'Discard' })).toBeEnabled()
    expect(within(bar).getByRole('button', { name: 'Save' })).toBeEnabled()
  })

  it('reads "No unsaved changes" with Redo on, no Discard and Save off while a step can be redone', () => {
    renderBar({ unsaved: false, undoable: false, redoable: true, saveable: false })

    const bar = screen.getByTestId('studio-save-bar')
    expect(bar).toHaveTextContent('No unsaved changes')
    expect(within(bar).getByRole('button', { name: 'Redo' })).toBeEnabled()
    expect(within(bar).getByRole('button', { name: 'Undo' })).toBeDisabled()
    expect(within(bar).queryByRole('button', { name: 'Discard' })).not.toBeInTheDocument()
    expect(within(bar).getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('draws no Undo and no Redo when the editor gives none, and still shows Discard and Save', () => {
    renderBar({ history: undefined })

    const bar = screen.getByTestId('studio-save-bar')
    expect(within(bar).queryByRole('button', { name: 'Undo' })).not.toBeInTheDocument()
    expect(within(bar).queryByRole('button', { name: 'Redo' })).not.toBeInTheDocument()
    expect(within(bar).getByRole('button', { name: 'Discard' })).toBeEnabled()
    expect(within(bar).getByRole('button', { name: 'Save' })).toBeEnabled()
  })

  it('is not drawn without a change when the editor gives no history', () => {
    renderBar({ history: undefined, unsaved: false, saveable: false })

    expect(screen.queryByTestId('studio-save-bar')).not.toBeInTheDocument()
  })

  it('draws in place outside a shell', () => {
    renderBar()

    expect(screen.getByRole('main')).toContainElement(screen.getByTestId('studio-save-bar'))
  })

  it('names its Save button by the given test id', () => {
    renderBar({ saveTestId: 'site-save' })

    expect(screen.getByTestId('site-save')).toHaveTextContent('Save')
  })

  it('draws Discard in the destructive tone', () => {
    renderBar()

    expect(within(screen.getByTestId('studio-save-bar')).getByRole('button', { name: 'Discard' })).toHaveStyle({
      color: designTokensFor('light').colour.dangerText,
    })
  })

  it('turns Undo, Redo and Discard off while a save runs', () => {
    renderBar({ redoable: true, busy: true })

    expect(screen.getByRole('button', { name: 'Undo' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Redo' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Discard' })).toBeDisabled()
  })

  it('calls Undo, Redo and Save', async () => {
    const handlers = renderBar({ redoable: true })

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }))
    await userEvent.click(screen.getByRole('button', { name: 'Redo' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(handlers.onUndo).toHaveBeenCalledOnce()
    expect(handlers.onRedo).toHaveBeenCalledOnce()
    expect(handlers.onSave).toHaveBeenCalledOnce()
  })

  it('asks before Discard, and Cancel leaves the changes', async () => {
    const { onDiscard } = renderBar()

    await userEvent.click(screen.getByRole('button', { name: 'Discard' }))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Discard all unsaved changes?')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    expect(onDiscard).not.toHaveBeenCalled()
  })

  it('finishes the Discard dialog in a solid red Discard', async () => {
    renderBar()

    await userEvent.click(screen.getByRole('button', { name: 'Discard' }))
    const dialog = await screen.findByRole('dialog')

    expect(within(dialog).getByRole('button', { name: 'Discard' })).toHaveStyle({
      background: designTokensFor('light').colour.danger,
    })
  })

  it('discards once the dialog is confirmed with Discard', async () => {
    const { onDiscard } = renderBar()

    await userEvent.click(screen.getByRole('button', { name: 'Discard' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Discard' }))

    expect(onDiscard).toHaveBeenCalledOnce()
  })
})
