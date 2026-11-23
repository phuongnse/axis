import { Flex, Form, Input, Modal, Select, Typography } from 'antd'
import { AxisForm } from '../platform/AxisForm'
import { AxisAlert } from '../platform/AxisAlert'
import { createContext, lazy, Suspense, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { AxisActionRow, AxisButton } from '../platform/AxisButton'
import { AxisCheckboxField } from '../platform/AxisCheckboxField'
import { AxisField } from '../platform/AxisField'
import { AxisTable } from '../platform/AxisTable'
import { fillText } from '../platform/fillText'
import { SectionPart } from '../platform/SectionParts'
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
import type { FilterKind } from '../platform/tableFilter'
import { recordPageSize } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useUnsavedChanges } from '../platform/unsavedChanges'
import { useTableView } from '../platform/useTableView'
import { type ContractView, contractView } from './contractView'
import { LayerMarkLabel } from './LayerMarkViews'
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
import { DialogFooter } from '../platform/DialogFooter'
import { StudioProblemList } from './StudioProblems'

const StudioContractLifecycle = lazy(() => import('./StudioContractLifecycle'))

type Content = Record<string, unknown>

/** The contract's tables keep no data source parameter in the address, only their views. */
const noParameters: string[] = []

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
  const [form] = Form.useForm<{ reason: string }>()
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

  function save({ reason }: { reason: string }) {
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
        <AxisButton
          role="secondary"
          size="small"
          data-testid={`studio-contract-relax-${value}`}
          disabled={busy}
          onClick={() => {
            setConflict(false)
            setOpen(true)
          }}
        >
          {t('shell.studio.contract.relax')}
        </AxisButton>
      ) : (
        <>
          <Typography.Text type="secondary" data-testid={`studio-contract-relaxed-${value}`}>
            {fillText(t('shell.studio.contract.relaxed'), { reason: entry.reason })}
          </Typography.Text>
          <AxisButton
            role="secondary"
            size="small"
            data-testid={`studio-contract-undo-relax-${value}`}
            disabled={busy}
            onClick={undo}
          >
            {t('shell.studio.contract.undoRelax')}
          </AxisButton>
        </>
      )}
      {conflict && (
        <AxisAlert
          data-testid="studio-contract-relax-conflict"
          kind="warning"
          message={t('shell.studio.contract.relaxConflict')}
        />
      )}
      <Modal
        open={open}
        width={size.dialog.small}
        title={t('shell.studio.contract.relaxTitle')}
        footer={
          <DialogFooter
            finishText={t('shell.studio.contract.relax')}
            cancelText={t('shell.studio.cancel')}
            finishing={busy}
            onFinish={() => form.submit()}
            onCancel={() => setOpen(false)}
          />
        }
        onCancel={() => setOpen(false)}
        // Each opening starts with an empty reason.
        afterClose={() => form.resetFields()}
      >
        <Flex vertical gap={4}>
          <Typography.Text code>{value}</Typography.Text>
          <AxisForm form={form} initialValues={{ reason: '' }} onFinish={save}>
            <AxisField
              name="reason"
              label={t('shell.studio.contract.relaxReason')}
              help={t('shell.studio.contract.relaxReason.help')}
              requiredMessage={t('shell.studio.contract.relaxReasonRequired')}
            >
              <Input.TextArea
                aria-label={t('shell.studio.contract.relaxReason')}
                maxLength={relaxReasonMaxLength}
                showCount
              />
            </AxisField>
          </AxisForm>
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

/** What a contract column filters on: its kind and how a row gives the value, with null for a cell that shows none. */
interface ContractFilter<T> {
  kind: FilterKind
  value: (row: T) => string | number | null
  options?: { value: string; label: string }[]
}

/**
 * A table of the contract with a Filter on each column that gives `filter`, kept in the address under `prefix`. It
 * shows every row on one page. A row that `keep` names stays shown under any filter. Its `action`, such as Add state,
 * is a secondary button on the table's title row, see docs/reference/shell.md#layout. The table shows with its empty message
 * while it has an `action` and no rows, so the action stays reachable.
 */
