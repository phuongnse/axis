import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { StudioConnectionContext } from '../platform/studioConnection'
import { ThemeModeProvider } from '../platform/theme'
import type { StudioDiagnostic, StudioResource } from '../platform/studio'
import { StudioModuleContract } from './StudioModuleContract'

afterEach(() => vi.unstubAllGlobals())

const contract = {
  entity: 'Checklist',
  lifecycle: {
    field: 'status',
    states: [{ name: 'open', initial: true, deletable: true }, { name: 'checked' }, { name: 'closed' }],
    transitions: [
      { name: 'check', from: ['open'], to: 'checked', guard: 'accountableUser() != null' },
      { name: 'reopen', from: ['checked'], to: 'open' },
      { name: 'close', from: ['checked'], to: 'closed' },
    ],
  },
  validations: [{ name: 'titleSet', expression: "title != ''", field: 'title' }],
  effects: [{ name: 'followUpCreated', on: 'close', create: { entity: 'Checklist', values: {} } }],
  ceiling: {
    slots: { reviewer: [{ actions: ['read'] }] },
    others: [{ actions: ['read', 'update'], fields: ['status'], filter: "status == 'open'" }],
  },
}

function renderView(response: Response) {
  const fetchMock = vi.fn(async (_input: string) => response)
  vi.stubGlobal('fetch', fetchMock)
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioModuleContract application="Checklist App" module="checklists" id="contract-id" />
      </TextProvider>
    </ThemeModeProvider>,
  )
  return fetchMock
}

function rowTexts(table: HTMLElement) {
  return within(table)
    .getAllByRole('row')
    .slice(1)
    .map((row) => row.textContent)
}

