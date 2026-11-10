import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { TenantModelKey } from '../platform/tenantModelKey'
import { PlatformContext, type Platform } from './context'
import { TenantModelKeySection } from './TenantModelKeySection'

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

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Answers the read with each of `reads` in turn, then the last one again, and every change with `changeStatus`. */
function stubServer(reads: Response[], changeStatus = 204) {
  let read = 0
  const fetchMock = vi.fn(async (_input: string, init?: RequestInit) => {
    if (init?.method === 'PUT' || init?.method === 'DELETE') {
      return new Response(null, { status: changeStatus })
    }
    return reads[Math.min(read++, reads.length - 1)].clone()
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function changes(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === 'PUT' || init?.method === 'DELETE')
}

function renderSection() {
  return render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <TenantModelKeySection />
      </TextProvider>
    </PlatformContext.Provider>,
  )
}

async function expectHidden(fetchMock: ReturnType<typeof stubServer>) {
  await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/tenant/model-key', expect.anything()))
  await act(() => new Promise((resolve) => setTimeout(resolve, 0)))
  expect(screen.queryByTestId('tenant-model-key')).toBeNull()
  expect(screen.queryByText('Model key')).toBeNull()
}

const keySet: TenantModelKey = { set: true, ending: '7890' }
const keyNotSet: TenantModelKey = { set: false, ending: null }

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('TenantModelKeySection', () => {
  it('renders nothing and logs nothing when the route answers 403', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubServer([new Response(null, { status: 403 })])
    renderSection()

    await expectHidden(fetchMock)
    expect(warn).not.toHaveBeenCalled()
  })

  it('renders nothing when the route answers 404', async () => {
    const fetchMock = stubServer([new Response(null, { status: 404 })])
    renderSection()

    await expectHidden(fetchMock)
  })

  it('renders nothing and logs a warning when the route answers 503', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubServer([new Response(null, { status: 503 })])
    renderSection()

    await expectHidden(fetchMock)
    expect(warn).toHaveBeenCalledWith('Reading the model key failed with status 503.')
  })

  it('shows a set key masked with its ending and offers Change and Remove', async () => {
    stubServer([json(keySet)])
    renderSection()

    const section = await screen.findByTestId('tenant-model-key')
    expect(within(section).getByRole('heading', { name: 'Model key' })).toBeVisible()
    expect(within(section).getByTestId('tenant-model-key-masked')).toHaveTextContent(/^••••7890$/)
    expect(within(section).getByRole('button', { name: 'Change key…' })).toBeVisible()
    expect(within(section).getByRole('button', { name: 'Remove key…' })).toBeVisible()
    expect(within(section).queryByRole('button', { name: 'Set key…' })).toBeNull()
  })

  it('shows only the dots for a key of four characters or fewer', async () => {
    stubServer([json({ set: true, ending: null })])
    renderSection()

    expect(await screen.findByTestId('tenant-model-key-masked')).toHaveTextContent(/^••••$/)
  })

  it('says that no key is set and offers Set', async () => {
    stubServer([json(keyNotSet)])
    renderSection()

    const section = await screen.findByTestId('tenant-model-key')
    expect(within(section).getByText('No key is set.')).toBeVisible()
    expect(within(section).getByRole('button', { name: 'Set key…' })).toBeVisible()
    expect(within(section).queryByRole('button', { name: 'Remove key…' })).toBeNull()
    expect(within(section).queryByTestId('tenant-model-key-masked')).toBeNull()
  })

  it('sends the typed key with PUT only when Save is pressed, then reads the key again', async () => {
    const user = userEvent.setup()
    const fetchMock = stubServer([json(keyNotSet), json(keySet)])
    renderSection()

    await user.click(await screen.findByRole('button', { name: 'Set key…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Set the model key' })
    const field = within(dialog).getByLabelText('Key', { exact: true })
    expect(field).toHaveAttribute('type', 'password')
    expect(within(dialog).getByRole('button', { name: 'Save' })).toBeDisabled()
    await user.type(field, 'sk-test-1234567890')
    expect(changes(fetchMock)).toHaveLength(0)
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await screen.findByTestId('tenant-model-key-masked')).toHaveTextContent(/^••••7890$/)
    expect(changes(fetchMock)).toHaveLength(1)
    const [input, init] = changes(fetchMock)[0]
    expect(input).toBe('/api/tenant/model-key')
    expect(init?.method).toBe('PUT')
    expect(JSON.parse(init?.body as string)).toEqual({ key: 'sk-test-1234567890' })
    expect(screen.queryByText('sk-test-1234567890')).toBeNull()
  })

  it('names the dialog Change when a key is set and sends nothing on Cancel', async () => {
    const user = userEvent.setup()
    const fetchMock = stubServer([json(keySet)])
    renderSection()

    await user.click(await screen.findByRole('button', { name: 'Change key…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Change the model key' })
    await user.type(within(dialog).getByLabelText('Key', { exact: true }), 'abc')
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    expect(changes(fetchMock)).toHaveLength(0)
  })

  it('shows a message when the key was not saved', async () => {
    const user = userEvent.setup()
    stubServer([json(keyNotSet)], 500)
    renderSection()

    await user.click(await screen.findByRole('button', { name: 'Set key…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Set the model key' })
    await user.type(within(dialog).getByLabelText('Key', { exact: true }), 'sk-test-1234567890')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('The key was not saved.')).toBeVisible()
  })

  it('sends DELETE only after the confirmation, then reads the key again', async () => {
    const user = userEvent.setup()
    const fetchMock = stubServer([json(keySet), json(keyNotSet)])
    renderSection()

    await user.click(await screen.findByRole('button', { name: 'Remove key…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Remove the model key?' })
    expect(changes(fetchMock)).toHaveLength(0)
    await user.click(within(dialog).getByRole('button', { name: 'Remove' }))

    expect(await screen.findByText('No key is set.')).toBeVisible()
    expect(changes(fetchMock)).toHaveLength(1)
    expect(changes(fetchMock)[0][0]).toBe('/api/tenant/model-key')
    expect(changes(fetchMock)[0][1]?.method).toBe('DELETE')
  })

  it('shows a message when the key was not removed', async () => {
    const user = userEvent.setup()
    stubServer([json(keySet)], 500)
    renderSection()

    await user.click(await screen.findByRole('button', { name: 'Remove key…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Remove the model key?' })
    await user.click(within(dialog).getByRole('button', { name: 'Remove' }))

    expect(await screen.findByText('The key was not removed.')).toBeVisible()
    expect(screen.getByTestId('tenant-model-key-masked')).toHaveTextContent(/^••••7890$/)
  })
})
