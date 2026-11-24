import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { StudioRevision, StudioRevisionChange } from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioRevisionPage } from './StudioHistory'

const application = 'PurchaseRequests'

const revision: StudioRevision = {
  id: 'r1',
  summary: 'Added the supplier field',
  author: 'Linh Nguyen',
  userId: 'u1',
  client: 'studio',
  request: null,
  time: '2026-11-02T09:30:00Z',
}

function stubServer(shown: StudioRevision, users: boolean, resources: StudioRevisionChange[] = []) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string) => {
      if (input === '/api/me') {
        return new Response(JSON.stringify({ id: 'me', displayName: 'Me', roles: [], users }))
      }
      if (input.startsWith(`/api/studio/apps/${application}/revisions/r1`)) {
        return new Response(JSON.stringify({ revision: shown, resources }))
      }
      return new Response(null, { status: 404 })
    }),
  )
}

function renderPage() {
  render(
    <MemoryRouter>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioRevisionPage application={application} revisionId="r1" resources={[]} />
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
}

/** The fact of the page's first part, a term over its value. */
function fact(name: string) {
  const term = within(screen.getByTestId('studio-section-facts')).getByText(name, { selector: 'dt' })
  return within(term.parentElement as HTMLElement)
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('StudioRevisionPage', () => {
  it('is headed by the summary with Revision as its line, and its first part lists who, when and through', async () => {
    stubServer(revision, true)
    renderPage()

    const heading = within(await screen.findByTestId('page-heading'))
    expect(heading.getByRole('heading', { level: 1, name: 'Added the supplier field' })).toBeVisible()
    expect(heading.getByText('Revision')).toBeVisible()
    expect(fact('Who').getByText('Linh Nguyen')).toBeVisible()
    expect(fact('When').getByText((_, element) => element?.tagName === 'TIME')).toHaveAttribute(
      'datetime',
      revision.time,
    )
    expect(fact('Through').getByText('Studio')).toBeVisible()
  })

  it('links the author to the user page for a reader who may administer users', async () => {
    stubServer(revision, true)
    renderPage()

    expect(await screen.findByRole('link', { name: 'Linh Nguyen' })).toHaveAttribute('href', '/users/u1')
  })

  it('shows the author as plain text, with no link, for a reader who may not administer users', async () => {
    stubServer(revision, false)
    renderPage()

    await screen.findByTestId('studio-section-facts')
    expect(fact('Who').getByText('Linh Nguyen')).toBeVisible()
    // /api/me has answered once the page stays without a link.
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/me', expect.anything()))
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })

  it('shows a changed package file as its line diff, with its removed and added lines marked', async () => {
    stubServer(revision, true, [
      {
        file: 'packages/approval-rules/adapter.py',
        change: 'changed',
        id: null,
        kind: null,
        name: null,
        before: 'def limit():\n    return 1000\n',
        after: 'def limit():\n    return 5000\n',
      },
    ])
    renderPage()

    const section = (await screen.findAllByTestId('studio-changes-file'))[0]
    const editor = await within(section).findByRole('textbox', { name: 'packages/approval-rules/adapter.py' })
    expect(editor).toBeVisible()
    await waitFor(() => expect(section.querySelector('.cm-deletedLine')).toHaveTextContent('return 1000'))
    expect(section.querySelector('.cm-changedLine')).toHaveTextContent('return 5000')
    expect(within(section).queryByTestId('json-diff-before')).toBeNull()
  })

  it('shows a revision the server made as System, in plain text, and leaves out Through without a client', async () => {
    stubServer({ ...revision, userId: null, author: 'startup', client: null }, true)
    renderPage()

    await screen.findByTestId('studio-section-facts')
    expect(fact('Who').getByText('System')).toBeVisible()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
    expect(screen.queryByText('Through')).not.toBeInTheDocument()
  })
})
