import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioMatrixCell, StudioMatrixRole, StudioNames, StudioPolicyMatrix } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { removeTag } from '../test/antd'
import { StudioPolicyMatrixTable } from './StudioPolicyMatrix'

const sharedPolicyId = '3f1c2d4e-5a6b-4c7d-8e9f-0a1b2c3d4e5f'

function cell(kind: string, target: string, overrides: Partial<StudioMatrixCell> = {}): StudioMatrixCell {
  return { kind, target, actions: [], missingEntityRead: null, grants: [], ...overrides }
}

const entityGrants: StudioMatrixCell['grants'] = [
  {
    policyId: 'p1',
    policy: 'EmployeeAccess',
    index: 0,
    actions: ['read'],
    filter: null,
    fields: null,
    shared: false,
  },
  {
    policyId: sharedPolicyId,
    policy: 'StaffAccess',
    index: 1,
    actions: ['read', 'update'],
    filter: 'record.requester == user.id',
    fields: ['title', 'amount'],
    shared: true,
  },
]

const matrix: StudioPolicyMatrix = {
  targets: [
    {
      kind: 'entity',
      id: 'e1',
      name: 'PurchaseRequest',
      module: null,
      actions: ['read', 'create', 'update', 'delete'],
      entity: null,
    },
    { kind: 'dataSource', id: 'd1', name: 'OpenRequests', module: null, actions: ['read'], entity: 'PurchaseRequest' },
    {
      kind: 'process',
      id: 'pr1',
      name: 'PurchaseApproval',
      module: null,
      actions: ['start', 'inspect', 'cancel', 'retry', 'resolve', 'reassign'],
      entity: null,
    },
    { kind: 'endpoint', id: 'ep1', name: 'Intake', module: null, actions: ['invoke'], entity: null },
  ],
  roles: [
    {
      id: 'r1',
      name: 'employee',
      accessPolicy: { name: 'EmployeeAccess', id: 'p1' },
      editable: true,
      readOnlyReason: null,
      cells: [
        cell('entity', 'PurchaseRequest', {
          actions: ['read', 'update'],
          grants: entityGrants,
        }),
        cell('dataSource', 'OpenRequests', { missingEntityRead: 'PurchaseRequest' }),
        cell('process', 'PurchaseApproval'),
        cell('endpoint', 'Intake'),
      ],
    },
    {
      id: 'r2',
      name: 'manager',
      accessPolicy: { name: 'EmployeeAccess', id: 'p1' },
      editable: false,
      readOnlyReason: 'accessPolicyShared',
      cells: [
        cell('entity', 'PurchaseRequest', { actions: ['read', 'update'], grants: entityGrants }),
        cell('dataSource', 'OpenRequests'),
        cell('process', 'PurchaseApproval'),
        cell('endpoint', 'Intake'),
      ],
    },
  ],
}

const application = 'Shop'
const policyUrl = `/api/studio/apps/${application}/resources/p1`

const policyContent = {
  id: 'p1',
  kind: 'policy',
  name: 'EmployeeAccess',
  formatVersion: 1,
  roles: ['employee'],
  grants: [
    { entity: 'PurchaseRequest', actions: ['read'] },
    { process: 'PurchaseApproval', actions: ['start'] },
    { entity: 'PurchaseRequest', actions: ['update'], fields: ['title'] },
  ],
}

const names = {
  entities: [{ id: 'e1', name: 'PurchaseRequest', module: null, fields: [{ name: 'title', type: 'text' }] }],
  rules: [],
} as unknown as StudioNames

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Stubs the policy read and the patch. `patchStatus` 409 makes the patch a conflict. */
function stubFetch(content: unknown = policyContent, patchStatus = 200) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input === policyUrl && init?.method === 'PATCH') {
      return patchStatus === 409 ? new Response(null, { status: 409 }) : json({ version: 'v2', diagnostics: [] })
    }
    if (input === policyUrl) {
      return json({
        id: 'p1',
        kind: 'policy',
        name: 'EmployeeAccess',
        file: 'policies/employee-access.json',
        version: 'v1',
        usedBy: 0,
        content,
      })
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function patches(fetchMock: ReturnType<typeof stubFetch>) {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === 'PATCH')
}

