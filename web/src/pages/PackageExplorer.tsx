import { FileOutlined, FolderOpenOutlined, FolderOutlined, PlusOutlined, ReloadOutlined } from '@ant-design/icons'
import { Dropdown, Flex, Tree, type MenuProps, type TreeDataNode } from 'antd'
import { useContext, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { navigationItemLook } from '../platform/antTheme'
import { AxisButton } from '../platform/AxisButton'
import { Chevron } from '../platform/Chevron'
import { useText } from '../platform/texts'
import { ThemeModeContext, useDesignTokens } from '../platform/themeMode'
import { fileTree } from './packageTree'
import { PaneHeader } from './PaneHeader'

/** A node of the explorer: a file by its path, or a folder by its path with a trailing slash. */
export type ExplorerTarget = { kind: 'file'; path: string } | { kind: 'folder'; path: string }

/** What the explorer asks its page to do. Each folder path ends with a slash, and the root folder is `''`. */
export interface ExplorerActions {
  onOpen: (path: string) => void
  onNewFile: (folder: string) => void
  onNewFolder: (folder: string) => void
  onRename: (target: ExplorerTarget) => void
  onDelete: (target: ExplorerTarget) => void
  /** Moves a file or a folder into `folder`. */
  onMove: (target: ExplorerTarget, folder: string) => void
  onCopyPath: (target: ExplorerTarget) => void
  onRefresh: () => void
}

/** The folder a node's new file goes in: the folder itself, or the folder that holds the file. */
function folderOf(target: ExplorerTarget): string {
  return target.kind === 'folder' ? target.path : target.path.slice(0, target.path.lastIndexOf('/') + 1)
}

function targetOf(key: string): ExplorerTarget {
  return key.endsWith('/') ? { kind: 'folder', path: key } : { kind: 'file', path: key }
}

/** Every folder key of a tree. */
function folderKeys(nodes: readonly TreeDataNode[]): string[] {
  return nodes.flatMap((node) => (node.isLeaf ? [] : [String(node.key), ...folderKeys(node.children ?? [])]))
}

/** The height of a row of the tree: compact, as a code editor's file tree is, and a small icon button's height and more. */
const rowHeight = 28
/** The chevron's place, and the step each level moves in, so a child's icon sits a little right of its folder's. */
const switcherWidth = 16
const indentWidth = 12
/** The space between two rows, so they step by 34 px. */
const rowGap = 6

/**
 * Proposed, see #3978: a package's explorer, a tree of folders and files as a code editor's. A folder has its chevron
 * on its left, which turns down when it is open, and a folder icon, open or closed; a file has a file icon, so the two
 * read apart at a glance. A chosen or hovered row is marked across its whole width, the chevron and indent included, as
 * in a code editor, and each level steps in by 12 px. A file keeps the chevron's place empty, so its icon lines up with a folder's, only when the
 * tree has a folder: without one, the files start at the explorer's edge. A chosen file is marked as the navigation marks its entry, with the same tint, text colour and
 * corners, read through `navigationItemLook`. The title row, as high as the editor's tab row so their rules meet, holds
 * New, which offers New file and New folder, and Refresh. A node's actions are on a right click, or Shift+F10 or the
 * menu key on the focused node: New file and New folder in it, Rename, Delete and Copy path; F2 renames and Delete
 * deletes, as in a code editor. A file or folder dragged onto a folder moves into it, and onto the explorer's free
 * space to the package's root. A file with unsaved changes has a dot, and a folder that holds no file yet reads in
 * `textMuted`, as it is kept only once a file is in it. Without the developer role only the files, Copy path and
 * Refresh show.
 */
export function PackageExplorer({
  paths,
  emptyFolders,
  dirty,
  active,
  developer,
  locale,
  rowHeight: titleHeight,
  actions,
}: {
  paths: readonly string[]
  /** Folders made in this session that hold no file yet, each with a trailing slash. */
  emptyFolders: readonly string[]
  dirty: readonly string[]
  active: string
  developer: boolean
  locale: string
  /** The height of the title row, the same as the editor's tab row. */
  rowHeight: number
  actions: ExplorerActions
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const mode = useContext(ThemeModeContext)?.mode ?? 'light'
  const look = navigationItemLook(mode)
  const tree = useMemo(() => fileTree(paths, emptyFolders, locale), [paths, emptyFolders, locale])
  // Every folder lies under one at the root, so a root without folders means a tree without them.
  const flat = tree.every((node) => node.isLeaf)
  const allFolders = useMemo(() => folderKeys(tree), [tree])
  const [closed, setClosed] = useState<ReadonlySet<string>>(new Set())
  const [menuFor, setMenuFor] = useState<string | null>(null)
  const [focused, setFocused] = useState<string | null>(null)
  const [rootDrop, setRootDrop] = useState(false)
  const dragged = useRef<ExplorerTarget | null>(null)
  const expanded = allFolders.filter((key) => !closed.has(key))

  const menu: MenuProps['items'] = [
    ...(developer
      ? [
          { key: 'newFile', label: t('shell.studio.package.newFile') },
          { key: 'newFolder', label: t('shell.studio.package.newFolder') },
          { type: 'divider' as const },
          { key: 'rename', label: t('shell.studio.package.rename'), extra: 'F2' },
          { key: 'delete', label: t('shell.studio.delete'), danger: true, extra: t('shell.studio.package.deleteKey') },
          { type: 'divider' as const },
        ]
      : []),
    { key: 'copyPath', label: t('shell.studio.package.copyPath') },
  ]
  const run = (target: ExplorerTarget, key: string) => {
    setMenuFor(null)
    const folder = folderOf(target)
    if (key === 'newFile') actions.onNewFile(folder)
    if (key === 'newFolder') actions.onNewFolder(folder)
    if (key === 'rename') actions.onRename(target)
    if (key === 'delete') actions.onDelete(target)
    if (key === 'copyPath') actions.onCopyPath(target)
  }

  const onKeyDown = (event: KeyboardEvent) => {
    if (focused === null) {
      return
    }
    const target = targetOf(focused)
    if (event.key === 'ContextMenu' || (event.shiftKey && event.key === 'F10')) {
      event.preventDefault()
      setMenuFor(focused)
    } else if (developer && event.key === 'F2') {
      event.preventDefault()
      actions.onRename(target)
    } else if (developer && event.key === 'Delete') {
      event.preventDefault()
      actions.onDelete(target)
    }
  }

  const node = (data: TreeDataNode): ReactNode => {
    const key = String(data.key)
    const target = targetOf(key)
    const empty = !data.isLeaf && emptyFolders.includes(key) && (data.children?.length ?? 0) === 0
    const Icon = data.isLeaf ? FileOutlined : expanded.includes(key) ? FolderOpenOutlined : FolderOutlined
    return (
      <Dropdown
        trigger={['contextMenu']}
        open={menuFor === key}
        onOpenChange={(open) => setMenuFor(open ? key : null)}
        menu={{ items: menu, onClick: (info) => run(target, info.key) }}
      >
        <Flex
          component="span"
          align="center"
          gap={spacing.xs}
          data-testid={`package-node-${key}`}
          style={{ display: 'flex', minWidth: 0, height: rowHeight }}
          title={empty ? t('shell.studio.package.emptyFolder') : key}
        >
          <Icon aria-hidden style={{ flex: 'none', color: colour.textMuted }} />
          <span
            style={{
              flex: 1,
              minWidth: 0,
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              color: empty ? colour.textMuted : undefined,
            }}
          >
            {String(data.title)}
          </span>
          {dirty.includes(key) && (
            <span
              role="img"
              aria-label={t('shell.studio.package.unsaved')}
              style={{ width: 6, height: 6, borderRadius: '50%', background: colour.primary, flex: 'none' }}
            />
          )}
        </Flex>
      </Dropdown>
    )
  }

  return (
    <nav
      aria-label={t('shell.studio.package.files')}
      data-testid="package-explorer"
      style={{ display: 'flex', flexDirection: 'column', minHeight: 0, height: '100%' }}
    >
      {/* A chosen row is marked as the navigation marks its entry, and a row under the pointer as its entry is, across the row. */}
      <style>
        {[
          `.axis-file-tree .ant-tree-treenode { border-radius: ${look.radius}px; padding: 0 ${spacing.xs}px; margin-bottom: ${rowGap}px; }`,
          `.axis-file-tree .ant-tree-treenode:hover, .axis-file-tree .ant-tree-treenode-active { background: ${look.hover}; }`,
          `.axis-file-tree .ant-tree-treenode-selected, .axis-file-tree .ant-tree-treenode-selected:hover { background: ${look.selected}; color: ${colour.primaryText}; }`,
          `.axis-file-tree .ant-tree-treenode-selected .anticon { color: ${colour.primaryText} !important; }`,
          `.axis-file-tree .ant-tree-node-content-wrapper, .axis-file-tree .ant-tree-node-content-wrapper:hover, .axis-file-tree .ant-tree-node-content-wrapper.ant-tree-node-selected { background: transparent; color: inherit; min-height: ${rowHeight}px; line-height: ${rowHeight}px; padding-inline: 0; }`,
          `.axis-file-tree .ant-tree-switcher { width: ${switcherWidth}px; line-height: ${rowHeight}px; height: ${rowHeight}px; margin-inline-end: ${spacing.xs}px; }`,
          `.axis-file-tree .ant-tree-switcher:hover, .axis-file-tree .ant-tree-switcher:not(.ant-tree-switcher-noop):hover:before { background: transparent; }`,
          `.axis-file-tree .ant-tree-indent-unit { width: ${indentWidth}px; }`,
          `.axis-file-tree-flat .ant-tree-switcher-noop { display: none; }`,
        ].join(' ')}
      </style>
      <PaneHeader
        title={t('shell.studio.package.files')}
        height={titleHeight}
        actions={
          <Flex gap={spacing.xs}>
            {developer && (
              <Dropdown
                trigger={['click']}
                menu={{
                  items: [
                    { key: 'newFile', label: t('shell.studio.package.newFile') },
                    { key: 'newFolder', label: t('shell.studio.package.newFolder') },
                  ],
                  onClick: (info) => (info.key === 'newFile' ? actions.onNewFile('') : actions.onNewFolder('')),
                }}
              >
                <AxisButton
                  role="icon"
                  size="small"
                  nameKey="shell.studio.package.new"
                  icon={<PlusOutlined />}
                  data-testid="package-new"
                />
              </Dropdown>
            )}
            <AxisButton
              role="icon"
              size="small"
              nameKey="shell.studio.package.refresh"
              icon={<ReloadOutlined />}
              onClick={actions.onRefresh}
            />
          </Flex>
        }
      />
      <div
        onKeyDown={onKeyDown}
        onDragOver={(event) => {
          if (developer && dragged.current !== null) {
            event.preventDefault()
            setRootDrop(true)
          }
        }}
        onDragLeave={() => setRootDrop(false)}
        onDrop={(event) => {
          setRootDrop(false)
          const target = dragged.current
          if (target !== null && event.target === event.currentTarget && folderOf(target) !== '') {
            event.preventDefault()
            actions.onMove(target, '')
          }
        }}
        style={{
          flex: 1,
          minHeight: 0,
          overflowY: 'auto',
          overscrollBehavior: 'contain',
          padding: spacing.xs,
          outline: rootDrop ? `2px solid ${colour.focusRing}` : undefined,
          outlineOffset: -2,
        }}
      >
        <Tree
          className={flat ? 'axis-file-tree axis-file-tree-flat' : 'axis-file-tree'}
          treeData={tree}
          blockNode
          switcherIcon={({ expanded: open }: { expanded?: boolean }) => <Chevron direction={open ? 'down' : 'right'} />}
          expandedKeys={expanded}
          onExpand={(keys) => {
            const open = new Set(keys.map(String))
            setClosed(new Set(allFolders.filter((key) => !open.has(key))))
          }}
          selectedKeys={[active]}
          onActiveChange={(key) => setFocused(key === null ? null : String(key))}
          onSelect={(_keys, info) => {
            const key = String(info.node.key)
            if (info.node.isLeaf) {
              actions.onOpen(key)
            } else {
              setClosed((current) => {
                const next = new Set(current)
                if (next.has(key)) next.delete(key)
                else next.add(key)
                return next
              })
            }
          }}
          draggable={developer ? { icon: false } : false}
          onDragStart={(info) => {
            dragged.current = targetOf(String(info.node.key))
          }}
          onDragEnd={() => {
            dragged.current = null
          }}
          allowDrop={({ dropNode, dropPosition }) => !dropNode.isLeaf && dropPosition === 0}
          onDrop={(info) => {
            const target = targetOf(String(info.dragNode.key))
            const folder = String(info.node.key)
            dragged.current = null
            const inside = target.kind === 'folder' && folder.startsWith(target.path)
            if (!info.node.isLeaf && folderOf(target) !== folder && !inside) {
              actions.onMove(target, folder)
            }
          }}
          titleRender={node}
        />
      </div>
    </nav>
  )
}
