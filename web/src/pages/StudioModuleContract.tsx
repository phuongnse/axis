import { Alert, Button, Checkbox, Flex, Input, Modal, Select, Tag, Typography } from 'antd'
import { createContext, lazy, Suspense, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { AxisActionRow, AxisButton } from '../platform/AxisButton'
import { AxisTable } from '../platform/AxisTable'
import { diffPatch, getAt, setAt } from '../platform/jsonPatch'
import { layerMarks, markOf } from '../platform/layerMarks'
import { QuotaRefusedError } from '../platform/quota'
import {
  fetchStudioModuleResource,
  patchStudioResource,
  saveStudioModuleResource,
  ServerUnreachableError,
  type StudioDiagnostic,
  type StudioModuleLayer,
  type StudioModuleResource,
  type StudioResource,
  type StudioResourceItem,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useUnsavedChanges } from '../platform/unsavedChanges'
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

const StudioContractLifecycle = lazy(() => import('./StudioContractLifecycle'))

type Content = Record<string, unknown>

const statesList = '/lifecycle/states'
const transitionsList = '/lifecycle/transitions'
const lifecycleLists: Record<string, string> = { [statesList]: 'name', [transitionsList]: 'name' }

/** What the contract loaded: the module file with the layer applied, and the layer when there is one. */
interface Loaded {
  content: Content
  layer: StudioModuleLayer | null
  marks: StudioModuleResource['marks']
  removed: StudioModuleResource['removed']
}

/** A state or a transition that the dialogs build, as it goes into the lifecycle. */
type Added = Record<string, unknown>

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
  const { size } = useDesignTokens()
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
        // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
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
          {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
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
        width={size.dialog.small}
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
  columns: { key: string; title: string; render: (row: T, index: number) => React.ReactNode }[]
}) {
  const t = useText()
  if (rows.length === 0) {
    return null
  }
  return (
    <Flex vertical gap="small" data-testid={testId}>
      <Typography.Text strong>{title}</Typography.Text>
      <AxisTable<{ key: number; row: T }>
        rowKey={(entry) => String(entry.key)}
        emptyText={t('shell.table.empty')}
        dataSource={rows.map((row, key) => ({ key, row }))}
        columns={columns.map((column) => ({
          key: column.key,
          kind: 'freeText' as const,
          title: column.title,
          render: (_: unknown, entry: { key: number; row: T }) => column.render(entry.row, entry.key),
        }))}
      />
    </Flex>
  )
}

/** True when `name` is not empty and no name in `names` equals it, ignoring letter case. */
function nameFree(name: string, names: string[]) {
  const trimmed = name.trim()
  return trimmed !== '' && !names.some((other) => other.toLowerCase() === trimmed.toLowerCase())
}

/** The dialog that adds a state: its name, and whether it is initial and deletable. */
function AddStateDialog({
  open,
  names,
  onAdd,
  onClose,
}: {
  open: boolean
  names: string[]
  onAdd: (state: Added) => void
  onClose: () => void
}) {
  const t = useText()
  const { size } = useDesignTokens()
  const [name, setName] = useState('')
  const [initial, setInitial] = useState(false)
  const [deletable, setDeletable] = useState(false)

  function close() {
    setName('')
    setInitial(false)
    setDeletable(false)
    onClose()
  }

  function add() {
    onAdd({
      name: name.trim(),
      ...(initial ? { initial: true } : {}),
      ...(deletable ? { deletable: true } : {}),
    })
    close()
  }

  return (
    <Modal
      open={open}
      width={size.dialog.medium}
      title={t('shell.studio.contract.addState')}
      okText={t('shell.studio.addItem')}
      cancelText={t('shell.studio.cancel')}
      okButtonProps={{ disabled: !nameFree(name, names) }}
      onOk={add}
      onCancel={close}
    >
      <Flex vertical gap="small">
        <Input
          aria-label={t('shell.studio.contract.name')}
          placeholder={t('shell.studio.contract.name')}
          value={name}
          onChange={(event) => setName(event.target.value)}
        />
        <Checkbox checked={initial} onChange={(event) => setInitial(event.target.checked)}>
          {t('shell.studio.contract.initial')}
        </Checkbox>
        <Checkbox checked={deletable} onChange={(event) => setDeletable(event.target.checked)}>
          {t('shell.studio.contract.deletable')}
        </Checkbox>
      </Flex>
    </Modal>
  )
}

/**
 * The dialog that adds a transition: its name, the states it leaves, the state it enters and an optional guard. Every
 * state of the lifecycle can be picked, a protected one included, since the server reports a transition into it.
 */