describe('StudioModuleContract', () => {
  it('shows the add-only notice, every state in the diagram and every edge', async () => {
    const fetchMock = renderView(
      new Response(
        JSON.stringify({ id: 'contract-id', kind: 'contract', name: 'ChecklistContract', content: contract }),
      ),
    )

    expect(await screen.findByTestId('studio-contract-add-only')).toHaveTextContent('add states and transitions')
    expect(screen.queryByTestId('studio-module-read-only')).toBeNull()
    expect(String(fetchMock.mock.calls[0]?.[0])).toBe(
      '/api/studio/apps/Checklist%20App/modules/checklists/resources/contract-id',
    )

    const lifecycle = await screen.findByTestId('studio-contract-lifecycle')
    expect(lifecycle).toHaveAccessibleName('Lifecycle')
    const open = await within(lifecycle).findByTestId('studio-contract-state-open')
    expect(open).toHaveTextContent('Initial')
    expect(open).toHaveTextContent('Deletable')
    const checked = within(lifecycle).getByTestId('studio-contract-state-checked')
    expect(checked).not.toHaveTextContent('Initial')
    expect(checked).not.toHaveTextContent('Deletable')
    expect(within(lifecycle).getByTestId('studio-contract-state-closed')).toBeInTheDocument()

    expect(await within(lifecycle).findByLabelText('open to checked · check · Guard')).toBeInTheDocument()
    expect(within(lifecycle).getByLabelText('checked to open · reopen · No guard')).toBeInTheDocument()
    expect(within(lifecycle).getByLabelText('checked to closed · close · No guard')).toBeInTheDocument()
  })

  it('lists the states, transitions, validations, effects and ceiling entries with their named points', async () => {
    renderView(new Response(JSON.stringify({ id: 'contract-id', kind: 'contract', name: 'C', content: contract })))

    const states = await screen.findByTestId('studio-contract-states')
    expect(rowTexts(states)).toEqual([
      'openInitial, Deletable',
      'checkedChecklist/states/checked/initialChecklist/states/checked/deletable',
      'closedChecklist/states/closed/initialChecklist/states/closed/deletable',
    ])

    expect(rowTexts(screen.getByTestId('studio-contract-transitions'))).toEqual([
      'checkopencheckedaccountableUser() != nullChecklist/transitions/check',
      'reopencheckedopenNo guardChecklist/transitions/reopen',
      'closecheckedclosedNo guardChecklist/transitions/close',
    ])
    expect(rowTexts(screen.getByTestId('studio-contract-validations'))).toEqual([
      "titleSettitletitle != ''Checklist/validations/titleSet",
    ])
    expect(rowTexts(screen.getByTestId('studio-contract-effects'))).toEqual([
      'followUpCreatedcloseChecklistChecklist/effects/followUpCreated',
    ])
    expect(rowTexts(screen.getByTestId('studio-contract-ceiling'))).toEqual([
      'reviewerreadAll fieldsChecklist/ceiling/reviewer',
      "Roles mapped to no slotread, updatestatusstatus == 'open'Checklist/ceiling/others",
    ])
    expect(screen.getByText('Checklist/states/closed/initial')).toBeInTheDocument()
  })

  it('shows no diagram and no table for a contract without a lifecycle', async () => {
    renderView(
      new Response(
        JSON.stringify({
          id: 'contract-id',
          kind: 'contract',
          name: 'C',
          content: { entity: 'Checklist', validations: contract.validations },
        }),
      ),
    )

    expect(await screen.findByTestId('studio-contract-validations')).toBeInTheDocument()
    expect(screen.queryByTestId('studio-contract-lifecycle')).toBeNull()
    expect(screen.queryByTestId('studio-contract-states')).toBeNull()
    expect(screen.queryByTestId('studio-contract-effects')).toBeNull()
  })

  it('shows the load error when the file does not load', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    renderView(new Response(null, { status: 404 }))

    expect(await screen.findByTestId('studio-resource-load-error')).toBeInTheDocument()
    expect(screen.queryByTestId('studio-module-read-only')).toBeNull()
  })

  describe('adding and removing states and transitions', () => {
    const layer = { id: 'layer-id', file: 'customizations/checklists/ChecklistContract.json', version: 'v1' }
    const withHold = {
      ...contract,
      lifecycle: {
        ...contract.lifecycle,
        states: [...contract.lifecycle.states, { name: 'onHold' }],
        transitions: [...contract.lifecycle.transitions, { name: 'hold', from: ['open'], to: 'onHold' }],
      },
    }
    const layered = {
      id: 'contract-id',
      kind: 'contract',
      name: 'ChecklistContract',
      content: withHold,
      layer,
      marks: [
        { path: '/lifecycle/states/3', mark: 'added' },
        { path: '/lifecycle/transitions/3', mark: 'added' },
      ],
      removed: [],
    }

    function renderLayered(diagnostics: StudioDiagnostic[] = []) {
      const fetchMock = vi.fn(async (_input: string, init?: RequestInit) =>
        init?.method === 'PUT'
          ? new Response(JSON.stringify({ file: null, version: null, diagnostics: [] }))
          : new Response(JSON.stringify(layered)),
      )
      vi.stubGlobal('fetch', fetchMock)
      const onWritten = vi.fn()
      render(
        <ThemeModeProvider>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <StudioModuleContract
              application="Checklist App"
              module="checklists"
              id="contract-id"
              diagnostics={diagnostics}
              onWritten={onWritten}
            />
          </TextProvider>
        </ThemeModeProvider>,
      )
      const puts = () => fetchMock.mock.calls.filter(([, init]) => init?.method === 'PUT')
      return { puts, onWritten }
    }

    it('marks an added state and transition Added with Remove, and the module rows have no Remove', async () => {
      renderLayered()

      expect(await screen.findByRole('note', { name: 'Added onHold' })).toBeInTheDocument()
      expect(screen.getByRole('note', { name: 'Added hold' })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Remove onHold' })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Remove hold' })).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Remove open' })).toBeNull()
      expect(screen.queryByRole('button', { name: 'Remove check' })).toBeNull()
      expect(screen.queryByRole('note', { name: 'Added open' })).toBeNull()
      expect(screen.queryByText('Checklist/states/onHold/initial')).toBeNull()
      expect(screen.queryByText('Checklist/transitions/hold')).toBeNull()
      expect(screen.getByText('Checklist/transitions/check')).toBeInTheDocument()
      expect(screen.getByTestId('studio-save')).toBeDisabled()
    })

    it('sends the contract without a removed state to the module save route on Save', async () => {
      const { puts, onWritten } = renderLayered()

      await userEvent.click(await screen.findByRole('button', { name: 'Remove onHold' }))
      expect(screen.queryByRole('note', { name: 'Added onHold' })).toBeNull()
      await userEvent.click(screen.getByTestId('studio-save'))

      await waitFor(() => expect(puts()).toHaveLength(1))
      const [url, init] = puts()[0]
      expect(url).toBe('/api/studio/apps/Checklist%20App/modules/checklists/resources/contract-id')
      expect(new Headers(init?.headers).get('If-Match')).toBe('"v1"')
      const body = JSON.parse(String(init?.body))
      expect(body.lifecycle.states.map((state: { name: string }) => state.name)).toEqual(['open', 'checked', 'closed'])
      expect(body.lifecycle.transitions.map((transition: { name: string }) => transition.name)).toContain('hold')
      await waitFor(() => expect(onWritten).toHaveBeenCalledTimes(1))
    })

    it('adds a state and a transition from the dialogs as unsaved Added rows', async () => {
      renderLayered()

      await userEvent.click(await screen.findByTestId('studio-contract-add-state'))
      const stateDialog = await screen.findByRole('dialog')
      expect(within(stateDialog).getByRole('button', { name: 'Add' })).toBeDisabled()
      await userEvent.type(within(stateDialog).getByRole('textbox', { name: 'Name' }), 'ONHOLD')
      expect(within(stateDialog).getByRole('button', { name: 'Add' })).toBeDisabled()
      await userEvent.clear(within(stateDialog).getByRole('textbox', { name: 'Name' }))
      await userEvent.type(within(stateDialog).getByRole('textbox', { name: 'Name' }), 'review')
      await userEvent.click(within(stateDialog).getByRole('checkbox', { name: 'Deletable' }))
      await userEvent.click(within(stateDialog).getByRole('button', { name: 'Add' }))

      expect(await screen.findByRole('note', { name: 'Added review' })).toBeInTheDocument()
      expect(screen.getByTestId('studio-save')).toBeEnabled()
      expect(screen.getByTestId('studio-contract-state-review')).toHaveTextContent('Deletable')
    })

    it('shows the diagnostics that name the contract layer and no other', async () => {
      const diagnostic = {
        code: 'AXC0370',
        severity: 'error' as const,
        message: 'The transition enters a protected state.',
        file: layer.file,
        path: '/changes/lifecycle/transitions/3/to',
      }
      renderLayered([
        { ...diagnostic, resourceId: 'layer-id' },
        { ...diagnostic, code: 'AXC0999', resourceId: 'other-id' },
      ])

      const list = await screen.findByTestId('studio-contract-diagnostics')
      expect(list).toHaveTextContent('AXC0370')
      expect(list).toHaveTextContent('The transition enters a protected state.')
      expect(list).not.toHaveTextContent('AXC0999')
    })
  })

  describe('relaxing a point', () => {
    const point = 'Checklist/transitions/check'
    const manifestPath = '/api/studio/apps/Checklist%20App/resources/manifest-id'

    function manifestWith(modules: unknown[]): StudioResource {
      return {
        id: 'manifest-id',
        kind: 'application',
        name: null,
        file: 'application.json',
        version: 'v1',
        usedBy: 0,
        content: { modules },
      }
    }

    function renderRelax(
      manifest: StudioResource,
      patch: Response = new Response(JSON.stringify({ version: 'v2', diagnostics: [] })),
    ) {
      const onManifestSaved = vi.fn()
      const onManifestStale = vi.fn()
      const fetchMock = vi.fn(async (_input: string, init?: RequestInit) =>
        init?.method === 'PATCH'
          ? patch
          : new Response(JSON.stringify({ id: 'contract-id', kind: 'contract', name: 'C', content: contract })),
      )
      vi.stubGlobal('fetch', fetchMock)
      render(
        <ThemeModeProvider>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <StudioConnectionContext.Provider value={{ generation: 0, reconnecting: false, lost: () => {} }}>
              <StudioModuleContract
                application="Checklist App"
                module="checklists"
                id="contract-id"
                manifest={manifest}
                onManifestSaved={onManifestSaved}
                onManifestStale={onManifestStale}
              />
            </StudioConnectionContext.Provider>
          </TextProvider>
        </ThemeModeProvider>,
      )
      const patches = () => fetchMock.mock.calls.filter(([, init]) => init?.method === 'PATCH')
      return { fetchMock, patches, onManifestSaved, onManifestStale }
    }

    async function openRelax() {
      await userEvent.click(await screen.findByTestId(`studio-contract-relax-${point}`))
      return screen.findByRole('textbox', { name: 'Reason' })
    }

    function okButton() {
      return within(screen.getByRole('dialog')).getByRole('button', { name: 'Relax' })
    }

    it('sends no write for a blank or whitespace-only reason', async () => {
      const { patches } = renderRelax(manifestWith([{ id: 'checklists', version: '^1.0.0' }]))
      const reason = await openRelax()

      expect(okButton()).toBeDisabled()
      await userEvent.click(okButton())
      await userEvent.type(reason, '   ')
      expect(okButton()).toBeDisabled()
      await userEvent.click(okButton())

      expect(patches()).toHaveLength(0)
    })

    it('adds the whole relax list for a module without one', async () => {
      const { patches, onManifestSaved } = renderRelax(manifestWith([{ id: 'checklists', version: '^1.0.0' }]))
      await userEvent.type(await openRelax(), '  Small team  ')
      await userEvent.click(okButton())

      await waitFor(() => expect(patches()).toHaveLength(1))
      const [url, init] = patches()[0]
      expect(url).toBe(manifestPath)
      expect(new Headers(init?.headers).get('If-Match')).toBe('"v1"')
      expect(JSON.parse(String(init?.body))).toEqual([
        { op: 'add', path: '/modules/0/relax', value: [{ point, reason: 'Small team' }] },
      ])
      await waitFor(() => expect(onManifestSaved).toHaveBeenCalledTimes(1))
      expect(onManifestSaved.mock.calls[0][0]).toMatchObject({
        version: 'v2',
        content: { modules: [{ id: 'checklists', relax: [{ point, reason: 'Small team' }] }] },
      })
    })

    it('adds one item at the end of an existing relax list', async () => {
      const { patches } = renderRelax(
        manifestWith([
          { id: 'other', version: '^1.0.0' },
          { id: 'checklists', version: '^1.0.0', relax: [{ point: 'Checklist/transitions/close', reason: 'Old' }] },
        ]),
      )
      await userEvent.type(await openRelax(), 'Small team')
      await userEvent.click(okButton())

      await waitFor(() => expect(patches()).toHaveLength(1))
      expect(JSON.parse(String(patches()[0][1]?.body))).toEqual([
        { op: 'add', path: '/modules/1/relax/1', value: { point, reason: 'Small team' } },
      ])
    })

    it('shows the reason of a relaxed point and removes its entry with Undo relax', async () => {
      const { patches } = renderRelax(
        manifestWith([
          {
            id: 'checklists',
            version: '^1.0.0',
            relax: [
              { point: 'Checklist/transitions/close', reason: 'Old' },
              { point, reason: 'Small team' },
            ],
          },
        ]),
      )

      expect(await screen.findByTestId(`studio-contract-relaxed-${point}`)).toHaveTextContent('Relaxed: Small team')
      expect(screen.queryByTestId(`studio-contract-relax-${point}`)).toBeNull()
      await userEvent.click(screen.getByTestId(`studio-contract-undo-relax-${point}`))

      await waitFor(() => expect(patches()).toHaveLength(1))
      expect(JSON.parse(String(patches()[0][1]?.body))).toEqual([{ op: 'remove', path: '/modules/0/relax/1' }])
    })

    it('removes the whole relax list when the last entry is undone', async () => {
      const { patches, onManifestSaved } = renderRelax(
        manifestWith([{ id: 'checklists', version: '^1.0.0', relax: [{ point, reason: 'Small team' }] }]),
      )
      await userEvent.click(await screen.findByTestId(`studio-contract-undo-relax-${point}`))

      await waitFor(() => expect(patches()).toHaveLength(1))
      expect(JSON.parse(String(patches()[0][1]?.body))).toEqual([{ op: 'remove', path: '/modules/0/relax' }])
      await waitFor(() => expect(onManifestSaved).toHaveBeenCalledTimes(1))
      expect(onManifestSaved.mock.calls[0][0].content).toEqual({ modules: [{ id: 'checklists', version: '^1.0.0' }] })
    })

    it('shows no Relax button for a module that application.json does not declare directly', async () => {
      renderRelax(manifestWith([{ id: 'other', version: '^1.0.0' }]))

      expect(await screen.findByTestId('studio-contract-states')).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Relax' })).toBeNull()
    })

    it('says the file changed on a conflict and asks for it to be loaded again', async () => {
      const { onManifestSaved, onManifestStale } = renderRelax(
        manifestWith([{ id: 'checklists', version: '^1.0.0' }]),
        new Response(null, { status: 409 }),
      )
      await userEvent.type(await openRelax(), 'Small team')
      await userEvent.click(okButton())

      expect(await screen.findByTestId('studio-contract-relax-conflict')).toHaveTextContent(
        'application.json changed since Studio loaded it',
      )
      expect(onManifestStale).toHaveBeenCalledTimes(1)
      expect(onManifestSaved).not.toHaveBeenCalled()
    })
  })
})
