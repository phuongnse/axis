import { EllipsisOutlined, FileAddOutlined, FolderAddOutlined, ReloadOutlined } from '@ant-design/icons'
import { Dropdown, Flex, Menu, Typography, type MenuProps, type TreeDataNode } from 'antd'
import { useMemo, useState, type DragEvent, type ReactNode } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { fileTree } from './packageTree'

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

/** Every folder key of a tree. */
function folderKeys(nodes: readonly TreeDataNode[]): string[] {
  return nodes.flatMap((node) => (node.isLeaf ? [] : [String(node.key), ...folderKeys(node.children ?? [])]))
}

const dragType = 'application/x-axis-package-node'

/**
 * Proposed, see #3978: a package's explorer, drawn by the same inline `Menu` as the shell's navigation, so a file is
 * chosen, marked and indented as a navigation entry is, and a folder opens and closes as a group does. Its title row,
 * as high as the editor's tab row so their rules meet, holds New file, New folder and Refresh. Each node has its
 * actions in a "…" menu and on a right click: New file and New folder in it, Rename, Delete and Copy path. A file or
 * folder dragged onto a folder moves into it, and onto the explorer's free space to the package's root. A file with
 * unsaved changes has a dot, and a folder that holds no file yet reads in `textMuted`, as it is kept only once a file is
 * in it. Without the developer role only the files and Refresh show.
 */
