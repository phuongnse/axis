import { Alert, Button, Flex, Table, Tag, Typography } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useState } from 'react'
import {
  fetchStudioPolicyMatrix,
  ServerUnreachableError,
  type StudioMatrixCell,
  type StudioMatrixRole,
  type StudioMatrixTarget,
  type StudioPolicyMatrix as PolicyMatrix,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'

/** The cell a person selected: the role's row and the target's column, by index. */
interface Selection {
  role: string
  index: number
}

/**
 * The policy matrix, read-only. Roles are rows and the matrix's targets are columns, and each cell lists the role's
 * combined actions there. Selecting a cell opens its grants under the table: the policy, the actions, the record
 * filter and the fields. A shared grant is tagged Shared and its Open policy link calls `onOpenPolicy` with the
 * policy's id. A data source cell that lacks the root entity's `read` warns and names that entity. A row that cannot
 * be edited shows why.
 */
export function StudioPolicyMatrixTable({
  matrix,
  onOpenPolicy,
}: {
  matrix: PolicyMatrix
  onOpenPolicy: (policyId: string) => void
}) {
  const t = useText()
  const [selection, setSelection] = useState<Selection>()
  const role = matrix.roles.find((item) => item.name === selection?.role)
  const target = selection === undefined ? undefined : matrix.targets[selection.index]
  const cell = selection === undefined ? undefined : role?.cells[selection.index]

  const columns: ColumnsType<StudioMatrixRole> = [
    {
      key: 'role',
      title: t('shell.studio.matrix.role'),
      fixed: 'left',
      render: (_, row) => <RoleHeader role={row} />,
    },
    ...matrix.targets.map((item, index) => ({
      key: `${item.kind}-${item.name}`,
      title: (
        <Flex vertical>
          <span>{item.name}</span>
          <Typography.Text type="secondary">
            {item.module ? `${item.kind} · ${item.module}` : item.kind}
          </Typography.Text>
        </Flex>
      ),
      render: (_: unknown, row: StudioMatrixRole) => (
        <MatrixCell
          role={row}
          cell={row.cells[index]}
          selected={selection?.role === row.name && selection.index === index}
          onSelect={() => setSelection({ role: row.name, index })}
        />
      ),
    })),
  ]

  return (
    <Flex vertical gap="middle" data-testid="studio-matrix">
      <Table<StudioMatrixRole>
        size="small"
        pagination={false}
        rowKey={(row) => row.id}
        dataSource={matrix.roles}
        columns={columns}
        scroll={{ x: 'max-content' }}
      />
      {role && target && cell && <GrantPanel role={role} target={target} cell={cell} onOpenPolicy={onOpenPolicy} />}
    </Flex>
  )
}

function RoleHeader({ role }: { role: StudioMatrixRole }) {
  const t = useText()
  const reasonKey = `shell.studio.matrix.reason.${role.readOnlyReason}`
  const reasonText = t(reasonKey) === reasonKey ? role.readOnlyReason : t(reasonKey)
  return (
    <Flex vertical align="flex-start">
      <span>
        {role.name}
        {!role.editable && (
          <Tag style={{ marginInlineStart: 8 }} data-testid={`studio-matrix-readonly-tag-${role.name}`}>
            {t('shell.studio.matrix.readOnly')}
          </Tag>
        )}
      </span>
      {!role.editable && role.readOnlyReason !== null && (
        <Typography.Text type="secondary" data-testid={`studio-matrix-readonly-${role.name}`}>
          {reasonText}
          {role.readOnlyReason === 'accessPolicyShared' && ` ${role.accessPolicy.name}`}
        </Typography.Text>
      )}
    </Flex>
  )
}

function MatrixCell({
  role,
  cell,
  selected,
  onSelect,
}: {
  role: StudioMatrixRole
  cell: StudioMatrixCell
  selected: boolean
  onSelect: () => void
}) {
  const t = useText()
  return (
    <Button
      type={selected ? 'primary' : 'text'}
      size="small"
      aria-pressed={selected}
      data-testid={`studio-matrix-cell-${role.name}-${cell.kind}-${cell.target}`}
      style={{ height: 'auto', minWidth: 32 }}
      onClick={onSelect}
    >
      <Flex wrap gap={4} align="center">
        {cell.actions.map((action) => (
          <Tag key={action} style={{ marginInlineEnd: 0 }}>
            {action}
          </Tag>
        ))}
        {cell.missingEntityRead !== null && (
          <Tag color="warning" style={{ marginInlineEnd: 0 }} data-testid="studio-matrix-missing-read">
            {t('shell.studio.matrix.missingEntityRead')} {cell.missingEntityRead}
          </Tag>
        )}
      </Flex>
    </Button>
  )
}

function GrantPanel({
  role,
  target,
  cell,
  onOpenPolicy,
}: {
  role: StudioMatrixRole
  target: StudioMatrixTarget
  cell: StudioMatrixCell
  onOpenPolicy: (policyId: string) => void
}) {
  const t = useText()
  return (
    <section data-testid="studio-matrix-grants">
      <Typography.Title level={5}>
        {t('shell.studio.matrix.grants')} {role.name} · {target.name}
      </Typography.Title>
      {cell.grants.length === 0 && (
        <Typography.Text type="secondary">{t('shell.studio.matrix.noGrants')}</Typography.Text>
      )}
      <Flex vertical gap="small">
        {cell.grants.map((grant) => (
          <Flex key={`${grant.policyId}/${grant.index}`} vertical gap={4} data-testid="studio-matrix-grant">
            <Flex gap="small" align="center" wrap>
              <strong>{grant.policy}</strong>
              {grant.actions.map((action) => (
                <Tag key={action} style={{ marginInlineEnd: 0 }}>
                  {action}
                </Tag>
              ))}
              {grant.shared && <Tag color="processing">{t('shell.studio.matrix.shared')}</Tag>}
              {grant.shared && (
                <Button type="link" size="small" onClick={() => onOpenPolicy(grant.policyId)}>
                  {t('shell.studio.matrix.openPolicy')}
                </Button>
              )}
            </Flex>
            <div>
              {grant.filter === null || grant.filter === '' ? (
                <Typography.Text type="secondary">{t('shell.studio.matrix.noFilter')}</Typography.Text>
              ) : (
                <Typography.Text code>{grant.filter}</Typography.Text>
              )}
            </div>
            <Flex gap={4} wrap align="center">
              {grant.fields === null || grant.fields.length === 0 ? (
                <Typography.Text type="secondary">{t('shell.studio.matrix.allFields')}</Typography.Text>
              ) : (
                grant.fields.map((field) => (
                  <Tag key={field} style={{ marginInlineEnd: 0 }}>
                    {field}
                  </Tag>
                ))
              )}
            </Flex>
          </Flex>
        ))}
      </Flex>
    </section>
  )
}

/**
 * Loads the application's policy matrix and shows it. It loads again after a save, a text write, a delete and a
 * reconnect, because any change to the folder can change the matrix. It shows nothing until the matrix loads.
 */
export function StudioPolicyMatrix({
  application,
  folderRevision,
  onOpenPolicy,
}: {
  application: string
  folderRevision: number
  onOpenPolicy: (policyId: string) => void
}) {
  const t = useText()
  const { generation, lost } = useStudioConnection()
  const [state, setState] = useState<{ application: string; matrix: PolicyMatrix | null }>()

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioPolicyMatrix(application, controller.signal).then(
      (matrix) => setState({ application, matrix }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the Studio policy matrix of '${application}' failed`, error)
        setState({ application, matrix: null })
      },
    )
    return () => controller.abort()
  }, [application, generation, folderRevision, lost])

  if (state?.application !== application) {
    return null
  }
  if (state.matrix === null) {
    return <Alert data-testid="studio-matrix-load-error" type="error" message={t('shell.studio.matrix.loadFailed')} />
  }
  return <StudioPolicyMatrixTable matrix={state.matrix} onOpenPolicy={onOpenPolicy} />
}