function AddTransitionDialog({
  open,
  names,
  states,
  onAdd,
  onClose,
}: {
  open: boolean
  names: string[]
  states: string[]
  onAdd: (transition: Added) => void
  onClose: () => void
}) {
  const t = useText()
  const { size } = useDesignTokens()
  const [name, setName] = useState('')
  const [from, setFrom] = useState<string[]>([])
  const [to, setTo] = useState<string>()
  const [guard, setGuard] = useState('')
  const options = states.map((state) => ({ value: state, label: state }))

  function close() {
    setName('')
    setFrom([])
    setTo(undefined)
    setGuard('')
    onClose()
  }

  function add() {
    if (to === undefined) {
      return
    }
    onAdd({ name: name.trim(), from, to, ...(guard.trim() === '' ? {} : { guard: guard.trim() }) })
    close()
  }

  return (
    <Modal
      open={open}
      width={size.dialog.medium}
      title={t('shell.studio.contract.addTransition')}
      okText={t('shell.studio.addItem')}
      cancelText={t('shell.studio.cancel')}
      okButtonProps={{ disabled: !nameFree(name, names) || from.length === 0 || to === undefined || from.includes(to) }}
      onOk={add}
      onCancel={close}
    >
      <Flex vertical gap="small">
        <Input
          aria-label={t('shell.studio.contract.name')}
          placeholder={t('shell.studio.contract.name')}
          value={name}
          onChange={(event) => setName(event.target.value)}
        />
        <Select
          virtual={false}
          mode="multiple"
          aria-label={t('shell.studio.contract.from')}
          placeholder={t('shell.studio.contract.from')}
          options={options}
          value={from}
          onChange={setFrom}
        />
        <Select
          virtual={false}
          aria-label={t('shell.studio.contract.to')}
          placeholder={t('shell.studio.contract.to')}
          options={options}
          value={to}
          onChange={setTo}
        />
        <Input
          aria-label={t('shell.studio.contract.guard')}
          placeholder={t('shell.studio.contract.guard')}
          value={guard}
          onChange={(event) => setGuard(event.target.value)}
        />
      </Flex>
    </Modal>
  )
}

