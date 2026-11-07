import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { axisLicenceUrl, axisSourceUrl, type Licences } from '../platform/licences'
import { isSignInNeeded } from '../platform/signInNeeded'
import { TextProvider } from '../platform/TextProvider'
import { PlatformContext, type Platform } from './context'
import { LicencesPage } from './LicencesPage'

const platform: Platform = {
  site: {
    name: 'platform',
    titleKey: 'shell.title',
    locales: { default: 'en', fallback: 'en', available: ['en'] },
    navigation: [],
  },
  fallbackTexts: platformEnglish,
  current: { locale: 'en', texts: platformEnglish },
  setCurrent: () => {},
  development: false,
}

const publicLicences: Licences = {
  npm: [{ name: 'react', licence: 'MIT' }],
  nuget: [{ name: 'Npgsql', licence: 'PostgreSQL' }],
}

const privateLicences: Licences = {
  npm: [{ name: 'react', version: '19.0.0', licence: 'MIT' }],
  nuget: [{ name: 'Npgsql', version: '2.0.0', licence: 'PostgreSQL' }],
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

const unauthorized = () => json({ title: 'Sign in to read or write this data.', status: 401 }, 401)

/** Stubs `fetch` with the three calls the page makes. */
function stubServer({
  licences,
  about,
  fallback = publicLicences,
}: {
  licences: () => Response
  about: () => Response
  fallback?: Licences
}) {
  const fetchMock = vi.fn(async (input: string) => {
    if (input === '/api/licences') {
      return licences()
    }
    if (input === '/api/about') {
      return about()
    }
    if (input === '/licences.json') {
      return json(fallback)
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderPage() {
  render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter>
          <LicencesPage />
        </MemoryRouter>
      </TextProvider>
    </PlatformContext.Provider>,
  )
}

const link = (name: string) => screen.getByRole('link', { name })

describe('LicencesPage', () => {
  it('shows names and licences without versions to an anonymous visitor, and never asks to sign in', async () => {
    const fetchMock = stubServer({ licences: unauthorized, about: unauthorized })

    renderPage()

    expect(await screen.findByText('react')).toBeInTheDocument()
    expect(screen.getByText('Npgsql')).toBeInTheDocument()
    expect(screen.getByText('MIT')).toBeInTheDocument()
    expect(screen.getByText('PostgreSQL')).toBeInTheDocument()
    expect(screen.queryByText('19.0.0')).not.toBeInTheDocument()
    expect(screen.queryByRole('columnheader', { name: 'Version' })).not.toBeInTheDocument()
    expect(link("Axis's source code")).toHaveAttribute('href', axisSourceUrl)
    expect(link('Read the licence text')).toHaveAttribute('href', axisLicenceUrl)
    expect(link('MIT')).toHaveAttribute('href', 'https://spdx.org/licenses/MIT.html')
    expect(isSignInNeeded()).toBe(false)
    expect(fetchMock).toHaveBeenCalledWith('/licences.json', expect.anything())
  })

  it('shows the versions to a signed-in person, and links the source at the commit the version names', async () => {
    stubServer({
      licences: () => json(privateLicences),
      about: () => json({ version: '0.1.0+749b198abc' }),
    })

    renderPage()

    expect(await screen.findByText('19.0.0')).toBeInTheDocument()
    expect(screen.getByText('2.0.0')).toBeInTheDocument()
    expect(screen.getAllByRole('columnheader', { name: 'Version' })).toHaveLength(2)
    expect(link("Axis's source code")).toHaveAttribute('href', `${axisSourceUrl}/tree/749b198abc`)
  })

  it('links the repository root when the version names no commit', async () => {
    stubServer({ licences: () => json(privateLicences), about: () => json({ version: '0.1.0' }) })

    renderPage()

    await screen.findByText('19.0.0')
    expect(link("Axis's source code")).toHaveAttribute('href', axisSourceUrl)
  })

  it('filters both lists by name or licence', async () => {
    stubServer({ licences: unauthorized, about: unauthorized })
    renderPage()
    await screen.findByText('react')

    await userEvent.type(screen.getByRole('textbox', { name: 'Search by package name or licence' }), 'postgres')

    expect(screen.queryByText('react')).not.toBeInTheDocument()
    expect(screen.getByText('Npgsql')).toBeInTheDocument()

    await userEvent.clear(screen.getByRole('textbox', { name: 'Search by package name or licence' }))
    await userEvent.type(screen.getByRole('textbox', { name: 'Search by package name or licence' }), 'mit')

    expect(within(screen.getByTestId('licences-npm')).getByText('react')).toBeInTheDocument()
    expect(screen.queryByText('Npgsql')).not.toBeInTheDocument()
  })

  it('puts the licence link and the source link side by side', async () => {
    stubServer({ licences: unauthorized, about: unauthorized })
    renderPage()
    await screen.findByText('react')

    const parent = link('Read the licence text').parentElement!

    expect(link("Axis's source code").parentElement).toBe(parent)
  })

  it('shows an error when the list fails for a reason other than 401', async () => {
    stubServer({
      licences: () => json({ title: 'The licence list was not built.', status: 404 }, 404),
      about: unauthorized,
    })

    renderPage()

    await waitFor(() =>
      expect(screen.getByTestId('licences-error')).toHaveTextContent('The licences could not be loaded.'),
    )
    expect(screen.queryByText('react')).not.toBeInTheDocument()
  })
})
