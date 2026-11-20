import { Flex, Select, Tabs, Typography } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { ActionBar } from '../platform/ActionBar'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { ChoiceActions } from '../platform/ChoiceActions'
import { AxisField } from '../platform/AxisField'
import { CodeBlock } from '../platform/CodeBlock'
import { EmptyState } from '../platform/EmptyState'
import { FieldInput } from '../platform/FieldInput'
import { SavedItemsDialog, type SavedItemsTexts } from '../platform/SavedItemsDialog'
import type { RecordValue } from '../platform/records'
import type { FieldMetadata, FieldType } from '../platform/site'
import { StatusTag } from '../platform/StatusTag'
import {
  fetchStudioPackageRunInputs,
  saveStudioPackageRunInputs,
  type StudioPackageRun,
  type StudioPackageRunInput,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import CodeEditor from './CodeEditor'
import { workspaceFindTexts } from './jsonFindPanel'
import { codeLanguage } from './codeLanguage'
import { PaneHeader } from './PaneHeader'

/** One typed field of an operation's input, as the manifest lists it. */
export interface OperationField {
  name: string
  type: string
  required?: boolean
}

/** An operation of the package, as the manifest lists it. */
export interface PackageOperation {
  name: string
  input?: OperationField[]
}

/** The field the shared input draws for one of an operation's input fields, which hold the rule parameter types. */
function inputField(field: OperationField): FieldMetadata {
  return {
    name: field.name,
    type: field.type as FieldType,
    labelKey: null,
    required: field.required === true,
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

const jsonLanguage = codeLanguage('input.json')

/** What the dialog of a package's saved run inputs says. */
const inputTexts: SavedItemsTexts = {
  title: 'shell.studio.package.run.inputs.title',
  empty: 'shell.studio.package.run.inputs.empty',
  name: 'shell.studio.package.run.inputs.name',
  nameHelp: 'shell.studio.package.run.inputs.name.help',
  nameRequired: 'shell.studio.package.run.inputs.nameRequired',
  nameTaken: 'shell.studio.package.run.inputs.nameTaken',
  save: 'shell.studio.package.run.inputs.save',
  saveFailed: 'shell.studio.package.run.inputs.saveFailed',
  rename: 'shell.studio.package.run.inputs.rename',
  renameFailed: 'shell.studio.package.run.inputs.renameFailed',
  overwrite: 'shell.studio.package.run.inputs.overwrite',
  overwriteConfirm: 'shell.studio.package.run.inputs.overwriteConfirm',
  overwriteFailed: 'shell.studio.package.run.inputs.overwriteFailed',
  delete: 'shell.studio.package.run.inputs.delete',
  deleteConfirm: 'shell.studio.package.run.inputs.deleteConfirm',
  deleteFailed: 'shell.studio.package.run.inputs.deleteFailed',
}

/** Proposed, see #3978: the input as JSON, with each of the operation's fields that it lacks as `null`, so its shape shows. */
function inputJson(fields: readonly OperationField[], values: Record<string, unknown>): string {
  return JSON.stringify({ ...Object.fromEntries(fields.map((field) => [field.name, null])), ...values }, null, 2)
}

/**
 * Proposed, see #3978: the Run tab of a package's output panel, as AWS Lambda's Test tab: an operation of the manifest,
 * a named input kept with the application and shared by its developers, the input as a form of the operation's typed
 * fields or as JSON, and Run, which builds the working files and calls the operation in the tenant's sandbox. The last
 * run shows beside the input: its outcome, how long it took and the CPU time it counted, its output as names and values,
 * or why it failed, and the lines it wrote.
 */
export function PackageRun({
  application,
  packageId,
  operations,
  developer,
  locale,
  running,
  run,
  output,
  onRun,
  rowHeight,
}: {
  application: string
  packageId: string
  operations: readonly PackageOperation[]
  developer: boolean
  locale: string
  /** A run is on its way, so Run waits. */
  running: boolean
  /** The last run, or `null` before one. */
  run: StudioPackageRun | null
  /** The lines the last run wrote. */
  output: readonly string[]
  onRun: (operation: string, input: Record<string, unknown>) => void
  /** The height of a pane's title row, as the editor's tab row. */
  rowHeight: number
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [saved, setSaved] = useState<{ items: StudioPackageRunInput[]; version: string | null }>({
    items: [],
    version: null,
  })
  const [chosen, setChosen] = useState<string | null>(null)
  const [operation, setOperation] = useState<string | undefined>(operations[0]?.name)
  const [values, setValues] = useState<Record<string, unknown>>({})
  const [shown, setShown] = useState<'form' | 'json'>('form')
  const [jsonText, setJsonText] = useState('{}')
  const [jsonError, setJsonError] = useState(false)
  const [manageOpen, setManageOpen] = useState(false)
  const [savedOpen, setSavedOpen] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioPackageRunInputs(application, packageId, controller.signal).then(
      (answer) => {
        setSaved(answer)
        const first = answer.items[0]
        if (first !== undefined) {
          setChosen(first.name)
          setOperation(first.operation)
          setValues(first.input)
          setJsonText(JSON.stringify(first.input, null, 2))
        }
      },
      () => {},
    )
    return () => controller.abort()
  }, [application, packageId])

  const fields = useMemo(
    () => operations.find((candidate) => candidate.name === operation)?.input ?? [],
    [operations, operation],
  )

  function choose(name: string | null) {
    setChosen(name)
    const item = saved.items.find((candidate) => candidate.name === name)
    const next = item?.input ?? {}
    if (item !== undefined) {
      setOperation(item.operation)
    }
    setValues(next)
    const nextFields = operations.find((candidate) => candidate.name === (item?.operation ?? operation))?.input ?? []
    setJsonText(inputJson(nextFields, next))
    setJsonError(false)
  }

  function switchTo(next: 'form' | 'json') {
    if (next === 'json') {
      setJsonText(inputJson(fields, values))
      setJsonError(false)
    } else {
      try {
        const parsed: unknown = JSON.parse(jsonText)
        if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
          throw new Error('not an object')
        }
        setValues(parsed as Record<string, unknown>)
      } catch {
        setJsonError(true)
        return
      }
    }
    setShown(next)
  }

  function current(): Record<string, unknown> | null {
    if (shown === 'form') {
      return values
    }
    try {
      const parsed: unknown = JSON.parse(jsonText)
      return parsed !== null && typeof parsed === 'object' && !Array.isArray(parsed)
        ? (parsed as Record<string, unknown>)
        : null
    } catch {
      return null
    }
  }

  /** Replaces the saved inputs, and answers whether the server kept them; the dialog says when it did not. */
  async function store(items: StudioPackageRunInput[]) {
    try {
      const result = await saveStudioPackageRunInputs(application, packageId, items, saved.version)
      if (result.status === 'conflict') {
        return false
      }
      setSaved({ items, version: result.version })
      return true
    } catch {
      return false
    }
  }

  const input = current()
  const missing = fields.some((field) => field.required && (input?.[field.name] ?? '') === '')
  const runnable = operation !== undefined && input !== null && !missing && !running

  const runButton = (
    <AxisButton
      role="main"
      data-testid="package-run-send"
      disabled={!runnable || !developer}
      loading={running}
      onClick={() => input !== null && operation !== undefined && onRun(operation, input)}
    >
      {t('shell.studio.package.run.run')}
    </AxisButton>
  )

  const rule = `1px solid ${colour.borderSubtle}`
  const pad = `${spacing.sm}px ${spacing.md}px`

  // Proposed, see #3978: two panes side by side, parted by a rule that runs the panel's height, as the explorer and the
  // editor are. The input pane follows what a person does: the operation, then the Input title row with a saved input
  // and the fields, then Run in the action bar at the pane's foot, where it stays in view; the result pane has the Output title row with the
  // outcome and duration, the output, then the Log title row and its lines. Each rule runs its pane's width, and no
  // region has a box of its own, as the workspace draws them.
  return (
    <div data-testid="package-run" style={{ display: 'flex', height: '100%', minHeight: 0 }}>
      <div
        style={{
          flex: '0 0 45%',
          minWidth: 320,
          display: 'flex',
          flexDirection: 'column',
          minHeight: 0,
          borderInlineEnd: rule,
        }}
      >
        {/* One scroll for the pane above its action bar, so no region scrolls inside another and Run stays in view. */}
        <div
          style={{
            flex: 1,
            minHeight: 0,
            display: 'flex',
            flexDirection: 'column',
            overflowY: 'auto',
            overscrollBehavior: 'contain',
          }}
        >
          {/* Proposed, see #3978: one block, Input, from what runs to its values, under its title row: no rule between the
              parts of its content, and the Form and JSON tabs with the rule all workspace tabs have. */}
          <PaneHeader title={t('shell.studio.package.run.input')} height={rowHeight} />
          <div style={{ flex: 'none', padding: pad }}>
            <Flex vertical gap={spacing.sm}>
              <AxisField label={t('shell.studio.package.run.operation')} help="">
                <Select
                  data-testid="package-run-operation"
                  value={operation}
                  onChange={(next: string) => setOperation(next)}
                  options={operations.map((candidate) => ({ value: candidate.name, label: candidate.name }))}
                  style={{ width: '100%' }}
                />
              </AxisField>
              <AxisField label={t('shell.studio.package.run.savedInput')} help="">
                {/* Proposed, see #3978: managing the saved inputs, the select's choices, is the last item of its list, and it
                    opens the dialog that keeps saved items, as a table's Views does, so the select keeps the field's width. */}
                <Select
                  data-testid="package-run-saved-input"
                  placeholder={t('shell.studio.package.run.newInput')}
                  value={chosen ?? undefined}
                  onChange={(next: string) => choose(next)}
                  allowClear
                  onClear={() => choose(null)}
                  open={savedOpen}
                  onOpenChange={setSavedOpen}
                  notFoundContent={null}
                  options={saved.items
                    .filter((item) => item.operation === operation)
                    .map((item) => ({ value: item.name, label: item.name }))}
                  popupRender={(list) => (
                    <>
                      {list}
                      {developer && (
                        <ChoiceActions
                          onDone={() => setSavedOpen(false)}
                          actions={[
                            {
                              key: 'manage',
                              label: t('shell.studio.package.run.inputs.manage'),
                              onClick: () => setManageOpen(true),
                            },
                          ]}
                        />
                      )}
                    </>
                  )}
                  style={{ width: '100%' }}
                />
              </AxisField>
            </Flex>
          </div>
          <Tabs
            className="axis-workspace-tabs axis-file-tabs"
            aria-label={t('shell.studio.package.run.inputAs')}
            activeKey={shown}
            onChange={(key) => switchTo(key as 'form' | 'json')}
            items={[
              { key: 'form', label: t('shell.studio.package.run.form') },
              { key: 'json', label: t('shell.value.json') },
            ]}
          />
          {(jsonError || shown === 'form') && (
            <div style={{ flex: 'none', padding: pad }}>
              <Flex vertical gap={spacing.sm}>
                {jsonError && <AxisAlert kind="error" message={t('shell.studio.package.run.jsonInvalid')} />}
                {shown === 'form' &&
                  (fields.length === 0 ? (
                    <Typography.Text type="secondary">{t('shell.studio.package.run.noInput')}</Typography.Text>
                  ) : (
                    <Flex vertical gap={spacing.sm}>
                      {fields.map((field) => (
                        <AxisField
                          key={field.name}
                          label={field.name}
                          help={t(`shell.studio.package.run.type.${field.type}`)}
                          required={field.required}
                        >
                          <FieldInput
                            field={inputField(field)}
                            value={(values[field.name] ?? null) as RecordValue}
                            onChange={(next) => setValues((all) => ({ ...all, [field.name]: next }))}
                            locale={locale}
                          />
                        </AxisField>
                      ))}
                    </Flex>
                  ))}
              </Flex>
            </div>
          )}
          {shown === 'json' && (
            <div style={{ flex: '1 0 auto', display: 'flex', flexDirection: 'column' }}>
              <CodeEditor
                label={t('shell.studio.package.run.inputJson')}
                value={jsonText}
                onChange={setJsonText}
                language={jsonLanguage}
                grow
                findTexts={workspaceFindTexts(t)}
              />
            </div>
          )}
        </div>
        <ActionBar pane end={runButton} />
      </div>
      <div
        data-testid="package-run-result"
        style={{ flex: 1, minWidth: 0, minHeight: 0, overflowY: 'auto', overscrollBehavior: 'contain' }}
      >
        {/* One scroll for the whole pane, its output and its log, so the pane scrolls once from top to bottom. */}
        {run === null ? (
          <div style={{ padding: pad }}>
            <EmptyState text={t('shell.studio.package.run.none')} />
          </div>
        ) : (
          <>
            <PaneHeader
              height={rowHeight}

              title={
                <Flex gap="small" align="center" wrap>
                  <Typography.Text strong>{t('shell.studio.package.run.output')}</Typography.Text>
                  <StatusTag tone={run.outcome === 'succeeded' ? 'success' : 'danger'}>
                    {t(`shell.studio.package.run.outcome.${run.outcome}`)}
                  </StatusTag>
                </Flex>
              }
              actions={
                // How long the run took is a fact of the output, so it sits on the row's right, named, apart from
                // the state beside the title.
                <Flex gap="small" align="baseline" data-testid="package-run-duration">
                  <Typography.Text type="secondary">{t('shell.studio.package.run.duration')}</Typography.Text>
                  <Typography.Text>
                    {t('shell.studio.package.run.milliseconds').replace('{count}', String(run.durationMs))}
                  </Typography.Text>
                </Flex>
              }
            />
            {/* Proposed, see #3978: the output as read-only JSON in the code editor the input's JSON uses, filling its
                block, and the log as a block of its own under a rule, with its own title row. */}
            {run.error !== null ? (
              <div style={{ flex: 'none', padding: pad }}>
                <AxisAlert kind="error" message={`${run.error.code}: ${run.error.message}`} />
              </div>
            ) : (
              run.output !== null && (
                <div data-testid="package-run-output">
                  <CodeEditor
                    label={t('shell.studio.package.run.output')}
                    value={JSON.stringify(run.output, null, 2)}
                    onChange={() => {}}
                    readOnly
                    language={jsonLanguage}
                    grow
                    findTexts={workspaceFindTexts(t)}
                  />
                </div>
              )
            )}
            {output.length > 0 && (
              <div style={{ borderTop: rule }}>
                <PaneHeader title={t('shell.studio.package.run.log')} height={rowHeight} />
                <div style={{ padding: pad }}>
                  <CodeBlock text={output.join('\n')} plain wrap />
                </div>
              </div>
            )}
          </>
        )}
      </div>
      <SavedItemsDialog
        open={manageOpen}
        onClose={() => setManageOpen(false)}
        texts={inputTexts}
        saved={{
          items: saved.items.filter((item) => item.operation === operation).map((item) => ({ ...item, id: item.name })),
          open: (item) => choose(item.name),
          save: async (name) => {
            if (input === null || operation === undefined) {
              return false
            }
            const done = await store([...saved.items.filter((item) => item.name !== name), { name, operation, input }])
            if (done) {
              setChosen(name)
            }
            return done
          },
          rename: async (item, name) => {
            const done = await store(
              saved.items.map((candidate) => (candidate.name === item.name ? { ...candidate, name } : candidate)),
            )
            if (done && chosen === item.name) {
              setChosen(name)
            }
            return done
          },
          overwrite: (item) =>
            input === null || operation === undefined
              ? Promise.resolve(false)
              : store(
                  saved.items.map((candidate) =>
                    candidate.name === item.name ? { ...candidate, operation, input } : candidate,
                  ),
                ),
          remove: async (item) => {
            const done = await store(saved.items.filter((candidate) => candidate.name !== item.name))
            if (done && chosen === item.name) {
              choose(null)
            }
            return done
          },
        }}
      />
    </div>
  )
}