function ContractTable<T extends object>({
  testId,
  title,
  prefix,
  rows,
  columns,
  keep,
  action,
}: {
  testId: string
  title: string
  prefix: string
  rows: T[]
  columns: {
    key: string
    title: string
    render: (row: T, index: number) => React.ReactNode
    filter?: ContractFilter<T>
  }[]
  keep?: (row: T) => boolean
  action?: React.ReactNode
}) {
  const t = useText()
  // The signature keeps the fields the same between draws, as `useTableView` needs.
  const signature = columns
    .filter((column) => column.filter)
    .map((column) => column.key)
    .join(',')
  const fields = useMemo(
    () =>
      signature
        .split(',')
        .filter(Boolean)
        .map((name) => ({ name, type: 'text' as const })),
    [signature],
  )
  const { view, setView } = useTableView(fields, recordPageSize, noParameters, false, true, false, prefix)
  if (rows.length === 0 && action === undefined) {
    return null
  }
  return (
    <SectionPart testId={testId} heading={{ title }} action={action}>
      <AxisTable<{ key: number; row: T }>
        rowKey={(entry) => String(entry.key)}
        emptyText={t('shell.table.empty')}
        dataSource={rows.map((row, key) => ({ key, row }))}
        view={{ query: view, onChange: setView, paging: false }}
        keep={keep && ((entry) => keep(entry.row))}
        columns={columns.map((column) => {
          const { filter } = column
          return {
            key: column.key,
            kind: 'freeText' as const,
            title: column.title,
            render: (_: unknown, entry: { key: number; row: T }) => column.render(entry.row, entry.key),
            filter: filter && {
              kind: filter.kind,
              options: filter.options,
              value: (entry: { key: number; row: T }) => filter.value(entry.row),
            },
          }
        })}
      />
    </SectionPart>
  )
}

/** True when a name in `names` equals `name`, ignoring letter case and the spaces around it. */
function nameTaken(name: string, names: string[]) {
  const trimmed = name.trim().toLowerCase()
  return names.some((other) => other.toLowerCase() === trimmed)
}

/** The rule that rejects a name that `names` already holds, with the message `taken`. An empty name is the required rule's. */
function freeNameRule(names: string[], taken: string) {
  return {
    validator: (_: unknown, value: string) =>
      nameTaken(value, names) ? Promise.reject(new Error(taken)) : Promise.resolve(),
  }
}

/**
 * The dialog that adds a state: its name, and whether it is initial and deletable. Add is on until a state is added. A
 * click with the name empty or taken shows what is wrong under it and focuses it, see
 * [Added states](docs/reference/module-contracts.md#in-studio).
 */
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
  const [form] = Form.useForm<{ name: string; initial: boolean; deletable: boolean }>()

  function close() {
    form.resetFields()
    onClose()
  }

  function add({ name, initial, deletable }: { name: string; initial: boolean; deletable: boolean }) {
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
      footer={
        <DialogFooter
          finishText={t('shell.studio.addItem')}
          cancelText={t('shell.studio.cancel')}
          onFinish={() => form.submit()}
          onCancel={close}
        />
      }
      onCancel={close}
    >
      <AxisForm form={form} initialValues={{ name: '', initial: false, deletable: false }} onFinish={add}>
        <AxisField
          name="name"
          label={t('shell.studio.contract.name')}
          help={t('shell.studio.contract.stateName.help')}
          requiredMessage={t('shell.studio.contract.nameRequired')}
          rules={[freeNameRule(names, t('shell.studio.contract.stateNameTaken'))]}
        >
          <Input aria-label={t('shell.studio.contract.name')} />
        </AxisField>
        <AxisCheckboxField
          name="initial"
          label={t('shell.studio.contract.initial')}
          help={t('shell.studio.contract.initial.help')}
        />
        <AxisCheckboxField
          name="deletable"
          label={t('shell.studio.contract.deletable')}
          help={t('shell.studio.contract.deletable.help')}
        />
      </AxisForm>
    </Modal>
  )
}

type TransitionValues = { name: string; from: string[]; to?: string; guard: string }

