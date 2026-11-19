import type { TreeDataNode } from 'antd'
import { nameCompare } from '../platform/listOrder'

/**
 * The files as a tree of folders, each folder before the files beside it and each group sorted by name, see
 * docs/reference/actions-and-controls.md#list-order. `folders` adds folders that hold no file yet. A folder's key is its
 * path with a trailing slash.
 */
export function fileTree(paths: readonly string[], folders: readonly string[], locale: string): TreeDataNode[] {
  interface Folder {
    folders: Map<string, Folder>
    files: string[]
  }
  const root: Folder = { folders: new Map(), files: [] }
  const folderAt = (parts: readonly string[]) => {
    let folder = root
    for (const part of parts) {
      const next = folder.folders.get(part) ?? { folders: new Map(), files: [] }
      folder.folders.set(part, next)
      folder = next
    }
    return folder
  }
  for (const path of paths) {
    folderAt(path.split('/').slice(0, -1)).files.push(path)
  }
  for (const folder of folders) {
    folderAt(folder.split('/').filter((part) => part !== ''))
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
