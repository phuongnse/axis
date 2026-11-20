import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { useCurrentUser } from './currentUser'
import { PersonName } from './PersonName'
import { TextProvider } from './TextProvider'
import { usePersonText } from './usePersonText'

function currentUser(overrides: { users: boolean }) {
  return { id: 'u1', displayName: 'Ann', roles: [], ...overrides }
}

function stubMe(body: object) {
  const fetchMock = vi.fn(
    async (_input: string) =>
      new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }),
  )
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function Words({ id, name }: { id: string; name: string }) {
  return <p data-testid={`words-${id}`}>{usePersonText(id, name)}</p>
}

function Probe({ label }: { label: string }) {
  return <p>{`${label}: ${useCurrentUser()?.displayName ?? 'loading'}`}</p>
}

function renderInPage(ui: React.ReactNode) {
  return render(
    <MemoryRouter>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        {ui}
      </TextProvider>
    </MemoryRouter>,
  )
}

describe('PersonName', () => {
  it("follows the signed-in person's name with (you) and no other name, and usePersonText gives the same words", async () => {
    stubMe(currentUser({ users: false }))
    renderInPage(
      <>
        <p data-testid="own">
          <PersonName id="u1" name="Ann" />
        </p>
        <p data-testid="other">
          <PersonName id="u2" name="Bob" />
        </p>
        <Words id="u1" name="Ann" />
        <Words id="u2" name="Bob" />
      </>,
    )

    expect(await screen.findByText('Ann (you)', { selector: '[data-testid="words-u1"]' })).toBeVisible()
    expect(screen.getByTestId('own')).toHaveTextContent('Ann (you)')
    expect(screen.getByTestId('other')).toHaveTextContent(/^Bob$/)
    expect(screen.getByTestId('words-u2')).toHaveTextContent(/^Bob$/)
  })

  it('links to the user page for a reader who may administer users', async () => {
    stubMe(currentUser({ users: true }))
    renderInPage(<PersonName id="u2" name="Bob" />)

    expect(await screen.findByRole('link', { name: 'Bob' })).toHaveAttribute('href', '/users/u2')
  })

  it('is plain text, with no link, for a reader who may not administer users', async () => {
    stubMe(currentUser({ users: false }))
    renderInPage(
      <>
        <PersonName id="u2" name="Bob" />
        <Probe label="reader" />
      </>,
    )

    expect(await screen.findByText('reader: Ann')).toBeVisible()
    expect(screen.getByText('Bob')).toBeVisible()
    expect(screen.queryByRole('link')).toBeNull()
  })

  it('makes one GET /api/me for two callers on one page', async () => {
    const fetchMock = stubMe(currentUser({ users: false }))
    renderInPage(
      <>
        <Probe label="first" />
        <Probe label="second" />
      </>,
    )

    expect(await screen.findByText('first: Ann')).toBeVisible()
    expect(await screen.findByText('second: Ann')).toBeVisible()
    expect(fetchMock.mock.calls.filter(([input]) => input === '/api/me')).toHaveLength(1)
  })
})
