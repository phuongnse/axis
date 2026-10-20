import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioMatrixCell, StudioMatrixRole, StudioPolicyMatrix } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { StudioPolicyMatrixTable } from './StudioPolicyMatrix'

const sharedPolicyId = '3f1c2d4e-5a6b-4c7d-8e9f-0a1b2c3d4e5f'

function cell(kind: string, target: string, overrides: Partial<StudioMatrixCell> = {}): StudioMatrixCell {
  return { kind, target, actions: [], missingEntityRead: null, grants: [], ...overrides }
}

const matrix: StudioPolicyMatrix = {
  targets: [
    { kind: 'entity', id: 'e1', name: 'PurchaseRequest', module: null, actions: ['read', 'update'], entity: null },
    { kind: 'dataSource', id: 'd1', name: 'OpenRequests', module: null, actions: ['read'], entity: 'PurchaseRequest' },
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
          grants: [
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
          ],
        }),
        cell('dataSource', 'OpenRequests', { missingEntityRead: 'PurchaseRequest' }),
      ],
    },
    {
      id: 'r2',
      name: 'manager',
      accessPolicy: { name: 'EmployeeAccess', id: 'p1' },
      editable: false,
      readOnlyReason: 'accessPolicyShared',
      cells: [cell('entity', 'PurchaseRequest'), cell('dataSource', 'OpenRequests')],
    },
  ],
}

function renderMatrix() {
  const onOpenPolicy = vi.fn()
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioPolicyMatrixTable matrix={matrix} onOpenPolicy={onOpenPolicy} />
      </TextProvider>
    </ThemeModeProvider>,
  )
  return onOpenPolicy
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('StudioPolicyMatrixTable in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('lists the combined actions of a role on a target', () => {
    renderMatrix()

    const actions = within(screen.getByTestId('studio-matrix-cell-employee-entity-PurchaseRequest'))
    expect(actions.getByText('read')).toBeInTheDocument()
    expect(actions.getByText('update')).toBeInTheDocument()
  })

  it('marks a shared grant and opens its policy', async () => {
    const onOpenPolicy = renderMatrix()

    await userEvent.click(screen.getByTestId('studio-matrix-cell-employee-entity-PurchaseRequest'))

    const grants = screen.getAllByTestId('studio-matrix-grant')
    expect(grants).toHaveLength(2)
    expect(within(grants[0]).queryByText('Shared')).not.toBeInTheDocument()
    expect(within(grants[1]).getByText('Shared')).toBeInTheDocument()
    await userEvent.click(within(grants[1]).getByRole('button', { name: 'Open policy' }))
    expect(onOpenPolicy).toHaveBeenCalledWith(sharedPolicyId)
  })

  it('shows the filter and the fields of a grant, and all fields when none are listed', async () => {
    renderMatrix()

    await userEvent.click(screen.getByTestId('studio-matrix-cell-employee-entity-PurchaseRequest'))

    const grants = screen.getAllByTestId('studio-matrix-grant')
    expect(within(grants[0]).getByText('No filter')).toBeInTheDocument()
    expect(within(grants[0]).getByText('All fields')).toBeInTheDocument()
    expect(within(grants[1]).getByText('record.requester == user.id')).toBeInTheDocument()
    expect(within(grants[1]).getByText('title')).toBeInTheDocument()
    expect(within(grants[1]).getByText('amount')).toBeInTheDocument()
    expect(within(grants[1]).queryByText('All fields')).not.toBeInTheDocument()
  })

  it('says a cell has no grant', async () => {
    renderMatrix()

    await userEvent.click(screen.getByTestId('studio-matrix-cell-manager-entity-PurchaseRequest'))

    expect(screen.getByText('No grant.')).toBeInTheDocument()
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
          <StudioPolicyMatrixTable matrix={{ ...matrix, roles }} onOpenPolicy={vi.fn()} />
        </TextProvider>
      </ThemeModeProvider>,
    )

    expect(screen.getByTestId('studio-matrix-readonly-manager')).toHaveTextContent('somethingNew')
  })
})
