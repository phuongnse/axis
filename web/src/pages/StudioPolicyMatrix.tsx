import { Alert, Button, Checkbox, Flex, Input, Select, Table, type TableColumnsType, Tag, Typography } from 'antd'
import { lazy, Suspense, useEffect, useMemo, useState } from 'react'
import { diffPatch, setAt } from '../platform/jsonPatch'
import {
  createStudioResource,
  fetchStudioPolicyMatrix,
  fetchStudioResource,
  patchStudioResource,
  ServerUnreachableError,
  type StudioDiagnostic,
  type StudioMatrixCell,
  type StudioMatrixGrant,
  type StudioMatrixRole,
  type StudioMatrixTarget,
  type StudioNames,
  type StudioPolicyMatrix as PolicyMatrix,
  type StudioResourceItem,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { expressionScope } from './expressionScope'
import { newGrant, newPolicy, ownGrantIndexes } from './policyMatrixDraft'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

type Content = Record<string, unknown>

/** The matrix objects get an id, so a panel loads its policy again when the matrix loads again. */
const matrixIds = new WeakMap<object, number>()
let matrixCount = 0

function matrixId(matrix: object): number {
  let id = matrixIds.get(matrix)
  if (id === undefined) {
    id = matrixCount++
    matrixIds.set(matrix, id)
  }
  return id
}

/** The cell a person selected: the role's row and the target's column, by index. */
interface Selection {
  role: string
  index: number
}

/**
 * The policy matrix. Roles are rows and the matrix's targets are columns, and each cell lists the role's combined
 * actions there. Selecting a cell opens its grants under the table: the policy, the actions, the record filter and
 * the fields. A shared grant is tagged Shared and its Open policy link calls `onOpenPolicy` with the policy's id. A
 * data source cell that lacks the root entity's `read` warns and names that entity. A row that cannot be edited shows
 * why. In an editable row the cell's own grants, the ones in the role's `<Role>Access` policy, can be edited and
 * saved, which calls `onWritten` with the folder's diagnostics and, when the save created the policy, its item.
 */
export function StudioPolicyMatrixTable({
  application,
  matrix,
  names,
  onOpenPolicy,
  onWritten,
}: {
  application: string
  matrix: PolicyMatrix
  names: StudioNames | null
  onOpenPolicy: (policyId: string) => void
  onWritten: (diagnostics: StudioDiagnostic[], created?: StudioResourceItem) => void
}) {
  const t = useText()
  const [selection, setSelection] = useState<Selection>()
  const [saved, setSaved] = useState(false)
  const role = matrix.roles.find((item) => item.name === selection?.role)
  const target = selection === undefined ? undefined : matrix.targets[selection.index]
  const cell = selection === undefined ? undefined : role?.cells[selection.index]

  const columns: TableColumnsType<StudioMatrixRole> = [
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
          onSelect={() => {
            setSaved(false)
            setSelection({ role: row.name, index })
          }}
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
      {role && target && cell && (
        <GrantPanel
          key={`${role.name}/${selection?.index}/${matrixId(matrix)}`}
          application={application}
          role={role}
          target={target}
          cell={cell}
          names={names}
          saved={saved}
          onOpenPolicy={onOpenPolicy}
          onWritten={(diagnostics, created) => {
            setSaved(true)
            onWritten(diagnostics, created)
          }}
        />
      )}
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
  application,
  role,
  target,
  cell,
  names,
  saved,
  onOpenPolicy,
  onWritten,
}: {
  application: string
  role: StudioMatrixRole
  target: StudioMatrixTarget
  cell: StudioMatrixCell
  names: StudioNames | null
  saved: boolean
  onOpenPolicy: (policyId: string) => void
  onWritten: (diagnostics: StudioDiagnostic[], created?: StudioResourceItem) => void
}) {
  const t = useText()
  const readOnlyGrants = role.editable ? cell.grants.filter((grant) => grant.shared) : cell.grants
  return (
    <section data-testid="studio-matrix-grants">
      <Typography.Title level={5}>
        {t('shell.studio.matrix.grants')} {role.name} · {target.name}
      </Typography.Title>
      {!role.editable && cell.grants.length === 0 && (
        <Typography.Text type="secondary">{t('shell.studio.matrix.noGrants')}</Typography.Text>
      )}
      <Flex vertical gap="small">
        {readOnlyGrants.map((grant) => (
          <ReadOnlyGrant key={`${grant.policyId}/${grant.index}`} grant={grant} onOpenPolicy={onOpenPolicy} />
        ))}
        {role.editable && (
          <OwnGrants
            application={application}
            role={role}
            target={target}
            names={names}
            saved={saved}
            onWritten={onWritten}
          />
        )}
      </Flex>
    </section>
  )
}

function ReadOnlyGrant({
  grant,
  onOpenPolicy,
}: {
  grant: StudioMatrixGrant
  onOpenPolicy: (policyId: string) => void
}) {
  const t = useText()
  return (
    <Flex vertical gap={4} data-testid="studio-matrix-grant">
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
  )
}

/** The `<Role>Access` policy as it was read, and the version a save must send. `version` is null for a policy to create. */
interface LoadedPolicy {
  id: string | null
  content: Content
  version: string | null
}

type PolicyState = { loaded: LoadedPolicy; draft: Content } | 'failed' | undefined

function freshPolicy(role: StudioMatrixRole): PolicyState {
  const content = newPolicy(role)
  return { loaded: { id: null, content, version: null }, draft: content }
}

function stringsOf(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : []
}

/**
 * The editable grants of one role on one target. They are the grants of the role's own `<Role>Access` policy that name
 * the target, taken from the policy file itself so that every index fits the file that a save patches. A save sends
 * one patch with the version the policy was loaded at, or creates the policy when the role has none.
 */
function OwnGrants({
  application,
  role,
  target,
  names,
  saved,
  onWritten,
}: {
  application: string
  role: StudioMatrixRole
  target: StudioMatrixTarget
  names: StudioNames | null
  saved: boolean
  onWritten: (diagnostics: StudioDiagnostic[], created?: StudioResourceItem) => void
}) {
  const t = useText()
  const { generation, reconnecting, lost } = useStudioConnection()
  const accessId = role.accessPolicy.id
  const [state, setState] = useState<PolicyState>(() => (accessId === null ? freshPolicy(role) : undefined))
  const [reloads, setReloads] = useState(0)
  const [saving, setSaving] = useState(false)
  const [conflict, setConflict] = useState(false)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    if (accessId === null) {
      return
    }
    const controller = new AbortController()
    fetchStudioResource(application, accessId, controller.signal).then(
      (resource) =>
        setState({
          loaded: { id: resource.id, content: resource.content, version: resource.version },
          draft: resource.content,
        }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the policy '${accessId}' failed`, error)
        setState('failed')
      },
    )
    return () => controller.abort()
  }, [application, accessId, generation, reloads, lost])

  const fields = useMemo(
    () => names?.entities.find((entity) => entity.name.toLowerCase() === target.name.toLowerCase())?.fields ?? [],
    [names, target.name],
  )

  if (state === 'failed') {
    return <Alert type="error" message={t('shell.studio.matrix.loadFailed')} />
  }
  if (state === undefined) {
    return null
  }
  const { loaded, draft } = state
  const grants: unknown[] = Array.isArray(draft.grants) ? draft.grants : []
  const own = ownGrantIndexes(grants, target)
  const patch = diffPatch(loaded.content, draft)
  const changed = patch.length > 0
  const incomplete = own.some((index) => stringsOf((grants[index] as Content).actions).length === 0)

  function edit(pointer: string, value: unknown) {
    setState({ loaded, draft: setAt(draft, pointer, value) as Content })
  }

  function setActions(index: number, ticked: string[]) {
    const kept = stringsOf((grants[index] as Content).actions).filter((action) => !target.actions.includes(action))
    edit(`/grants/${index}/actions`, [...target.actions.filter((action) => ticked.includes(action)), ...kept])
  }

  function reload() {
    setConflict(false)
    setFailed(false)
    if (accessId === null) {
      setState(freshPolicy(role))
    } else {
      setState(undefined)
      setReloads((value) => value + 1)
    }
  }

  async function save() {
    setSaving(true)
    setFailed(false)
    try {
      if (loaded.version === null) {
        const result = await createStudioResource(application, draft)
        if (result.status === 'conflict') {
          setConflict(true)
          return
        }
        if (result.status === 'invalid') {
          setFailed(true)
          return
        }
        setState({ loaded: { id: result.item.id, content: draft, version: result.item.version }, draft })
        onWritten(result.diagnostics, result.item)
        return
      }
      const result = await patchStudioResource(application, loaded.id!, loaded.version, patch)
      if (result.status === 'conflict') {
        setConflict(true)
        return
      }
      setState({ loaded: { id: loaded.id, content: draft, version: result.version }, draft })
      onWritten(result.diagnostics)
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Saving the policy '${role.accessPolicy.name}' failed`, error)
        setFailed(true)
      }
    } finally {
      setSaving(false)
    }
  }

  return (
    <Flex vertical gap="small" data-testid="studio-matrix-own-grants">
      {own.length === 0 && <Typography.Text type="secondary">{t('shell.studio.matrix.noGrants')}</Typography.Text>}
      {own.map((index) => {
        const grant = grants[index] as Content
        const actions = stringsOf(grant.actions)
        const unaccepted = actions.filter((action) => !target.actions.includes(action))
        const filterPath = `/grants/${index}/filter`
        const filter = typeof grant.filter === 'string' ? grant.filter : ''
        return (
          <Flex key={index} vertical gap={4} data-testid="studio-matrix-own-grant">
            <Flex gap="small" align="center" wrap>
              <strong>{role.accessPolicy.name}</strong>
              <Checkbox.Group
                data-testid="studio-matrix-actions"
                options={target.actions}
                value={actions.filter((action) => target.actions.includes(action))}
                onChange={(ticked) => setActions(index, ticked)}
              />
              {unaccepted.map((action) => (
                <Tag key={action} color="warning" style={{ marginInlineEnd: 0 }}>
                  {t('shell.studio.matrix.unacceptedAction')} {action}
                </Tag>
              ))}
              <Button size="small" disabled={grants.length === 1} onClick={() => edit(`/grants/${index}`, undefined)}>
                {t('shell.studio.matrix.removeGrant')}
              </Button>
            </Flex>
            {target.kind === 'entity' && (
              <>
                <div>
                  <Typography.Text type="secondary">{t('shell.studio.matrix.filter')}</Typography.Text>
                  <Suspense fallback={<Input aria-label={t('shell.studio.matrix.filter')} value={filter} readOnly />}>
                    <LazyExpressionEditor
                      application={application}
                      resourceId={String(draft.id)}
                      path={filterPath}
                      name={t('shell.studio.matrix.filter')}
                      value={filter}
                      required={false}
                      options={expressionScope('policy', draft, filterPath, names)}
                      onChange={(path, next) => edit(path, next)}
                    />
                  </Suspense>
                </div>
                <Select
                  mode="multiple"
                  aria-label={t('shell.studio.matrix.fields')}
                  placeholder={t('shell.studio.matrix.allFields')}
                  style={{ minWidth: 240 }}
                  virtual={false}
                  value={stringsOf(grant.fields)}
                  options={fields.map((field) => ({ value: field.name, label: field.name }))}
                  onChange={(next: string[]) => edit(`/grants/${index}/fields`, next.length === 0 ? undefined : next)}
                />
              </>
            )}
          </Flex>
        )
      })}
      <Flex gap="small" wrap>
        <Button onClick={() => edit('/grants', [...grants, newGrant(target)])}>
          {t('shell.studio.matrix.addGrant')}
        </Button>
        <Button type="primary" disabled={!changed || incomplete || saving || reconnecting} onClick={save}>
          {t('shell.studio.matrix.save')}
        </Button>
      </Flex>
      {saved && !changed && (
        <Alert data-testid="studio-matrix-saved" type="success" showIcon message={t('shell.studio.matrix.saved')} />
      )}
      {conflict && (
        <Alert
          data-testid="studio-matrix-conflict"
          type="warning"
          showIcon
          message={t('shell.studio.matrix.conflict')}
          action={<Button onClick={reload}>{t('shell.studio.matrix.reload')}</Button>}
        />
      )}
      {failed && (
        <Alert data-testid="studio-matrix-invalid" type="error" showIcon message={t('shell.studio.matrix.invalid')} />
      )}
    </Flex>
  )
}

/**
 * Loads the application's policy matrix and shows it. It loads again after a save, a text write, a delete and a
 * reconnect, because any change to the folder can change the matrix. It shows nothing until the matrix loads.
 */
export function StudioPolicyMatrix({
  application,
  folderRevision,
  names,
  onOpenPolicy,
  onWritten,
}: {
  application: string
  folderRevision: number
  names: StudioNames | null
  onOpenPolicy: (policyId: string) => void
  onWritten: (diagnostics: StudioDiagnostic[], created?: StudioResourceItem) => void
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
  return (
    <StudioPolicyMatrixTable
      application={application}
      matrix={state.matrix}
      names={names}
      onOpenPolicy={onOpenPolicy}
      onWritten={onWritten}
    />
  )
}
