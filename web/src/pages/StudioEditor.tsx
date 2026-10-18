import { Alert, Button, Flex, Input, Modal, Tabs } from 'antd'
import { useEffect, useMemo, useRef, useState } from 'react'
import { diffPatch, getAt, setAt } from '../platform/jsonPatch'
import {
  deleteStudioResource,
  fetchStudioResource,
  patchStudioResource,
  ServerUnreachableError,
  type StudioDiagnostic,
  type StudioResource,
  type StudioResourceItem,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { loadKindSchema, type JsonSchema } from '../platform/studioSchemas'
import { useText } from '../platform/texts'
import { SchemaForm } from './SchemaForm'

type Content = Record<string, unknown>

/** What the editor loaded: the file's content and the version a save must send back. */
interface Loaded {
  content: Content
  version: string
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

/**
 * The editor of one resource. A form built from the kind's JSON Schema and a JSON view edit one draft. Save sends
 * only the difference from what was loaded, as a JSON Patch with the loaded version. A `409` keeps the draft and
 * offers Reload, which reads the file again and drops the draft. Delete asks first and warns with the number of
 * references to the resource, and it is off for the application manifest. When `path` is set, the editor scrolls
 * to the field that holds it once the resource has loaded. When the server does not answer, the draft stays, and
 * the resource loads again after the reconnect unless the draft has changes.
 */
export function StudioEditor({
  application,
  item,
  path,
  onSaved,
  onDeleted,
}: {
  application: string
  item: StudioResourceItem
  path: string | null
  onSaved: (diagnostics: StudioDiagnostic[]) => void
  onDeleted: (diagnostics: StudioDiagnostic[]) => void
}) {
  const t = useText()
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

  function adopt(resource: StudioResource) {
    setLoaded({ content: resource.content, version: resource.version })
    setDraft(resource.content)
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

  const patch = useMemo(() => (loaded ? diffPatch(loaded.content, draft) : []), [loaded, draft])
  const isLoaded = loaded !== undefined

  useEffect(() => {
    dirtyRef.current = patch.length > 0 || jsonInvalid
  }, [patch, jsonInvalid])

  useEffect(() => {
    if (!isLoaded || path === null || !rootRef.current) {
      return
    }
    fieldElement(rootRef.current, path)?.scrollIntoView?.({ block: 'center' })
  }, [isLoaded, path])

  if (loadFailed) {
    return (
      <Alert data-testid="studio-resource-load-error" type="error" message={t('shell.studio.loadResourceFailed')} />
    )
  }
  if (!loaded) {
    return null
  }

  function change(pointer: string, next: unknown) {
    // Unticking a box that the file never had leaves the key out, so the undo of a tick is no change.
    // An array item always keeps its value, because a missing item would shift the others.
    const parent = getAt(loaded!.content, pointer.slice(0, pointer.lastIndexOf('/')))
    const value =
      next === false && !Array.isArray(parent) && getAt(loaded!.content, pointer) === undefined ? undefined : next
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
      setJsonInvalid(false)
    } else {
      setJsonInvalid(true)
    }
  }

  async function save() {
    setSaving(true)
    setSaveFailed(false)
    try {
      const result = await patchStudioResource(application, item.id, loaded!.version, patch)
      if (result.status === 'conflict') {
        setConflict(true)
        return
      }
      setLoaded({ content: draft, version: result.version })
      setSaved(true)
      onSaved(result.diagnostics)
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
      <Tabs
        items={[
          {
            key: 'form',
            label: t('shell.studio.form'),
            children: schema ? (
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
                  style={{ fontFamily: 'monospace' }}
                  value={jsonText ?? JSON.stringify(draft, null, 2)}
                  onChange={(event) => changeJson(event.target.value)}
                />
                {jsonInvalid && (
                  <Alert data-testid="studio-json-error" type="error" message={t('shell.studio.invalidJson')} />
                )}
              </Flex>
            ),
          },
        ]}
      />
      {conflict && (
        <Alert
          data-testid="studio-conflict"
          type="warning"
          message={t('shell.studio.conflict')}
          action={<Button onClick={reload}>{t('shell.studio.reload')}</Button>}
        />
      )}
      {saveFailed && <Alert data-testid="studio-save-error" type="error" message={t('shell.studio.saveFailed')} />}
      {deleteFailed && (
        <Alert data-testid="studio-delete-error" type="error" message={t('shell.studio.deleteFailed')} />
      )}
      <Flex gap="small" align="center">
        <Button
          type="primary"
          disabled={patch.length === 0 || jsonInvalid || conflict || saving || reconnecting}
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
            message={
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
