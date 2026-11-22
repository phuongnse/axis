import { FileOutlined, FolderOutlined, PlusOutlined, ReloadOutlined } from '@ant-design/icons'
import { Dropdown, Flex, Modal, Tree, type TreeDataNode } from 'antd'
import { useContext, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { DialogFooter } from '../platform/DialogFooter'
import { Chevron } from '../platform/Chevron'
import { fillText } from '../platform/fillText'
import type { CommandResult } from '../platform/inspection'
import { sortByName } from '../platform/listOrder'
import { menuOf, type MenuAction } from '../platform/menuActions'
import { PaneHeader } from '../platform/PaneHeader'
import { QuotaRefusedError } from '../platform/quota'
import { ReasonDialog } from '../platform/ReasonDialog'
import {
  createStudioPackageFile,
  deleteStudioPackageFile,
  moveStudioPackageFiles,
  ServerUnreachableError,
  type StudioDiagnostic,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useCopy } from '../platform/useCopy'
import { PlatformContext } from './context'
import { UnsavedDot } from './UnsavedDot'
import type { PackageRow } from './useStudioPackages'

const folderPrefix = 'folder:'

/**
 * A package's tree: each level's folders first, then its files, each group by name. A folder's key is `folder:<path>` and
 * a file's its path. `extraFolders` are folders that hold no file yet, which the explorer keeps until one is created in them.
 */
function treeOf(paths: readonly string[], extraFolders: readonly string[], locale: string): TreeDataNode[] {
  interface Branch {
    name: string
    path: string
    folders: Map<string, Branch>
    files: string[]
  }
  const root: Branch = { name: '', path: '', folders: new Map(), files: [] }
  const branchOf = (parts: readonly string[]) => {
    let branch = root
    for (const [index, name] of parts.entries()) {
      let next = branch.folders.get(name)
      if (next === undefined) {
        next = { name, path: parts.slice(0, index + 1).join('/'), folders: new Map(), files: [] }
        branch.folders.set(name, next)
      }
      branch = next
    }
    return branch
  }
  for (const path of paths) {
    branchOf(path.split('/').slice(0, -1)).files.push(path)
  }
  for (const folder of extraFolders) {
    branchOf(folder.split('/'))
  }
  const nodesOf = (branch: Branch): TreeDataNode[] =>
    sortByName(
      [
        ...[...branch.folders.values()].map((folder) => ({
          name: folder.name,
          isFolder: true,
          node: { key: `${folderPrefix}${folder.path}`, title: folder.name, children: nodesOf(folder) },
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

/** The keys of every folder in `nodes`. */
function folderKeysOf(nodes: readonly TreeDataNode[]): string[] {
  return nodes.flatMap((node) => (node.isLeaf ? [] : [String(node.key), ...folderKeysOf(node.children ?? [])]))
}

/** A file or a folder of the tree: its `path` in the package and its `name`, the last segment. */
interface Entry {
  path: string
  name: string
  isFolder: boolean
}

const entryOf = (key: string): Entry => {
  const isFolder = key.startsWith(folderPrefix)
  const path = isFolder ? key.slice(folderPrefix.length) : key
  return { path, name: path.slice(path.lastIndexOf('/') + 1), isFolder }
}

const keyOf = (node: Entry) => (node.isFolder ? `${folderPrefix}${node.path}` : node.path)
const dirnameOf = (path: string) => path.slice(0, Math.max(path.lastIndexOf('/'), 0))
const join = (parent: string, name: string) => (parent === '' ? name : `${parent}/${name}`)

/** A name that the server takes as one path segment, see docs/reference/studio-packages.md#the-authoring-api. */
const isName = (name: string) => name !== '' && name !== '..' && !name.startsWith('.') && !/[/\\\r\n]/.test(name)

/** `path` with the prefix `from` swapped for `to`, when it is `from` or under it. */
const swapPrefix = (path: string, from: string, to: string) =>
  path === from ? to : path.startsWith(`${from}/`) ? to + path.slice(from.length) : path

/** What a name dialog is for: a new file or a new folder under `parent`, or the rename of `node`. */
type NameDialog = { kind: 'file' | 'folder'; parent: string } | { kind: 'rename'; node: Entry }

/**
 * A package's explorer, the Files pane of the workspace: a tree of the package's folders and files under the title row
 * Files, see docs/reference/studio-packages.md#a-packages-files. With `canDevelop` the title row has New, which offers New
 * file… and New folder…, and a file's or folder's menu, on a right click, Shift+F10 or the menu key, offers New file…,
 * New folder…, Rename… (also F2), Delete… and Copy path. Without it the menu offers only Copy path. New file… creates an
 * empty file with `If-None-Match: *` and opens it. New folder… adds an empty folder to the tree only, which stays until a
 * file is in it. Rename… sends one move that lists each file of the node with its version, `versionOf`, and `onRenamed`
 * hears the new path of each. Delete… asks first and then deletes each file of the node, one after another, `onDeleted`
 * hears the paths gone and a failure goes to `onFailed`. Every write reads the list again through `onRefresh` and tells
 * the folder's diagnostics to `onWritten`. Refresh reads the list again too. With `canDevelop` a file or folder can also
 * be dragged onto a folder, or onto the free space for the package's root, which sends the same one move as Rename…, and
 * a refusal goes to `onFailed`.
 */
export function StudioPackageExplorer({
  application,
  row,
  canDevelop,
  active,
  isUnsaved,
  versionOf,
  onOpen,
  onRenamed,
  onDeleted,
  onWritten,
  onRefresh,
  onFailed,
}: {
  application: string
  row: PackageRow
  canDevelop: boolean
  active: string | null
  isUnsaved: (path: string) => boolean
  versionOf: (path: string) => string
  onOpen: (path: string) => void
  onRenamed: (moved: Map<string, string>) => void
  onDeleted: (paths: string[]) => void
  onWritten: (diagnostics: StudioDiagnostic[]) => void
  onRefresh: () => void
  onFailed: (message: string) => void
}) {
  const t = useText()
  const { lost } = useStudioConnection()
  const { copy } = useCopy()
  const { colour, spacing, size } = useDesignTokens()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const [newFolders, setNewFolders] = useState<string[]>([])
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(new Set())
  const [current, setCurrent] = useState<string | null>(null)
  const [menuKey, setMenuKey] = useState<string | null>(null)
  const [dialog, setDialog] = useState<NameDialog | null>(null)
  const [deleting, setDeleting] = useState<Entry | null>(null)
  const [deleteBusy, setDeleteBusy] = useState(false)
  // The key of the node being dragged, for a drop on the free space, which the tree does not report.
  const dragged = useRef<string | null>(null)

  // A new folder stays only until a file is in it, as the folder holds only files.
  const emptyFolders = newFolders.filter((folder) => !row.files.some((path) => path.startsWith(`${folder}/`)))
  if (emptyFolders.length !== newFolders.length) {
    setNewFolders(emptyFolders)
  }

  const tree = useMemo(() => treeOf(row.files, newFolders, locale), [row.files, newFolders, locale])
  const expandedKeys = useMemo(() => folderKeysOf(tree).filter((key) => !collapsed.has(key)), [tree, collapsed])

  const filesUnder = (folder: string) => row.files.filter((path) => path.startsWith(`${folder}/`))
  const taken = (path: string) =>
    row.files.some((file) => file === path || file.startsWith(`${path}/`)) || newFolders.includes(path)
  const problem = (title: string): CommandResult => ({ ok: false, problem: { status: 400, title, errors: {} } })
  const nameInvalid = () => problem(t('shell.studio.packages.nameInvalid'))
  const nameTaken = () => problem(t('shell.studio.packages.nameTaken'))

  /** Opens the folders on the way to `folder`, so a new folder shows. */
  const reveal = (folder: string) =>
    setCollapsed((before) => {
      const parts = folder.split('/')
      const ancestors = parts.map((_, index) => `${folderPrefix}${parts.slice(0, index + 1).join('/')}`)
      return ancestors.some((key) => before.has(key))
        ? new Set([...before].filter((key) => !ancestors.includes(key)))
        : before
    })

  async function createFile(parent: string, text: string): Promise<CommandResult> {
    const name = text.trim()
    if (!isName(name)) {
      return nameInvalid()
    }
    const path = join(parent, name)
    try {
      const result = await createStudioPackageFile(application, row.id, path, '')
      if (result.status === 'conflict') {
        return nameTaken()
      }
      if (result.status === 'invalid') {
        return nameInvalid()
      }
      onWritten(result.diagnostics)
      onRefresh()
      onOpen(path)
      return { ok: true }
    } catch (error) {
      if (error instanceof QuotaRefusedError) {
        return problem(error.message)
      }
      if (error instanceof ServerUnreachableError) {
        lost()
      }
      throw error
    }
  }

  function createFolder(parent: string, text: string): CommandResult {
    const name = text.trim()
    if (!isName(name)) {
      return nameInvalid()
    }
    const path = join(parent, name)
    if (taken(path)) {
      return nameTaken()
    }
    setNewFolders((before) => [...before, path])
    reveal(path)
    return { ok: true }
  }

  /**
   * Moves `node` to the path `to` with one move that lists each file it holds with its version, or with no request for an
   * empty folder that only the explorer knows. The caller has checked that `to` is not taken.
   */
  async function moveNode(node: Entry, to: string): Promise<'moved' | 'conflict' | 'invalid'> {
    const remap = (path: string) => swapPrefix(path, node.path, to)
    const remapState = () => {
      setNewFolders((before) => before.map(remap))
      setCollapsed(
        (before) => new Set([...before].map((key) => `${folderPrefix}${remap(key.slice(folderPrefix.length))}`)),
      )
      setCurrent((before) =>
        before === null ? before : keyOf({ ...entryOf(before), path: remap(entryOf(before).path) }),
      )
    }
    const paths = node.isFolder ? filesUnder(node.path) : [node.path]
    if (paths.length === 0) {
      // An empty folder that only the explorer knows.
      remapState()
      return 'moved'
    }
    try {
      const result = await moveStudioPackageFiles(
        application,
        row.id,
        paths.map((from) => ({ from, to: remap(from), version: versionOf(from) })),
      )
      if (result.status === 'invalid') {
        return 'invalid'
      }
      if (result.status === 'conflict') {
        onRefresh()
        return 'conflict'
      }
      onRenamed(new Map(paths.map((from) => [from, remap(from)])))
      remapState()
      onWritten(result.diagnostics)
      onRefresh()
      return 'moved'
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      }
      throw error
    }
  }

  async function rename(node: Entry, text: string): Promise<CommandResult> {
    const name = text.trim()
    if (!isName(name)) {
      return nameInvalid()
    }
    if (name === node.name) {
      return { ok: true }
    }
    const to = join(dirnameOf(node.path), name)
    // The server refuses only a file as the target, so a folder with that name would merge.
    if (taken(to)) {
      return nameTaken()
    }
    const result = await moveNode(node, to)
    return result === 'invalid' ? nameInvalid() : result === 'conflict' ? nameTaken() : { ok: true }
  }

  /** Moves `node` into `folder`, the package's root when it is empty. A refusal goes to `onFailed`. */
  async function moveInto(node: Entry, folder: string) {
    if (
      folder === dirnameOf(node.path) ||
      (node.isFolder && (folder === node.path || folder.startsWith(`${node.path}/`)))
    ) {
      return
    }
    const to = join(folder, node.name)
    const refused = () => onFailed(fillText(t('shell.studio.packages.moveRefused'), { name: node.name }))
    if (taken(to)) {
      refused()
      return
    }
    try {
      const result = await moveNode(node, to)
      if (result !== 'moved') {
        refused()
      } else if (folder !== '') {
        reveal(folder)
      }
    } catch (error) {
      if (!(error instanceof ServerUnreachableError)) {
        console.warn(`Moving '${node.path}' of the package '${row.id}' failed`, error)
        refused()
      }
    }
  }

  async function deleteNode(node: Entry) {
    const paths = node.isFolder ? filesUnder(node.path) : [node.path]
    const gone: string[] = []
    let diagnostics: StudioDiagnostic[] | null = null
    let failed: string | null = null
    setDeleteBusy(true)
    for (const path of paths) {
      try {
        const result = await deleteStudioPackageFile(application, row.id, path, versionOf(path))
        if (result.status === 'conflict') {
          failed = path
          break
        }
        gone.push(path)
        if (result.status === 'deleted') {
          diagnostics = result.diagnostics
        }
      } catch (error) {
        failed = path
        if (error instanceof ServerUnreachableError) {
          lost()
        } else {
          console.warn(`Deleting '${path}' of the package '${row.id}' failed`, error)
        }
        break
      }
    }
    setDeleteBusy(false)
    setDeleting(null)
    if (gone.length > 0) {
      onDeleted(gone)
    }
    if (diagnostics !== null) {
      onWritten(diagnostics)
    }
    if (failed === null) {
      setNewFolders((before) => before.filter((folder) => folder !== node.path && !folder.startsWith(`${node.path}/`)))
    } else {
      onFailed(fillText(t('shell.studio.packages.deleteFailed'), { file: failed }))
    }
    onRefresh()
  }

  const parentOf = (node: Entry) => (node.isFolder ? node.path : dirnameOf(node.path))
  const actionsFor = (node: Entry): MenuAction[] => {
    const copyPath: MenuAction = {
      key: 'copyPath',
      label: t('shell.studio.packages.copyPath'),
      onClick: () => void copy(`packages/${row.id}/${node.path}`),
    }
    return canDevelop
      ? [
          {
            key: 'newFile',
            label: t('shell.studio.packages.newFile'),
            onClick: () => setDialog({ kind: 'file', parent: parentOf(node) }),
          },
          {
            key: 'newFolder',
            label: t('shell.studio.packages.newFolder'),
            onClick: () => setDialog({ kind: 'folder', parent: parentOf(node) }),
          },
          {
            key: 'rename',
            label: t('shell.studio.packages.rename'),
            onClick: () => setDialog({ kind: 'rename', node }),
          },
          {
            key: 'delete',
            label: t('shell.studio.packages.delete'),
            destructive: true,
            onClick: () => setDeleting(node),
          },
          copyPath,
        ]
      : [copyPath]
  }

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    // Keys of a menu or a dialog bubble through React's portals, and are not the tree's.
    if (current === null || !event.currentTarget.contains(event.target as Node)) {
      return
    }
    if (event.key === 'F2' && canDevelop) {
      event.preventDefault()
      setDialog({ kind: 'rename', node: entryOf(current) })
    } else if ((event.key === 'F10' && event.shiftKey) || event.key === 'ContextMenu') {
      event.preventDefault()
      setMenuKey(current)
    }
  }

  const newActions: MenuAction[] = [
    {
      key: 'newFile',
      label: t('shell.studio.packages.newFile'),
      onClick: () => setDialog({ kind: 'file', parent: '' }),
    },
    {
      key: 'newFolder',
      label: t('shell.studio.packages.newFolder'),
      onClick: () => setDialog({ kind: 'folder', parent: '' }),
    },
  ]

  const dialogTexts =
    dialog === null
      ? null
      : dialog.kind === 'rename'
        ? {
            title: fillText(t('shell.studio.packages.renameTitle'), { name: dialog.node.name }),
            ok: t('shell.studio.packages.renameOk'),
          }
        : {
            title: t(
              dialog.kind === 'file' ? 'shell.studio.packages.newFileTitle' : 'shell.studio.packages.newFolderTitle',
            ),
            ok: t('shell.studio.packages.create'),
          }
  const deletingCount = deleting?.isFolder ? filesUnder(deleting.path).length : 0

  return (
    <section
      aria-label={t('shell.studio.packages.files')}
      style={{
        display: 'flex',
        flexDirection: 'column',
        minWidth: 0,
        borderInlineEnd: `1px solid ${colour.borderSubtle}`,
      }}
    >
      <PaneHeader
        title={t('shell.studio.packages.files')}
        actions={
          <Flex gap={spacing.xs}>
            {canDevelop && (
              <Dropdown
                trigger={['click']}
                autoFocus
                destroyOnHidden
                menu={{ items: menuOf(newActions), 'aria-label': t('shell.studio.packages.new') }}
              >
                <AxisButton role="subtle" nameKey="shell.studio.packages.new" icon={<PlusOutlined />} />
              </Dropdown>
            )}
            <AxisButton
              role="subtle"
              nameKey="shell.studio.packages.refresh"
              icon={<ReloadOutlined />}
              onClick={onRefresh}
            />
          </Flex>
        }
      />
      <div style={{ position: 'relative', flex: 1 }}>
        <div
          data-testid="studio-package-tree"
          style={{ position: 'absolute', inset: 0, overflow: 'auto', overscrollBehavior: 'contain' }}
          onKeyDown={onKeyDown}
          onDragOver={(event) => {
            if (dragged.current !== null) {
              event.preventDefault()
            }
          }}
          onDrop={(event) => {
            if (dragged.current === null) {
              return
            }
            event.preventDefault()
            const key = dragged.current
            dragged.current = null
            void moveInto(entryOf(key), '')
          }}
        >
          <Tree.DirectoryTree
            treeData={tree}
            titleRender={(data) => {
              const key = String(data.key)
              const node = entryOf(key)
              return (
                <Dropdown
                  trigger={['contextMenu']}
                  autoFocus
                  destroyOnHidden
                  open={menuKey === key}
                  onOpenChange={(open) => {
                    setMenuKey(open ? key : null)
                    if (open) {
                      setCurrent(key)
                    }
                  }}
                  menu={{
                    items: menuOf(actionsFor(node)),
                    // The menu sits in a portal, whose clicks reach the tree's row through React and would select or toggle it.
                    onClick: ({ domEvent }) => domEvent.stopPropagation(),
                    'aria-label': fillText(t('shell.studio.packages.nodeMenu'), { name: node.name }),
                  }}
                >
                  <span>
                    {String(data.title)}
                    {data.isLeaf && isUnsaved(node.path) && (
                      <>
                        {' '}
                        <UnsavedDot />
                      </>
                    )}
                  </span>
                </Dropdown>
              )
            }}
            expandAction="click"
            draggable={canDevelop ? { icon: false } : false}
            allowDrop={({ dropNode }) => !dropNode.isLeaf}
            onDragStart={({ node }) => {
              dragged.current = String(node.key)
            }}
            onDragEnd={() => {
              dragged.current = null
            }}
            onDrop={(info) => {
              const key = String(info.dragNode.key)
              dragged.current = null
              // `allowDrop` refuses files, so the node dropped on is a folder.
              void moveInto(entryOf(key), entryOf(String(info.node.key)).path)
            }}
            // A file explorer opens and closes its folders at once, and a renamed or new folder shows its rows with no motion to wait for.
            motion={false}
            multiple={false}
            expandedKeys={expandedKeys}
            onExpand={(keys) => {
              const open = new Set(keys.map(String))
              setCollapsed(new Set(folderKeysOf(tree).filter((key) => !open.has(key))))
            }}
            selectedKeys={active === null ? [] : [active]}
            switcherIcon={({ expanded }) => <Chevron point={expanded ? 'down' : 'right'} />}
            icon={({ isLeaf }) => (isLeaf ? <FileOutlined /> : <FolderOutlined />)}
            onActiveChange={(key) => setCurrent(key === null ? null : String(key))}
            onSelect={(_keys, { node }) => {
              setCurrent(String(node.key))
              if (node.isLeaf) {
                onOpen(String(node.key))
              }
            }}
          />
        </div>
      </div>
      {dialog !== null && dialogTexts !== null && (
        <ReasonDialog
          title={dialogTexts.title}
          label={t('shell.studio.packages.name')}
          help={t('shell.studio.packages.nameHelp')}
          finishText={dialogTexts.ok}
          cancelText={t('shell.studio.cancel')}
          requiredMessage={t('shell.studio.packages.nameRequired')}
          tooLongMessage={t('shell.studio.packages.nameInvalid')}
          maxLength={255}
          initialValue={dialog.kind === 'rename' ? dialog.node.name : undefined}
          onSend={(text) =>
            dialog.kind === 'rename'
              ? rename(dialog.node, text)
              : dialog.kind === 'file'
                ? createFile(dialog.parent, text)
                : Promise.resolve(createFolder(dialog.parent, text))
          }
          onClose={() => setDialog(null)}
          onChanged={() => undefined}
        />
      )}
      <Modal
        open={deleting !== null}
        width={size.dialog.small}
        title={
          deleting === null
            ? ''
            : deletingCount > 0
              ? fillText(t(`shell.studio.packages.deleteFolder${deletingCount === 1 ? 'One' : 'Many'}`), {
                  folder: deleting.path,
                  count: deletingCount,
                })
              : fillText(t('shell.studio.packages.deleteFile'), { file: deleting.path })
        }
        footer={
          <DialogFooter
            destructive
            finishText={t('shell.studio.packages.deleteOk')}
            cancelText={t('shell.studio.cancel')}
            finishing={deleteBusy}
            onFinish={() => deleting !== null && void deleteNode(deleting)}
            onCancel={() => setDeleting(null)}
          />
        }
        onCancel={() => setDeleting(null)}
      />
    </section>
  )
}
