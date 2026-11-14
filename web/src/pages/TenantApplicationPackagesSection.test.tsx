import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { PlatformContext, type Platform } from './context'
import { TenantApplicationPackagesSection } from './TenantApplicationPackagesSection'

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

const path = '/api/tenant/application-packages'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Answers the read with each of `reads` in turn, then the last one again, and every change with `change`. */
function stubServer(reads: Response[], change = new Response(null, { status: 204 })) {
  let read = 0
  const fetchMock = vi.fn(async (_input: string, init?: RequestInit) => {
    if (init?.method === 'PUT') {
      return change.clone()
    }
    return reads[Math.min(read++, reads.length - 1)].clone()
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function changes(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === 'PUT')
}

function reads(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.filter(([, init]) => init?.method !== 'PUT')
}

function renderSection() {
  return render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <TenantApplicationPackagesSection />
      </TextProvider>
    </PlatformContext.Provider>,
  )
}

async function expectHidden(fetchMock: ReturnType<typeof stubServer>) {
  await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledWith(path, expect.anything()))
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)))
  expect(screen.queryByTestId('tenant-application-packages')).toBeNull()
  expect(screen.queryByText('Application packages')).toBeNull()
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('TenantApplicationPackagesSection', () => {
  it.each([403, 404])('renders nothing and logs nothing when the route answers %i', async (status) => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubServer([new Response(null, { status })])
    renderSection()

    await expectHidden(fetchMock)
    expect(warn).not.toHaveBeenCalled()
  })

  it('renders nothing and logs a warning when the route answers 503', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubServer([new Response(null, { status: 503 })])
    renderSection()

    await expectHidden(fetchMock)
    expect(warn).toHaveBeenCalledWith('Reading the application packages switch failed with status 503.')
  })

  it('says that packages are off and offers Turn on', async () => {
    stubServer([json({ on: false })])
    renderSection()

    const section = await screen.findByTestId('tenant-application-packages')
    expect(within(section).getByRole('heading', { name: 'Application packages' })).toBeVisible()
    expect(within(section).getByText('Application packages are off.')).toBeVisible()
    expect(within(section).getByRole('button', { name: 'Turn on…' })).toBeVisible()
    expect(within(section).queryByRole('button', { name: 'Turn off…' })).toBeNull()
  })

  it('sends the PUT only from the confirmation, then reads the switch again', async () => {
    const user = userEvent.setup()
    const fetchMock = stubServer([json({ on: false }), json({ on: true })])
    renderSection()

    await user.click(await screen.findByRole('button', { name: 'Turn on…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Turn on application packages?' })
    expect(changes(fetchMock)).toHaveLength(0)
    await user.click(within(dialog).getByRole('button', { name: 'Turn on' }))

    expect(await screen.findByText('Application packages are on.')).toBeVisible()
    expect(changes(fetchMock)).toHaveLength(1)
    const [input, init] = changes(fetchMock)[0]
    expect(input).toBe(path)
    expect(JSON.parse(init?.body as string)).toEqual({ on: true })
    expect(reads(fetchMock)).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Turn off…' })).toBeVisible()
  })

  it('turns packages off from the confirmation', async () => {
    const user = userEvent.setup()
    const fetchMock = stubServer([json({ on: true }), json({ on: false })])
    renderSection()

    await user.click(await screen.findByRole('button', { name: 'Turn off…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Turn off application packages?' })
    await user.click(within(dialog).getByRole('button', { name: 'Turn off' }))

    expect(await screen.findByText('Application packages are off.')).toBeVisible()
    expect(JSON.parse(changes(fetchMock)[0][1]?.body as string)).toEqual({ on: false })
  })

  it('sends nothing on Cancel', async () => {
    const user = userEvent.setup()
    const fetchMock = stubServer([json({ on: false })])
    renderSection()

    await user.click(await screen.findByRole('button', { name: 'Turn on…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Turn on application packages?' })
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    expect(changes(fetchMock)).toHaveLength(0)
    expect(reads(fetchMock)).toHaveLength(1)
  })

  it("shows a quota refusal's title and keeps the state", async () => {
    const user = userEvent.setup()
    stubServer(
      [json({ on: false })],
      json(
        {
          title: 'This tenant has reached its package run time quota.',
          reason: 'quota-exceeded',
          quota: 'packageCpuSecondsPerDay',
          limit: 0,
          used: 0,
        },
        403,
      ),
    )
    renderSection()

    await user.click(await screen.findByRole('button', { name: 'Turn on…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Turn on application packages?' })
    await user.click(within(dialog).getByRole('button', { name: 'Turn on' }))

    expect(await screen.findByText('This tenant has reached its package run time quota.')).toBeVisible()
    expect(screen.getByText('Application packages are off.')).toBeVisible()
  })

  it('shows a message when the change was not saved', async () => {
    const user = userEvent.setup()
    stubServer([json({ on: false })], new Response(null, { status: 500 }))
    renderSection()

    await user.click(await screen.findByRole('button', { name: 'Turn on…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Turn on application packages?' })
    await user.click(within(dialog).getByRole('button', { name: 'Turn on' }))

    expect(await screen.findByText('The change was not saved.')).toBeVisible()
  })
})
