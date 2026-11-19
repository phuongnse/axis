import { DownOutlined, UpOutlined } from '@ant-design/icons'
import { Flex, Modal, Tabs, Tree, Typography, type TreeDataNode } from 'antd'
import { useContext, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { EmptyState } from '../platform/EmptyState'
import { nameCompare } from '../platform/listOrder'
import { QuotaRefusedError } from '../platform/quota'
import { ReasonDialog } from '../platform/ReasonDialog'
import { ResizeHandle } from '../platform/ResizeHandle'
import { navigationWidth } from '../platform/ShellNavigation'
import {
  createStudioPackageFile,
  deleteStudioPackageFile,
  fetchStudioLiveDiff,
  fetchStudioPackageFile,
  packageManifestFile as manifestFile,
  saveStudioPackageFile,
  type StudioDiagnostic,
  type StudioPackageFile,
  type StudioPackageFileItem,
} from '../platform/studio'
import { studioPackageAddress } from '../platform/studioAddress'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import CodeEditor, { type CodeEditorHandle } from './CodeEditor'
import { codeLanguage } from './codeLanguage'
import { PlatformContext } from './context'
import { SaveBar } from './SaveBar'
import { StudioPageHeading } from './StudioPageHeading'
import { StudioProblemList } from './StudioProblems'
import { StudioResourceMenu } from './StudioResourceMenu'

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

/** A file the author opened: as loaded, as edited, and whether a save found it changed by someone else. */
interface OpenFile {
  loaded: StudioPackageFile
  text: string
  conflict: boolean
  /** Counts the times the file was put back, so its editor starts again with no steps. */
  generation: number
}

/** Whether the package differs from the live release, read from the live diff. */
type PackageStatus = { release: number; changed: boolean } | 'unpublished' | undefined

/**
 * The files as a tree of folders, each folder before the files beside it and each group sorted by name, see
 * docs/reference/actions-and-controls.md#list-order. A folder's key is its path with a trailing slash.
 */
function fileTree(paths: readonly string[], locale: string): TreeDataNode[] {
  interface Folder {
    folders: Map<string, Folder>
    files: string[]
  }
  const root: Folder = { folders: new Map(), files: [] }
  for (const path of paths) {
    const parts = path.split('/')
    let folder = root
    for (const part of parts.slice(0, -1)) {
      const next = folder.folders.get(part) ?? { folders: new Map(), files: [] }
      folder.folders.set(part, next)
      folder = next
    }
    folder.files.push(path)
  }
  const compare = nameCompare(locale)
  const nodes = (folder: Folder, prefix: string): TreeDataNode[] => [
    ...[...folder.folders.entries()]
      .sort(([left], [right]) => compare(left, right))
      .map(([name, child]) => ({ key: `${prefix}${name}/`, title: name, children: nodes(child, `${prefix}${name}/`) })),
    ...folder.files
      .map((path) => ({ path, name: path.slice(prefix.length) }))
      .sort((left, right) => compare(left.name, right.name))
      .map(({ path, name }) => ({ key: path, title: name, isLeaf: true })),
  ]
  return nodes(root, '')
}

const panelLeast = 120
const panelFirst = 200

/**
 * Proposed: an open package as a place to work, see #3978 and docs/reference/studio-packages.md#a-packages-files. Its
 * files are a tree of folders beside the code editor of the open one, and every file the author opens keeps its own
 * editor and steps, so several can change before one save writes them all. The output panel under the editor holds the
 * package's problems, its tests and the output of its build. Without the developer role the editor is read-only and no
 * save bar, New file… or Delete file… shows.
 */
export default function StudioPackage({
  application,
  packageId,
  file: requestedFile,
  files,
  diagnostics,
  developer,
  busy,
  onChanged,
}: {
  application: string
  packageId: string
  /** The file the address opens, or `null` to open the adapter, else the manifest. */
  file: string | null
  /** The package's files. */
  files: readonly StudioPackageFileItem[]
  /** The folder's diagnostics, of which the package's show in the output panel. */
  diagnostics: readonly StudioDiagnostic[]
  developer: boolean
  /** Studio is reconnecting, so nothing is saved. */
  busy: boolean
  /** A write changed the folder, with the diagnostics it answered. */
  onChanged: (diagnostics: StudioDiagnostic[]) => void
}) {
  const t = useText()
  const navigate = useNavigate()
  const { colour, spacing } = useDesignTokens()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const [open, setOpen] = useState<Record<string, OpenFile>>({})
  const [failed, setFailed] = useState<Record<string, true>>({})
  const [steps, setSteps] = useState<Record<string, { undo: number; redo: number }>>({})
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [status, setStatus] = useState<PackageStatus>()
  const [newFileOpen, setNewFileOpen] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [deleteError, setDeleteError] = useState(false)
  const [panelOpen, setPanelOpen] = useState(true)
  const [panelHeight, setPanelHeight] = useState(panelFirst)
  const [panelTab, setPanelTab] = useState('problems')
  const root = useRef<HTMLDivElement>(null)
  const panel = useRef<HTMLElement>(null)
  const editors = useRef(new Map<string, CodeEditorHandle | null>())

  const paths = useMemo(() => files.map((item) => item.path), [files])
  const tree = useMemo(() => fileTree(paths, locale), [paths, locale])
  const manifest = readManifest(open[manifestFile]?.text)
  const file =
    requestedFile ??
    (manifest.adapter !== undefined && paths.includes(manifest.adapter) ? manifest.adapter : manifestFile)
  const known = paths.includes(file)
  const packagePrefix = `packages/${packageId}/`
  const problems = diagnostics.filter((diagnostic) => diagnostic.file.startsWith(packagePrefix))
  const dirty = Object.entries(open)
    .filter(([, entry]) => entry.text !== entry.loaded.content)
    .map(([path]) => path)
  const conflicts = Object.entries(open)
    .filter(([, entry]) => entry.conflict)
    .map(([path]) => path)

  // The manifest is open from the start, as the heading reads it, and every file opens once, when it is first shown.
  const wanted = [...new Set([manifestFile, file])].filter((path) => paths.includes(path))
  const missing = wanted.filter((path) => open[path] === undefined && failed[path] === undefined).join('\n')
  useEffect(() => {
    if (missing === '') {
      return
    }
    const controller = new AbortController()
    for (const path of missing.split('\n')) {
      loadFile(application, packageId, path, controller.signal).then(
        (loaded) =>
          setOpen((current) => ({
            ...current,
            [path]: { loaded, text: loaded.content, conflict: false, generation: 0 },
          })),
        () => {
          if (!controller.signal.aborted) {
            setFailed((current) => ({ ...current, [path]: true }))
          }
        },
      )
    }
    return () => controller.abort()
  }, [application, packageId, missing])

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioLiveDiff(application, controller.signal).then(
      (diff) =>
        setStatus(
          diff === null
            ? 'unpublished'
            : { release: diff.release, changed: diff.resources.some((entry) => entry.file.startsWith(packagePrefix)) },
        ),
      () => {
        if (!controller.signal.aborted) {
          setStatus(undefined)
        }
      },
    )
    return () => controller.abort()
  }, [application, packagePrefix, files])

  function edit(path: string, text: string) {
    setOpen((current) => ({ ...current, [path]: { ...current[path], text } }))
    setSaved(false)
  }

  /** Puts each file back as loaded, or as the server now holds it, with a new editor so no step undoes into the change. */
  async function putBack(paths: readonly string[], reload: boolean) {
    for (const path of paths) {
      const loaded = reload ? await loadFile(application, packageId, path) : open[path].loaded
      setOpen((current) => ({
        ...current,
        [path]: { loaded, text: loaded.content, conflict: false, generation: current[path].generation + 1 },
      }))
      setSteps((current) => ({ ...current, [path]: { undo: 0, redo: 0 } }))
    }
  }

  async function saveAll() {
    setSaving(true)
    setSaveError(null)
    let answered: StudioDiagnostic[] | null = null
    try {
      for (const path of dirty) {
        const entry = open[path]
        if (entry.conflict) {
          continue
        }
        const result = await saveStudioPackageFile(application, packageId, path, entry.loaded.version, entry.text)
        if (result.status === 'conflict') {
          setOpen((current) => ({ ...current, [path]: { ...current[path], conflict: true } }))
          continue
        }
        answered = result.diagnostics
        setOpen((current) => ({
          ...current,
          [path]: {
            ...current[path],
            loaded: { ...current[path].loaded, version: result.version, content: entry.text },
          },
        }))
      }
      if (answered !== null) {
        setSaved(true)
        onChanged(answered)
      }
    } catch (error) {
      setSaveError(error instanceof QuotaRefusedError ? error.message : t('shell.studio.package.saveFailed'))
    } finally {
      setSaving(false)
    }
  }

  async function deleteFile() {
    const entry = open[file]
    if (!entry) {
      return
    }
    setDeleting(true)
    setDeleteError(false)
    try {
      const result = await deleteStudioPackageFile(application, packageId, file, entry.loaded.version)
      if (result.status === 'conflict') {
        setOpen((current) => ({ ...current, [file]: { ...current[file], conflict: true } }))
      } else {
        setOpen((current) => Object.fromEntries(Object.entries(current).filter(([path]) => path !== file)))
        onChanged(result.diagnostics)
        navigate(studioPackageAddress(application, packageId))
      }
      setDeleteOpen(false)
    } catch {
      setDeleteError(true)
    } finally {
      setDeleting(false)
    }
  }

  const languageName = manifest.language ?? 'csharp'
  const lines = [
    manifest.version === undefined
      ? null
      : t('shell.studio.package.line')
          .replace('{language}', t(`shell.studio.package.language.${languageName}`))
          .replace('{version}', manifest.version),
    status === undefined
      ? null
      : status === 'unpublished'
        ? t('shell.studio.band.notPublished')
        : t(status.changed ? 'shell.studio.package.status.changed' : 'shell.studio.package.status.unchanged').replace(
            '{release}',
            String(status.release),
          ),
  ].filter((line): line is string => line !== null)

  const current = open[file]
  const activeSteps = steps[file] ?? { undo: 0, redo: 0 }
  const panelMax = Math.max(panelLeast, Math.floor(window.innerHeight / 2))

  return (
    <Flex ref={root} vertical gap="middle" data-testid="studio-package">
      <StudioPageHeading
        title={manifest.label ?? packageId}
        line={lines}
        actions={
          developer && (
            <AxisButton role="secondary" onClick={() => setNewFileOpen(true)}>
              {t('shell.studio.package.newFile')}
            </AxisButton>
          )
        }
      />
      {!developer && (
        <AxisAlert data-testid="studio-package-read-only" kind="info" message={t('shell.studio.package.readOnly')} />
      )}
      {conflicts.map((path) => (
        <AxisAlert
          key={path}
          data-testid="studio-conflict"
          kind="warning"
          message={t('shell.studio.package.conflict').replace('{file}', path)}
          action={
            <AxisButton role="secondary" onClick={() => void putBack([path], true)}>
              {t('shell.studio.reload')}
            </AxisButton>
          }
        />
      ))}
      {saveError !== null && <AxisAlert data-testid="studio-save-error" kind="error" message={saveError} />}
      <Flex gap="middle" align="start">
        <nav aria-label={t('shell.studio.package.files')} style={{ width: navigationWidth, flex: 'none' }}>
          <Tree
            treeData={tree}
            defaultExpandAll
            blockNode
            selectedKeys={[file]}
            titleRender={(node) =>
              node.isLeaf ? (
                <Flex component="span" align="center" gap={spacing.xs} style={{ display: 'inline-flex' }}>
                  <Link
                    to={studioPackageAddress(application, packageId, String(node.key))}
                    style={{ color: 'inherit' }}
                  >
                    {String(node.title)}
                  </Link>
                  {dirty.includes(String(node.key)) && (
                    <span
                      role="img"
                      aria-label={t('shell.studio.package.unsaved')}
                      style={{ width: 6, height: 6, borderRadius: '50%', background: colour.primary, flex: 'none' }}
                    />
                  )}
                </Flex>
              ) : (
                String(node.title)
              )
            }
          />
        </nav>
        <Flex vertical gap="small" style={{ flex: 1, minWidth: 0 }}>
          <Flex justify="space-between" align="center" gap="small" style={{ minHeight: spacing.xl }}>
            <Typography.Text strong>{file}</Typography.Text>
            {developer && known && current && (
              <StudioResourceMenu disabled={busy || deleting} onDelete={() => setDeleteOpen(true)} />
            )}
          </Flex>
          {!known ? (
            <AxisAlert kind="warning" message={t('shell.studio.package.notFound')} />
          ) : failed[file] ? (
            <AxisAlert kind="error" message={t('shell.studio.package.loadFailed')} />
          ) : null}
          {Object.entries(open).map(([path, entry]) => (
            // Each open file keeps its editor, hidden while another file shows, so its steps live on.
            <div key={path} style={{ display: path === file ? undefined : 'none' }}>
              <CodeEditor
                key={entry.generation}
                ref={(handle) => {
                  editors.current.set(path, handle)
                }}
                label={path}
                value={entry.text}
                onChange={(text) => edit(path, text)}
                language={codeLanguage(path)}
                readOnly={!developer}
                boundary={root}
                findTexts={{
                  field: t('shell.studio.package.findField'),
                  count: t('shell.studio.json.findCount'),
                  none: t('shell.studio.json.findNone'),
                }}
                onHistory={(next) => setSteps((all) => ({ ...all, [path]: next }))}
              />
            </div>
          ))}
          {saved && <span data-testid="studio-saved">{t('shell.studio.saved')}</span>}
        </Flex>
      </Flex>
      <section
        ref={panel}
        aria-label={t('shell.studio.package.panel')}
        data-testid="studio-package-panel"
        style={{
          position: 'relative',
          borderTop: `1px solid ${colour.borderSubtle}`,
          height: panelOpen ? panelHeight : undefined,
          display: 'flex',
          flexDirection: 'column',
        }}
      >
        {panelOpen && (
          <ResizeHandle
            edge="top"
            size={panelHeight}
            min={panelLeast}
            max={panelMax}
            measure={(event) =>
              panel.current ? panel.current.getBoundingClientRect().bottom - event.clientY : panelHeight
            }
            onChange={(next) => setPanelHeight(Math.min(panelMax, Math.max(panelLeast, Math.round(next))))}
          />
        )}
        <Tabs
          activeKey={panelTab}
          onChange={(key) => {
            setPanelTab(key)
            setPanelOpen(true)
          }}
          size="small"
          style={{ flex: 1, minHeight: 0 }}
          tabBarStyle={{ marginBottom: panelOpen ? spacing.sm : 0 }}
          tabBarExtraContent={
            <AxisButton
              role="icon"
              nameKey={panelOpen ? 'shell.studio.package.panel.hide' : 'shell.studio.package.panel.show'}
              icon={panelOpen ? <DownOutlined /> : <UpOutlined />}
              aria-expanded={panelOpen}
              onClick={() => setPanelOpen((value) => !value)}
            />
          }
          items={[
            {
              key: 'problems',
              label: `${t('shell.studio.problems.title')} (${problems.length})`,
              children: panelOpen && (
                <div style={{ height: '100%', overflowY: 'auto', overscrollBehavior: 'contain' }}>
                  {problems.length === 0 ? (
                    <EmptyState text={t('shell.studio.problems.none')} />
                  ) : (
                    <StudioProblemList diagnostics={problems} />
                  )}
                </div>
              ),
            },
            {
              key: 'tests',
              label: t('shell.studio.package.panel.tests'),
              children: panelOpen && <EmptyState text={t('shell.studio.package.panel.testsNone')} />,
            },
            {
              key: 'output',
              label: t('shell.studio.package.panel.output'),
              children: panelOpen && <EmptyState text={t('shell.studio.package.panel.outputNone')} />,
            },
          ]}
        />
      </section>
      {developer && (
        <SaveBar
          unsaved={dirty.length > 0}
          undoable={activeSteps.undo > 0}
          redoable={activeSteps.redo > 0}
          saveable={dirty.some((path) => !open[path].conflict) && !saving && !busy}
          busy={saving}
          onUndo={() => editors.current.get(file)?.undo()}
          onRedo={() => editors.current.get(file)?.redo()}
          onDiscard={() => void putBack(dirty, false)}
          onSave={() => void saveAll()}
        />
      )}
      {newFileOpen && (
        <ReasonDialog
          title={t('shell.studio.package.newFile.title')}
          label={t('shell.studio.package.newFile.label')}
          help={t('shell.studio.package.newFile.help')}
          okText={t('shell.studio.package.newFile.create')}
          cancelText={t('shell.studio.cancel')}
          requiredMessage={t('shell.studio.package.newFile.required')}
          failedText={t('shell.studio.package.saveFailed')}
          maxLength={200}
          onClose={() => setNewFileOpen(false)}
          onChanged={() => {}}
          onSend={async (path) => {
            const trimmed = path.trim()
            const result = await createStudioPackageFile(application, packageId, trimmed, '')
            if (result.status === 'created') {
              onChanged(result.diagnostics)
              navigate(studioPackageAddress(application, packageId, trimmed))
              return { ok: true }
            }
            const title = t(
              result.status === 'conflict'
                ? 'shell.studio.package.newFile.exists'
                : 'shell.studio.package.newFile.refused',
            )
            return { ok: false, problem: { status: result.status === 'conflict' ? 409 : 400, title, errors: {} } }
          }}
        />
      )}
      <Modal
        open={deleteOpen}
        title={t('shell.studio.package.deleteFile.title').replace('{file}', file)}
        okText={t('shell.studio.delete')}
        okButtonProps={{ danger: true }}
        cancelText={t('shell.studio.cancel')}
        confirmLoading={deleting}
        onOk={() => void deleteFile()}
        onCancel={() => setDeleteOpen(false)}
      >
        <Flex vertical gap="small">
          <span>{t('shell.studio.package.deleteFile.body')}</span>
          {deleteError && <AxisAlert kind="error" message={t('shell.studio.package.deleteFile.failed')} />}
        </Flex>
      </Modal>
    </Flex>
  )
}

/** Loads a package file with its text. A file that is gone throws, as any other failure does. */
async function loadFile(
  application: string,
  packageId: string,
  path: string,
  signal?: AbortSignal,
): Promise<StudioPackageFile> {
  const loaded = await fetchStudioPackageFile(application, packageId, path, signal)
  if (loaded === null) {
    throw new Error(`The package file '${path}' is gone.`)
  }
  return { package: packageId, path, ...loaded }
}
