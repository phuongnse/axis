import { DeleteOutlined, FileAddOutlined, SaveOutlined } from '@ant-design/icons'
import { Flex, Segmented, Select, Typography } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { CodeBlock } from '../platform/CodeBlock'
import { EmptyState } from '../platform/EmptyState'
import { FieldInput } from '../platform/FieldInput'
import { ReasonDialog } from '../platform/ReasonDialog'
import { SectionPart, SectionParts } from '../platform/SectionParts'
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
import { ValueTable } from '../platform/ValueTable'
import CodeEditor from './CodeEditor'
import { codeLanguage } from './codeLanguage'

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
}) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
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
  const [saveAsOpen, setSaveAsOpen] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)

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

  async function store(items: StudioPackageRunInput[]) {
    setSaveError(null)
    try {
      const result = await saveStudioPackageRunInputs(application, packageId, items, saved.version)
      if (result.status === 'conflict') {
        setSaveError(t('shell.studio.package.run.inputConflict'))
        return false
      }
      setSaved({ items, version: result.version })
      return true
    } catch {
      setSaveError(t('shell.studio.package.run.inputSaveFailed'))
      return false
    }
  }

  const input = current()
  const missing = fields.some((field) => field.required && (input?.[field.name] ?? '') === '')
  const runnable = operation !== undefined && input !== null && !missing && !running

  const runButton = (
    <AxisButton
      role="finish"
      data-testid="package-run-send"
      disabled={!runnable || !developer}
      loading={running}
      onClick={() => input !== null && operation !== undefined && onRun(operation, input)}
    >
      {t('shell.studio.package.run.run')}
    </AxisButton>
  )

  // Proposed, see #3978: what runs and Run on the first row, the saved input under it with its own small actions, the
  // input's fields under a rule, and the result beside them, headed by its outcome and times on one line, as a request
  // tool such as Postman or AWS Lambda's Test tab lays them out.
  return (
    <Flex gap="large" wrap align="start" data-testid="package-run">
      <div style={{ flex: '1 1 360px', minWidth: 0 }}>
        <SectionParts>
          <SectionPart testName="run-what">
            <Flex vertical gap={spacing.sm}>
              <Flex gap="small" align="end">
                <div style={{ flex: 1, minWidth: 0 }}>
                  <AxisField label={t('shell.studio.package.run.operation')} help="">
                    <Select
                      data-testid="package-run-operation"
                      value={operation}
                      onChange={(next: string) => setOperation(next)}
                      options={operations.map((candidate) => ({ value: candidate.name, label: candidate.name }))}
                      style={{ width: '100%' }}
                    />
                  </AxisField>
                </div>
                {runButton}
              </Flex>
              <Flex gap="small" align="end">
                <div style={{ flex: 1, minWidth: 0 }}>
                  <AxisField label={t('shell.studio.package.run.savedInput')} help="">
                    <Select
                      placeholder={t('shell.studio.package.run.newInput')}
                      value={chosen ?? undefined}
                      onChange={(next: string) => choose(next)}
                      allowClear
                      onClear={() => choose(null)}
                      options={saved.items
                        .filter((item) => item.operation === operation)
                        .map((item) => ({ value: item.name, label: item.name }))}
                      style={{ width: '100%' }}
                    />
                  </AxisField>
                </div>
                {developer && (
                  <Flex gap={spacing.xs}>
                    <AxisButton
                      role="icon"
                      nameKey="shell.studio.package.run.saveInput"
                      icon={<SaveOutlined />}
                      disabled={chosen === null || input === null}
                      onClick={() =>
                        void store(
                          saved.items.map((item) =>
                            item.name === chosen ? { ...item, operation: operation!, input: input! } : item,
                          ),
                        )
                      }
                    />
                    <AxisButton
                      role="icon"
                      nameKey="shell.studio.package.run.saveInputAs"
                      icon={<FileAddOutlined />}
                      disabled={input === null}
                      onClick={() => setSaveAsOpen(true)}
                    />
                    <AxisButton
                      role="icon"
                      nameKey="shell.studio.package.run.deleteInput"
                      destructive
                      icon={<DeleteOutlined />}
                      disabled={chosen === null}
                      onClick={() =>
                        void store(saved.items.filter((item) => item.name !== chosen)).then(
                          (done) => done && choose(null),
                        )
                      }
                    />
                  </Flex>
                )}
              </Flex>
              {saveError !== null && <AxisAlert kind="error" message={saveError} />}
            </Flex>
          </SectionPart>
          <SectionPart
            testName="run-input"
            heading={{ title: t('shell.studio.package.run.input') }}
            action={
              <Segmented<'form' | 'json'>
                aria-label={t('shell.studio.package.run.inputAs')}
                size="small"
                value={shown}
                onChange={switchTo}
                options={[
                  { value: 'form', label: t('shell.studio.package.run.form') },
                  { value: 'json', label: t('shell.value.json') },
                ]}
              />
            }
          >
            <Flex vertical gap={spacing.sm}>
              {jsonError && <AxisAlert kind="error" message={t('shell.studio.package.run.jsonInvalid')} />}
              {shown === 'form' ? (
                fields.length === 0 ? (
                  <Typography.Text type="secondary">{t('shell.studio.package.run.noInput')}</Typography.Text>
                ) : (
                  fields.map((field) => (
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
                  ))
                )
              ) : (
                <div style={{ height: 160, border: `1px solid ${colour.borderSubtle}`, borderRadius: radius.base }}>
                  <CodeEditor
                    label={t('shell.studio.package.run.inputJson')}
                    value={jsonText}
                    onChange={setJsonText}
                    language={jsonLanguage}
                    fill
                    findTexts={{
                      field: t('shell.studio.package.findField'),
                      count: t('shell.studio.json.findCount'),
                      none: t('shell.studio.json.findNone'),
                    }}
                  />
                </div>
              )}
            </Flex>
          </SectionPart>
        </SectionParts>
      </div>
      <div style={{ flex: '1 1 360px', minWidth: 0 }} data-testid="package-run-result">
        {run === null ? (
          <EmptyState text={t('shell.studio.package.run.none')} />
        ) : (
          <SectionParts>
            <SectionPart
              testName="run-result"
              heading={{
                title: (
                  <Flex gap="small" align="center" wrap>
                    {t('shell.studio.package.run.outcome')}
                    <StatusTag tone={run.outcome === 'succeeded' ? 'success' : 'danger'}>
                      {t(`shell.studio.package.run.outcome.${run.outcome}`)}
                    </StatusTag>
                    <Typography.Text type="secondary" style={{ fontWeight: 'normal', fontSize: 'inherit' }}>
                      {t('shell.studio.package.run.summary')
                        .replace(
                          '{duration}',
                          t('shell.studio.package.run.milliseconds').replace('{count}', String(run.durationMs)),
                        )
                        .replace(
                          '{cpu}',
                          t('shell.studio.package.run.seconds').replace('{count}', String(run.cpuSeconds)),
                        )}
                    </Typography.Text>
                  </Flex>
                ),
              }}
            >
              {run.error !== null ? (
                <AxisAlert kind="error" message={`${run.error.code}: ${run.error.message}`} />
              ) : (
                run.output !== null && <ValueTable value={run.output} testId="package-run-output" />
              )}
            </SectionPart>
            {output.length > 0 && (
              <SectionPart testName="run-log" heading={{ title: t('shell.studio.package.run.log') }}>
                <CodeBlock text={output.join('\n')} wrap />
              </SectionPart>
            )}
          </SectionParts>
        )}
      </div>
      {saveAsOpen && (
        <ReasonDialog
          title={t('shell.studio.package.run.saveInputAs.title')}
          label={t('shell.studio.package.run.saveInputAs.label')}
          help={t('shell.studio.package.run.saveInputAs.help')}
          okText={t('shell.studio.package.run.saveInput')}
          cancelText={t('shell.studio.cancel')}
          requiredMessage={t('shell.studio.package.run.saveInputAs.required')}
          failedText={t('shell.studio.package.run.inputSaveFailed')}
          maxLength={100}
          onClose={() => setSaveAsOpen(false)}
          onChanged={() => {}}
          onSend={async (name) => {
            const trimmed = name.trim()
            const items = [
              ...saved.items.filter((item) => item.name !== trimmed),
              { name: trimmed, operation: operation!, input: input! },
            ]
            const done = await store(items)
            if (done) {
              setChosen(trimmed)
              return { ok: true }
            }
            return {
              ok: false,
              problem: { status: 409, title: t('shell.studio.package.run.inputConflict'), errors: {} },
            }
          }}
        />
      )}
    </Flex>
  )
}
