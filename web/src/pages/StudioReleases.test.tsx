import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioReleaseItem, StudioReleases } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { StudioReleasesList } from './StudioReleases'

function item(releaseId: string, number: number, activatedAt: string, label: string | null): StudioReleaseItem {
  return {
    releaseId,
    number,
    contentHash: `hash-${number}`,
    activatedAt,
    actor: 'user-1',
    actorName: number === 1 ? 'Author User' : null,
    label,
    note: null,
    requestId: null,
  }
}

// Out of order, and release 2 is active twice: it was rolled back to after release 3.
const history: StudioReleases = {
  active: { releaseId: 'r2', number: 2, contentHash: 'hash-2' },
  folderContentHash: null,
  items: [
    item('r1', 1, '2026-05-01T10:00:00Z', 'First'),
    item('r2', 2, '2026-05-04T10:00:00Z', 'Second'),
    item('r3', 3, '2026-05-03T10:00:00Z', null),
    item('r2', 2, '2026-05-02T10:00:00Z', 'Second'),
  ],
}

function renderList(overrides: { history?: StudioReleases; canRollBack?: boolean; busy?: boolean } = {}) {
  const onRollBack = vi.fn()
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioReleasesList
          history={overrides.history ?? history}
          canRollBack={overrides.canRollBack ?? true}
          busy={overrides.busy ?? false}
          onRollBack={onRollBack}
        />
      </TextProvider>
    </ThemeModeProvider>,
  )
  return onRollBack
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('StudioReleasesList in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('lists the rows newest first and marks exactly one active', () => {
    renderList()

    const rows = screen.getAllByTestId('studio-release-row')
    expect(rows.map((row) => row.getAttribute('data-number'))).toEqual(['2', '3', '2', '1'])
    expect(rows.filter((row) => row.hasAttribute('data-active'))).toEqual([rows[0]])
    expect(within(rows[0]).getByText('Active')).toBeInTheDocument()
    expect(within(rows[3]).getByText('Author User')).toBeInTheDocument()
    expect(within(rows[1]).getByText('user-1')).toBeInTheDocument()
  })

  it('shows an empty text without releases', () => {
    renderList({ history: { active: null, folderContentHash: null, items: [] } })

    expect(screen.getByText('No release yet.')).toBeInTheDocument()
  })

  it('shows no Roll back button to a user who is not a publisher', () => {
    renderList({ canRollBack: false })

    expect(screen.queryByRole('button', { name: 'Roll back' })).not.toBeInTheDocument()
  })

  it('shows Roll back on the rows of other releases and never on the active release', () => {
    renderList()

    const rows = screen.getAllByTestId('studio-release-row')
    const buttons = rows.map((row) => within(row).queryByRole('button', { name: 'Roll back' }))
    expect(buttons.map((button) => button !== null)).toEqual([false, true, false, true])
  })

  it('turns the buttons off while busy', () => {
    renderList({ busy: true })

    for (const button of screen.getAllByRole('button', { name: 'Roll back' })) {
      expect(button).toBeDisabled()
    }
  })

  it('confirms a rollback with the release number and label', async () => {
    const user = userEvent.setup()
    const onRollBack = renderList()

    const row = screen.getAllByTestId('studio-release-row')[3]
    await user.click(within(row).getByRole('button', { name: 'Roll back' }))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Roll back to this release?')
    expect(dialog).toHaveTextContent('Release 1 First')
    expect(onRollBack).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole('button', { name: 'Roll back' }))

    expect(onRollBack).toHaveBeenCalledTimes(1)
    expect(onRollBack.mock.calls[0][0]).toMatchObject({ releaseId: 'r1', number: 1 })
  })

  it('does not roll back when the dialog is cancelled', async () => {
    const user = userEvent.setup()
    const onRollBack = renderList()

    await user.click(within(screen.getAllByTestId('studio-release-row')[1]).getByRole('button', { name: 'Roll back' }))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Release 3')
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    expect(onRollBack).not.toHaveBeenCalled()
  })
})
