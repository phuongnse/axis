import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioNewApplication } from './StudioNewApplication'

const template = {
  id: 'request-approval',
  version: '1.0.0',
  builtIn: false,
  title: { en: 'Request approval', vi: 'Phê duyệt yêu cầu' },
  description: { en: 'A request form, a list and a two-step approval.', vi: 'Một biểu mẫu yêu cầu.' },
  diagnostics: [],
}

const builtInTemplate = {
  ...template,
  id: 'tracker-test',
  builtIn: true,
  title: { en: 'Tracker test', vi: 'Kiểm thử theo dõi' },
  description: { en: 'A built-in test template.', vi: 'Một mẫu kiểm thử tích hợp sẵn.' },
}

const existsText = platformEnglish['shell.studio.newApplication.exists']
const invalidText = platformEnglish['shell.studio.newApplication.invalid']
const failedText = platformEnglish['shell.studio.newApplication.failed']
const templatesFailedText = platformEnglish['shell.studio.newApplication.templatesFailed']

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Stubs `fetch` with the template list and the create route, which answers what `create` returns. */
function stubFetch(
  create: () => Response = () => json({ name: 'Inventory', diagnostics: [] }, 201),
  templates: () => Response = () => json({ items: [template] }),
) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input === '/api/studio/templates') {
      return templates()
    }
    if (input === '/api/studio/apps' && init?.method === 'POST') {
      return create()
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderButton(onCreated = vi.fn()) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioNewApplication onCreated={onCreated} />
      </TextProvider>
    </ThemeModeProvider>,
  )
  return onCreated
}

async function openDialog() {
  await userEvent.click(screen.getByRole('button', { name: 'New application' }))
  return screen.findByRole('dialog')
}

async function typeName(dialog: HTMLElement, name: string) {
  await userEvent.type(within(dialog).getByRole('textbox', { name: 'Name' }), name)
}

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe.each(['light', 'dark'])('StudioNewApplication in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it.each(['1abc', 'a-b', 'Api', 'a'.repeat(61)])(
    'turns Create off and shows the invalid text for %s',
    async (name) => {
      stubFetch()
      renderButton()
      const dialog = await openDialog()

      await typeName(dialog, name)

      expect(within(dialog).getByRole('button', { name: 'Create' })).toBeDisabled()
      expect(within(dialog).getByText(invalidText)).toBeInTheDocument()
    },
  )

  it('allows Inventory and shows no invalid text', async () => {
    stubFetch()
    renderButton()
    const dialog = await openDialog()

    await typeName(dialog, 'Inventory')

    expect(within(dialog).getByRole('button', { name: 'Create' })).toBeEnabled()
    expect(within(dialog).queryByText(invalidText)).not.toBeInTheDocument()
  })

  it('lists Blank first, then each template with its title and description', async () => {
    stubFetch()
    renderButton()
    const dialog = await openDialog()

    expect(await within(dialog).findByText('Request approval')).toBeInTheDocument()
    expect(within(dialog).getByText('A request form, a list and a two-step approval.')).toBeInTheDocument()
    const labels = within(dialog)
      .getAllByRole('radio')
      .map((radio) => radio.closest('label')?.textContent)
    expect(labels).toEqual(['Blank', 'Request approvalA request form, a list and a two-step approval.'])
  })

  it('lists a built-in template first, with Built in in its name, and no mark on the other', async () => {
    stubFetch(undefined, () => json({ items: [builtInTemplate, template] }))
    renderButton()
    const dialog = await openDialog()

    await within(dialog).findByRole('radio', { name: /^Tracker test/ })
    const radios = within(dialog).getAllByRole('radio')
    expect(radios.map((radio) => radio.getAttribute('value'))).toEqual(['', 'tracker-test', 'request-approval'])
    expect(radios[1]).toHaveAccessibleName(/^Tracker test.*Built in/)
    expect(radios[2]).toHaveAccessibleName(/^Request approval/)
    expect(radios[2]).not.toHaveAccessibleName(/Built in/)
  })

  it('offers only Blank and a notice when the templates do not load', async () => {
    stubFetch(undefined, () => json({ title: 'No.' }, 500))
    renderButton()
    const dialog = await openDialog()

    expect(await within(dialog).findByText(templatesFailedText)).toBeInTheDocument()
    expect(within(dialog).getAllByRole('radio')).toHaveLength(1)
    expect(within(dialog).getByRole('radio', { name: 'Blank' })).toBeChecked()
  })

  it('creates a blank application without a template and passes its name on', async () => {
    const fetchMock = stubFetch()
    const onCreated = renderButton()
    const dialog = await openDialog()

    await typeName(dialog, 'Inventory')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create' }))

    await waitFor(() => expect(onCreated).toHaveBeenCalledWith('Inventory'))
    const post = fetchMock.mock.calls.find(([input, init]) => input === '/api/studio/apps' && init?.method === 'POST')!
    expect(JSON.parse(post[1]!.body as string)).toEqual({ name: 'Inventory' })
    expect(new Headers(post[1]!.headers).get('Content-Type')).toBe('application/json')
  })

  it('sends the chosen template with the name', async () => {
    const fetchMock = stubFetch(() => json({ name: 'Onboarding', diagnostics: [] }, 201))
    const onCreated = renderButton()
    const dialog = await openDialog()

    await userEvent.click(await within(dialog).findByRole('radio', { name: /Request approval/ }))
    await typeName(dialog, 'Onboarding')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create' }))

    await waitFor(() => expect(onCreated).toHaveBeenCalledWith('Onboarding'))
    const post = fetchMock.mock.calls.find(([input, init]) => input === '/api/studio/apps' && init?.method === 'POST')!
    expect(JSON.parse(post[1]!.body as string)).toEqual({ name: 'Onboarding', template: 'request-approval' })
  })

  it('shows that the name is taken on a 409 and keeps the dialog open', async () => {
    stubFetch(() => json({ title: 'Conflict' }, 409))
    const onCreated = renderButton()
    const dialog = await openDialog()

    await typeName(dialog, 'PurchaseRequests')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText(existsText)).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(onCreated).not.toHaveBeenCalled()
  })

  it('shows the generic failure on an answer it does not expect and keeps the dialog open', async () => {
    stubFetch(() => json({ title: 'Not found' }, 404))
    const onCreated = renderButton()
    const dialog = await openDialog()

    await typeName(dialog, 'Inventory')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText(failedText)).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(onCreated).not.toHaveBeenCalled()
  })
})