function renderMatrix(overrides: Partial<StudioPolicyMatrix> = {}) {
  const onOpenPolicy = vi.fn()
  const onWritten = vi.fn()
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioPolicyMatrixTable
          application={application}
          matrix={{ ...matrix, ...overrides }}
          names={names}
          onOpenPolicy={onOpenPolicy}
          onWritten={onWritten}
        />
      </TextProvider>
    </ThemeModeProvider>,
  )
  return Object.assign(onOpenPolicy, { onWritten })
}

afterEach(() => {
  localStorage.clear()
  vi.unstubAllGlobals()
})

describe.each(['light', 'dark'])('StudioPolicyMatrixTable in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('lists the combined actions of a role on a target', () => {
    renderMatrix()

    const actions = within(screen.getByTestId('studio-matrix-cell-employee-entity-PurchaseRequest'))
    expect(actions.getByText('read')).toBeInTheDocument()
    expect(actions.getByText('update')).toBeInTheDocument()
  })

  it('marks a shared grant and opens its policy', async () => {
    stubFetch()
    const onOpenPolicy = renderMatrix()

    await userEvent.click(screen.getByTestId('studio-matrix-cell-employee-entity-PurchaseRequest'))

    const grants = screen.getAllByTestId('studio-matrix-grant')
    expect(grants).toHaveLength(1)
    expect(within(grants[0]).getByText('Shared')).toBeInTheDocument()
    expect(within(grants[0]).queryByRole('checkbox')).not.toBeInTheDocument()
    await userEvent.click(within(grants[0]).getByRole('button', { name: 'Open policy' }))
    expect(onOpenPolicy).toHaveBeenCalledWith(sharedPolicyId)
  })

  it('shows the filter and the fields of a grant of a read-only row, and all fields when none are listed', async () => {
    renderMatrix()

    await userEvent.click(screen.getByTestId('studio-matrix-cell-manager-entity-PurchaseRequest'))

    const grants = screen.getAllByTestId('studio-matrix-grant')
    expect(within(grants[0]).getByText('No filter')).toBeInTheDocument()
    expect(within(grants[0]).getByText('All fields')).toBeInTheDocument()
    expect(within(grants[1]).getByText('record.requester == user.id')).toBeInTheDocument()
    expect(within(grants[1]).getByText('title')).toBeInTheDocument()
    expect(within(grants[1]).getByText('amount')).toBeInTheDocument()
    expect(within(grants[1]).queryByText('All fields')).not.toBeInTheDocument()
  })

  it('offers no checkbox and no Add grant on a read-only row', async () => {
    const fetchMock = stubFetch()
    renderMatrix()

    await userEvent.click(screen.getByTestId('studio-matrix-cell-manager-entity-PurchaseRequest'))

    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add grant' })).not.toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('says a cell has no grant', async () => {
    renderMatrix()

    await userEvent.click(screen.getByTestId('studio-matrix-cell-manager-dataSource-OpenRequests'))

    expect(screen.getByText('No grant.')).toBeInTheDocument()
  })

  it('offers only the actions a process takes', async () => {
    stubFetch()
    renderMatrix()

    await userEvent.click(screen.getByTestId('studio-matrix-cell-employee-process-PurchaseApproval'))

    const group = await screen.findByTestId('studio-matrix-actions')
    expect(
      within(group)
        .getAllByRole('checkbox')
        .map((box) => (box as HTMLInputElement).value),
    ).toEqual(['start', 'inspect', 'cancel', 'retry', 'resolve', 'reassign'])
    expect(within(group).getByRole('checkbox', { name: 'start' })).toBeChecked()
    expect(within(group).getByRole('checkbox', { name: 'inspect' })).not.toBeChecked()
  })

  it('offers no start on an entity, and only invoke on an endpoint', async () => {
    stubFetch()
    renderMatrix()

    await userEvent.click(screen.getByTestId('studio-matrix-cell-employee-entity-PurchaseRequest'))
    const groups = await screen.findAllByTestId('studio-matrix-actions')

    expect(groups).toHaveLength(2)
    for (const group of groups) {
      expect(
        within(group)
          .getAllByRole('checkbox')
          .map((box) => (box as HTMLInputElement).value),
      ).toEqual(['read', 'create', 'update', 'delete', 'export', 'import'])
      expect(within(group).queryByRole('checkbox', { name: 'start' })).not.toBeInTheDocument()
    }

    await userEvent.click(screen.getByTestId('studio-matrix-cell-employee-endpoint-Intake'))
    await screen.findByRole('button', { name: 'Add grant' })
    expect(screen.queryByTestId('studio-matrix-actions')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Add grant' }))
    expect(screen.getAllByRole('checkbox').map((box) => (box as HTMLInputElement).value)).toEqual(['invoke'])
  })

  it('sends one patch with the loaded version that changes only the edited grant', async () => {
    const fetchMock = stubFetch()
    const { onWritten } = renderMatrix()

    await userEvent.click(screen.getByTestId('studio-matrix-cell-employee-process-PurchaseApproval'))
    const group = await screen.findByTestId('studio-matrix-actions')
    await userEvent.click(within(group).getByRole('checkbox', { name: 'inspect' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(onWritten).toHaveBeenCalled())
    expect(onWritten.mock.calls[0]).toEqual([[], undefined])
    const sent = patches(fetchMock)
    expect(sent).toHaveLength(1)
    const [url, init] = sent[0]
    expect(url).toBe(policyUrl)
    expect(new Headers(init!.headers).get('If-Match')).toBe('"v1"')
    expect(JSON.parse(init!.body as string)).toEqual([
      { op: 'replace', path: '/grants/1/actions', value: ['start', 'inspect'] },
    ])
    expect(await screen.findByTestId('studio-matrix-saved')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled())
  })

  it('keeps an action the target does not accept and shows a warning for it', async () => {
    const content = { ...policyContent, grants: [{ process: 'PurchaseApproval', actions: ['read', 'start'] }] }
    const fetchMock = stubFetch(content)
    renderMatrix()

    await userEvent.click(screen.getByTestId('studio-matrix-cell-employee-process-PurchaseApproval'))
    const group = await screen.findByTestId('studio-matrix-actions')
    expect(screen.getByText('Not accepted here: read')).toBeInTheDocument()
    await userEvent.click(within(group).getByRole('checkbox', { name: 'cancel' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(patches(fetchMock)).toHaveLength(1))
    expect(JSON.parse(patches(fetchMock)[0][1]!.body as string)).toEqual([
      { op: 'replace', path: '/grants/0/actions', value: ['start', 'cancel', 'read'] },
    ])
  })

  it('removes the field list when the last field is cleared', async () => {
    const fetchMock = stubFetch()
    renderMatrix()

    await userEvent.click(screen.getByTestId('studio-matrix-cell-employee-entity-PurchaseRequest'))
    await screen.findAllByTestId('studio-matrix-actions')
    const own = screen.getAllByTestId('studio-matrix-own-grant')
    await removeTag(own[1], 'title')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(patches(fetchMock)).toHaveLength(1))
    expect(JSON.parse(patches(fetchMock)[0][1]!.body as string)).toEqual([{ op: 'remove', path: '/grants/2/fields' }])
  })

  it('disables Save while a grant has no action and Remove on the only grant', async () => {
    stubFetch({ ...policyContent, grants: [{ process: 'PurchaseApproval', actions: ['start'] }] })
    renderMatrix()

    await userEvent.click(screen.getByTestId('studio-matrix-cell-employee-process-PurchaseApproval'))
    const group = await screen.findByTestId('studio-matrix-actions')
    expect(screen.getByRole('button', { name: 'Remove grant' })).toBeDisabled()

    await userEvent.click(within(group).getByRole('checkbox', { name: 'start' }))
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()

    await userEvent.click(within(group).getByRole('checkbox', { name: 'retry' }))
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()

    await userEvent.click(screen.getByRole('button', { name: 'Add grant' }))
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
    expect(screen.getAllByRole('button', { name: 'Remove grant' })[0]).toBeEnabled()
  })

  it('shows the conflict message on a 409, keeps the draft and reloads on request', async () => {
    const fetchMock = stubFetch(policyContent, 409)
    renderMatrix()

    await userEvent.click(screen.getByTestId('studio-matrix-cell-employee-process-PurchaseApproval'))
    const group = await screen.findByTestId('studio-matrix-actions')
    await userEvent.click(within(group).getByRole('checkbox', { name: 'cancel' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByTestId('studio-matrix-conflict')).toHaveTextContent('Someone else changed this policy')
    expect(within(screen.getByTestId('studio-matrix-actions')).getByRole('checkbox', { name: 'cancel' })).toBeChecked()

    await userEvent.click(screen.getByRole('button', { name: 'Reload' }))
    await waitFor(() => expect(screen.queryByTestId('studio-matrix-conflict')).not.toBeInTheDocument())
    expect(
      within(await screen.findByTestId('studio-matrix-actions')).getByRole('checkbox', { name: 'cancel' }),
    ).not.toBeChecked()
    expect(fetchMock.mock.calls.filter(([url, init]) => url === policyUrl && init?.method === undefined)).toHaveLength(
      2,
    )
  })

  it('creates the access policy for a role that has none', async () => {
    const fetchMock = vi.fn(async (_input: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        const body = JSON.parse(init.body as string) as Record<string, unknown>
        return json(
          {
            id: body.id,
            kind: 'policy',
            name: body.name,
            file: 'policies/tester-access.json',
            version: 'c1',
            usedBy: 0,
            diagnostics: [],
          },
          201,
        )
      }
      return new Response(null, { status: 404 })
    })
    vi.stubGlobal('fetch', fetchMock)
    const roles: StudioMatrixRole[] = [
      {
        ...matrix.roles[0],
        id: 'r9',
        name: 'tester',
        accessPolicy: { name: 'TesterAccess', id: null },
        cells: matrix.roles[0].cells.map((item) => ({ ...item, grants: [] })),
      },
    ]
    const { onWritten } = renderMatrix({ roles })

    await userEvent.click(screen.getByTestId('studio-matrix-cell-tester-process-PurchaseApproval'))
    await userEvent.click(await screen.findByRole('button', { name: 'Add grant' }))
    await userEvent.click(screen.getByRole('checkbox', { name: 'start' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(onWritten).toHaveBeenCalled())
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const body = JSON.parse(fetchMock.mock.calls[0][1]!.body as string)
    expect(body).toMatchObject({
      kind: 'policy',
      name: 'TesterAccess',
      formatVersion: 1,
      roles: ['tester'],
      grants: [{ process: 'PurchaseApproval', actions: ['start'] }],
    })
    expect(onWritten.mock.calls[0][1]).toMatchObject({ id: body.id, version: 'c1' })
  })

  it('shows why a row cannot be edited', () => {
    renderMatrix()

    expect(screen.getByTestId('studio-matrix-readonly-manager')).toHaveTextContent(
      'The matrix cannot change this role, because its access policy also names other roles. Policy: EmployeeAccess',
    )
    expect(screen.queryByTestId('studio-matrix-readonly-employee')).not.toBeInTheDocument()
  })

  it('warns that a data source cell lacks the read of its entity and names it', () => {
    renderMatrix()

    const warning = within(screen.getByTestId('studio-matrix-cell-employee-dataSource-OpenRequests')).getByTestId(
      'studio-matrix-missing-read',
    )
    expect(warning).toHaveTextContent('PurchaseRequest')
  })
})

describe('StudioPolicyMatrixTable reasons', () => {
  it('shows the raw code of a reason it has no text for', () => {
    const roles: StudioMatrixRole[] = [{ ...matrix.roles[1], readOnlyReason: 'somethingNew' }]
    render(
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioPolicyMatrixTable
            application={application}
            matrix={{ ...matrix, roles }}
            names={null}
            onOpenPolicy={vi.fn()}
            onWritten={vi.fn()}
          />
        </TextProvider>
      </ThemeModeProvider>,
    )

    expect(screen.getByTestId('studio-matrix-readonly-manager')).toHaveTextContent('somethingNew')
  })
})
