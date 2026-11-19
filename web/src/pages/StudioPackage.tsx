import { Flex } from 'antd'
import { useContext, useEffect, useMemo, useRef, useState } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { sortByName } from '../platform/listOrder'
import { navigationWidth, ShellNavigation } from '../platform/ShellNavigation'
import {
  fetchStudioPackageFile,
  packageManifestFile as manifestFile,
  saveStudioPackageFile,
  type StudioDiagnostic,
  type StudioPackageFile,
  type StudioPackageFileItem,
} from '../platform/studio'
import { studioPackageAddress } from '../platform/studioAddress'
import { QuotaRefusedError } from '../platform/quota'
import { useText } from '../platform/texts'
import CodeEditor, { type CodeEditorHandle } from './CodeEditor'
import { codeLanguage } from './codeLanguage'
import { PlatformContext } from './context'
import { SaveBar } from './SaveBar'
import { StudioPageHeading } from './StudioPageHeading'

/** What the heading reads from a package's manifest. */
interface PackageManifest {
  label?: string
  version?: string
  language?: string
  adapter?: string
}

function readManifest(content: string | undefined): PackageManifest {
  try {
    const parsed: unknown = content === undefined ? {} : JSON.parse(content)
    return parsed !== null && typeof parsed === 'object' ? (parsed as PackageManifest) : {}
  } catch {
    return {}
  }
}

type FileState = { file: string; loaded: StudioPackageFile } | { file: string; failed: true }

/**
 * Proposed: an open package, its files by path beside the code editor of the open one, see #3978 and
 * docs/reference/studio-packages.md#a-packages-files. The file list is the shell's navigation menu, so the open file
 * is marked as an open sidebar entry is. Without the developer role the editor is read-only and no save bar shows.
 */
