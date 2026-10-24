import { Alert, Button, Flex, Input, Modal, Tabs } from 'antd'
import { lazy, Suspense, useContext, useEffect, useMemo, useRef, useState } from 'react'
import {
  applyAnswers,
  fieldQuestions,
  fieldWarnings,
  loadedOrigins,
  matchOrigins,
  migrationFile,
  migrationResource,
  type FieldAnswer,
  type FieldOrigin,
  type FieldQuestion,
  type MigrationChange,
} from '../platform/entityMigrations'
import { diffPatch, escapePointerSegment, getAt, setAt } from '../platform/jsonPatch'
import { seedColumns, seedProblems, type SeedField } from '../platform/seedValues'
import {
  deleteStudioResource,
  fetchStudioResource,
  patchStudioResource,
  saveStudioChanges,
  ServerUnreachableError,
  type StudioDiagnostic,
  type StudioResource,
  type StudioResourceItem,
  writeStudioText,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { StudioNamesContext } from '../platform/studioNames'
import { loadKindSchema, type JsonSchema } from '../platform/studioSchemas'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { ConnectorEditor } from './ConnectorEditor'
import { EndpointValues } from './EndpointValues'
import { FormSteps } from './FormSteps'
import { EntityDesigner } from './EntityDesigner'
import { EntityMigrationQuestions } from './EntityMigrationQuestions'
import { ExpressionCheckContext } from './expressionCheckContext'
import { LabelTextsContext } from './labelTextsContext'
import { PageEditor, PagePalette, PageWidgets } from './PageEditor'
import { ProcessEditor } from './ProcessEditor'
import { ScenarioEditor } from './ScenarioEditor'
import { SchemaForm } from './SchemaForm'
import { SeedGrid } from './SeedGrid'
import { StudioFormCanvas } from './StudioFormCanvas'
import { StudioPageCanvas } from './StudioPageCanvas'
import { TextTable, type TextColumn } from './TextTable'

type Content = Record<string, unknown>

/** What the editor loaded: the file's content and the version a save must send back. */
interface Loaded {
  content: Content
  version: string
}

/** The fields of the entity a seed names, or `null` when they could not be read. */
interface SeedEntity {
  id: string
  fields: SeedField[] | null
}

const noProblems = new Map<string, string>()

const LazyStudioProcessGraph = lazy(() => import('./StudioProcessGraph'))

function seedFieldsOf(content: Content): SeedField[] | null {
  if (!Array.isArray(content.fields)) {
    return null
  }
  return content.fields.filter(
    (field): field is SeedField => isContent(field) && typeof field.name === 'string' && typeof field.type === 'string',
  )
}

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** The form field of the longest prefix of `path` that the form shows, so a path inside a field lands on that field. */
function fieldElement(root: HTMLElement, path: string): HTMLElement | undefined {
  const fields = new Map<string, HTMLElement>()
  for (const element of root.querySelectorAll<HTMLElement>('[data-testid^="studio-field-"]')) {
    fields.set(element.getAttribute('data-testid')!.slice('studio-field-'.length), element)
  }
  let prefix = path
  while (prefix !== '') {
    const element = fields.get(prefix)
    if (element) {
      return element
    }
    prefix = prefix.slice(0, Math.max(prefix.lastIndexOf('/'), 0))
  }
  return undefined
}

/** Whether the form points to the JSON view for this field, as it does for a `oneOf`, a map or an unknown node. */
function isJsonOnlyField(field: HTMLElement): boolean {
  return field.querySelector(':scope > [data-testid="studio-json-only"]') !== null
}

/**
 * The editor of one resource. A form built from the kind's JSON Schema and a JSON view edit one draft. Save sends
 * only the difference from what was loaded, as a JSON Patch with the loaded version. A `409` keeps the draft and
 * offers Reload, which reads the file again and drops the draft. Delete asks first and warns with the number of
 * references to the resource, and it is off for the application manifest. When `path` is set, the editor opens the
 * form at the field that holds it and scrolls to that field once the resource has loaded. A path the form cannot
 * show opens the JSON view instead. A seed whose entity is a resource of the folder shows its records as a grid with
 * a column per field, below the form of its other properties, and Save is off while a value would fail seeding. When
 * the entity is not in the folder, the seed uses the generic form. A scenario shows a typed editor of its name, users,
 * stubs and steps, and the JSON view edits the same draft. A connector opens in the connector editor, which shows a
 * binding as a name only. A page opens in the page editor, which shows its widgets as a tree with add, move and remove
 * and a panel for the selected widget, and keeps a key it does not show. A process opens in the process editor, which shows its
 * steps as cards, with the decision, end and task panels and a picker of the draft's step names for every transition.
 * A page also has a Design tab, which draws the draft with the site's widgets over placeholder data, beside a palette
 * of the ten widget types and the same widget tree and panel as the Form tab. The two tabs share the selected widget, which is not written to the URL.
 * Form stays the tab that opens first. A process also shows its steps and transitions as a read-only graph above the tabs, and a click on a node or an edge
 * calls `onOpenPath`. A step that a diagnostic path names is marked on the graph. An entity opens in the entity designer, which shows its header, a table of its fields with the
 * settings of each field's type, and its validations. An endpoint keeps the generic form and shows its `values` as rows
 * of a field of its entity and an expression. When the server does not answer, the draft stays, and the resource loads
 * again after the reconnect unless the draft has changes. When an entity's loaded field was renamed or removed, Save asks one question per field first, and the answers
 * add a `renameField` or `dropField` migration that is saved with the entity in one changes call.
 */
export function StudioEditor({
  application,
  item,
  resources,
  path,
  diagnosticPaths,
  onSaved,
  onTextsWritten,
  onDeleted,
  onOpenPath,
}: {
  application: string
  item: StudioResourceItem
  resources: StudioResourceItem[]
  path: string | null
  diagnosticPaths: readonly string[]
  onOpenPath: (path: string) => void
  onSaved: (diagnostics: StudioDiagnostic[], created?: StudioResourceItem[]) => void
  onTextsWritten: (diagnostics: StudioDiagnostic[]) => void
  onDeleted: (diagnostics: StudioDiagnostic[]) => void
}) {
  const t = useText()
  const { type } = useDesignTokens()
  const names = useContext(StudioNamesContext) ?? null
  const { generation, reconnecting, lost } = useStudioConnection()
  const rootRef = useRef<HTMLDivElement>(null)
  const dirtyRef = useRef(false)
  const [loaded, setLoaded] = useState<Loaded>()
  const [schema, setSchema] = useState<JsonSchema>()
  const [loadFailed, setLoadFailed] = useState(false)
  const [draft, setDraft] = useState<Content>({})
  // The text of the JSON view while the author types in it. `null` shows the draft, formatted.
  const [jsonText, setJsonText] = useState<string | null>(null)
  const [jsonInvalid, setJsonInvalid] = useState(false)
  const [conflict, setConflict] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [saveFailed, setSaveFailed] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [deleteFailed, setDeleteFailed] = useState(false)
  const [tab, setTab] = useState<'design' | 'form' | 'json'>('form')
  const [widgetPath, setWidgetPath] = useState('/widgets/0')
  const [seedEntity, setSeedEntity] = useState<SeedEntity>()
  // The other locales of a text resource, read from their files, for the text table to show beside the open one.
  const [otherTexts, setOtherTexts] = useState<TextColumn[]>()
  // The loaded name of each row of an entity's fields, and the loaded names the author edited or removed, in order.
  const [origins, setOrigins] = useState<FieldOrigin[]>([])
  const [touched, setTouched] = useState<string[]>([])
  const [questions, setQuestions] = useState<FieldQuestion[]>()
  const handledPath = useRef<string | null>(null)
  // A field to scroll to once the form tab is shown, because a hidden field cannot be scrolled to.
  const pendingField = useRef<HTMLElement | null>(null)

  function adopt(resource: StudioResource) {
    setLoaded({ content: resource.content, version: resource.version })
    setDraft(resource.content)
    setOrigins(loadedOrigins(resource.content))
    setTouched([])
    setJsonText(null)
    setJsonInvalid(false)
    setConflict(false)
    setSaved(false)
    setSaveFailed(false)
  }

  useEffect(() => {
    const controller = new AbortController()
    Promise.all([fetchStudioResource(application, item.id, controller.signal), loadKindSchema(item.kind)]).then(
      ([resource, kindSchema]) => {
        setSchema(kindSchema)
        setLoadFailed(false)
        if (!dirtyRef.current) {
          adopt(resource)
        }
      },
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the resource '${item.id}' failed`, error)
        setLoadFailed(true)
      },
    )
    return () => controller.abort()
  }, [application, item.id, item.kind, generation, lost])

  // The entity of a seed is found by name among the folder's entities, ignoring letter case, as the compiler does.
  const entityName = item.kind === 'seed' && typeof draft.entity === 'string' ? draft.entity.toLowerCase() : undefined
  const entityId = entityName
    ? resources.find((resource) => resource.kind === 'entity' && resource.name?.toLowerCase() === entityName)?.id
    : undefined

  useEffect(() => {
    if (entityId === undefined) {
      return
    }
    const controller = new AbortController()
    fetchStudioResource(application, entityId, controller.signal).then(
      (entity) => setSeedEntity({ id: entityId, fields: seedFieldsOf(entity.content) }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the entity '${entityId}' of the seed failed`, error)
        setSeedEntity({ id: entityId, fields: null })
      },
    )
    return () => controller.abort()
  }, [application, entityId, generation, lost])

  const textIds = resources
    .filter((resource) => resource.kind === 'text' && resource.id !== item.id)
    .map((resource) => resource.id)
    .join(',')

  useEffect(() => {
    if (item.kind !== 'text') {
      return
    }
    const controller = new AbortController()
    Promise.all(
      (textIds === '' ? [] : textIds.split(',')).map((id) => fetchStudioResource(application, id, controller.signal)),
    ).then(
      (others) =>
        setOtherTexts(
          others.map((other) => ({
            id: other.id,
            locale: String(other.content.locale),
            texts: isContent(other.content.texts) ? other.content.texts : {},
            editable: false,
          })),
        ),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the other texts of '${item.id}' failed`, error)
        setOtherTexts([])
      },
    )
    return () => controller.abort()
  }, [application, item.id, item.kind, textIds, generation, lost])

  const seedFields = entityId !== undefined && seedEntity?.id === entityId ? seedEntity.fields : null
  const labelContext = useMemo(
    () => ({
      application,
      file: item.file,
      name: item.name,
      saved: loaded?.content ?? {},
      onTextsWritten,
    }),
    [application, item.file, item.name, loaded?.content, onTextsWritten],
  )
  const checkContext = useMemo(
    () => ({ application, resourceId: item.id, kind: item.kind, draft, names: names ?? null }),
    [application, item.id, item.kind, draft, names],
  )
  // The form is final once the seed's entity has loaded, so a path into a record finds its cell.
  const formReady =
    (entityId === undefined || seedEntity?.id === entityId) && (item.kind !== 'text' || otherTexts !== undefined)
  const problems = useMemo(
    () => (seedFields ? seedProblems(draft.records, seedColumns(seedFields)) : noProblems),
    [seedFields, draft.records],
  )
  const seedSchema = useMemo(() => {
    if (!schema || !seedFields) {
      return undefined
    }
    const { records: _records, ...properties } = schema.properties ?? {}
    return { ...schema, properties, required: schema.required?.filter((name) => name !== 'records') }
  }, [schema, seedFields])

  const processSchema = useMemo(() => {
    if (item.kind !== 'process' || !schema) {
      return undefined
    }
    const { start: _start, steps: _steps, ...properties } = schema.properties ?? {}
    return { ...schema, properties, required: schema.required?.filter((name) => name !== 'start' && name !== 'steps') }
  }, [item.kind, schema])

  const endpointSchema = useMemo(() => {
    if (item.kind !== 'endpoint' || !schema) {
      return undefined
    }
    const { values: _values, ...properties } = schema.properties ?? {}
    return { ...schema, properties, required: schema.required?.filter((name) => name !== 'values') }
  }, [item.kind, schema])

  const formSchema = useMemo(() => {
    if (item.kind !== 'form' || !schema) {
      return undefined
    }
    const { steps: _steps, ...properties } = schema.properties ?? {}
    return { ...schema, properties, required: schema.required?.filter((name) => name !== 'steps') }
  }, [item.kind, schema])
  const endpointEntity = item.kind === 'endpoint' ? String(draft.entity).toLowerCase() : undefined
  const endpointFields = names?.entities
    .find((entity) => entity.name.toLowerCase() === endpointEntity)
    ?.fields.map((field) => field.name)

  const patch = useMemo(() => (loaded ? diffPatch(loaded.content, draft) : []), [loaded, draft])
  const isLoaded = loaded !== undefined

  useEffect(() => {
    dirtyRef.current = patch.length > 0 || jsonInvalid
  }, [patch, jsonInvalid])

  useEffect(() => {
    if (!path) {
      handledPath.current = null
      return
    }
    if (!isLoaded || !formReady || !rootRef.current || handledPath.current === path) {
      return
    }
    handledPath.current = path
    const field = fieldElement(rootRef.current, path)
    if (field === undefined || isJsonOnlyField(field)) {
      setTab('json')
      return
    }
    pendingField.current = field
    setTab('form')
  }, [isLoaded, formReady, path])

  useEffect(() => {
    if (tab === 'form' && pendingField.current) {
      pendingField.current.scrollIntoView?.({ block: 'center' })
      pendingField.current = null
    }
  })

  if (loadFailed) {
    return <Alert data-testid="studio-resource-load-error" type="error" title={t('shell.studio.loadResourceFailed')} />
  }
  if (!loaded) {
    return null
  }

  function change(pointer: string, next: unknown) {
    const edited = /^\/fields\/(\d+)(\/|$)/.exec(pointer)
    const origin = edited ? origins[Number(edited[1])] : null
    if (origin) {
      touch(origin)
    }
    // Unticking a box that the file never had leaves the key out, so the undo of a tick is no change.
    // An array item always keeps its value, because a missing item would shift the others.
    const parent = getAt(loaded!.content, pointer.slice(0, pointer.lastIndexOf('/')))
    update(
      pointer,
      next === false && !Array.isArray(parent) && getAt(loaded!.content, pointer) === undefined ? undefined : next,
    )
  }

  function touch(origin: string) {
    setTouched((current) => (current.includes(origin) ? current : [...current, origin]))
  }

  /** Sets the draft's value at the pointer as it is. A seed cell that picks `false` must keep it. */
  function update(pointer: string, value: unknown) {
    setDraft((current) => setAt(current, pointer, value) as Content)
    setJsonText(null)
    setJsonInvalid(false)
    setSaved(false)
    setSaveFailed(false)
  }

  function changeJson(text: string) {
    setJsonText(text)
    setSaved(false)
    setSaveFailed(false)
    let parsed: unknown
    try {
      parsed = JSON.parse(text)
    } catch {
      setJsonInvalid(true)
      return
    }
    if (isContent(parsed)) {
      setDraft(parsed)
      setOrigins(matchOrigins(loaded!.content, parsed))
      setJsonInvalid(false)
    } else {
      setJsonInvalid(true)
    }
  }

  /** Asks about the changed fields of an entity first, and writes when there are none. */
  async function save() {
    if (item.kind === 'entity') {
      const asked = fieldQuestions(loaded!.content, draft, origins, touched)
      if (asked.length > 0) {
        setQuestions(asked)
        return
      }
    }
    await write(draft, [])
  }

  function answered(asked: FieldQuestion[], answers: FieldAnswer[]) {
    setQuestions(undefined)
    const outcome = applyAnswers(loaded!.content, draft, origins, asked, answers)
    void write(outcome.draft, outcome.changes, outcome.origins)
  }

  /**
   * Writes `final` as the entity or resource. With migration `changes`, the patch and the new migration go in one
   * changes call. When nothing differs from what was loaded, no request is sent.
   */
  async function write(final: Content, changes: MigrationChange[], finalOrigins?: FieldOrigin[]) {
    const operations = diffPatch(loaded!.content, final)
    if (changes.length === 0 && operations.length === 0) {
      setDraft(final)
      setOrigins(finalOrigins ?? loadedOrigins(final))
      setTouched([])
      setJsonText(null)
      return
    }
    setSaving(true)
    setSaveFailed(false)
    try {
      let version: string
      let diagnostics: StudioDiagnostic[]
      let created: StudioResourceItem[] | undefined
      if (changes.length === 0) {
        const result = await patchStudioResource(application, item.id, loaded!.version, operations)
        if (result.status === 'conflict') {
          setConflict(true)
          return
        }
        version = result.version
        diagnostics = result.diagnostics
      } else {
        const migration = migrationResource(String(final.name), changes, new Date())
        const result = await saveStudioChanges(application, [
          { id: item.id, version: loaded!.version, patch: operations },
          { create: migration },
        ])
        if (result.status === 'conflict') {
          setConflict(true)
          return
        }
        version = result.items[0].version
        diagnostics = result.diagnostics
        created = [
          {
            id: String(migration.id),
            kind: 'migration',
            name: String(migration.name),
            file: migrationFile(String(migration.name)),
            version: result.items[1].version,
            usedBy: 0,
          },
        ]
      }
      setLoaded({ content: final, version })
      setDraft(final)
      setOrigins(loadedOrigins(final))
      setTouched([])
      setJsonText(null)
      setSaved(true)
      onSaved(diagnostics, created)
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Saving the resource '${item.id}' failed`, error)
        setSaveFailed(true)
      }
    } finally {
      setSaving(false)
    }
  }

  /** Writes a key in every locale. The open file changed, so the editor takes its new version and the key. */
  async function addTextKey(key: string, texts: Record<string, string>): Promise<'added' | 'invalid' | 'failed'> {
    try {
      const result = await writeStudioText(application, key, texts)
      if (result.status === 'invalid') {
        return 'invalid'
      }
      const pointer = `/texts/${escapePointerSegment(key)}`
      const openLocale = String(loaded!.content.locale)
      const version = result.resources.find((resource) => resource.id === item.id)?.version
      setLoaded(
        (current) =>
          current && {
            content: setAt(current.content, pointer, texts[openLocale]) as Content,
            version: version ?? current.version,
          },
      )
      setDraft((current) => setAt(current, pointer, texts[openLocale]) as Content)
      setJsonText(null)
      setOtherTexts((current) =>
        current?.map((column) => ({ ...column, texts: { ...column.texts, [key]: texts[column.locale] } })),
      )
      onTextsWritten(result.diagnostics)
      return 'added'
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Adding the text key '${key}' failed`, error)
      }
      return 'failed'
    }
  }

  async function reload() {
    try {
      adopt(await fetchStudioResource(application, item.id))
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
        return
      }
      console.warn(`Loading the resource '${item.id}' failed`, error)
      setLoadFailed(true)
    }
  }

  async function remove() {
    setDeleting(true)
    setDeleteFailed(false)
    try {
      const result = await deleteStudioResource(application, item.id, loaded!.version)
      setDeleteOpen(false)
      if (result.status === 'conflict') {
        setConflict(true)
        return
      }
      onDeleted(result.diagnostics)
    } catch (error) {
      setDeleteOpen(false)
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Deleting the resource '${item.id}' failed`, error)
        setDeleteFailed(true)
      }
    } finally {
      setDeleting(false)
    }
  }

  return (
    <Flex vertical gap="small" data-testid="studio-editor" ref={rootRef}>
      <ExpressionCheckContext.Provider value={checkContext}>
        <LabelTextsContext.Provider value={labelContext}>
          {item.kind === 'process' && (
            <Suspense fallback={null}>
              <LazyStudioProcessGraph
                draft={draft}
                path={path}
                diagnosticPaths={diagnosticPaths}
                onOpenPath={onOpenPath}
              />
            </Suspense>
          )}
          <Tabs
            activeKey={tab}
            onChange={(key) => setTab(key as 'design' | 'form' | 'json')}
            items={[
              // The canvas is mounted only while its tab is active, so it posts again and sets the placeholder
              // guard on return, and clears the guard when the author leaves.
              ...(item.kind === 'page' && schema
                ? [
                    {
                      key: 'design',
                      label: t('shell.studio.design'),
                      children:
                        tab === 'design' ? (
                          <Flex gap="middle" align="flex-start" data-testid="studio-page-design">
                            <div style={{ flex: 1, minWidth: 0 }}>
                              <StudioPageCanvas
                                application={application}
                                resourceId={item.id}
                                draft={draft}
                                selected={widgetPath}
                                onSelect={setWidgetPath}
                              />
                            </div>
                            <div data-testid="studio-design-panel" style={{ width: 400, flexShrink: 0 }}>
                              <PagePalette
                                value={draft}
                                selected={widgetPath}
                                onSelect={setWidgetPath}
                                onChange={change}
                              />
                              <PageWidgets
                                value={draft}
                                selected={widgetPath}
                                onSelect={setWidgetPath}
                                onChange={change}
                              />
                            </div>
                          </Flex>
                        ) : null,
                    },
                  ]
                : item.kind === 'form' && schema
                  ? [
                      {
                        key: 'design',
                        label: t('shell.studio.design'),
                        children:
                          tab === 'design' ? (
                            <div data-testid="studio-form-design">
                              <StudioFormCanvas application={application} resourceId={item.id} draft={draft} />
                            </div>
                          ) : null,
                      },
                    ]
                  : []),
              {
                key: 'form',
                label: t('shell.studio.form'),
                children: seedSchema ? (
                  <Flex vertical gap="middle">
                    <SchemaForm schema={seedSchema} value={draft} onChange={change} />
                    <SeedGrid fields={seedFields!} records={draft.records} problems={problems} onChange={update} />
                  </Flex>
                ) : item.kind === 'scenario' ? (
                  <ScenarioEditor value={draft} onChange={update} />
                ) : item.kind === 'text' && schema && isContent(draft.texts) && otherTexts ? (
                  <TextTable
                    columns={resources
                      .filter((resource) => resource.kind === 'text')
                      .map((resource) =>
                        resource.id === item.id
                          ? {
                              id: item.id,
                              locale: String(loaded.content.locale),
                              texts: draft.texts as Record<string, unknown>,
                              editable: true,
                            }
                          : otherTexts.find((column) => column.id === resource.id),
                      )
                      .filter((column): column is TextColumn => column !== undefined)}
                    onChange={update}
                    onAdd={addTextKey}
                  />
                ) : item.kind === 'entity' && schema ? (
                  <EntityDesigner
                    value={draft}
                    onChange={change}
                    origins={origins}
                    warnings={fieldWarnings(loaded.content, draft, origins)}
                    onOriginsChange={(next, removed) => {
                      setOrigins(next)
                      if (removed) {
                        touch(removed)
                      }
                    }}
                  />
                ) : item.kind === 'connector' && schema ? (
                  <ConnectorEditor value={draft} onChange={change} />
                ) : processSchema ? (
                  <ProcessEditor schema={processSchema} value={draft} onChange={change} />
                ) : item.kind === 'page' && schema ? (
                  <PageEditor value={draft} onChange={change} selected={widgetPath} onSelect={setWidgetPath} />
                ) : endpointSchema ? (
                  <Flex vertical gap="middle">
                    <SchemaForm schema={endpointSchema} value={draft} onChange={change} />
                    <EndpointValues fields={endpointFields} values={draft.values} onChange={change} />
                  </Flex>
                ) : formSchema ? (
                  <Flex vertical gap="middle">
                    <SchemaForm schema={formSchema} value={draft} onChange={change} />
                    <FormSteps steps={draft.steps} sections={draft.sections} onChange={change} />
                  </Flex>
                ) : schema ? (
                  <SchemaForm schema={schema} value={draft} onChange={change} />
                ) : (
                  <span data-testid="studio-json-only">{t('shell.studio.editInJson')}</span>
                ),
              },
              {
                key: 'json',
                label: t('shell.studio.json'),
                children: (
                  <Flex vertical gap="small">
                    <Input.TextArea
                      aria-label={t('shell.studio.json')}
                      autoSize={{ minRows: 10, maxRows: 40 }}
                      spellCheck={false}
                      style={{ fontFamily: type.codeFontFamily }}
                      value={jsonText ?? JSON.stringify(draft, null, 2)}
                      onChange={(event) => changeJson(event.target.value)}
                    />
                    {jsonInvalid && (
                      <Alert data-testid="studio-json-error" type="error" title={t('shell.studio.invalidJson')} />
                    )}
                  </Flex>
                ),
              },
            ]}
          />
        </LabelTextsContext.Provider>
      </ExpressionCheckContext.Provider>
      {conflict && (
        <Alert
          data-testid="studio-conflict"
          type="warning"
          title={t('shell.studio.conflict')}
          action={<Button onClick={reload}>{t('shell.studio.reload')}</Button>}
        />
      )}
      {saveFailed && <Alert data-testid="studio-save-error" type="error" title={t('shell.studio.saveFailed')} />}
      {deleteFailed && <Alert data-testid="studio-delete-error" type="error" title={t('shell.studio.deleteFailed')} />}
      <Flex gap="small" align="center">
        <Button
          type="primary"
          disabled={patch.length === 0 || jsonInvalid || problems.size > 0 || conflict || saving || reconnecting}
          onClick={save}
        >
          {t('shell.studio.save')}
        </Button>
        {item.kind !== 'application' && (
          <Button danger disabled={reconnecting} onClick={() => setDeleteOpen(true)}>
            {t('shell.studio.delete')}
          </Button>
        )}
        {saved && <span data-testid="studio-saved">{t('shell.studio.saved')}</span>}
      </Flex>
      {questions && (
        <EntityMigrationQuestions
          questions={questions}
          onDone={(answers) => answered(questions, answers)}
          onCancel={() => setQuestions(undefined)}
        />
      )}
      <Modal
        open={deleteOpen}
        title={t('shell.studio.deleteConfirm')}
        okText={t('shell.studio.delete')}
        okButtonProps={{ danger: true }}
        cancelText={t('shell.studio.cancel')}
        confirmLoading={deleting}
        onOk={remove}
        onCancel={() => setDeleteOpen(false)}
      >
        {item.usedBy > 0 && (
          <Alert
            data-testid="studio-delete-warning"
            type="warning"
            title={
              <>
                {t('shell.studio.deleteUsedBy')} <strong>{item.usedBy}</strong>
              </>
            }
          />
        )}
      </Modal>
    </Flex>
  )
}
