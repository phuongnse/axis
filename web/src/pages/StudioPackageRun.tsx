import { Flex, Select, Tabs } from 'antd'
import { lazy, Suspense, useCallback, useContext, useEffect, useState, type ComponentProps } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { ActionBar } from '../platform/ActionBar'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { ChoiceActions } from '../platform/ChoiceActions'
import { CodeBlock } from '../platform/CodeBlock'
import { EmptyState } from '../platform/EmptyState'
import { FieldInput } from '../platform/FieldInput'
import { fillText } from '../platform/fillText'
import { formatDuration } from '../platform/formatValue'
import { setOwn } from '../platform/ownProperty'
import { keepsOwnWidth } from '../platform/ownWidth'
import { PaneHeader } from '../platform/PaneHeader'
import { QuotaRefusedError } from '../platform/quota'
import type { RecordValue } from '../platform/records'
import { SavedItemsDialog, type SavedItems, type SavedItemsTexts } from '../platform/SavedItemsDialog'
import type { FieldMetadata, FieldType } from '../platform/site'
import { StatusTag, type StatusTone } from '../platform/StatusTag'
import {
  fetchStudioRequest,
  fetchStudioRunInputs,
  runStudioPackage,
  saveStudioRunInputs,
  ServerUnreachableError,
  type StudioRequest,
  type StudioRunInput,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useFollowJob } from '../platform/useFollowJob'
import { WarningLine } from '../platform/WarningLine'
import { workspaceTabRow } from '../platform/workspaceTabRow'
import { PlatformContext } from './context'
import type { PackageOperation, PackageOperationField } from './useStudioPackages'

const CodeEditor = lazy(() => import('./CodeEditor'))

/** The field types of a manifest that `FieldInput` draws as they are. Any other type is drawn as text. */
const knownTypes = new Set<string>(['text', 'integer', 'decimal', 'boolean', 'date', 'date-time'])

/** The texts of the saved items dialog for a package's run inputs. */
const runInputTexts: SavedItemsTexts = {
  title: 'shell.studio.packages.runInputs.title',
  save: 'shell.studio.packages.runInputs.save',
  empty: 'shell.studio.packages.runInputs.empty',
  name: 'shell.studio.packages.runInputs.name',
  nameHelp: 'shell.studio.packages.runInputs.name.help',
  nameRequired: 'shell.studio.packages.runInputs.nameRequired',
  nameTaken: 'shell.studio.packages.runInputs.nameTaken',
  saveFailed: 'shell.studio.packages.runInputs.saveFailed',
  overwrite: 'shell.studio.packages.runInputs.overwrite',
  overwriteConfirm: 'shell.studio.packages.runInputs.overwriteConfirm',
  overwriteFailed: 'shell.studio.packages.runInputs.overwriteFailed',
  rename: 'shell.studio.packages.runInputs.rename',
  renameFailed: 'shell.studio.packages.runInputs.renameFailed',
  delete: 'shell.studio.packages.runInputs.delete',
  deleteConfirm: 'shell.studio.packages.runInputs.deleteConfirm',
  deleteFailed: 'shell.studio.packages.runInputs.deleteFailed',
}

/** The metadata `FieldInput` needs for a manifest's input field. Every property the manifest does not give is empty. */
function fieldOf(field: PackageOperationField): FieldMetadata {
  return {
    name: field.name,
    type: (knownTypes.has(field.type) ? field.type : 'text') as FieldType,
    labelKey: null,
    required: field.required,
    unique: false,
    computed: false,
    sequence: false,
    creator: false,
    maxLength: null,
    precision: null,
    scale: null,
    min: null,
    max: null,
    minLength: null,
    values: null,
    target: null,
    fields: null,
  }
}

/** The input for one field, which takes the attributes that `AxisField` gives its input and passes them on to `FieldInput`. */
function ValueInput({
  field,
  value,
  onChange,
  locale,
  id,
  'aria-describedby': describedBy,
  'aria-required': required,
}: {
  field: FieldMetadata
  value: unknown
  onChange: (value: RecordValue) => void
  locale: string
  id?: string
  'aria-describedby'?: string
  'aria-required'?: boolean
}) {
  const shown: RecordValue =
    typeof value === 'string' || typeof value === 'boolean' ? value : typeof value === 'number' ? String(value) : null
  return (
    <FieldInput
      field={field}
      value={shown}
      onChange={onChange}
      locale={locale}
      id={id}
      ariaDescribedBy={describedBy}
      ariaRequired={required}
    />
  )
}

/**
 * The input for a boolean field, a checkbox. It is a component of its own, so `AxisField` gives it no wrapper and the
 * checkbox keeps its own width, see docs/reference/shell.md#layout.
 */
