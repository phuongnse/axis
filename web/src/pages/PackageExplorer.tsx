import {
  FileAddOutlined,
  FileOutlined,
  FolderAddOutlined,
  FolderOpenOutlined,
  FolderOutlined,
  MinusSquareOutlined,
  MoreOutlined,
  ReloadOutlined,
} from '@ant-design/icons'
import { Dropdown, Flex, Tree, Typography, type MenuProps, type TreeDataNode } from 'antd'
import { useMemo, useState, type Key, type ReactNode } from 'react'
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

/** Every folder key of a tree, to open them all. */
function folderKeys(nodes: readonly TreeDataNode[]): string[] {
  return nodes.flatMap((node) => (node.isLeaf ? [] : [String(node.key), ...folderKeys(node.children ?? [])]))
}

/** The folder a node's new file goes in: the folder itself, or the folder that holds the file. */
function folderOf(target: ExplorerTarget): string {
  return target.kind === 'folder' ? target.path : target.path.slice(0, target.path.lastIndexOf('/') + 1)
}

/**
 * Proposed, see #3978: a package's explorer, as a code editor's file explorer. Its title row holds New file, New folder,
 * Collapse all and Refresh. A file opens on a click. Each node has its actions in a "…" menu and on a right click: New
 * file and New folder in it, Rename, Delete and Copy path. A file or folder dragged onto a folder moves into it. A file
 * with unsaved changes has a dot, and a folder that holds no file yet reads its name in `textMuted`, as it is kept only
 * once a file is in it. Without the developer role only the files and Refresh show.
 */
export function PackageExplorer({
  paths,
  emptyFolders,
  dirty,
  active,
  developer,
  locale,
  actions,
}: {
  paths: readonly string[]
  /** Folders made in this session that hold no file yet, each with a trailing slash. */
  emptyFolders: readonly string[]
  dirty: readonly string[]
  active: string
  developer: boolean
  locale: string
  actions: ExplorerActions
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const tree = useMemo(() => fileTree(paths, emptyFolders, locale), [paths, emptyFolders, locale])
  const allFolders = useMemo(() => folderKeys(tree), [tree])
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(new Set())
  const expanded = allFolders.filter((key) => !collapsed.has(key))

  const menu = (): MenuProps['items'] => [
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
  const onMenu = (target: ExplorerTarget) => (info: { key: string }) => {
    const folder = folderOf(target)
    if (info.key === 'newFile') actions.onNewFile(folder)
    if (info.key === 'newFolder') actions.onNewFolder(folder)
    if (info.key === 'rename') actions.onRename(target)
    if (info.key === 'delete') actions.onDelete(target)
    if (info.key === 'copyPath') actions.onCopyPath(target)
  }

  const node = (data: TreeDataNode): ReactNode => {
    const key = String(data.key)
    const target: ExplorerTarget = data.isLeaf ? { kind: 'file', path: key } : { kind: 'folder', path: key }
    const empty = !data.isLeaf && emptyFolders.includes(key) && (data.children?.length ?? 0) === 0
    return (
      <Dropdown trigger={['contextMenu']} menu={{ items: menu(), onClick: onMenu(target) }}>
        <Flex
          component="span"
          align="center"
          gap={spacing.xs}
          className="axis-explorer-node"
          data-testid={`package-node-${key}`}
          style={{ display: 'flex', minWidth: 0 }}
        >
          <span aria-hidden style={{ flex: 'none', color: colour.textMuted }}>
            {data.isLeaf ? <FileOutlined /> : expanded.includes(key) ? <FolderOpenOutlined /> : <FolderOutlined />}
          </span>
          <span
            style={{
              flex: 1,
              minWidth: 0,
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              color: empty ? colour.textMuted : undefined,
            }}
            title={empty ? t('shell.studio.package.emptyFolder') : undefined}
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
          <span className="axis-explorer-more" onClick={(event) => event.stopPropagation()}>
            <Dropdown trigger={['click']} menu={{ items: menu(), onClick: onMenu(target) }}>
              <AxisButton role="icon" size="small" nameKey="shell.studio.package.nodeActions" icon={<MoreOutlined />} />
            </Dropdown>
          </span>
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
      {/* The "…" of a node shows while the pointer is on it, or the node or its menu has the focus. */}
      <style>{`.axis-explorer-more { visibility: hidden; } .ant-tree-treenode:hover .axis-explorer-more, .ant-tree-treenode:focus-within .axis-explorer-more, .ant-tree-treenode-selected .axis-explorer-more { visibility: visible; }`}</style>
      <Flex
        align="center"
        justify="space-between"
        style={{ minHeight: 32, paddingInlineStart: spacing.sm, borderBottom: `1px solid ${colour.borderSubtle}` }}
      >
        <Typography.Text strong>{t('shell.studio.package.files')}</Typography.Text>
        <Flex>
          {developer && (
            <>
              <AxisButton
                role="icon"
                size="small"
                nameKey="shell.studio.package.newFile"
                icon={<FileAddOutlined />}
                data-testid="package-new-file"
                onClick={() => actions.onNewFile('')}
              />
              <AxisButton
                role="icon"
                size="small"
                nameKey="shell.studio.package.newFolder"
                icon={<FolderAddOutlined />}
                data-testid="package-new-folder"
                onClick={() => actions.onNewFolder('')}
              />
            </>
          )}
          <AxisButton
            role="icon"
            size="small"
            nameKey="shell.studio.package.collapseAll"
            icon={<MinusSquareOutlined />}
            onClick={() => setCollapsed(new Set(allFolders))}
          />
          <AxisButton
            role="icon"
            size="small"
            nameKey="shell.studio.package.refresh"
            icon={<ReloadOutlined />}
            onClick={actions.onRefresh}
          />
        </Flex>
      </Flex>
      <div
        style={{ flex: 1, minHeight: 0, overflowY: 'auto', overscrollBehavior: 'contain', paddingBlock: spacing.xs }}
      >
        <Tree
          treeData={tree}
          blockNode
          expandedKeys={expanded}
          onExpand={(keys: Key[]) => {
            const open = new Set(keys.map(String))
            setCollapsed(new Set(allFolders.filter((key) => !open.has(key))))
          }}
          selectedKeys={[active]}
          onSelect={(_keys, info) => {
            if (info.node.isLeaf) {
              actions.onOpen(String(info.node.key))
            } else {
              const key = String(info.node.key)
              setCollapsed((current) => {
                const next = new Set(current)
                if (next.has(key)) next.delete(key)
                else next.add(key)
                return next
              })
            }
          }}
          draggable={developer ? { icon: false, nodeDraggable: () => true } : false}
          allowDrop={({ dropNode, dropPosition }) => !dropNode.isLeaf && dropPosition === 0}
          onDrop={(info) => {
            const dragged = String(info.dragNode.key)
            const folder = info.node.isLeaf ? '' : String(info.node.key)
            const target: ExplorerTarget = info.dragNode.isLeaf
              ? { kind: 'file', path: dragged }
              : { kind: 'folder', path: dragged }
            if (folderOf(target) !== folder && !folder.startsWith(target.kind === 'folder' ? dragged : '\0')) {
              actions.onMove(target, folder)
            }
          }}
          titleRender={node}
        />
      </div>
    </nav>
  )
}
