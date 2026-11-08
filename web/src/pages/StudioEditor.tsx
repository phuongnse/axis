import { AlignLeftOutlined, CopyOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, Button, ConfigProvider, Flex, message, Modal, Tabs } from 'antd'
import {
  lazy,
  Suspense,
  useCallback,
  useContext,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
  type ReactNode,
  type Ref,
} from 'react'
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
import { AxisActionRow, AxisButton } from '../platform/AxisButton'
import { QuotaRefusedError } from '../platform/quota'
import { seedColumns, seedFieldsOf, seedProblems, type SeedField } from '../platform/seedValues'
import {
  deleteStudioResource,
  fetchResourceReferences,
  fetchStudioBindings,
  fetchStudioCatalog,
  fetchStudioRelaxed,
  fetchStudioResource,
  patchStudioResource,
  putStudioTexts,
  saveStudioChanges,
  ServerUnreachableError,
  type StudioBindings,
  type StudioCatalog,
  type StudioDiagnostic,
  type StudioFolderRelaxedPoint,
  type StudioResourceItem,
  writeStudioText,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { StudioBindingsContext, StudioCatalogContext, StudioRelaxedContext } from '../platform/studioCatalog'
import { StudioNamesContext, textsOf } from '../platform/studioNames'
import { loadKindSchema, type JsonSchema } from '../platform/studioSchemas'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { ConnectorEditor } from './ConnectorEditor'
import { EndpointEvent } from './EndpointEvent'
import { EndpointValues } from './EndpointValues'
import { FormSteps } from './FormSteps'
import { EntityDesigner } from './EntityDesigner'
import { IdentityProviderEditor } from './IdentityProviderEditor'
import {
  emptyHistory,
  endStep,
  recordStep,
  redoStep,
  removedItem,
  undoStep,
  type DraftSnapshot,
  type LabelTextDrafts,
} from './draftHistory'
import { EntityMigrationQuestions } from './EntityMigrationQuestions'
import { ExpressionCheckContext } from './expressionCheckContext'
import type { JsonViewHandle } from './JsonView'
import { LabelTextsContext } from './labelTextsContext'
import { PageEditor } from './PageEditor'
import { ProcessEditor } from './ProcessEditor'
import { ScenarioEditor } from './ScenarioEditor'
import { SaveBar } from './SaveBar'
import { SchemaForm } from './SchemaForm'
import { SeedGrid } from './SeedGrid'
import { SiteSignIn } from './SiteSignIn'
import { StudioAutoAccept } from './StudioAutoAccept'
import type { FormUse, OpenResource } from './StudioFormUsedOn'
import { StudioUsedBy } from './StudioUsedBy'
import { TextTable, type TextColumn } from './TextTable'
import { triggerSchema } from './triggerSchema'

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

const LazyStudioProcessDesign = lazy(() => import('./StudioProcessDesign'))
const LazyStudioPageDesign = lazy(() => import('./StudioPageDesign'))
const LazyStudioFormDesign = lazy(() => import('./StudioFormDesign'))
const LazyJsonView = lazy(() => import('./JsonView'))

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** The draft a text parses to, or `null` when it is not valid JSON. */
function tryParse(text: string): unknown {
  try {
    return JSON.parse(text)
  } catch {
    return null
  }
}

/** Every non-empty `textKey` that a label of `value` names, at any depth. */
function textKeysOf(value: unknown, keys = new Set<string>()): Set<string> {
  if (Array.isArray(value)) {
    value.forEach((item) => textKeysOf(item, keys))
  } else if (isContent(value)) {
    for (const [name, child] of Object.entries(value)) {
      if (name === 'textKey' && typeof child === 'string') {
        if (child !== '') {
          keys.add(child)
        }
      } else {
        textKeysOf(child, keys)
      }
    }
  }
  return keys
}

/** The step key of an edit made while a text box has focus, so typing in one box is one step. Else `null`. */
function typingStep(key: string): string | null {
  const active = document.activeElement
  if (!(active instanceof HTMLElement)) {
    return null
  }
  const text =
    active instanceof HTMLTextAreaElement ||
    (active instanceof HTMLInputElement &&
      !['checkbox', 'radio', 'button', 'submit'].includes(active.type) &&
      active.getAttribute('role') !== 'combobox') ||
    active.closest('[contenteditable="true"]') !== null
  return text ? key : null
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

function Drawn({ onDrawn, children }: { onDrawn: (drawn: boolean) => void; children: ReactNode }) {
  useEffect(() => {
    onDrawn(true)
  }, [onDrawn])
  return children
}

/** What the page calls on the editor: open the delete dialog, see studio.md#delete. */
export interface StudioEditorHandle {
  askDelete(): void
}

/**
 * The editor of one resource. A form built from the kind's JSON Schema and a JSON view edit one draft. Save sends
 * only the difference from what was loaded, as a JSON Patch with the loaded version. A `409` keeps the draft and
 * offers Reload, which reads the file again and drops the draft. The page heading's "…" menu opens Delete through
 * `StudioEditorHandle`, which asks first and warns with the number of references to the resource, with a link to the
 * Used by tab, see studio.md#delete. The application manifest has no Delete. When `path` is set, the editor opens the
 * form at the field that holds it and scrolls to that field once the resource has loaded. A path the form cannot
 * show opens the JSON view instead. A seed whose entity is a resource of the folder shows its records as a grid with
 * a column per field, below the form of its other properties, and Save is off while a value would fail seeding. When
 * the entity is not in the folder, the seed uses the generic form. A scenario shows a typed editor of its name, users,
 * stubs and steps, and the JSON view edits the same draft. A connector opens in the connector editor, which shows a
 * binding as a name only. An identity provider opens in the identity provider editor, which shows the fields of its type
 * and keeps a key it does not show, and a site's `signIn` is a list of rows that each pick one of the application's
 * identity providers, beside the generic form of the site's other properties. A page opens in the page editor, which shows its widgets as a tree with add, move and remove
 * and a panel for the selected widget, and keeps a key it does not show. A process opens in the process editor, which shows its
 * steps as cards, with the decision, end and task panels and a picker of the draft's step names for every transition.
 * A page also has a Design tab, which draws the draft with the site's widgets over placeholder data, beside a palette
 * of the ten widget types and the same widget tree and panel as the Form tab. The two tabs share the selected widget, which is not written to the URL.
 * A label's texts are part of the draft: typing them is an unsaved change that Undo steps through, and Save writes each
 * changed key through the text write route before the patch. A text write that fails shows the save error and sends no
 * patch.
 * Every kind has Undo and Redo on the save bar, which shows while there is something to save or a step can be redone,
 * and as Ctrl+Z and Ctrl+Shift+Z (Cmd on macOS), for up to 100 steps of its draft on all its tabs. Typing in one box is one
 * step, and a load of the file clears the history. JSON that does not parse is an unsaved change with Save off, and
 * while it has an error the Form and Design tabs are read-only and the editor ignores every edit from them. Discard
 * asks first and puts back the loaded resource. A removed row asks nothing and shows a notice with Undo.
 * A form's Design tab shows a Used on list above its canvas, with the pages and process steps that use the form, and an entry calls `onOpen`.
 * Every resource but the application has a Used by tab, which lists the resources that use it and loads only when it opens.
 * Beside the canvas it has a palette of the entity's fields that no section lists and an outline of the draft's steps, sections and fields.
 * Its code loads when the tab first opens.
 * A page, a form or a process opens on its Design tab, and every other kind opens on Form. A path switches to the Form tab, or to JSON when the form cannot show it. A process's Design tab draws its steps and transitions as a graph, laid out again after every edit,
 * beside the card of the selected step. A click on a node or an edge selects a step, which is not written to the URL, and a path into a step selects it too.
 * A step that a diagnostic path names is marked on the graph. An entity opens in the entity designer, which shows its header, a table of its fields with the
 * settings of each field's type, and its validations. An endpoint keeps the generic form and shows its `values` as rows
 * of a field of its entity and an expression, and its event as a picker with the event's `fields` as rows. When the server does not answer, the draft stays, and the resource loads
 * again after the reconnect unless the draft has changes. When an entity's loaded field was renamed or removed, Save asks one question per field first, and the answers
 * add a `renameField` or `dropField` migration that is saved with the entity in one changes call. The application manifest's
 * page also shows the auto-accept switch in a panel of its own below the editor, which saves on its own, apart from
 * the manifest. It is editable when `publisher` is true and disabled otherwise.
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
  onOpen,
  publisher = false,
  ref,
}: {
  application: string
  item: StudioResourceItem
  resources: StudioResourceItem[]
  path: string | null
  diagnosticPaths: readonly string[]
  onOpen: OpenResource
  onSaved: (diagnostics: StudioDiagnostic[], created?: StudioResourceItem[]) => void
  onTextsWritten: (diagnostics: StudioDiagnostic[]) => void
  onDeleted: (diagnostics: StudioDiagnostic[]) => void
  publisher?: boolean
  ref?: Ref<StudioEditorHandle>
}) {
  const t = useText()
  const { size, colour } = useDesignTokens()
  const names = useContext(StudioNamesContext) ?? null
  const { generation, reconnecting, lost } = useStudioConnection()
  const rootRef = useRef<HTMLDivElement>(null)
  const jsonViewRef = useRef<JsonViewHandle>(null)
  const [messageApi, messageHolder] = message.useMessage()
  const dirtyRef = useRef(false)
  const [loaded, setLoaded] = useState<Loaded>()
  const [schema, setSchema] = useState<JsonSchema>()
  const [loadFailed, setLoadFailed] = useState(false)
  const [draft, setDraft] = useState<Content>({})
  // The text of the JSON view while the author types in it. `null` shows the draft, formatted.
  const [jsonText, setJsonText] = useState<string | null>(null)
  const [jsonInvalid, setJsonInvalid] = useState(false)
  const shownJson = jsonText ?? JSON.stringify(draft, null, 2)
  // The problems that the JSON view marks, syntax errors and schema breaks.
  const [jsonProblems, setJsonProblems] = useState(0)
  const [conflict, setConflict] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [deleteFailed, setDeleteFailed] = useState(false)
  useImperativeHandle(ref, () => ({ askDelete: () => setDeleteOpen(true) }), [])
  const [tab, setTab] = useState<'design' | 'form' | 'json' | 'usedBy'>(
    item.kind === 'page' || item.kind === 'form' || item.kind === 'process' ? 'design' : 'form',
  )
  const [formDrawn, setFormDrawn] = useState(false)
  const [seenPath, setSeenPath] = useState<string | null>(null)
  // The step that the Design tab of a process shows. It is not written to the URL, which would switch to Form.
  const [processStep, setProcessStep] = useState<number | null>(null)
  if (path !== seenPath) {
    setSeenPath(path)
    if (path !== null) {
      setTab('form')
      const step = item.kind === 'process' ? /^\/steps\/(\d+)(\/|$)/.exec(path) : null
      if (step) {
        setProcessStep(Number(step[1]))
      }
    }
  }
  const [widgetPath, setWidgetPath] = useState('/widgets/0')
  const [formPath, setFormPath] = useState<string | null>(null)
  const [seedEntity, setSeedEntity] = useState<SeedEntity>()
  // The other locales of a text resource, read from their files, for the text table to show beside the open one.
  const [otherTexts, setOtherTexts] = useState<TextColumn[]>()
  // The loaded name of each row of an entity's fields, and the loaded names the author edited or removed, in order.
  const [origins, setOrigins] = useState<FieldOrigin[]>([])
  const [touched, setTouched] = useState<string[]>([])
  const [questions, setQuestions] = useState<FieldQuestion[]>()
  // The pages and process steps that use a form, or `failed` when their references did not load.
  const [usedOn, setUsedOn] = useState<FormUse[] | 'failed'>()
  const [history, setHistory] = useState(emptyHistory)
  // The label texts of the draft by key, which Save writes, and the texts this editor wrote, which stand in for the
  // names until they load again.
  const [labelTexts, setLabelTexts] = useState<LabelTextDrafts>({})
  const [writtenTexts, setWrittenTexts] = useState<LabelTextDrafts>({})
  const [seenNames, setSeenNames] = useState(names)
  if (names !== seenNames) {
    setSeenNames(names)
    setWrittenTexts({})
  }
  const handledPath = useRef<string | null>(null)
  // A field to scroll to once the form tab is shown, because a hidden field cannot be scrolled to.
  const pendingField = useRef<HTMLElement | null>(null)

  function adopt(resource: Loaded) {
    setLoaded({ content: resource.content, version: resource.version })
    setDraft(resource.content)
    setOrigins(loadedOrigins(resource.content))
    setTouched([])
    setJsonText(null)
    setJsonInvalid(false)
    setConflict(false)
    setSaved(false)
    setSaveError(null)
    setHistory(emptyHistory)
    setLabelTexts({})
  }

  /** Steps through the history. It restores the snapshot, and it does nothing while a save runs. */
  function step(move: typeof undoStep) {
    const current: DraftSnapshot = { draft, jsonText, texts: labelTexts }
    const result = saving ? null : move(history, current)
    if (!result) {
      return
    }
    const { history: next, snapshot } = result
    setHistory(next)
    setDraft(snapshot.draft)
    setJsonText(snapshot.jsonText)
    setLabelTexts(snapshot.texts)
    setJsonInvalid(snapshot.jsonText !== null && !isContent(tryParse(snapshot.jsonText)))
    if (item.kind === 'entity') {
      setOrigins(matchOrigins(loaded!.content, snapshot.draft))
    }
    setSaved(false)
    setSaveError(null)
    messageApi.destroy('studio-removed')
  }

  const undo = () => step(undoStep)
  const redo = () => step(redoStep)
  const latest = useRef({ undo, redo, changeTexts })
  const onTextsChange = useCallback(
    (pointer: string, key: string, texts: Record<string, string>) => latest.current.changeTexts(pointer, key, texts),
    [],
  )

  useEffect(() => {
    latest.current = { undo, redo, changeTexts }
  })

  // The settings and a trigger's time zone choose from what the server has installed. Until it loads, or if it
  // fails to, their fields keep their text inputs.
  const [catalog, setCatalog] = useState<StudioCatalog>()
  const choosesFromCatalog = item.kind === 'application' || item.kind === 'trigger'
  useEffect(() => {
    if (!choosesFromCatalog) {
      return
    }
    const controller = new AbortController()
    fetchStudioCatalog(controller.signal).then(setCatalog, (error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the Studio catalog failed', error)
      }
    })
    return () => controller.abort()
  }, [choosesFromCatalog, generation])

  // The settings, a webhook and a connector choose their bindings from the tenant's names, with the same fallback.
  const [bindings, setBindings] = useState<StudioBindings>()
  const choosesBindings = item.kind === 'application' || item.kind === 'webhook' || item.kind === 'connector'
  useEffect(() => {
    if (!choosesBindings) {
      return
    }
    const controller = new AbortController()
    fetchStudioBindings(controller.signal).then(setBindings, (error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the Studio bindings failed', error)
      }
    })
    return () => controller.abort()
  }, [choosesBindings, generation])

  // The settings table shows the relaxed points of the saved file, so it asks again after each save.
  const [relaxed, setRelaxed] = useState<StudioFolderRelaxedPoint[]>()
  const showsRelaxed = item.kind === 'application'
  const loadedVersion = loaded?.version
  useEffect(() => {
    if (!showsRelaxed) {
      return
    }
    const controller = new AbortController()
    fetchStudioRelaxed(application, controller.signal).then(setRelaxed, (error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the relaxed points failed', error)
      }
    })
    return () => controller.abort()
  }, [showsRelaxed, application, generation, loadedVersion])
  const relaxedContext = useMemo(
    () => (relaxed === undefined ? undefined : { application, points: relaxed }),
    [application, relaxed],
  )

  // Studio takes the undo keys before the browser's own undo and CodeMirror's. They work inside the editor and when
  // nothing has focus, which is where a mouse drop leaves it.
  useEffect(() => {
    const inside = (target: EventTarget | null) =>
      target === document.body || (target instanceof Node && rootRef.current?.contains(target) === true)
    const onKeyDown = (event: KeyboardEvent) => {
      if (
        !(event.ctrlKey || event.metaKey) ||
        event.altKey ||
        event.key.toLowerCase() !== 'z' ||
        !inside(event.target)
      ) {
        return
      }
      event.preventDefault()
      event.stopPropagation()
      if (event.shiftKey) {
        latest.current.redo()
      } else {
        latest.current.undo()
      }
    }
    const onBeforeInput = (event: InputEvent) => {
      if ((event.inputType !== 'historyUndo' && event.inputType !== 'historyRedo') || !inside(event.target)) {
        return
      }
      event.preventDefault()
      if (event.inputType === 'historyRedo') {
        latest.current.redo()
      } else {
        latest.current.undo()
      }
    }
    document.addEventListener('keydown', onKeyDown, true)
    document.addEventListener('beforeinput', onBeforeInput, true)
    return () => {
      document.removeEventListener('keydown', onKeyDown, true)
      document.removeEventListener('beforeinput', onBeforeInput, true)
    }
  }, [])

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

  useEffect(() => {
    if (item.kind !== 'form') {
      return
    }
    const controller = new AbortController()
    fetchResourceReferences(application, item.id, controller.signal).then(
      ({ usedBy }) =>
        setUsedOn(
          usedBy.filter(
            (entry): entry is FormUse =>
              (entry.relation === 'widget form' || entry.relation === 'task form') && entry.node.type === 'resource',
          ),
        ),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the references of '${item.id}' failed`, error)
        setUsedOn('failed')
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
      texts: { ...writtenTexts, ...labelTexts },
      onTextsChange,
    }),
    [application, item.file, item.name, loaded?.content, writtenTexts, labelTexts, onTextsChange],
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
    const { values: _values, event: _event, ...properties } = schema.properties ?? {}
    return { ...schema, properties, required: schema.required?.filter((name) => name !== 'values') }
  }, [item.kind, schema])

  const siteSchema = useMemo(() => {
    if (item.kind !== 'site' || !schema) {
      return undefined
    }
    const { signIn: _signIn, ...properties } = schema.properties ?? {}
    return { ...schema, properties, required: schema.required?.filter((name) => name !== 'signIn') }
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
  // The keys that the draft names whose texts differ from the last known texts: the ones this editor wrote, else the names.
  const changedTexts = useMemo(() => {
    if (!names) {
      return []
    }
    return [...textKeysOf(draft)].flatMap((key) => {
      if (!(key in labelTexts)) {
        return []
      }
      const texts = Object.fromEntries(names.locales.map((locale) => [locale, labelTexts[key][locale] ?? '']))
      const known = writtenTexts[key] ?? textsOf(names, key)
      return names.locales.some((locale) => texts[locale] !== (known[locale] ?? '')) ? [{ key, texts }] : []
    })
  }, [names, draft, labelTexts, writtenTexts])
  const unsaved = patch.length > 0 || changedTexts.length > 0 || jsonInvalid

  useEffect(() => {
    dirtyRef.current = unsaved
  }, [unsaved])

  useEffect(() => {
    if (!path) {
      handledPath.current = null
      return
    }
    if (!isLoaded || !formReady || !rootRef.current || handledPath.current === path) {
      return
    }
    // Ant Design draws a tab's content after the tab becomes active, so the field is looked up once Form has drawn.
    if (tab !== 'form' || !formDrawn) {
      return
    }
    handledPath.current = path
    const field = fieldElement(rootRef.current, path)
    if (field === undefined || isJsonOnlyField(field)) {
      setTab('json')
      return
    }
    pendingField.current = field
  }, [isLoaded, formReady, path, tab, formDrawn])

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
    if (jsonInvalid) {
      return
    }
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
    if (jsonInvalid) {
      return
    }
    const removed = removedItem(draft, pointer, value)
    messageApi.destroy('studio-removed')
    setHistory((h) => recordStep(h, { draft, jsonText, texts: labelTexts }, removed ? null : typingStep(pointer)))
    setDraft((current) => setAt(current, pointer, value) as Content)
    setJsonText(null)
    setJsonInvalid(false)
    setSaved(false)
    setSaveError(null)
    if (removed) {
      const name = isContent(removed.item) && typeof removed.item.name === 'string' ? removed.item.name : null
      void messageApi.open({
        key: 'studio-removed',
        duration: 10,
        content: (
          <>
            {name === null ? t('shell.studio.removedRow') : t('shell.studio.removedNamed').replace('{name}', name)}{' '}
            <AxisButton
              role="plain"
              aria-label={t('shell.studio.undoRemoval')}
              onClick={() => {
                messageApi.destroy('studio-removed')
                latest.current.undo()
              }}
            >
              {t('shell.studio.undo')}
            </AxisButton>
          </>
        ),
      })
    }
  }

  /** Sets the texts of a label's key in the draft, as typing in the label's boxes does, so it joins the typing step. */
  function changeTexts(pointer: string, key: string, texts: Record<string, string>) {
    if (jsonInvalid) {
      return
    }
    setHistory((h) => recordStep(h, { draft, jsonText, texts: labelTexts }, typingStep(pointer)))
    setLabelTexts((current) => ({ ...current, [key]: texts }))
    setSaved(false)
    setSaveError(null)
  }

  function changeJson(text: string) {
    messageApi.destroy('studio-removed')
    setHistory((h) => recordStep(h, { draft, jsonText, texts: labelTexts }, typingStep('#json')))
    setJsonText(text)
    setSaved(false)
    setSaveError(null)
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

  /** Lays the JSON out on two-space indents as an edit of the view, so it takes the path of typing. */
  function format() {
    jsonViewRef.current?.replace(JSON.stringify(JSON.parse(shownJson), null, 2))
  }

  async function copy() {
    try {
      await navigator.clipboard.writeText(shownJson)
      void messageApi.success(t('shell.studio.json.copied'))
    } catch (error) {
      console.warn('Copying the JSON failed', error)
      void messageApi.error(t('shell.studio.json.copyFailed'))
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
   * Writes `final` as the entity or resource. Each changed label key's texts go first, one after another, and the first
   * failure shows the save error and sends no patch. With migration `changes`, the patch and the new migration go in
   * one changes call. When nothing differs from what was loaded, no request is sent.
   */
  async function write(final: Content, changes: MigrationChange[], finalOrigins?: FieldOrigin[]) {
    const operations = diffPatch(loaded!.content, final)
    if (changes.length === 0 && operations.length === 0 && changedTexts.length === 0) {
      setDraft(final)
      setOrigins(finalOrigins ?? loadedOrigins(final))
      setTouched([])
      setJsonText(null)
      return
    }
    setSaving(true)
    setSaveError(null)
    try {
      for (const { key, texts } of changedTexts) {
        const result = await putStudioTexts(application, key, texts)
        setWrittenTexts((current) => ({ ...current, [key]: texts }))
        onTextsWritten(result.diagnostics)
      }
      if (changes.length === 0 && operations.length === 0) {
        setSaved(true)
        return
      }
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
        setSaveError(error instanceof QuotaRefusedError ? error.message : t('shell.studio.saveFailed'))
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
      messageApi.destroy('studio-removed')
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

  /** While the JSON has an error, a tab's content is read-only under a warning, see studio-resource-form.md#form-and-json. */
  function editable(children: ReactNode): ReactNode {
    if (!jsonInvalid) {
      return children
    }
    return (
      <>
        <Alert
          data-testid="studio-form-read-only"
          type="warning"
          title={t('shell.studio.jsonError.readOnly')}
          action={
            <AxisButton role="secondary" onClick={() => setTab('json')}>
              {t('shell.studio.goToJson')}
            </AxisButton>
          }
        />
        <ConfigProvider componentDisabled>
          <div inert>{children}</div>
        </ConfigProvider>
      </>
    )
  }

  return (
    <StudioCatalogContext.Provider value={catalog}>
      <StudioRelaxedContext.Provider value={relaxedContext}>
        <StudioBindingsContext.Provider value={bindings}>
          <Flex vertical gap="small" data-testid="studio-editor" ref={rootRef} onBlur={() => setHistory(endStep)}>
            {messageHolder}
            <ExpressionCheckContext.Provider value={checkContext}>
              <LabelTextsContext.Provider value={labelContext}>
                <Tabs
                  activeKey={tab}
                  onChange={(key) => setTab(key as 'design' | 'form' | 'json' | 'usedBy')}
                  tabBarExtraContent={
                    tab === 'json' ? (
                      <AxisActionRow align="center">
                        {jsonProblems > 0 && (
                          <AxisButton
                            role="plain"
                            style={{ color: colour.dangerText }}
                            onClick={() => jsonViewRef.current?.nextProblem()}
                          >
                            {jsonProblems === 1
                              ? t('shell.studio.jsonView.problem')
                              : t('shell.studio.jsonView.problems').replace('{count}', String(jsonProblems))}
                          </AxisButton>
                        )}
                        <AxisButton
                          role="icon"
                          nameKey="shell.studio.json.find"
                          icon={<SearchOutlined />}
                          onClick={() => jsonViewRef.current?.openFind()}
                        />
                        <AxisButton
                          role="icon"
                          nameKey="shell.studio.json.format"
                          icon={<AlignLeftOutlined />}
                          disabled={jsonInvalid}
                          onClick={format}
                        />
                        <AxisButton
                          role="icon"
                          nameKey="shell.studio.json.copy"
                          icon={<CopyOutlined />}
                          onClick={copy}
                        />
                      </AxisActionRow>
                    ) : undefined
                  }
                  items={[
                    // The canvas is mounted only while its tab is active, so it posts again and sets the placeholder
                    // guard on return, and clears the guard when the author leaves. Its code loads on first use.
                    ...(item.kind === 'page' && schema
                      ? [
                          {
                            key: 'design',
                            label: t('shell.studio.design'),
                            children:
                              tab === 'design'
                                ? editable(
                                    <Suspense fallback={null}>
                                      <LazyStudioPageDesign
                                        application={application}
                                        resourceId={item.id}
                                        draft={draft}
                                        selected={widgetPath}
                                        onSelect={setWidgetPath}
                                        onChange={change}
                                      />
                                    </Suspense>,
                                  )
                                : null,
                          },
                        ]
                      : item.kind === 'form' && schema
                        ? [
                            {
                              key: 'design',
                              label: t('shell.studio.design'),
                              children:
                                tab === 'design'
                                  ? editable(
                                      <Suspense fallback={null}>
                                        <LazyStudioFormDesign
                                          application={application}
                                          resourceId={item.id}
                                          draft={draft}
                                          selected={formPath}
                                          onSelect={setFormPath}
                                          onChange={change}
                                          usedOn={usedOn}
                                          onOpen={onOpen}
                                        />
                                      </Suspense>,
                                    )
                                  : null,
                            },
                          ]
                        : item.kind === 'process' && schema
                          ? [
                              {
                                key: 'design',
                                label: t('shell.studio.design'),
                                children:
                                  tab === 'design'
                                    ? editable(
                                        <Suspense fallback={null}>
                                          <LazyStudioProcessDesign
                                            schema={schema}
                                            draft={draft}
                                            diagnosticPaths={diagnosticPaths}
                                            selected={processStep}
                                            onSelect={setProcessStep}
                                            onChange={change}
                                          />
                                        </Suspense>,
                                      )
                                    : null,
                              },
                            ]
                          : []),
                    {
                      key: 'form',
                      label: t('shell.studio.form'),
                      children: (
                        <Drawn onDrawn={setFormDrawn}>
                          {editable(
                            seedSchema ? (
                              <Flex vertical gap="middle">
                                <SchemaForm schema={seedSchema} value={draft} onChange={change} />
                                <SeedGrid
                                  fields={seedFields!}
                                  records={draft.records}
                                  problems={problems}
                                  onChange={update}
                                />
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
                            ) : item.kind === 'identityProvider' && schema ? (
                              <IdentityProviderEditor value={draft} onChange={change} />
                            ) : processSchema ? (
                              <ProcessEditor schema={processSchema} value={draft} onChange={change} />
                            ) : item.kind === 'page' && schema ? (
                              <PageEditor
                                value={draft}
                                onChange={change}
                                selected={widgetPath}
                                onSelect={setWidgetPath}
                              />
                            ) : endpointSchema ? (
                              <Flex vertical gap="middle">
                                <SchemaForm schema={endpointSchema} value={draft} onChange={change} />
                                <EndpointValues fields={endpointFields} values={draft.values} onChange={change} />
                                <EndpointEvent events={names?.events} value={draft.event} onChange={change} />
                              </Flex>
                            ) : siteSchema ? (
                              <Flex vertical gap="middle">
                                <SchemaForm schema={siteSchema} value={draft} onChange={change} />
                                <SiteSignIn
                                  providers={resources
                                    .filter((resource) => resource.kind === 'identityProvider' && resource.name)
                                    .map((resource) => resource.name!)}
                                  value={draft.signIn}
                                  onChange={change}
                                />
                              </Flex>
                            ) : formSchema ? (
                              <Flex vertical gap="middle">
                                <SchemaForm schema={formSchema} value={draft} onChange={change} />
                                <FormSteps steps={draft.steps} sections={draft.sections} onChange={change} />
                              </Flex>
                            ) : schema ? (
                              <SchemaForm
                                schema={item.kind === 'trigger' ? triggerSchema(schema, draft) : schema}
                                value={draft}
                                onChange={change}
                              />
                            ) : (
                              <span data-testid="studio-json-only">{t('shell.studio.editInJson')}</span>
                            ),
                          )}
                        </Drawn>
                      ),
                    },
                    {
                      key: 'json',
                      label: t('shell.studio.json'),
                      children: (
                        <Suspense fallback={null}>
                          <LazyJsonView
                            ref={jsonViewRef}
                            label={t('shell.studio.json')}
                            value={shownJson}
                            onChange={changeJson}
                            boundary={rootRef}
                            kind={item.kind}
                            onProblems={setJsonProblems}
                            findTexts={{
                              field: t('shell.studio.json.findField'),
                              count: t('shell.studio.json.findCount'),
                              none: t('shell.studio.json.findNone'),
                            }}
                          />
                        </Suspense>
                      ),
                    },
                    ...(item.kind === 'application'
                      ? []
                      : [
                          {
                            key: 'usedBy',
                            label: t('shell.studio.usedBy'),
                            children:
                              tab === 'usedBy' ? (
                                <StudioUsedBy application={application} resourceId={item.id} resources={resources} />
                              ) : null,
                          },
                        ]),
                  ]}
                />
              </LabelTextsContext.Provider>
            </ExpressionCheckContext.Provider>
            {conflict && (
              <Alert
                data-testid="studio-conflict"
                type="warning"
                title={t('shell.studio.conflict')}
                // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
                action={<Button onClick={reload}>{t('shell.studio.reload')}</Button>}
              />
            )}
            {saveError !== null && <Alert data-testid="studio-save-error" type="error" title={saveError} />}
            {deleteFailed && (
              <Alert data-testid="studio-delete-error" type="error" title={t('shell.studio.deleteFailed')} />
            )}
            <AxisActionRow align="center">
              {saved && <span data-testid="studio-saved">{t('shell.studio.saved')}</span>}
            </AxisActionRow>
            {item.kind === 'application' && <StudioAutoAccept application={application} publisher={publisher} />}
            {questions && (
              <EntityMigrationQuestions
                questions={questions}
                onDone={(answers) => answered(questions, answers)}
                onCancel={() => setQuestions(undefined)}
              />
            )}
            <Modal
              open={deleteOpen}
              width={size.dialog.small}
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
                  action={
                    <AxisButton
                      role="secondary"
                      onClick={() => {
                        setDeleteOpen(false)
                        setTab('usedBy')
                      }}
                    >
                      {t('shell.studio.deleteSeeUses')}
                    </AxisButton>
                  }
                />
              )}
            </Modal>
            <SaveBar
              unsaved={unsaved}
              undoable={history.past.length > 0}
              redoable={history.future.length > 0}
              saveable={
                (patch.length > 0 || changedTexts.length > 0) &&
                !jsonInvalid &&
                problems.size === 0 &&
                !conflict &&
                !saving &&
                !reconnecting
              }
              busy={saving}
              onUndo={undo}
              onRedo={redo}
              onDiscard={() => {
                adopt(loaded)
                messageApi.destroy('studio-removed')
              }}
              onSave={save}
            />
          </Flex>
        </StudioBindingsContext.Provider>
      </StudioRelaxedContext.Provider>
    </StudioCatalogContext.Provider>
  )
}