const BooleanValueInput = keepsOwnWidth(function BooleanValueInput(props: ComponentProps<typeof ValueInput>) {
  return <ValueInput {...props} />
})

/**
 * The input as the JSON tab shows it and Run sends it: each field of the operation, in its order, with `null` for a
 * field that has no value, then any other key of the input. Whole-number and decimal text that is a finite number is a number.
 */
function shownInput(fields: readonly PackageOperationField[], input: Record<string, unknown>): Record<string, unknown> {
  const shown: Record<string, unknown> = Object.fromEntries(fields.map((field) => [field.name, null]))
  for (const [name, value] of Object.entries(input)) {
    const type = fields.find((field) => field.name === name)?.type
    setOwn(
      shown,
      name,
      typeof value === 'string' &&
        (type === 'integer' || type === 'decimal') &&
        value.trim() !== '' &&
        Number.isFinite(Number(value))
        ? Number(value)
        : (value ?? null),
    )
  }
  return shown
}

/** The object that a JSON text holds, or `null` when it is not JSON or not an object. */
function parseObject(text: string): Record<string, unknown> | null {
  try {
    const value: unknown = JSON.parse(text)
    return typeof value === 'object' && value !== null && !Array.isArray(value)
      ? (value as Record<string, unknown>)
      : null
  } catch {
    return null
  }
}

const jsonOf = (value: unknown) => JSON.stringify(value ?? null, null, 2)

/** The tone of an outcome: a success or a failure, and anything else as the API sends it. */
function toneOf(outcome: string): StatusTone {
  return outcome === 'succeeded' ? 'success' : outcome === 'failed' ? 'danger' : 'neutral'
}

/**
 * The Run tab of a package's workspace: two panes parted by a rule, see
 * docs/reference/studio-packages.md#a-packages-files. The Input pane is one block under its title: Operation, Saved
 * input, whose list ends with Manage saved inputs…, then the Form and JSON tabs with the values, and Run in the pane's
 * action bar. Run first calls `beforeRun`, which saves every changed file, and stops when it answers `false`. It then
 * posts the run, follows its request and fills the Output pane: the outcome as a status tag, Duration, the output as
 * read-only JSON, then the Log. The saved inputs are read when the tab first shows, and each change to them replaces
 * the package's run inputs with the version they were read at, see docs/reference/studio-packages.md#the-authoring-api.
 * Each pane is `height` tall and scrolls once.
 */
