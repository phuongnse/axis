import { Alert, Button, Flex, Input, Modal, Table, Typography } from 'antd'
import { createContext, lazy, Suspense, useContext, useMemo, useState } from 'react'
import {
  patchStudioResource,
  ServerUnreachableError,
  type StudioDiagnostic,
  type StudioResource,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { type ContractView, contractView } from './contractView'
import {
  moduleIndex,
  relaxedManifest,
  relaxEntries,
  relaxPatch,
  relaxReasonMaxLength,
  relaxReasonValid,
  undoRelaxPatch,
  unrelaxedManifest,
} from './relaxEntries'
import { StudioModuleReadOnly } from './StudioModuleReadOnly'
import { useStudioModuleContent } from './useStudioModuleContent'

const StudioContractLifecycle = lazy(() => import('./StudioContractLifecycle'))

/** What the point cells need to relax a point: the module and its `application.json`, and what to do after a write. */
interface RelaxContext {
  application: string
  module: string
  manifest: StudioResource
  onSaved: (manifest: StudioResource, diagnostics: StudioDiagnostic[]) => void
  onStale: () => void
}

const RelaxContextValue = createContext<RelaxContext | undefined>(undefined)

/** A named point with its Relax button, or its reason and Undo relax button when it is relaxed. */
function Point({ value }: { value: string }) {
  const t = useText()
  const { lost } = useStudioConnection()
  const relax = useContext(RelaxContextValue)
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [conflict, setConflict] = useState(false)
  const code = <Typography.Text code>{value}</Typography.Text>
  if (relax === undefined) {
    return code
  }
  const entry = relaxEntries(relax.manifest.content, relax.module).find((item) => item.point === value)

  async function write(operations: ReturnType<typeof relaxPatch>, content: Record<string, unknown>) {
    if (relax === undefined || operations.length === 0) {
      return
    }
    setBusy(true)
    setConflict(false)
    try {
      const result = await patchStudioResource(relax.application, relax.manifest.id, relax.manifest.version, operations)
      if (result.status === 'saved') {
        setOpen(false)
        relax.onSaved({ ...relax.manifest, version: result.version, content }, result.diagnostics)
      } else {
        setConflict(true)
        relax.onStale()
      }
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Relaxing '${value}' failed`, error)
        setConflict(false)
      }
    } finally {
      setBusy(false)
    }
  }

  function save() {
    if (relax === undefined || !relaxReasonValid(reason)) {
      return
    }
    void write(
      relaxPatch(relax.manifest.content, relax.module, value, reason),
      relaxedManifest(relax.manifest.content, relax.module, value, reason),
    )
  }

  function undo() {
    if (relax === undefined) {
      return
    }
    void write(
      undoRelaxPatch(relax.manifest.content, relax.module, value),
      unrelaxedManifest(relax.manifest.content, relax.module, value),
    )
  }

  return (
    <Flex vertical gap={4} align="flex-start">
      {code}
      {entry === undefined ? (
        <Button
          size="small"
          data-testid={`studio-contract-relax-${value}`}
          disabled={busy}
          onClick={() => {
            setReason('')
            setConflict(false)
            setOpen(true)
          }}
        >
          {t('shell.studio.contract.relax')}
        </Button>
      ) : (
        <>
          <Typography.Text type="secondary" data-testid={`studio-contract-relaxed-${value}`}>
            {t('shell.studio.contract.relaxed')} {entry.reason}
          </Typography.Text>
          <Button size="small" data-testid={`studio-contract-undo-relax-${value}`} disabled={busy} onClick={undo}>
            {t('shell.studio.contract.undoRelax')}
          </Button>
        </>
      )}
      {conflict && (
        <Alert
          data-testid="studio-contract-relax-conflict"
          type="warning"
          showIcon
          title={t('shell.studio.contract.relaxConflict')}
        />
      )}
      <Modal
        open={open}
        title={t('shell.studio.contract.relaxTitle')}
        okText={t('shell.studio.contract.relax')}
        cancelText={t('shell.studio.cancel')}
        okButtonProps={{ disabled: !relaxReasonValid(reason) }}
        confirmLoading={busy}
        onOk={save}
        onCancel={() => setOpen(false)}
      >
        <Flex vertical gap={4}>
          <Typography.Text code>{value}</Typography.Text>
          <Input.TextArea
            aria-label={t('shell.studio.contract.relaxReason')}
            placeholder={t('shell.studio.contract.relaxReason')}
            maxLength={relaxReasonMaxLength}
            showCount
            value={reason}
            onChange={(event) => setReason(event.target.value)}
          />
        </Flex>
      </Modal>
    </Flex>
  )
}

function Points({ values }: { values: string[] }) {
  return (
    <Flex vertical gap={4}>
      {values.map((value) => (
        <Point key={value} value={value} />
      ))}
    </Flex>
  )
}

function ContractTable<T extends object>({
  testId,
  title,
  rows,
  columns,
}: {
  testId: string
  title: string
  rows: T[]
  columns: { key: string; title: string; render: (row: T) => React.ReactNode }[]
}) {
  if (rows.length === 0) {
    return null
  }
  return (
    <Table<{ key: number; row: T }>
      data-testid={testId}
      size="small"
      pagination={false}
      title={() => <Typography.Text strong>{title}</Typography.Text>}
      dataSource={rows.map((row, key) => ({ key, row }))}
      columns={columns.map((column) => ({
        key: column.key,
        title: column.title,
        render: (_: unknown, entry: { row: T }) => column.render(entry.row),
      }))}
    />
  )
}

/**
 * A contract of a module the application uses, read from the module version the application pins and shown as its
 * lifecycle in a state diagram, then tables of the states, the transitions, the core validations, the effects and the
 * ceiling entries. Each row shows its named point, the `<entity>/<part>/<name>` handle that a relax entry or a contract
 * scenario names. The module owns the file, so the view does not edit it. When `manifest`, the application's
 * `application.json`, declares the module directly, each point has a Relax button, which asks for a reason of 1 to 500
 * characters and adds the relax entry to the module's entry in `application.json` with an `If-Match` patch. A relaxed
 * point shows its reason and an Undo relax button, which removes the entry, and the whole `relax` list when it was the
 * last. `onManifestSaved` gets the file as written. A 409 shows that the file changed, and `onManifestStale` asks for it
 * to be loaded again.
 */
export function StudioModuleContract({
  application,
  module,
  id,
  manifest,
  onManifestSaved,
  onManifestStale,
}: {
  application: string
  module: string
  id: string
  manifest?: StudioResource
  onManifestSaved?: (manifest: StudioResource, diagnostics: StudioDiagnostic[]) => void
  onManifestStale?: () => void
}) {
  const t = useText()
  const { content, loadFailed } = useStudioModuleContent(application, module, id)
  const view: ContractView | undefined = useMemo(() => (content ? contractView(content) : undefined), [content])

  if (loadFailed) {
    return <Alert data-testid="studio-resource-load-error" type="error" title={t('shell.studio.loadResourceFailed')} />
  }
  if (!view) {
    return null
  }

  const noGuard = t('shell.studio.contract.noGuard')
  const relax: RelaxContext | undefined =
    manifest && moduleIndex(manifest.content, module) >= 0
      ? {
          application,
          module,
          manifest,
          onSaved: (saved, diagnostics) => onManifestSaved?.(saved, diagnostics),
          onStale: () => onManifestStale?.(),
        }
      : undefined
  return (
    <RelaxContextValue.Provider value={relax}>
      <Flex vertical gap="middle">
        <StudioModuleReadOnly />
        {view.states.length > 0 && (
          <Suspense fallback={null}>
            <StudioContractLifecycle states={view.states} edges={view.edges} />
          </Suspense>
        )}
        <ContractTable
          testId="studio-contract-states"
          title={t('shell.studio.contract.states')}
          rows={view.states}
          columns={[
            { key: 'name', title: t('shell.studio.contract.state'), render: (row) => row.name },
            {
              key: 'marks',
              title: t('shell.studio.contract.lifecycle'),
              render: (row) =>
                [
                  row.initial ? t('shell.studio.contract.initial') : '',
                  row.deletable ? t('shell.studio.contract.deletable') : '',
                ]
                  .filter((mark) => mark !== '')
                  .join(', '),
            },
            { key: 'point', title: t('shell.studio.contract.point'), render: (row) => <Points values={row.points} /> },
          ]}
        />
        <ContractTable
          testId="studio-contract-transitions"
          title={t('shell.studio.contract.transitions')}
          rows={view.transitions}
          columns={[
            { key: 'name', title: t('shell.studio.contract.name'), render: (row) => row.name },
            { key: 'from', title: t('shell.studio.contract.from'), render: (row) => row.from.join(', ') },
            { key: 'to', title: t('shell.studio.contract.to'), render: (row) => row.to },
            { key: 'guard', title: t('shell.studio.contract.guard'), render: (row) => row.guard ?? noGuard },
            { key: 'point', title: t('shell.studio.contract.point'), render: (row) => <Point value={row.point} /> },
          ]}
        />
        <ContractTable
          testId="studio-contract-validations"
          title={t('shell.studio.contract.validations')}
          rows={view.validations}
          columns={[
            { key: 'name', title: t('shell.studio.contract.name'), render: (row) => row.name },
            { key: 'field', title: t('shell.studio.contract.field'), render: (row) => row.field },
            { key: 'expression', title: t('shell.studio.contract.expression'), render: (row) => row.expression },
            { key: 'point', title: t('shell.studio.contract.point'), render: (row) => <Point value={row.point} /> },
          ]}
        />
        <ContractTable
          testId="studio-contract-effects"
          title={t('shell.studio.contract.effects')}
          rows={view.effects}
          columns={[
            { key: 'name', title: t('shell.studio.contract.name'), render: (row) => row.name },
            { key: 'on', title: t('shell.studio.contract.on'), render: (row) => row.on },
            { key: 'creates', title: t('shell.studio.contract.creates'), render: (row) => row.entity },
            { key: 'point', title: t('shell.studio.contract.point'), render: (row) => <Point value={row.point} /> },
          ]}
        />
        <ContractTable
          testId="studio-contract-ceiling"
          title={t('shell.studio.contract.ceiling')}
          rows={view.ceiling}
          columns={[
            {
              key: 'slot',
              title: t('shell.studio.contract.slot'),
              render: (row) => (row.slot === 'others' ? t('shell.studio.contract.others') : row.slot),
            },
            { key: 'actions', title: t('shell.studio.moduleGrants.actions'), render: (row) => row.actions.join(', ') },
            {
              key: 'fields',
              title: t('shell.studio.moduleGrants.fields'),
              render: (row) =>
                row.fields.length === 0 ? t('shell.studio.moduleGrants.allFields') : row.fields.join(', '),
            },
            { key: 'filter', title: t('shell.studio.moduleGrants.filter'), render: (row) => row.filter },
            { key: 'point', title: t('shell.studio.contract.point'), render: (row) => <Point value={row.point} /> },
          ]}
        />
      </Flex>
    </RelaxContextValue.Provider>
  )
}