/**
 * The dialog that adds a transition: its name, the states it leaves, the state it enters and an optional guard. Every
 * state of the lifecycle can be picked, a protected one included, since the server reports a transition into it. Add is
 * on until a transition is added. A click with the name empty or taken, no From state, or no To state or one that is
 * among the From states shows what is wrong under the first such field and focuses it. A change of From clears the
 * message under To, since the check of To depends on From.
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
  const [form] = Form.useForm<TransitionValues>()
  const options = states.map((state) => ({ value: state, label: state }))

  function close() {
    form.resetFields()
    onClose()
  }

  function add({ name, from, to, guard }: TransitionValues) {
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
      footer={
        <DialogFooter
          finishText={t('shell.studio.addItem')}
          cancelText={t('shell.studio.cancel')}
          onFinish={() => form.submit()}
          onCancel={close}
        />
      }
      onCancel={close}
    >
      <AxisForm
        form={form}
        initialValues={{ name: '', from: [], to: undefined, guard: '' }}
        onValuesChange={(changed) => 'from' in changed && form.setFields([{ name: 'to', errors: [] }])}
        onFinish={add}
      >
        <AxisField
          name="name"
          label={t('shell.studio.contract.name')}
          help={t('shell.studio.contract.transitionName.help')}
          requiredMessage={t('shell.studio.contract.nameRequired')}
          rules={[freeNameRule(names, t('shell.studio.contract.transitionNameTaken'))]}
        >
          <Input aria-label={t('shell.studio.contract.name')} />
        </AxisField>
        <AxisField
          name="from"
          label={t('shell.studio.contract.from')}
          help={t('shell.studio.contract.from.help')}
          requiredMessage={t('shell.studio.contract.fromRequired')}
        >
          <Select virtual={false} mode="multiple" aria-label={t('shell.studio.contract.from')} options={options} />
        </AxisField>
        <AxisField
          name="to"
          label={t('shell.studio.contract.to')}
          help={t('shell.studio.contract.to.help')}
          requiredMessage={t('shell.studio.contract.toRequired')}
          rules={[
            ({ getFieldValue }) => ({
              validator: (_, value: string | undefined) =>
                value !== undefined && (getFieldValue('from') as string[]).includes(value)
                  ? Promise.reject(new Error(t('shell.studio.contract.toInFrom')))
                  : Promise.resolve(),
            }),
          ]}
        >
          <Select virtual={false} aria-label={t('shell.studio.contract.to')} options={options} />
        </AxisField>
        <AxisField name="guard" label={t('shell.studio.contract.guard')} help={t('shell.studio.contract.guard.help')}>
          <Input aria-label={t('shell.studio.contract.guard')} />
        </AxisField>
      </AxisForm>
    </Modal>
  )
}

/** A row's name, and when the application added the row, its Added tag and a Delete button named after it. */
function RowName({ name, added, onRemove }: { name: string; added: boolean; onRemove: () => void }) {
  const t = useText()
  if (!added) {
    return name
  }
  return (
    <Flex gap="small" align="center" wrap>
      {name}
      <LayerMarkLabel mark="added" name={name} />
      <AxisButton
        role="secondary"
        destructive
        size="small"
        aria-label={fillText(t('shell.studio.contract.removeName'), { name })}
        onClick={onRemove}
      >
        {t('shell.studio.contract.remove')}
      </AxisButton>
    </Flex>
  )
}