export default function StudioPackage({
  application,
  packageId,
  file: requestedFile,
  files,
  developer,
  busy,
  onSaved,
}: {
  application: string
  packageId: string
  /** The file the address opens, or `null` to open the adapter, else the manifest. */
  file: string | null
  /** The package's files. */
  files: readonly StudioPackageFileItem[]
  developer: boolean
  /** Studio is reconnecting, so nothing is saved. */
  busy: boolean
  onSaved: (diagnostics: StudioDiagnostic[]) => void
}) {
  const t = useText()
  const [manifestText, setManifestText] = useState<string>()
  const [state, setState] = useState<FileState>()
  const [text, setText] = useState('')
  const [generation, setGeneration] = useState(0)
  const [steps, setSteps] = useState({ undo: 0, redo: 0 })
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [conflict, setConflict] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [reload, setReload] = useState(0)
  const editor = useRef<CodeEditorHandle>(null)

  const manifest = readManifest(manifestText)
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  // The files are a set, so they sort by path, see docs/reference/actions-and-controls.md#list-order.
  const paths = useMemo(
    () =>
      sortByName(
        files.map((item) => item.path),
        (path) => path,
        locale,
      ),
    [files, locale],
  )
  const file =
    requestedFile ??
    (manifest.adapter !== undefined && paths.includes(manifest.adapter) ? manifest.adapter : manifestFile)
  const known = paths.includes(file)

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioPackageFile(application, packageId, manifestFile, controller.signal).then(
      (loaded) => setManifestText(loaded.content),
      () => {
        if (!controller.signal.aborted) {
          setManifestText(undefined)
        }
      },
    )
    return () => controller.abort()
  }, [application, packageId])

  useEffect(() => {
    if (!known) {
      return
    }
    const controller = new AbortController()
    fetchStudioPackageFile(application, packageId, file, controller.signal).then(
      (loaded) => {
        setState({ file, loaded })
        setText(loaded.content)
        setGeneration((value) => value + 1)
        setSteps({ undo: 0, redo: 0 })
        setConflict(false)
        setSaved(false)
        setSaveError(null)
      },
      () => {
        if (!controller.signal.aborted) {
          setState({ file, failed: true })
        }
      },
    )
    return () => controller.abort()
  }, [application, packageId, file, known, reload])

  const current = state?.file === file ? state : undefined
  const loaded = current && 'loaded' in current ? current.loaded : undefined
  const unsaved = loaded !== undefined && text !== loaded.content

  async function save() {
    if (!loaded) {
      return
    }
    setSaving(true)
    setSaveError(null)
    try {
      const result = await saveStudioPackageFile(application, packageId, file, loaded.version, text)
      if (result.status === 'conflict') {
        setConflict(true)
        return
      }
      setState({ file, loaded: { ...loaded, version: result.version, content: text } })
      if (file === manifestFile) {
        setManifestText(text)
      }
      setSaved(true)
      onSaved(result.diagnostics)
    } catch (error) {
      setSaveError(error instanceof QuotaRefusedError ? error.message : t('shell.studio.package.saveFailed'))
    } finally {
      setSaving(false)
    }
  }

  function discard() {
    if (loaded) {
      setText(loaded.content)
      // A new editor starts with no steps, so nothing undoes back into the discarded change.
      setGeneration((value) => value + 1)
      setSteps({ undo: 0, redo: 0 })
    }
  }

  const languageName = manifest.language ?? 'csharp'
  const line =
    manifest.version === undefined
      ? null
      : t('shell.studio.package.line')
          .replace('{language}', t(`shell.studio.package.language.${languageName}`))
          .replace('{version}', manifest.version)

  return (
    <Flex vertical gap="middle" data-testid="studio-package">
      <StudioPageHeading title={manifest.label ?? packageId} line={line} />
      {!developer && (
        <AxisAlert data-testid="studio-package-read-only" kind="info" message={t('shell.studio.package.readOnly')} />
      )}
      <Flex gap="middle" align="start">
        <nav aria-label={t('shell.studio.package.files')} style={{ width: navigationWidth, flex: 'none' }}>
          <ShellNavigation
            entries={paths.map((path) => ({
              key: path,
              path: studioPackageAddress(application, packageId, path),
              label: path,
            }))}
            activeKey={file}
          />
        </nav>
        <Flex vertical gap="small" style={{ flex: 1, minWidth: 0 }}>
          {!known ? (
            <AxisAlert kind="warning" message={t('shell.studio.package.notFound')} />
          ) : current && 'failed' in current ? (
            <AxisAlert kind="error" message={t('shell.studio.package.loadFailed')} />
          ) : (
            loaded && (
              <>
                {conflict && (
                  <AxisAlert
                    data-testid="studio-conflict"
                    kind="warning"
                    message={t('shell.studio.conflict')}
                    action={
                      <AxisButton role="secondary" onClick={() => setReload((value) => value + 1)}>
                        {t('shell.studio.reload')}
                      </AxisButton>
                    }
                  />
                )}
                {saveError !== null && <AxisAlert data-testid="studio-save-error" kind="error" message={saveError} />}
                <CodeEditor
                  key={`${file}-${generation}`}
                  ref={editor}
                  label={file}
                  value={text}
                  onChange={(next) => {
                    setText(next)
                    setSaved(false)
                  }}
                  language={codeLanguage(file)}
                  readOnly={!developer}
                  findTexts={{
                    field: t('shell.studio.package.findField'),
                    count: t('shell.studio.json.findCount'),
                    none: t('shell.studio.json.findNone'),
                  }}
                  onHistory={setSteps}
                />
                {saved && <span data-testid="studio-saved">{t('shell.studio.saved')}</span>}
              </>
            )
          )}
        </Flex>
      </Flex>
      {developer && (
        <SaveBar
          unsaved={unsaved}
          undoable={steps.undo > 0}
          redoable={steps.redo > 0}
          saveable={unsaved && !conflict && !saving && !busy}
          busy={saving}
          onUndo={() => editor.current?.undo()}
          onRedo={() => editor.current?.redo()}
          onDiscard={discard}
          onSave={save}
        />
      )}
    </Flex>
  )
}
