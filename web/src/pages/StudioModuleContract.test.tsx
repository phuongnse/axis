import { render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
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
  it('shows the read-only notice, every state in the diagram and every edge', async () => {
    const fetchMock = renderView(
      new Response(
        JSON.stringify({ id: 'contract-id', kind: 'contract', name: 'ChecklistContract', content: contract }),
      ),
    )

    expect(await screen.findByTestId('studio-module-read-only')).toHaveAttribute('role', 'alert')
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
})