/**
 * A contract of a module the application uses, read from the module version the application pins with the
 * application's layer applied. It shows its lifecycle in a state diagram, then tables of the states, the transitions,
 * the core validations, the effects and the ceiling entries. Each row shows its named point, the
 * `<entity>/<part>/<name>` handle that a relax entry or a contract scenario names. The module owns the file, so only
 * the application's own states and transitions can be edited: Add state and Add transition open a dialog from the title
 * row of their table, and a row that the application added shows an Added tag and a Remove button, and the module's own
 * rows show neither. Save sends the whole
 * contract to the module save route, which writes the difference as the contract's layer. Removing every added row and
 * saving deletes the layer. An added row has no named point, because a relax entry cannot name it. `diagnostics`, the
 * folder's, are shown when they name the contract's layer, such as `AXC0370` for a transition into a protected state.
 * `onWritten` reports the folder's diagnostics after a save, with the layer the save added to the folder's list or the
 * id of the layer it removed. When `manifest`, the application's `application.json`, declares the module directly, each
 * point has a Relax button, which asks for a reason of 1 to 500 characters and adds the relax entry to the module's
 * entry in `application.json` with an `If-Match` patch. A relaxed point shows its reason and an Undo relax button, which
 * removes the entry, and the whole `relax` list when it was the last. `onManifestSaved` gets the file as written. A 409
 * shows that the file changed, and `onManifestStale` asks for it to be loaded again. Each table has a Filter on every
 * column, kept in the address under the table's own prefix, and shows every row on one page. A state or transition that
 * the saved contract does not have stays shown under any filter, see
 * [module contracts in Studio](docs/reference/module-contracts.md#in-studio).
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
    return (
      <AxisAlert data-testid="studio-resource-load-error" kind="error" message={t('shell.studio.loadResourceFailed')} />
    )
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
  const marksText = (row: ContractView['states'][number]) =>
    [row.initial ? t('shell.studio.contract.initial') : '', row.deletable ? t('shell.studio.contract.deletable') : '']
      .filter((mark) => mark !== '')
      .join(', ')
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
        <AxisAlert data-testid="studio-contract-add-only" kind="info" message={t('shell.studio.contract.addOnly')} />
        {view.states.length > 0 && (
          <Suspense fallback={null}>
            <StudioContractLifecycle states={view.states} edges={view.edges} />
          </Suspense>
        )}
        <ContractTable
          testId="studio-contract-states"
          title={t('shell.studio.contract.states')}
          prefix="states."
          rows={view.states}
          keep={(row) => !savedStates.has(row.name)}
          action={
            hasLifecycle ? (
              <AxisButton
                role="secondary"
                data-testid="studio-contract-add-state"
                disabled={saving || reconnecting}
                onClick={() => setStateOpen(true)}
              >
                {t('shell.studio.contract.addState')}
              </AxisButton>
            ) : undefined
          }
          columns={[
            {
              key: 'name',
              title: t('shell.studio.contract.state'),
              filter: { kind: 'text', value: (row) => row.name },
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
              filter: { kind: 'text', value: (row) => marksText(row) || null },
              render: (row) => marksText(row),
            },
            {
              key: 'point',
              title: t('shell.studio.contract.point'),
              filter: {
                kind: 'text',
                value: (row) => (isAdded(statesList, row.name, savedStates) ? null : row.points.join(', ') || null),
              },
              render: (row) => (isAdded(statesList, row.name, savedStates) ? null : <Points values={row.points} />),
            },
          ]}
        />
        <ContractTable
          testId="studio-contract-transitions"
          title={t('shell.studio.contract.transitions')}
          prefix="transitions."
          rows={view.transitions}
          keep={(row) => !savedTransitions.has(row.name)}
          action={
            hasLifecycle ? (
              <AxisButton
                role="secondary"
                data-testid="studio-contract-add-transition"
                disabled={saving || reconnecting}
                onClick={() => setTransitionOpen(true)}
              >
                {t('shell.studio.contract.addTransition')}
              </AxisButton>
            ) : undefined
          }
          columns={[
            {
              key: 'name',
              title: t('shell.studio.contract.name'),
              filter: { kind: 'text', value: (row) => row.name },
              render: (row, index) => (
                <RowName
                  name={row.name}
                  added={isAdded(transitionsList, row.name, savedTransitions)}
                  onRemove={() => remove(transitionsList, index)}
                />
              ),
            },
            {
              key: 'from',
              title: t('shell.studio.contract.from'),
              filter: { kind: 'text', value: (row) => row.from.join(', ') || null },
              render: (row) => row.from.join(', '),
            },
            {
              key: 'to',
              title: t('shell.studio.contract.to'),
              filter: { kind: 'text', value: (row) => row.to || null },
              render: (row) => row.to,
            },
            {
              key: 'guard',
              title: t('shell.studio.contract.guard'),
              filter: { kind: 'text', value: (row) => row.guard || null },
              render: (row) => row.guard ?? noGuard,
            },
            {
              key: 'point',
              title: t('shell.studio.contract.point'),
              filter: {
                kind: 'text',
                value: (row) => (isAdded(transitionsList, row.name, savedTransitions) ? null : row.point || null),
              },
              render: (row) =>
                isAdded(transitionsList, row.name, savedTransitions) ? null : <Point value={row.point} />,
            },
          ]}
        />
        {layerDiagnostics.length > 0 && (
          <Flex vertical gap="small">
            <Typography.Text strong>{t('shell.studio.contract.diagnostics')}</Typography.Text>
            <div data-testid="studio-contract-diagnostics">
              <StudioProblemList diagnostics={layerDiagnostics} />
            </div>
          </Flex>
        )}
        <ContractTable
          testId="studio-contract-validations"
          title={t('shell.studio.contract.validations')}
          prefix="validations."
          rows={view.validations}
          columns={[
            {
              key: 'name',
              title: t('shell.studio.contract.name'),
              filter: { kind: 'text', value: (row) => row.name || null },
              render: (row) => row.name,
            },
            {
              key: 'field',
              title: t('shell.studio.contract.field'),
              filter: { kind: 'text', value: (row) => row.field || null },
              render: (row) => row.field,
            },
            {
              key: 'expression',
              title: t('shell.studio.contract.expression'),
              filter: { kind: 'text', value: (row) => row.expression || null },
              render: (row) => row.expression,
            },
            {
              key: 'point',
              title: t('shell.studio.contract.point'),
              filter: { kind: 'text', value: (row) => row.point || null },
              render: (row) => <Point value={row.point} />,
            },
          ]}
        />
        <ContractTable
          testId="studio-contract-effects"
          title={t('shell.studio.contract.effects')}
          prefix="effects."
          rows={view.effects}
          columns={[
            {
              key: 'name',
              title: t('shell.studio.contract.name'),
              filter: { kind: 'text', value: (row) => row.name || null },
              render: (row) => row.name,
            },
            {
              key: 'on',
              title: t('shell.studio.contract.on'),
              filter: { kind: 'text', value: (row) => row.on || null },
              render: (row) => row.on,
            },
            {
              key: 'creates',
              title: t('shell.studio.contract.creates'),
              filter: { kind: 'text', value: (row) => row.entity || null },
              render: (row) => row.entity,
            },
            {
              key: 'point',
              title: t('shell.studio.contract.point'),
              filter: { kind: 'text', value: (row) => row.point || null },
              render: (row) => <Point value={row.point} />,
            },
          ]}
        />
        <ContractTable
          testId="studio-contract-ceiling"
          title={t('shell.studio.contract.ceiling')}
          prefix="ceiling."
          rows={view.ceiling}
          columns={[
            {
              key: 'slot',
              title: t('shell.studio.contract.slot'),
              filter: {
                kind: 'enum',
                value: (row) => row.slot,
                options: [...new Set(view.ceiling.map((row) => row.slot))].map((slot) => ({
                  value: slot,
                  label: slot === 'others' ? t('shell.studio.contract.others') : slot,
                })),
              },
              render: (row) => (row.slot === 'others' ? t('shell.studio.contract.others') : row.slot),
            },
            {
              key: 'actions',
              title: t('shell.studio.moduleGrants.actions'),
              filter: { kind: 'text', value: (row) => row.actions.join(', ') || null },
              render: (row) => row.actions.join(', '),
            },
            {
              key: 'fields',
              title: t('shell.studio.moduleGrants.fields'),
              filter: { kind: 'text', value: (row) => row.fields.join(', ') || null },
              render: (row) =>
                row.fields.length === 0 ? t('shell.studio.moduleGrants.allFields') : row.fields.join(', '),
            },
            {
              key: 'filter',
              title: t('shell.studio.moduleGrants.filter'),
              filter: { kind: 'text', value: (row) => row.filter || null },
              render: (row) => row.filter,
            },
            {
              key: 'point',
              title: t('shell.studio.contract.point'),
              filter: { kind: 'text', value: (row) => row.point || null },
              render: (row) => <Point value={row.point} />,
            },
          ]}
        />
        {conflict && (
          <AxisAlert
            data-testid="studio-conflict"
            kind="warning"
            message={t('shell.studio.conflict')}
            action={
              <AxisButton role="secondary" onClick={() => void reload()}>
                {t('shell.studio.reload')}
              </AxisButton>
            }
          />
        )}
        {saveError !== null && <AxisAlert data-testid="studio-save-error" kind="error" message={saveError} />}
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
