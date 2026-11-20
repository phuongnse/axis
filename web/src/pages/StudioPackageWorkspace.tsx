import { CloseOutlined, FileOutlined, FolderOutlined } from '@ant-design/icons'
import { Flex, Modal, Tabs, Tree, type TreeDataNode } from 'antd'
import { lazy, Suspense, useContext, useMemo, useRef, useState } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { Chevron } from '../platform/Chevron'
import { fillText } from '../platform/fillText'
import { sortByName } from '../platform/listOrder'
import { PageLayout } from '../platform/PageLayout'
import { PaneHeader } from '../platform/PaneHeader'
import { fetchStudioPackageFile } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useUnsavedChanges } from '../platform/unsavedChanges'
import { workspaceTabRow } from '../platform/workspaceTabRow'
import type { CodeCursor } from './CodeEditor'
import { PlatformContext } from './context'
import { fileLanguage } from './fileLanguage'
import { useFillHeight } from './jsonViewHeight'
import { useStudioPackages, type PackageRow } from './useStudioPackages'

const CodeEditor = lazy(() => import('./CodeEditor'))

/** The explorer's width. Handles that set it are planned, see docs/reference/studio-packages.md#a-packages-files. */
const explorerWidth = 240
/** The output panel's height. Handles that set it are planned, see docs/reference/studio-packages.md#a-packages-files. */
const outputHeight = 120

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
 * under them and an empty output panel. An id that names no package shows a warning. Without `canDevelop` the editor is
 * read-only and an info alert says why. The workspace reads the working folder, see
 * docs/reference/studio-packages.md#a-packages-files.
 */
export function StudioPackageWorkspace({
  application,
  packageId,
  folderRevision,
  canDevelop,
}: {
  application: string
  packageId: string
  folderRevision: number
  canDevelop: boolean
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
  return <Workspace key={row.id} application={application} row={row} canDevelop={canDevelop} />
}

function Workspace({ application, row, canDevelop }: { application: string; row: PackageRow; canDevelop: boolean }) {
  const t = useText()
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
  const isUnsaved = (path: string) => {
    const file = files[path]
    return file !== undefined && file !== 'failed' && file.text !== file.saved
  }
  const unsavedCount = Object.keys(files).filter(isUnsaved).length
  // Part 3's save bar counts the unsaved files in its place, see docs/reference/studio-packages.md#a-packages-files.
  useUnsavedChanges(unsavedCount > 0)

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
                      <Suspense fallback={null}>
                        <CodeEditor
                          key={path}
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
          <div data-testid="studio-package-output" style={{ height: outputHeight, borderTop: rule }} />
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
    </PageLayout>
  )
}