export function PackageExplorer({
  paths,
  emptyFolders,
  dirty,
  active,
  developer,
  locale,
  rowHeight,
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
  const tree = useMemo(() => fileTree(paths, emptyFolders, locale), [paths, emptyFolders, locale])
  const allFolders = useMemo(() => folderKeys(tree), [tree])
  const [closed, setClosed] = useState<ReadonlySet<string>>(new Set())
  const [dropOn, setDropOn] = useState<string | null>(null)

  const menu: MenuProps['items'] = [
    ...(developer
      ? [
          { key: 'newFile', label: t('shell.studio.package.newFile') },
          { key: 'newFolder', label: t('shell.studio.package.newFolder') },
          { type: 'divider' as const },
          { key: 'rename', label: t('shell.studio.package.rename') },
          { key: 'delete', label: t('shell.studio.delete'), danger: true },
          { type: 'divider' as const },
        ]
      : []),
    { key: 'copyPath', label: t('shell.studio.package.copyPath') },
  ]
  const onMenu = (target: ExplorerTarget) => (info: { key: string; domEvent: { stopPropagation: () => void } }) => {
    info.domEvent.stopPropagation()
    const folder = folderOf(target)
    if (info.key === 'newFile') actions.onNewFile(folder)
    if (info.key === 'newFolder') actions.onNewFolder(folder)
    if (info.key === 'rename') actions.onRename(target)
    if (info.key === 'delete') actions.onDelete(target)
    if (info.key === 'copyPath') actions.onCopyPath(target)
  }

  const drop = (folder: string) => ({
    onDragOver: (event: DragEvent) => {
      if (developer && event.dataTransfer.types.includes(dragType)) {
        event.preventDefault()
        event.stopPropagation()
        setDropOn(folder)
      }
    },
    onDragLeave: () => setDropOn((current) => (current === folder ? null : current)),
    onDrop: (event: DragEvent) => {
      event.preventDefault()
      event.stopPropagation()
      setDropOn(null)
      const target = JSON.parse(event.dataTransfer.getData(dragType)) as ExplorerTarget
      const inside = target.kind === 'folder' && folder.startsWith(target.path)
      if (folderOf(target) !== folder && !inside && target.path !== folder) {
        actions.onMove(target, folder)
      }
    },
  })

  const label = (target: ExplorerTarget, name: string, empty: boolean): ReactNode => (
    <Dropdown trigger={['contextMenu']} menu={{ items: menu, onClick: onMenu(target) }}>
      <Flex
        component="span"
        align="center"
        gap={spacing.xs}
        className="axis-explorer-node"
        data-testid={`package-node-${target.path}`}
        draggable={developer}
        onDragStart={(event: DragEvent) => event.dataTransfer.setData(dragType, JSON.stringify(target))}
        {...(target.kind === 'folder' ? drop(target.path) : {})}
        style={{
          display: 'flex',
          minWidth: 0,
          outline: dropOn === target.path ? `2px solid ${colour.focusRing}` : undefined,
          outlineOffset: -2,
        }}
      >
        <span
          title={empty ? t('shell.studio.package.emptyFolder') : target.path}
          style={{
            flex: 1,
            minWidth: 0,
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            color: empty ? colour.textMuted : undefined,
          }}
        >
          {name}
        </span>
        {dirty.includes(target.path) && (
          <span
            role="img"
            aria-label={t('shell.studio.package.unsaved')}
            style={{ width: 6, height: 6, borderRadius: '50%', background: colour.primary, flex: 'none' }}
          />
        )}
        <span className="axis-explorer-more" onClick={(event) => event.stopPropagation()}>
          <Dropdown trigger={['click']} menu={{ items: menu, onClick: onMenu(target) }}>
            <AxisButton
              role="icon"
              size="small"
              nameKey="shell.studio.package.nodeActions"
              icon={<EllipsisOutlined />}
            />
          </Dropdown>
        </span>
      </Flex>
    </Dropdown>
  )

  const items = (nodes: readonly TreeDataNode[]): NonNullable<MenuProps['items']> =>
    nodes.map((node) => {
      const key = String(node.key)
      const name = String(node.title)
      if (node.isLeaf) {
        return { key, label: label({ kind: 'file', path: key }, name, false) }
      }
      const children = node.children ?? []
      return children.length === 0
        ? { key, label: label({ kind: 'folder', path: key }, name, true) }
        : { key, label: label({ kind: 'folder', path: key }, name, false), children: items(children) }
    })

  return (
    <nav
      aria-label={t('shell.studio.package.files')}
      data-testid="package-explorer"
      style={{ display: 'flex', flexDirection: 'column', minHeight: 0, height: '100%' }}
    >
      {/* The "…" of a node shows while the pointer is on it, or the node or its menu has the focus. */}
      <style>{`.axis-explorer-more { visibility: hidden; } .axis-explorer .ant-menu-item:hover .axis-explorer-more, .axis-explorer .ant-menu-submenu-title:hover .axis-explorer-more, .axis-explorer .ant-menu-item-selected .axis-explorer-more, .axis-explorer-node:focus-within .axis-explorer-more { visibility: visible; }`}</style>
      <Flex
        align="center"
        justify="space-between"
        style={{
          flex: 'none',
          height: rowHeight,
          paddingInline: spacing.md,
          borderBottom: `1px solid ${colour.borderSubtle}`,
        }}
      >
        <Typography.Text strong>{t('shell.studio.package.files')}</Typography.Text>
        <Flex>
          {developer && (
            <>
              <AxisButton
                role="icon"
                nameKey="shell.studio.package.newFile"
                icon={<FileAddOutlined />}
                data-testid="package-new-file"
                onClick={() => actions.onNewFile('')}
              />
              <AxisButton
                role="icon"
                nameKey="shell.studio.package.newFolder"
                icon={<FolderAddOutlined />}
                data-testid="package-new-folder"
                onClick={() => actions.onNewFolder('')}
              />
            </>
          )}
          <AxisButton
            role="icon"
            nameKey="shell.studio.package.refresh"
            icon={<ReloadOutlined />}
            onClick={actions.onRefresh}
          />
        </Flex>
      </Flex>
      <div
        {...drop('')}
        style={{
          flex: 1,
          minHeight: 0,
          overflowY: 'auto',
          overscrollBehavior: 'contain',
          paddingBlock: spacing.xs,
          outline: dropOn === '' ? `2px solid ${colour.focusRing}` : undefined,
          outlineOffset: -2,
        }}
      >
        <Menu
          className="axis-explorer"
          mode="inline"
          style={{ borderInlineEnd: 'none' }}
          selectedKeys={[active]}
          openKeys={allFolders.filter((key) => !closed.has(key))}
          onOpenChange={(open) => setClosed(new Set(allFolders.filter((key) => !open.includes(key))))}
          onClick={(info) => {
            if (!info.key.endsWith('/')) {
              actions.onOpen(info.key)
            }
          }}
          items={items(tree)}
        />
      </div>
    </nav>
  )
}