/** A row's name, and when the application added the row, its Added tag and a Remove button named after it. */
function RowName({ name, added, onRemove }: { name: string; added: boolean; onRemove: () => void }) {
  const t = useText()
  if (!added) {
    return name
  }
  return (
    <Flex gap="small" align="center" wrap>
      {name}
      <Tag role="note" aria-label={`${t('shell.studio.module.added')} ${name}`}>
        {t('shell.studio.module.added')}
      </Tag>
      {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
      <Button size="small" aria-label={`${t('shell.studio.contract.remove')} ${name}`} onClick={onRemove}>
        {t('shell.studio.contract.remove')}
      </Button>
    </Flex>
  )
}

/**
 * A contract of a module the application uses, read from the module version the application pins with the
 * application's layer applied. It shows its lifecycle in a state diagram, then tables of the states, the transitions,
 * the core validations, the effects and the ceiling entries. Each row shows its named point, the
 * `<entity>/<part>/<name>` handle that a relax entry or a contract scenario names. The module owns the file, so only
 * the application's own states and transitions can be edited: Add state and Add transition open a dialog, a row that the
 * application added shows an Added tag and a Remove button, and the module's own rows show neither. Save sends the whole
 * contract to the module save route, which writes the difference as the contract's layer. Removing every added row and
 * saving deletes the layer. An added row has no named point, because a relax entry cannot name it. `diagnostics`, the
 * folder's, are shown when they name the contract's layer, such as `AXC0370` for a transition into a protected state.
 * `onWritten` reports the folder's diagnostics after a save, with the layer the save added to the folder's list or the
 * id of the layer it removed. When `manifest`, the application's `application.json`, declares the module directly, each
 * point has a Relax button, which asks for a reason of 1 to 500 characters and adds the relax entry to the module's
 * entry in `application.json` with an `If-Match` patch. A relaxed point shows its reason and an Undo relax button, which
 * removes the entry, and the whole `relax` list when it was the last. `onManifestSaved` gets the file as written. A 409
 * shows that the file changed, and `onManifestStale` asks for it to be loaded again.
 */
export function StudioModuleContract({
  application,
  module,
  id,
  manifest,
  diagnostics = [],
  onWritten,
  onManifestSaved,
  onManifestStale,
}: {
  application: string
  module: string
  id: string
  manifest?: StudioResource
  diagnostics?: StudioDiagnostic[]
  onWritten?: (diagnostics: StudioDiagnostic[], layer: { added?: StudioResourceItem; removedId?: string }) => void
  onManifestSaved?: (manifest: StudioResource, diagnostics: StudioDiagnostic[]) => void
  onManifestStale?: () => void
}) {
  const t = useText()
  const { generation, reconnecting, lost } = useStudioConnection()
  const [loaded, setLoaded] = useState<Loaded>()
  const loadedRef = useRef<Loaded>(undefined)
  const [draft, setDraft] = useState<Content>({})
  const [loadFailed, setLoadFailed] = useState(false)
  const [conflict, setConflict] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [stateOpen, setStateOpen] = useState(false)
  const [transitionOpen, setTransitionOpen] = useState(false)
  const view: ContractView = useMemo(() => contractView(draft), [draft])

  /** Takes a read as what was loaded. Unless `force`, a draft with unsaved edits stays. */
  function adopt(resource: StudioModuleResource, force: boolean) {
    const previous = loadedRef.current
    const next: Loaded = {
      content: resource.content,
      layer: resource.layer ?? null,
      marks: resource.marks ?? [],
      removed: resource.removed ?? [],
    }
    loadedRef.current = next
    setLoaded(next)
    setLoadFailed(false)
    setDraft((current) =>
      !force && previous !== undefined && diffPatch(previous.content, current).length > 0 ? current : next.content,
    )
  }

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioModuleResource(application, module, id, controller.signal).then(
      (resource) => adopt(resource, false),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the module resource '${id}' of '${module}' failed`, error)
        setLoadFailed(true)
      },
    )
    return () => controller.abort()
  }, [application, module, id, generation, lost])

  const patch = useMemo(() => (loaded ? diffPatch(loaded.content, draft) : []), [loaded, draft])
  useUnsavedChanges(patch.length > 0)

  if (loadFailed) {
    return <Alert data-testid="studio-resource-load-error" type="error" title={t('shell.studio.loadResourceFailed')} />
  }
  if (!loaded) {
    return null
  }

  const hasLifecycle = typeof draft.lifecycle === 'object' && draft.lifecycle !== null
  const elements = layerMarks(loaded.content, loaded.marks, loaded.removed, lifecycleLists)

  function savedNames(list: string) {
    const items = getAt(loaded!.content, list)
    return new Set(
      Array.isArray(items) ? items.map((item) => String((item as Record<string, unknown> | null)?.name ?? '')) : [],
    )
  }
  const savedStates = savedNames(statesList)
  const savedTransitions = savedNames(transitionsList)
  /** A row is the application's when the layer added it, or when it is in the draft and not yet saved. */
  const isAdded = (list: string, name: string, saved: Set<string>) =>
    markOf(elements, list, name) === 'added' || !saved.has(name)

  function listOf(list: string): unknown[] {
    const items = getAt(draft, list)
    return Array.isArray(items) ? items : []
  }

  function edit(list: string, items: unknown[]) {
    setDraft((current) => setAt(current, list, items) as Content)
    setSaved(false)
    setSaveError(null)
  }

  function add(list: string, item: Added) {
    edit(list, [...listOf(list), item])
  }

  function remove(list: string, index: number) {
    edit(
      list,
      listOf(list).filter((_, position) => position !== index),
    )
  }

  /** Reads the contract again and takes it, so the draft is what the server holds. */
  async function reload() {
    try {
      const resource = await fetchStudioModuleResource(application, module, id)
      adopt(resource, true)
      setConflict(false)
      setSaveError(null)
      return resource
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
        return
      }
      console.warn(`Loading the module resource '${id}' of '${module}' failed`, error)
      setLoadFailed(true)
    }
  }

  async function save() {
    setSaving(true)
    setSaveError(null)
    try {
      const before = loaded!.layer
      const result = await saveStudioModuleResource(application, module, id, before?.version ?? null, draft)
      if (result.status === 'conflict') {
        setConflict(true)
        return
      }
      if (result.status === 'refused') {
        setSaveError(result.title !== '' ? result.title : t('shell.studio.saveFailed'))
        return
      }
      // The save answer has no layer id, which the diagnostics are found by, so the contract is read again.
      const resource = await reload()
      if (!resource) {
        return
      }
      setSaved(true)
      if (result.file === null) {
        onWritten?.(result.diagnostics, before ? { removedId: before.id } : {})
      } else if (before === null && resource.layer !== null) {
        onWritten?.(result.diagnostics, {
          added: {
            id: resource.layer.id,
            kind: 'layer',
            name: resource.name === null ? null : `${module}/${resource.name}`,
            file: result.file,
            version: result.version ?? resource.layer.version,
            usedBy: 0,
          },
        })
      } else {
        onWritten?.(result.diagnostics, {})
      }
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Saving the module resource '${id}' failed`, error)
        setSaveError(error instanceof QuotaRefusedError ? error.message : t('shell.studio.saveFailed'))
      }
    } finally {
      setSaving(false)
    }
  }

  const noGuard = t('shell.studio.contract.noGuard')
  const relax: RelaxContext | undefined =
    manifest && moduleIndex(manifest.content, module) >= 0
      ? {
          application,
          module,
          manifest,
          onSaved: (savedManifest, savedDiagnostics) => onManifestSaved?.(savedManifest, savedDiagnostics),
          onStale: () => onManifestStale?.(),
        }
      : undefined
  const layerDiagnostics =
    loaded.layer === null ? [] : diagnostics.filter((diagnostic) => diagnostic.resourceId === loaded.layer!.id)
  return (
    <RelaxContextValue.Provider value={relax}>
      <Flex vertical gap="middle">
        <Alert data-testid="studio-contract-add-only" type="info" title={t('shell.studio.contract.addOnly')} />
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
            {
              key: 'name',
              title: t('shell.studio.contract.state'),
              render: (row, index) => (
                <RowName
                  name={row.name}
                  added={isAdded(statesList, row.name, savedStates)}
                  onRemove={() => remove(statesList, index)}
                />
              ),
            },
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
            {
              key: 'point',
              title: t('shell.studio.contract.point'),
              render: (row) => (isAdded(statesList, row.name, savedStates) ? null : <Points values={row.points} />),
            },
          ]}
        />
        {hasLifecycle && (
          <Flex>
            <AxisButton
              role="secondary"
              data-testid="studio-contract-add-state"
              disabled={saving || reconnecting}
              onClick={() => setStateOpen(true)}
            >
              {t('shell.studio.contract.addState')}
            </AxisButton>
          </Flex>
        )}
        <ContractTable
          testId="studio-contract-transitions"
          title={t('shell.studio.contract.transitions')}
          rows={view.transitions}
          columns={[
            {
              key: 'name',
              title: t('shell.studio.contract.name'),
              render: (row, index) => (
                <RowName
                  name={row.name}
                  added={isAdded(transitionsList, row.name, savedTransitions)}
                  onRemove={() => remove(transitionsList, index)}
                />
              ),
            },
            { key: 'from', title: t('shell.studio.contract.from'), render: (row) => row.from.join(', ') },
            { key: 'to', title: t('shell.studio.contract.to'), render: (row) => row.to },
            { key: 'guard', title: t('shell.studio.contract.guard'), render: (row) => row.guard ?? noGuard },
            {
              key: 'point',
              title: t('shell.studio.contract.point'),
              render: (row) =>
                isAdded(transitionsList, row.name, savedTransitions) ? null : <Point value={row.point} />,
            },
          ]}
        />
        {hasLifecycle && (
          <Flex>
            <AxisButton
              role="secondary"
              data-testid="studio-contract-add-transition"
              disabled={saving || reconnecting}
              onClick={() => setTransitionOpen(true)}
            >
              {t('shell.studio.contract.addTransition')}
            </AxisButton>
          </Flex>
        )}
        {layerDiagnostics.length > 0 && (
          <Flex vertical gap="small">
            <Typography.Text strong>{t('shell.studio.contract.diagnostics')}</Typography.Text>
            <ul data-testid="studio-contract-diagnostics" style={{ margin: 0 }}>
              {layerDiagnostics.map((diagnostic, index) => (
                <li key={`${diagnostic.code}-${diagnostic.path}-${index}`}>
                  <Typography.Text strong type={diagnostic.severity === 'error' ? 'danger' : 'warning'}>
                    {diagnostic.code}
                  </Typography.Text>{' '}
                  {diagnostic.message}
                </li>
              ))}
            </ul>
          </Flex>
        )}
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
        {conflict && (
          <Alert
            data-testid="studio-conflict"
            type="warning"
            title={t('shell.studio.conflict')}
            // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
            action={<Button onClick={() => void reload()}>{t('shell.studio.reload')}</Button>}
          />
        )}
        {saveError !== null && <Alert data-testid="studio-save-error" type="error" title={saveError} />}
        <AxisActionRow align="center">
          <AxisButton
            role="finish"
            data-testid="studio-save"
            disabled={patch.length === 0 || conflict || saving || reconnecting}
            onClick={() => void save()}
          >
            {t('shell.studio.save')}
          </AxisButton>
          {saved && <span data-testid="studio-saved">{t('shell.studio.saved')}</span>}
        </AxisActionRow>
        <AddStateDialog
          open={stateOpen}
          names={view.states.map((state) => state.name)}
          onAdd={(state) => add(statesList, state)}
          onClose={() => setStateOpen(false)}
        />
        <AddTransitionDialog
          open={transitionOpen}
          names={view.transitions.map((transition) => transition.name)}
          states={view.states.map((state) => state.name)}
          onAdd={(transition) => add(transitionsList, transition)}
          onClose={() => setTransitionOpen(false)}
        />
      </Flex>
    </RelaxContextValue.Provider>
  )
}