export function StudioPackageRun({
  application,
  packageId,
  operations,
  height,
  beforeRun,
}: {
  application: string
  packageId: string
  operations: readonly PackageOperation[]
  height: number
  beforeRun: () => Promise<boolean>
}) {
  const t = useText()
  const { lost } = useStudioConnection()
  const tokens = useDesignTokens()
  const { colour, spacing } = tokens
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const [operationName, setOperationName] = useState<string | null>(null)
  const [input, setInput] = useState<Record<string, unknown>>({})
  // The text typed in the JSON tab, kept as typed until the form or a saved input replaces the input.
  const [typed, setTyped] = useState<string | null>(null)
  const [selected, setSelected] = useState<string | null>(null)
  const [selectOpen, setSelectOpen] = useState(false)
  const [managing, setManaging] = useState(false)
  const [saved, setSaved] = useState<{ items: StudioRunInput[]; version: string | null }>({
    items: [],
    version: null,
  })
  const [inputsFailed, setInputsFailed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [request, setRequest] = useState<StudioRequest | null>(null)
  const [followed, setFollowed] = useState<string | null>(null)
  const [problem, setProblem] = useState<string | null>(null)

  const operation = operations.find((candidate) => candidate.name === operationName) ?? operations[0]
  const fields = operation?.input ?? []

  const loadInputs = useCallback(
    (signal?: AbortSignal) =>
      fetchStudioRunInputs(application, packageId, signal).then(
        (inputs) => {
          setSaved(inputs)
          setInputsFailed(false)
        },
        (error: unknown) => {
          if (signal?.aborted) {
            return
          }
          if (error instanceof ServerUnreachableError) {
            lost()
            return
          }
          console.warn(`Loading the run inputs of the package '${packageId}' failed`, error)
          setInputsFailed(true)
        },
      ),
    [application, packageId, lost],
  )

  // The inputs are read when the tab first shows, and again after a change that the server refused.
  useEffect(() => {
    const controller = new AbortController()
    void loadInputs(controller.signal)
    return () => controller.abort()
  }, [loadInputs])

  useFollowJob(followed === null ? null : `${application}/${followed}`, {
    read: (signal) => fetchStudioRequest(application, followed!, signal),
    ends: (next) => next.status !== 'queued' && next.status !== 'running',
    interval: 1000,
    onAnswer: setRequest,
    onEnd: (next) => {
      setRequest(next)
      setFollowed(null)
      setBusy(false)
    },
    onError: (error) => {
      setFollowed(null)
      setBusy(false)
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Checking the run of the package '${packageId}' failed`, error)
        setProblem(t('shell.studio.packages.run.startFailed'))
      }
      return 'stop'
    },
  })

  const jsonText = typed ?? jsonOf(shownInput(fields, input))
  const parsed = typed === null ? shownInput(fields, input) : parseObject(typed)

  const changeInput = (next: Record<string, unknown>) => {
    setInput(next)
    setTyped(null)
  }

  const run = async () => {
    if (busy || operation === undefined || parsed === null) {
      return
    }
    setBusy(true)
    setProblem(null)
    try {
      if (!(await beforeRun())) {
        setBusy(false)
        return
      }
      const started = await runStudioPackage(application, {
        package: packageId,
        operation: operation.name,
        input: parsed,
      })
      setRequest(started)
      setFollowed(started.id)
    } catch (error) {
      setBusy(false)
      if (error instanceof ServerUnreachableError) {
        lost()
      } else if (error instanceof QuotaRefusedError) {
        setProblem(error.message)
      } else {
        console.warn(`Running the package '${packageId}' failed`, error)
        setProblem(t('shell.studio.packages.run.startFailed'))
      }
    }
  }

  /** Replaces the package's run inputs. A conflict or a failure reads them again and answers `false`. */
  const replaceInputs = async (items: StudioRunInput[]): Promise<boolean> => {
    try {
      const result = await saveStudioRunInputs(application, packageId, items, saved.version)
      if (result.status === 'saved') {
        setSaved({ items, version: result.version })
        return true
      }
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Saving the run inputs of the package '${packageId}' failed`, error)
      }
    }
    await loadInputs()
    return false
  }

  const savedItems: SavedItems<StudioRunInput> = {
    items: saved.items,
    keyOf: (item) => item.name,
    nameOf: (item) => item.name,
    open: (item) => {
      setOperationName(item.operation)
      setSelected(item.name)
      const value = item.input
      changeInput(typeof value === 'object' && value !== null && !Array.isArray(value) ? { ...value } : {})
    },
    save: async (name) => {
      if (parsed === null) {
        return false
      }
      const done = await replaceInputs([...saved.items, { name, operation: operation?.name ?? '', input: parsed }])
      if (done) {
        setSelected(name)
      }
      return done
    },
    overwrite: (item) =>
      parsed === null
        ? Promise.resolve(false)
        : replaceInputs(
            saved.items.map((candidate) =>
              candidate === item ? { ...item, operation: operation?.name ?? item.operation, input: parsed } : candidate,
            ),
          ),
    rename: async (item, name) => {
      const done = await replaceInputs(
        saved.items.map((candidate) => (candidate === item ? { ...item, name } : candidate)),
      )
      if (done && selected === item.name) {
        setSelected(name)
      }
      return done
    },
    remove: async (item) => {
      const done = await replaceInputs(saved.items.filter((candidate) => candidate !== item))
      if (done && selected === item.name) {
        setSelected(null)
      }
      return done
    },
  }

  const rule = `1px solid ${colour.borderSubtle}`
  const scroll = { flex: 1, overflowY: 'auto', overscrollBehavior: 'contain', padding: spacing.md } as const
  const pane = { display: 'flex', flexDirection: 'column', minWidth: 0, height } as const

  const ended = request !== null && request.status !== 'queued' && request.status !== 'running'
  const ran = request?.run ?? null
  const outcome = ran?.outcome ?? (ended ? request.status : null)
  const outcomeText =
    outcome === null
      ? null
      : outcome === 'succeeded' || outcome === 'failed'
        ? t(`shell.studio.packages.run.outcome.${outcome}`)
        : outcome
  const shownResult = ran === null ? null : ran.outcome === 'failed' && ran.error !== null ? ran.error : ran.output
  const savedHere = saved.items.filter((item) => item.operation === operation?.name)

  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1fr)' }}>
      <section aria-label={t('shell.studio.packages.run.input')} style={{ ...pane, borderInlineEnd: rule }}>
        <PaneHeader title={t('shell.studio.packages.run.input')} />
        {operation === undefined ? (
          <div style={scroll}>
            <EmptyState text={t('shell.studio.packages.run.noOperation')} />
          </div>
        ) : (
          <div style={{ ...scroll, display: 'flex', flexDirection: 'column', gap: spacing.md }}>
            {inputsFailed && <AxisAlert kind="warning" message={t('shell.studio.packages.run.inputsFailed')} />}
            {problem !== null && <AxisAlert data-testid="studio-run-error" kind="error" message={problem} />}
            <AxisField
              label={t('shell.studio.packages.run.operation')}
              help={t('shell.studio.packages.run.operationHelp')}
            >
              <Select
                virtual={false}
                value={operation.name}
                options={operations.map((candidate) => ({ value: candidate.name, label: candidate.name }))}
                onChange={(next: string) => {
                  setOperationName(next)
                  setSelected(null)
                  changeInput({})
                }}
              />
            </AxisField>
            <AxisField
              label={t('shell.studio.packages.run.savedInput')}
              help={t('shell.studio.packages.run.savedInputHelp')}
            >
              <Select
                virtual={false}
                open={selectOpen}
                onOpenChange={setSelectOpen}
                value={savedHere.some((item) => item.name === selected) ? selected : null}
                options={savedHere.map((item) => ({ value: item.name, label: item.name }))}
                onChange={(next: string) => {
                  const item = savedHere.find((candidate) => candidate.name === next)
                  if (item !== undefined) {
                    savedItems.open(item)
                  }
                }}
                popupRender={(list) => (
                  <ChoiceActions
                    list={list}
                    actions={[
                      { key: 'manage', labelKey: 'shell.studio.packages.run.manage', onClick: () => setManaging(true) },
                    ]}
                    close={() => setSelectOpen(false)}
                  />
                )}
              />
            </AxisField>
            <Tabs
              type="line"
              {...workspaceTabRow(tokens)}
              items={[
                {
                  key: 'form',
                  label: t('shell.studio.form'),
                  children: (
                    <Flex vertical gap={spacing.lg} style={{ paddingBlock: spacing.md }}>
                      {fields.map((field) => {
                        const Input = field.type === 'boolean' ? BooleanValueInput : ValueInput
                        return (
                          <AxisField
                            key={`${operation.name}/${field.name}`}
                            label={field.name}
                            help={t(`shell.studio.entity.typeOf.${knownTypes.has(field.type) ? field.type : 'text'}`)}
                            required={field.required}
                          >
                            <Input
                              field={fieldOf(field)}
                              value={input[field.name]}
                              locale={locale}
                              onChange={(value) => changeInput({ ...input, [field.name]: value })}
                            />
                          </AxisField>
                        )
                      })}
                    </Flex>
                  ),
                },
                {
                  key: 'json',
                  label: t('shell.studio.json'),
                  children: (
                    <div style={{ paddingBlock: spacing.md }}>
                      <Suspense fallback={null}>
                        <CodeEditor
                          label={t('shell.studio.json')}
                          value={jsonText}
                          onChange={(text) => {
                            setTyped(text)
                            const next = parseObject(text)
                            if (next !== null) {
                              setInput(next)
                            }
                          }}
                          language="json"
                          grow
                        />
                      </Suspense>
                      {parsed === null && (
                        <WarningLine testId="studio-run-not-json">{t('shell.studio.packages.run.notJson')}</WarningLine>
                      )}
                    </div>
                  ),
                },
              ]}
            />
          </div>
        )}
        <ActionBar
          pane
          start={
            <AxisButton
              role="main"
              loading={busy}
              disabled={busy || operation === undefined}
              onClick={() => void run()}
            >
              {t('shell.studio.packages.run.run')}
            </AxisButton>
          }
        />
      </section>
      <section aria-label={t('shell.studio.packages.run.output')} style={pane}>
        <PaneHeader
          title={t('shell.studio.packages.run.output')}
          tag={
            outcomeText !== null && (
              <StatusTag tone={toneOf(outcome ?? '')} data-testid="studio-run-outcome">
                {outcomeText}
              </StatusTag>
            )
          }
          actions={
            ran !== null && (
              <span data-testid="studio-run-duration" style={{ color: colour.textMuted }}>
                {fillText(t('shell.studio.packages.run.duration'), {
                  value: formatDuration(ran.durationMs, locale, t),
                })}
              </span>
            )
          }
        />
        {request === null ? (
          <div style={scroll}>
            <EmptyState text={t('shell.studio.packages.run.none')} />
          </div>
        ) : (
          <div style={{ ...scroll, padding: 0 }}>
            {shownResult !== null && (
              <div style={{ padding: spacing.md }}>
                <Suspense fallback={null}>
                  <CodeEditor
                    label={t('shell.studio.packages.run.output')}
                    value={jsonOf(shownResult)}
                    language="json"
                    readOnly
                    grow
                  />
                </Suspense>
              </div>
            )}
            <div style={{ borderTop: rule }}>
              <PaneHeader title={t('shell.studio.packages.run.log')} />
              <CodeBlock plain wrap testId="studio-run-log" text={request.output?.join('\n') ?? null} />
            </div>
          </div>
        )}
      </section>
      <SavedItemsDialog open={managing} onClose={() => setManaging(false)} saved={savedItems} texts={runInputTexts} />
    </div>
  )
}
