import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ComponentProps } from 'react'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { followLink, renderGuarded } from '../test/leaveGuard'
import { SaveBar } from './SaveBar'

function renderBar(props: Partial<ComponentProps<typeof SaveBar>> = {}) {
  const handlers = { onUndo: vi.fn(), onRedo: vi.fn(), onDiscard: vi.fn(), onSave: vi.fn() }
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <main>
          <SaveBar unsaved undoable redoable={false} saveable busy={false} {...handlers} {...props} />
        </main>
      </TextProvider>
    </ThemeModeProvider>,
  )
  return handlers
}

describe('SaveBar', () => {
  describe('leaving unsaved changes', () => {
    function renderGuardedBar(props: Partial<ComponentProps<typeof SaveBar>>) {
      return renderGuarded(
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <main>
            <SaveBar
              unsaved
              undoable
              redoable={false}
              saveable
              busy={false}
              onUndo={vi.fn()}
              onRedo={vi.fn()}
              onDiscard={vi.fn()}
              onSave={vi.fn()}
              {...props}
            />
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

  it('discards once the dialog is confirmed with Discard', async () => {
    const { onDiscard } = renderBar()

    await userEvent.click(screen.getByRole('button', { name: 'Discard' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Discard' }))

    expect(onDiscard).toHaveBeenCalledOnce()
  })
})
