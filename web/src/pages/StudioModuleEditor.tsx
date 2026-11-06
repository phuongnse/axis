import { Alert, Button, Flex, Modal, Tag } from 'antd'
import { lazy, Suspense, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { AxisActionRow, AxisButton } from '../platform/AxisButton'
import { diffPatch, escapePointerSegment, getAt, setAt } from '../platform/jsonPatch'
import {
  dataSourceKeyedLists,
  entityKeyedLists,
  formKeyedLists,
  layerMarks,
  pageListKey,
  policyKeyedLists,
  processKeyedLists,
  seedKeyedLists,
  textKeyedMaps,
  type KeyedLists,
} from '../platform/layerMarks'
import { QuotaRefusedError } from '../platform/quota'
import { seedColumns, seedFieldsOf, seedProblems, type SeedField } from '../platform/seedValues'
import {
  deleteStudioResource,
  fetchStudioModuleResource,
  resetStudioModuleElement,
  saveStudioModuleResource,
  ServerUnreachableError,
  type ModuleResourceItem,
  type StudioDiagnostic,
  type StudioModuleLayer,
  type StudioModuleResource,
  type StudioResourceItem,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { StudioNamesContext } from '../platform/studioNames'
import { isObject } from '../platform/scenarioSteps'
import { loadKindSchema, type JsonSchema } from '../platform/studioSchemas'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { EntityDesigner } from './EntityDesigner'
import type { DesignerLayer } from './LayerMarkViews'
import { SchemaForm } from './SchemaForm'
import { SeedGrid } from './SeedGrid'
import { StudioModulePolicy } from './StudioModulePolicy'
import { TextTable } from './TextTable'
import { triggerSchema } from './triggerSchema'

const LazyStudioProcessDesign = lazy(() => import('./StudioProcessDesign'))
const LazyStudioPageDesign = lazy(() => import('./StudioPageDesign'))
const LazyStudioFormDesign = lazy(() => import('./StudioFormDesign'))

type Content = Record<string, unknown>

const noDiagnosticPaths: readonly string[] = []
const noProblems = new Map<string, string>()

/** The kinds whose editor is the schema form, or the process designer, and so waits for the kind's schema. */
const schemaKinds: readonly string[] = ['process', 'dataSource', 'rule', 'trigger', 'sequence', 'event', 'seed']

/** The root properties of a module resource that show as text: its identity, and its name, which the save route refuses to change. */
const moduleReadOnlyNames = new Set(['id', 'kind', 'name', 'formatVersion'])

/** The fields of the entity a seed names, or `null` when they could not be read. */
interface SeedEntity {
  id: string
  fields: SeedField[] | null
}

/** What the editor loaded: the module resource with the layer applied, and the layer when there is one. */
interface Loaded {
  content: Content
  layer: StudioModuleLayer | null
  marks: StudioModuleResource['marks']
  removed: StudioModuleResource['removed']
}

/**
 * An editor of a module resource, for every kind but `scenario` and `contract`. The resource
 * opens with the application's layer applied and every input on. Save sends the whole resource to the module save
 * route, which writes only the difference as the layer. When the resource has a layer, a Customized tag and Reset all
 * show, which delete the layer file, and the entity designer, the process designer, the policy grants, the text table,
 * the form designer's outline or the page designer marks the elements the layer added, changed or removed, each with
 * Reset to module, which undoes that one element on the server. The page designer draws the saved page on its canvas,
 * so the canvas changes after a save or a reset. A data source, a rule, a trigger, a sequence and an event open in the
 * schema form, and a seed in the seed grid with a form of its other properties. The data source's `fields`,
 * `parameters` and `aggregate.groupBy` and the seed's `records` are marked like the other keyed lists, and the other
 * kinds show only the Customized tag. `name` shows as text, because the save route refuses a renamed module
 * resource. There is no JSON view, no Delete and no migration questions. `onWritten` reports the folder's
 * diagnostics after a save, a Reset all or a Reset to module, with the layer the write added to the folder's list or the
 * id of the layer it removed.
 */
export function StudioModuleEditor({
  application,
  item,
  onWritten,
}: {
  application: string
  item: ModuleResourceItem
  onWritten: (diagnostics: StudioDiagnostic[], layer: { added?: StudioResourceItem; removedId?: string }) => void
}) {
  const t = useText()
  const { size } = useDesignTokens()
  const { generation, reconnecting, lost } = useStudioConnection()
  const [loaded, setLoaded] = useState<Loaded>()
  const loadedRef = useRef<Loaded>(undefined)
  const [draft, setDraft] = useState<Content>({})
  const [loadFailed, setLoadFailed] = useState(false)
  const [conflict, setConflict] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [resetOpen, setResetOpen] = useState(false)
  const [resetting, setResetting] = useState(false)
  const [schema, setSchema] = useState<JsonSchema>()
  const [selected, setSelected] = useState<number | null>(null)
  const [pageSelected, setPageSelected] = useState('/widgets/0')
  const [formPath, setFormPath] = useState<string | null>(null)
  const names = useContext(StudioNamesContext)
  const [seedEntity, setSeedEntity] = useState<SeedEntity>()

  /** Takes a read as what was loaded. Unless `force`, a draft with unsaved edits stays. */
  function adopt(resource: StudioModuleResource, force: boolean) {
    const previous = loadedRef.current
    const next = {
      content: resource.content,
      layer: resource.layer,
      marks: resource.marks,
      removed: resource.removed,
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
    fetchStudioModuleResource(application, item.module, item.id, controller.signal).then(
      (resource) => adopt(resource, false),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the module resource '${item.id}' of '${item.module}' failed`, error)
        setLoadFailed(true)
      },
    )
    return () => controller.abort()
  }, [application, item.module, item.id, generation, lost])

  useEffect(() => {
    if (!schemaKinds.includes(item.kind)) {
      return
    }
    let current = true
    loadKindSchema(item.kind).then(
      (loadedSchema) => current && setSchema(loadedSchema),
      (error: unknown) => {
        console.warn(`Loading the ${item.kind} schema failed`, error)
        if (current) {
          setLoadFailed(true)
        }
      },
    )
    return () => {
      current = false
    }
  }, [item.kind])

  // The entity of a seed is found by name among the entities of the seed's module, ignoring letter case.
  const entityName =
    item.kind === 'seed' && typeof draft.entity === 'string'
      ? (draft.entity.includes('/') ? draft.entity : `${item.module}/${draft.entity}`).toLowerCase()
      : undefined
  const entityId = entityName
    ? names?.entities.find((entity) => entity.module === item.module && entity.name.toLowerCase() === entityName)?.id
    : undefined

  useEffect(() => {
    if (entityId === undefined) {
      return
    }
    const controller = new AbortController()
    fetchStudioModuleResource(application, item.module, entityId, controller.signal).then(
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
  }, [application, item.module, entityId, generation, lost])

  const seedFields = entityId !== undefined && seedEntity?.id === entityId ? seedEntity.fields : null
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

  if (loadFailed) {
    return <Alert data-testid="studio-resource-load-error" type="error" title={t('shell.studio.loadResourceFailed')} />
  }
  if (
    !loaded ||
    (schemaKinds.includes(item.kind) && !schema) ||
    (entityId !== undefined && seedEntity?.id !== entityId)
  ) {
    return null
  }

  const patch = diffPatch(loaded.content, draft)

  /** Sets the draft's value at the pointer as it is. A seed cell that picks `false` must keep it. */
  function update(pointer: string, value: unknown) {
    setDraft((current) => setAt(current, pointer, value) as Content)
    setSaved(false)
    setSaveError(null)
  }

  function change(pointer: string, next: unknown) {
    // Unticking a box that the file never had leaves the key out, so the undo of a tick is no change.
    // An array item always keeps its value, because a missing item would shift the others.
    const parent = getAt(loaded!.content, pointer.slice(0, pointer.lastIndexOf('/')))
    const value =
      next === false && !Array.isArray(parent) && getAt(loaded!.content, pointer) === undefined ? undefined : next
    update(pointer, value)
  }

  /** Reads the resource again and takes it, so the draft is what the server holds. */
  async function reload() {
    try {
      const resource = await fetchStudioModuleResource(application, item.module, item.id)
      adopt(resource, true)
      setConflict(false)
      setSaveError(null)
      return resource
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
        return
      }
      console.warn(`Loading the module resource '${item.id}' of '${item.module}' failed`, error)
      setLoadFailed(true)
    }
  }

  async function save() {
    setSaving(true)
    setSaveError(null)
    try {
      const before = loaded!.layer
      const result = await saveStudioModuleResource(application, item.module, item.id, before?.version ?? null, draft)
      if (result.status === 'conflict') {
        setConflict(true)
        return
      }
      if (result.status === 'refused') {
        setSaveError(result.title !== '' ? result.title : t('shell.studio.saveFailed'))
        return
      }
      // The save answer has no layer id, which a later Reset all needs, so the resource is read again.
      const resource = await reload()
      if (!resource) {
        return
      }
      setSaved(true)
      if (result.file === null) {
        onWritten(result.diagnostics, before ? { removedId: before.id } : {})
      } else if (before === null && resource.layer !== null) {
        onWritten(result.diagnostics, {
          added: {
            id: resource.layer.id,
            kind: 'layer',
            name: item.name,
            file: result.file,
            version: result.version ?? resource.layer.version,
            usedBy: 0,
          },
        })
      } else {
        onWritten(result.diagnostics, {})
      }
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Saving the module resource '${item.id}' failed`, error)
        setSaveError(error instanceof QuotaRefusedError ? error.message : t('shell.studio.saveFailed'))
      }
    } finally {
      setSaving(false)
    }
  }

  /** Deletes the layer file, so the designer shows the module's version again. */
  async function resetAll() {
    const layer = loaded!.layer
    if (layer === null) {
      return
    }
    setResetting(true)
    setSaveError(null)
    try {
      const result = await deleteStudioResource(application, layer.id, layer.version)
      setResetOpen(false)
      if (result.status === 'conflict') {
        setConflict(true)
        return
      }
      setSaved(false)
      if (await reload()) {
        onWritten(result.diagnostics, { removedId: layer.id })
      }
    } catch (error) {
      setResetOpen(false)
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Resetting the module resource '${item.id}' failed`, error)
        setSaveError(t('shell.studio.saveFailed'))
      }
    } finally {
      setResetting(false)
    }
  }

  /** Undoes the layer's change to one element on the server, keeps the rest of the layer and reads the resource again. */
  async function resetElement(list: string, key: string) {
    const layer = loaded!.layer
    if (layer === null) {
      return
    }
    setResetting(true)
    setSaveError(null)
    try {
      const result = await resetStudioModuleElement(application, item.module, item.id, layer.version, list, key)
      if (result.status === 'conflict') {
        setConflict(true)
        return
      }
      if (result.status === 'refused') {
        setSaveError(result.title !== '' ? result.title : t('shell.studio.saveFailed'))
        return
      }
      setSaved(false)
      if (await reload()) {
        onWritten(result.diagnostics, result.file === null ? { removedId: layer.id } : {})
      }
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Resetting an element of the module resource '${item.id}' failed`, error)
        setSaveError(error instanceof QuotaRefusedError ? error.message : t('shell.studio.saveFailed'))
      }
    } finally {
      setResetting(false)
    }
  }

  /** What the designer needs to mark the layer's elements, or nothing when the resource has no layer. */
  function designerLayer(keyedLists: KeyedLists, maps: readonly string[] = []): DesignerLayer | undefined {
    return loaded!.layer === null
      ? undefined
      : {
          elements: layerMarks(loaded!.content, loaded!.marks, loaded!.removed, keyedLists, maps),
          disabled: patch.length > 0 || saving || resetting || reconnecting || conflict,
          onReset: (list, key) => void resetElement(list, key),
        }
  }

  return (
    <Flex vertical gap="middle">
      {loaded.layer !== null && (
        <Flex gap="small" align="center">
          <Tag data-testid="studio-module-layer">{t('shell.studio.module.layer')}</Tag>
          <AxisButton
            role="destructive"
            data-testid="studio-module-reset-all"
            disabled={saving || resetting || reconnecting}
            onClick={() => setResetOpen(true)}
          >
            {t('shell.studio.module.resetAll')}
          </AxisButton>
        </Flex>
      )}
      {item.kind === 'process' && schema ? (
        <Suspense fallback={null}>
          <LazyStudioProcessDesign
            schema={schema}
            draft={draft}
            diagnosticPaths={noDiagnosticPaths}
            selected={selected}
            onSelect={setSelected}
            onChange={change}
            layer={designerLayer(processKeyedLists)}
          />
        </Suspense>
      ) : item.kind === 'page' ? (
        <Suspense fallback={null}>
          <LazyStudioPageDesign
            application={application}
            module={item.module}
            resourceId={item.id}
            draft={draft}
            selected={pageSelected}
            onSelect={setPageSelected}
            onChange={change}
            layer={designerLayer(pageListKey)}
          />
        </Suspense>
      ) : item.kind === 'text' ? (
        isObject(draft.texts) && (
          <TextTable
            columns={[{ id: item.id, locale: String(draft.locale ?? ''), texts: draft.texts, editable: true }]}
            onChange={change}
            onAdd={async (key, texts) => {
              change(`/texts/${escapePointerSegment(key)}`, texts[String(draft.locale ?? '')] ?? '')
              return 'added'
            }}
            layer={designerLayer({}, textKeyedMaps)}
          />
        )
      ) : item.kind === 'form' ? (
        <Suspense fallback={null}>
          <LazyStudioFormDesign
            application={application}
            module={item.module}
            resourceId={item.id}
            draft={draft}
            selected={formPath}
            onSelect={setFormPath}
            onChange={change}
            layer={designerLayer(formKeyedLists(loaded.content))}
          />
        </Suspense>
      ) : item.kind === 'seed' && schema ? (
        seedSchema ? (
          <Flex vertical gap="middle">
            <SchemaForm schema={seedSchema} value={draft} onChange={change} readOnlyNames={moduleReadOnlyNames} />
            <SeedGrid
              fields={seedFields!}
              records={draft.records}
              problems={problems}
              onChange={update}
              layer={designerLayer(seedKeyedLists)}
            />
          </Flex>
        ) : (
          <SchemaForm schema={schema} value={draft} onChange={change} readOnlyNames={moduleReadOnlyNames} />
        )
      ) : item.kind === 'dataSource' && schema ? (
        <SchemaForm
          schema={schema}
          value={draft}
          onChange={change}
          readOnlyNames={moduleReadOnlyNames}
          layer={designerLayer(dataSourceKeyedLists)}
          keyedLists={dataSourceKeyedLists}
        />
      ) : (item.kind === 'rule' || item.kind === 'sequence' || item.kind === 'event') && schema ? (
        <SchemaForm schema={schema} value={draft} onChange={change} readOnlyNames={moduleReadOnlyNames} />
      ) : item.kind === 'trigger' && schema ? (
        <SchemaForm
          schema={triggerSchema(schema, draft)}
          value={draft}
          onChange={change}
          readOnlyNames={moduleReadOnlyNames}
        />
      ) : item.kind === 'policy' ? (
        <StudioModulePolicy value={draft} onChange={change} layer={designerLayer(policyKeyedLists)} />
      ) : (
        <EntityDesigner value={draft} onChange={change} layer={designerLayer(entityKeyedLists)} />
      )}
      {conflict && (
        <Alert
          data-testid="studio-conflict"
          type="warning"
          title={t('shell.studio.conflict')}
          action={<Button onClick={() => void reload()}>{t('shell.studio.reload')}</Button>}
        />
      )}
      {saveError !== null && <Alert data-testid="studio-save-error" type="error" title={saveError} />}
      <AxisActionRow align="center">
        <AxisButton
          role="finish"
          data-testid="studio-save"
          disabled={patch.length === 0 || problems.size > 0 || conflict || saving || resetting || reconnecting}
          onClick={() => void save()}
        >
          {t('shell.studio.save')}
        </AxisButton>
        {saved && <span data-testid="studio-saved">{t('shell.studio.saved')}</span>}
      </AxisActionRow>
      <Modal
        open={resetOpen}
        width={size.dialog.small}
        title={t('shell.studio.module.resetAllConfirm')}
        okText={t('shell.studio.module.resetAll')}
        okButtonProps={{ danger: true }}
        cancelText={t('shell.studio.cancel')}
        confirmLoading={resetting}
        onOk={() => void resetAll()}
        onCancel={() => setResetOpen(false)}
      />
    </Flex>
  )
}
