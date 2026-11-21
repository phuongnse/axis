import { CloseOutlined, FileOutlined, FolderOutlined } from '@ant-design/icons'
import { Flex, Modal, Tabs, Tree, type TreeDataNode } from 'antd'
import { lazy, Suspense, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton, AxisButtonGroup } from '../platform/AxisButton'
import { Chevron } from '../platform/Chevron'
import { EmptyState } from '../platform/EmptyState'
import { fillText } from '../platform/fillText'
import { sortByName } from '../platform/listOrder'
import { PageActions, PageLayout } from '../platform/PageLayout'
import { PaneHeader } from '../platform/PaneHeader'
import { QuotaRefusedError } from '../platform/quota'
import { SaveBar } from '../platform/SaveBar'
import {
  fetchStudioPackageFile,
  saveStudioPackageFile,
  ServerUnreachableError,
  type StudioDiagnostic,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { workspaceRowHeight, workspaceTabRow } from '../platform/workspaceTabRow'
import type { CodeCursor, CodeEditorHandle } from './CodeEditor'
import { PlatformContext } from './context'
import { fileLanguage } from './fileLanguage'
import { useFillHeight } from './jsonViewHeight'
import { StudioPackageRun } from './StudioPackageRun'
import { StudioProblemList } from './StudioProblems'
import { useStudioPackages, type PackageRow } from './useStudioPackages'

const CodeEditor = lazy(() => import('./CodeEditor'))

/** The explorer's width. Handles that set it are planned, see docs/reference/studio-packages.md#a-packages-files. */
const explorerWidth = 240
/**
 * The output panel's height, tall enough for the Run tab's two panes. Handles that set it are planned, see
 * docs/reference/studio-packages.md#a-packages-files.
 */
const outputHeight = 320

/** A file the workspace has read: its `saved` text, the `text` the editor shows and its `version`, or `failed` when it did not load. */
type FileState = { saved: string; text: string; version: string } | 'failed'

/** A package's tree: each level's folders first, then its files, each group by name. A folder's key is `folder:<path>` and a file's its path. */
function treeOf(paths: readonly string[], locale: string): TreeDataNode[] {
  interface Branch {
    name: string
    path: string
    folders: Map<string, Branch>
    files: string[]
  }
  const root: Branch = { name: '', path: '', folders: new Map(), files: [] }
  for (const path of paths) {
    const parts = path.split('/')
    let branch = root
    for (const [index, name] of parts.slice(0, -1).entries()) {
      const folder = parts.slice(0, index + 1).join('/')
      let next = branch.folders.get(name)
      if (next === undefined) {
        next = { name, path: folder, folders: new Map(), files: [] }
        branch.folders.set(name, next)
      }
      branch = next
    }
    branch.files.push(path)
  }
  const nodesOf = (branch: Branch): TreeDataNode[] =>
    sortByName(
      [
        ...[...branch.folders.values()].map((folder) => ({
          name: folder.name,
          isFolder: true,
          node: { key: `folder:${folder.path}`, title: folder.name, children: nodesOf(folder) },
        })),
        ...branch.files.map((path) => {
          const name = path.slice(path.lastIndexOf('/') + 1)
          return { name, isFolder: false, node: { key: path, title: name, isLeaf: true } }
        }),
      ],
      (entry) => entry.name,
      locale,
      (entry) => (entry.isFolder ? 0 : 1),
    ).map((entry) => entry.node)
  return nodesOf(root)
}

/** The dot of a file with unsaved changes, on its tab and on its row of the tree. */
function UnsavedDot() {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  return (
    <span
      role="img"
      aria-label={t('shell.studio.saveBar.unsaved')}
      data-testid="studio-package-unsaved"
      style={{
        display: 'inline-block',
        flex: 'none',
        width: spacing.sm,
        height: spacing.sm,
        borderRadius: '50%',
        background: colour.text,
      }}
    />
  )
}

/** What shows in place of the tabs while no file is open. It fills the window down to the footer, so the layout does not jump. */
function EmptyEditorArea({ boundary }: { boundary: React.RefObject<HTMLElement | null> }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const ref = useRef<HTMLDivElement>(null)
  const height = useFillHeight(ref, true, boundary)
  return (
    <div ref={ref} style={{ height, padding: spacing.md, color: colour.textMuted }}>
      {t('shell.studio.packages.noFile')}
    </div>
  )
}

/**
 * A package's workspace, `&view=packages&package=<id>`: the package's label as the heading with its kind and version
 * under it, the explorer's tree of its files on the left, the open files as tabs over the code editor, the status line
 * under them and the output panel, whose Problems tab lists the package's problems: the `diagnostics` whose file is under
 * `packages/<id>/`, each file a link that opens it in the workspace at the problem's line, or at the top. An id that names
 * no package shows a warning. Without `canDevelop` the editor is
 * read-only and an info alert says why. With it, Ctrl+S and the save bar save every changed file, one after another, each
 * with the version it was read at, and `onWritten` hears the last answer's diagnostics. The workspace reads and writes the
 * working folder, see docs/reference/studio-packages.md#a-packages-files.
 */
export function StudioPackageWorkspace({
  application,
  packageId,
  folderRevision,
  canDevelop,
  diagnostics,
  onWritten,
}: {
  application: string
  packageId: string
  folderRevision: number
  canDevelop: boolean
  diagnostics: StudioDiagnostic[]
  onWritten: (diagnostics: StudioDiagnostic[]) => void
}) {
  const t = useText()
  const state = useStudioPackages(application, folderRevision)
  const row = state?.rows?.find((candidate) => candidate.id === packageId)

  if (state === undefined) {
    return null
  }
  if (state.rows === null) {
    return (
      <AxisAlert data-testid="studio-packages-load-error" kind="error" message={t('shell.studio.packages.failed')} />
    )
  }
  if (row === undefined) {
    return (
      <AxisAlert data-testid="studio-package-missing" kind="warning" message={t('shell.studio.packages.missing')} />
    )
  }
  return (
    <Workspace
      key={row.id}
      application={application}
      row={row}
      canDevelop={canDevelop}
      diagnostics={diagnostics}
      onWritten={onWritten}
    />
  )
}

function Workspace({
  application,
  row,
  canDevelop,
  diagnostics,
  onWritten,
}: {
  application: string
  row: PackageRow
  canDevelop: boolean
  diagnostics: StudioDiagnostic[]
  onWritten: (diagnostics: StudioDiagnostic[]) => void
}) {
  const t = useText()
  const { lost, reconnecting } = useStudioConnection()
  const tokens = useDesignTokens()
  const { colour, spacing, size } = tokens
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const editorColumn = useRef<HTMLDivElement>(null)
  const requested = useRef(new Set<string>())
  const [open, setOpen] = useState<string[]>([])
  const [active, setActive] = useState<string | null>(null)
  const [files, setFiles] = useState<Record<string, FileState>>({})
  const [cursors, setCursors] = useState<Record<string, CodeCursor>>({})
  const [discarding, setDiscarding] = useState<string | null>(null)
  const [conflicts, setConflicts] = useState<Record<string, true>>({})
  const [outputTab, setOutputTab] = useState('problems')
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const editors = useRef(new Map<string, CodeEditorHandle>())
  const pending = useRef<{ path: string; line: number } | null>(null)
  const [jump, setJump] = useState(0)
  const prefix = `packages/${row.id}/`
  const problems = diagnostics.filter((diagnostic) => diagnostic.file.startsWith(prefix))
  const isUnsaved = (path: string) => {
    const file = files[path]
    return file !== undefined && file !== 'failed' && file.text !== file.saved
  }
  const unsavedCount = Object.keys(files).filter(isUnsaved).length
  const saveable = Object.keys(files).some((path) => isUnsaved(path) && !conflicts[path]) && !saving && !reconnecting

  const tree = useMemo(() => treeOf(row.files, locale), [row.files, locale])

  const openFile = (path: string) => {
    setOpen((current) => (current.includes(path) ? current : [...current, path]))
    setActive(path)
    if (requested.current.has(path)) {
      return
    }
    requested.current.add(path)
    fetchStudioPackageFile(application, row.id, path).then(
      (file) =>
        setFiles((current) => ({
          ...current,
          [path]: file === null ? 'failed' : { saved: file.content, text: file.content, version: file.version },
        })),
      (error: unknown) => {
        console.warn(`Loading '${path}' of the package '${row.id}' failed`, error)
        setFiles((current) => ({ ...current, [path]: 'failed' }))
      },
    )
  }

  /** Opens a problem's file, or switches to it, and puts the cursor at the problem's line, or on line 1 without one. */
  const openAt = (file: string, line?: number) => {
    const path = file.slice(prefix.length)
    pending.current = { path, line: line ?? 1 }
    openFile(path)
    setJump((count) => count + 1)
  }

  // Moves the cursor of the file that openAt asked for once its editor is there. The editor builds its view in an effect
  // that runs before this one, so a file that loads later is reached through the editor's ref below, which bumps `jump`.
  useEffect(() => {
    const target = pending.current
    const editor = target && editors.current.get(target.path)
    if (target && editor) {
      editor.goTo(target.line)
      pending.current = null
    }
  }, [jump])

  /** Reads a file again, which drops its unsaved text and its conflict. */
  const reload = (path: string) =>
    fetchStudioPackageFile(application, row.id, path).then(
      (file) => {
        setFiles((current) => ({
          ...current,
          [path]: file === null ? 'failed' : { saved: file.content, text: file.content, version: file.version },
        }))
        setConflicts(({ [path]: _dropped, ...rest }) => rest)
      },
      (error: unknown) => {
        console.warn(`Loading '${path}' of the package '${row.id}' failed`, error)
        setFiles((current) => ({ ...current, [path]: 'failed' }))
      },
    )

  const closeTab = (path: string) => {
    const index = open.indexOf(path)
    const rest = open.filter((candidate) => candidate !== path)
    setOpen(rest)
    if (active === path) {
      setActive(rest[Math.min(index, rest.length - 1)] ?? null)
    }
  }

  const discard = (path: string) => {
    closeTab(path)
    requested.current.delete(path)
    setFiles(({ [path]: _dropped, ...rest }) => rest)
    setConflicts(({ [path]: _dropped, ...rest }) => rest)
  }

  /**
   * Saves every open unsaved file that is not in conflict, in the order of the tabs and one after another, as each write
   * takes the folder's write lock and makes one revision. A 409 marks that file and the run goes on. Any other failure
   * stops it, as it would fail every file after. See docs/reference/studio-packages.md#a-packages-files. It answers
   * whether every changed file is saved: `false` when a save failed or a changed file is in conflict, which a run waits
   * for. A file that was already in conflict opens its tab as a new conflict does.
   */
  async function saveAll(): Promise<boolean> {
    if (!canDevelop || saving || reconnecting) {
      return false
    }
    const stale = open.filter((path) => isUnsaved(path) && conflicts[path])
    const paths = open.filter((path) => isUnsaved(path) && !conflicts[path])
    if (paths.length === 0) {
      if (stale.length > 0 && (active === null || !conflicts[active])) {
        setActive(stale[0])
      }
      return stale.length === 0
    }
    setSaving(true)
    setSaveError(null)
    let diagnostics: StudioDiagnostic[] | null = null
    const conflicted: string[] = []
    let current = ''
    let failed = false
    try {
      for (const path of paths) {
        current = path
        const file = files[path]
        if (file === undefined || file === 'failed') {
          continue
        }
        const { text } = file
        const result = await saveStudioPackageFile(application, row.id, path, file.version, text)
        if (result.status === 'conflict') {
          conflicted.push(path)
          setConflicts((before) => ({ ...before, [path]: true }))
          continue
        }
        diagnostics = result.diagnostics
        // The saved text is the one sent, so what was typed while the save ran keeps the dot.
        setFiles((before) => {
          const now = before[path]
          return now === undefined || now === 'failed'
            ? before
            : { ...before, [path]: { ...now, saved: text, version: result.version } }
        })
      }
    } catch (error) {
      failed = true
      if (error instanceof ServerUnreachableError) {
        lost()
      } else if (error instanceof QuotaRefusedError) {
        setSaveError(error.message)
      } else {
        console.warn(`Saving the package '${row.id}' failed`, error)
        setSaveError(fillText(t('shell.studio.packages.saveFailed'), { file: current }))
      }
    } finally {
      setSaving(false)
    }
    if (diagnostics !== null) {
      onWritten(diagnostics)
    }
    const inConflict = [...stale, ...conflicted]
    if (inConflict.length > 0 && (active === null || !(conflicts[active] || conflicted.includes(active)))) {
      setActive(inConflict[0])
    }
    return !failed && inConflict.length === 0
  }

  const editFile = (path: string, text: string) =>
    setFiles((current) => {
      const file = current[path]
      return file === undefined || file === 'failed' ? current : { ...current, [path]: { ...file, text } }
    })

  const moveCursor = (path: string, cursor: CodeCursor) =>
    setCursors((current) => {
      const before = current[path]
      return before?.line === cursor.line && before.column === cursor.column ? current : { ...current, [path]: cursor }
    })

  // The latest saveAll, so the key listener below is set up once.
  const latest = useRef(saveAll)
  useEffect(() => {
    latest.current = saveAll
  })
  // With the developer role Ctrl+S, or Cmd+S, saves every changed file anywhere on the page and the browser's Save page
  // stays shut. Without it the browser keeps its key, see docs/reference/studio-packages.md#a-packages-files.
  useEffect(() => {
    if (!canDevelop) {
      return
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (!(event.ctrlKey || event.metaKey) || event.altKey || event.shiftKey || event.key.toLowerCase() !== 's') {
        return
      }
      event.preventDefault()
      event.stopPropagation()
      void latest.current()
    }
    document.addEventListener('keydown', onKeyDown, true)
    return () => document.removeEventListener('keydown', onKeyDown, true)
  }, [canDevelop])

  const rule = `1px solid ${colour.borderSubtle}`
  const cursor = active === null ? undefined : (cursors[active] ?? { line: 1, column: 1 })
  const unsavedText =
    unsavedCount === 0
      ? t('shell.studio.packages.unsavedNone')
      : unsavedCount === 1
        ? t('shell.studio.packages.unsavedOne')
        : fillText(t('shell.studio.packages.unsavedMany'), { count: unsavedCount })
  const findTexts = {
    field: t('shell.studio.json.findField'),
    count: t('shell.studio.json.findCount'),
    none: t('shell.studio.json.findNone'),
  }

  return (
    <PageLayout
      title={row.label}
      line={
        row.version
          ? fillText(t('shell.studio.packages.kindVersion'), { version: row.version })
          : t('shell.studio.packages.kind')
      }
    >
      {!canDevelop && (
        <AxisAlert data-testid="studio-package-read-only" kind="info" message={t('shell.studio.packages.readOnly')} />
      )}
      {saveError !== null && <AxisAlert data-testid="studio-package-save-error" kind="error" message={saveError} />}
      {canDevelop && (
        <PageActions>
          <AxisButtonGroup
            role="secondary"
            nameKey="shell.studio.packages.actions"
            actions={[{ key: 'run', label: t('shell.studio.packages.run.open'), onClick: () => setOutputTab('run') }]}
          />
        </PageActions>
      )}
      <div
        style={{ display: 'grid', gridTemplateColumns: `${explorerWidth}px minmax(0, 1fr)`, borderTop: rule }}
        data-testid="studio-package-workspace"
      >
        <section
          aria-label={t('shell.studio.packages.files')}
          style={{ display: 'flex', flexDirection: 'column', minWidth: 0, borderInlineEnd: rule }}
        >
          <PaneHeader title={t('shell.studio.packages.files')} />
          <div style={{ position: 'relative', flex: 1 }}>
            <div
              data-testid="studio-package-tree"
              style={{ position: 'absolute', inset: 0, overflow: 'auto', overscrollBehavior: 'contain' }}
            >
              <Tree.DirectoryTree
                treeData={tree}
                titleRender={(node) => (
                  <span>
                    {String(node.title)}
                    {node.isLeaf && isUnsaved(String(node.key)) && (
                      <>
                        {' '}
                        <UnsavedDot />
                      </>
                    )}
                  </span>
                )}
                expandAction="click"
                multiple={false}
                defaultExpandAll
                selectedKeys={active === null ? [] : [active]}
                switcherIcon={({ expanded }) => <Chevron point={expanded ? 'down' : 'right'} />}
                icon={({ isLeaf }) => (isLeaf ? <FileOutlined /> : <FolderOutlined />)}
                onSelect={(_keys, { node }) => {
                  if (node.isLeaf) {
                    openFile(String(node.key))
                  }
                }}
              />
            </div>
          </div>
        </section>
        <div ref={editorColumn} style={{ display: 'flex', flexDirection: 'column', minWidth: 0 }}>
          {open.length === 0 ? (
            <EmptyEditorArea boundary={editorColumn} />
          ) : (
            <Tabs
              type="line"
              {...workspaceTabRow(tokens)}
              activeKey={active ?? undefined}
              onChange={setActive}
              items={open.map((path) => {
                const file = files[path]
                const name = path.slice(path.lastIndexOf('/') + 1)
                return {
                  key: path,
                  label: (
                    <Flex align="center" gap={spacing.xs}>
                      <span>{name}</span>
                      {isUnsaved(path) && <UnsavedDot />}
                      <AxisButton
                        role="icon"
                        nameKey="shell.studio.packages.closeFile"
                        nameValues={{ file: path }}
                        icon={<CloseOutlined />}
                        onClick={(event) => {
                          event.stopPropagation()
                          if (isUnsaved(path)) {
                            setDiscarding(path)
                          } else {
                            closeTab(path)
                          }
                        }}
                      />
                    </Flex>
                  ),
                  children:
                    file === undefined ? null : file === 'failed' ? (
                      <AxisAlert kind="error" message={t('shell.studio.packages.fileFailed')} />
                    ) : (
                      <>
                        {conflicts[path] && (
                          <AxisAlert
                            data-testid="studio-package-conflict"
                            kind="warning"
                            message={t('shell.studio.conflict')}
                            action={
                              <AxisButton role="secondary" onClick={() => void reload(path)}>
                                {t('shell.studio.reload')}
                              </AxisButton>
                            }
                          />
                        )}
                        <Suspense fallback={null}>
                          <CodeEditor
                            key={path}
                            ref={(handle) => {
                              if (handle === null) {
                                editors.current.delete(path)
                                return
                              }
                              editors.current.set(path, handle)
                              if (pending.current?.path === path) {
                                setJump((count) => count + 1)
                              }
                            }}
                            label={path}
                            value={file.text}
                            onChange={(text) => editFile(path, text)}
                            language={fileLanguage(path)}
                            readOnly={!canDevelop}
                            fill
                            boundary={editorColumn}
                            onCursor={(next) => moveCursor(path, next)}
                            findTexts={findTexts}
                          />
                        </Suspense>
                      </>
                    ),
                }
              })}
            />
          )}
          <div
            data-testid="studio-package-status"
            style={{
              display: 'flex',
              gap: spacing.md,
              padding: `${spacing.xs}px ${spacing.md}px`,
              borderTop: rule,
              color: colour.textMuted,
            }}
          >
            {active !== null && cursor !== undefined && (
              <>
                <span>{fillText(t('shell.studio.packages.cursor'), { line: cursor.line, column: cursor.column })}</span>
                <span>{t(`shell.studio.code.language.${fileLanguage(active)}`)}</span>
              </>
            )}
            <span style={{ marginInlineStart: 'auto' }}>{unsavedText}</span>
          </div>
          <div data-testid="studio-package-output" style={{ height: outputHeight, borderTop: rule }}>
            <Tabs
              type="line"
              {...workspaceTabRow(tokens)}
              activeKey={outputTab}
              onChange={setOutputTab}
              items={[
                {
                  key: 'problems',
                  label: t('shell.studio.problems.title'),
                  children: (
                    <div
                      style={{
                        height: outputHeight - workspaceRowHeight(tokens),
                        overflowY: 'auto',
                        overscrollBehavior: 'contain',
                      }}
                    >
                      {problems.length === 0 ? (
                        <EmptyState text={t('shell.studio.problems.none')} />
                      ) : (
                        <StudioProblemList diagnostics={problems} onOpenFile={openAt} />
                      )}
                    </div>
                  ),
                },
                ...(canDevelop
                  ? [
                      {
                        key: 'run',
                        label: t('shell.studio.packages.run.tab'),
                        children: (
                          <StudioPackageRun
                            application={application}
                            packageId={row.id}
                            operations={row.operations}
                            height={outputHeight - workspaceRowHeight(tokens)}
                            beforeRun={saveAll}
                          />
                        ),
                      },
                    ]
                  : []),
              ]}
            />
          </div>
        </div>
      </div>
      <Modal
        open={discarding !== null}
        width={size.dialog.small}
        title={fillText(t('shell.studio.packages.discardFile'), { file: discarding ?? '' })}
        okText={t('shell.studio.discard')}
        okButtonProps={{ danger: true }}
        cancelText={t('shell.studio.cancel')}
        onOk={() => {
          if (discarding !== null) {
            discard(discarding)
          }
          setDiscarding(null)
        }}
        onCancel={() => setDiscarding(null)}
      />
      {canDevelop && (
        <SaveBar
          data-testid="studio-package-save-bar"
          saveTestId="studio-package-save"
          unsaved={unsavedCount > 0}
          saveable={saveable}
          busy={saving}
          onSave={() => void saveAll()}
          onDiscard={() =>
            setFiles((current) =>
              Object.fromEntries(
                Object.entries(current).map(([path, file]) => [
                  path,
                  file === 'failed' ? file : { ...file, text: file.saved },
                ]),
              ),
            )
          }
        />
      )}
    </PageLayout>
  )
}
