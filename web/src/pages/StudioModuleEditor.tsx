import { Alert, Button, Flex, Modal, Tag } from 'antd'
import { useEffect, useRef, useState } from 'react'
import { AxisActionRow, AxisButton } from '../platform/AxisButton'
import { diffPatch, getAt, setAt } from '../platform/jsonPatch'
import { entityKeyedLists, layerMarks } from '../platform/layerMarks'
import { QuotaRefusedError } from '../platform/quota'
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
import { useText } from '../platform/texts'
import { EntityDesigner } from './EntityDesigner'

type Content = Record<string, unknown>

/** What the editor loaded: the module resource with the layer applied, and the layer when there is one. */
interface Loaded {
  content: Content
  layer: StudioModuleLayer | null
  marks: StudioModuleResource['marks']
  removed: StudioModuleResource['removed']
}

/**
 * An editor of a module resource, for the `entity` kind now. The resource opens with the application's layer applied
 * and every input on. Save sends the whole entity to the module save route, which writes only the difference as the
 * layer. When the resource has a layer, a Customized tag and Reset all show, which delete the layer file, and the
 * designer marks the elements the layer added, changed or removed, each with Reset to module, which undoes that one
 * element on the server. There is no JSON view, no Delete and no migration questions. `onWritten` reports the folder's
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

  if (loadFailed) {
    return <Alert data-testid="studio-resource-load-error" type="error" title={t('shell.studio.loadResourceFailed')} />
  }
  if (!loaded) {
    return null
  }

  const patch = diffPatch(loaded.content, draft)

  function change(pointer: string, next: unknown) {
    // Unticking a box that the file never had leaves the key out, so the undo of a tick is no change.
    // An array item always keeps its value, because a missing item would shift the others.
    const parent = getAt(loaded!.content, pointer.slice(0, pointer.lastIndexOf('/')))
    const value =
      next === false && !Array.isArray(parent) && getAt(loaded!.content, pointer) === undefined ? undefined : next
    setDraft((current) => setAt(current, pointer, value) as Content)
    setSaved(false)
    setSaveError(null)
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
      <EntityDesigner
        value={draft}
        onChange={change}
        layer={
          loaded.layer === null
            ? undefined
            : {
                elements: layerMarks(loaded.content, loaded.marks, loaded.removed, entityKeyedLists),
                disabled: patch.length > 0 || saving || resetting || reconnecting || conflict,
                onReset: (list, key) => void resetElement(list, key),
              }
        }
      />
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
          disabled={patch.length === 0 || conflict || saving || resetting || reconnecting}
          onClick={() => void save()}
        >
          {t('shell.studio.save')}
        </AxisButton>
        {saved && <span data-testid="studio-saved">{t('shell.studio.saved')}</span>}
      </AxisActionRow>
      <Modal
        open={resetOpen}
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
