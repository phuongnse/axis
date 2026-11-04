import { getAt } from './jsonPatch'
import type { StudioModuleResource } from './studio'

/** What a customization layer did to a list element. */
export type LayerMark = 'added' | 'changed' | 'removed'

/** One element of a keyed list that the layer added, changed or removed, found by the list's pointer and its key. */
export interface LayerElement {
  list: string
  key: string
  mark: LayerMark
}

/** The keyed lists of an entity, each as its pointer and the property that holds the key of its elements. */
export const entityKeyedLists: Record<string, string> = {
  '/fields': 'name',
  '/validations': 'name',
  '/indexes': 'name',
}

/** The keyed list of a process: its steps, each keyed by name. */
export const processKeyedLists: Record<string, string> = {
  '/steps': 'name',
}

/**
 * The elements that the read route's `marks` and `removed` name. Only a whole element counts, a pointer such as
 * `/fields/1`, and its key is read from `content`, the resource with the layer applied. A pointer to a property
 * of an element or of the root, and a list that is not in `keyedLists`, are left out. The key stays with the
 * element when the author moves it, which a position would not.
 */
export function layerMarks(
  content: unknown,
  marks: StudioModuleResource['marks'],
  removed: StudioModuleResource['removed'],
  keyedLists: Record<string, string>,
): LayerElement[] {
  const elements: LayerElement[] = []
  for (const { path, mark } of marks) {
    const slash = path.lastIndexOf('/')
    const list = path.slice(0, slash)
    if (slash < 0 || !/^\d+$/.test(path.slice(slash + 1)) || !Object.hasOwn(keyedLists, list)) {
      continue
    }
    const element = getAt(content, path)
    const key =
      typeof element === 'object' && element !== null
        ? (element as Record<string, unknown>)[keyedLists[list]]
        : undefined
    if (typeof key === 'string') {
      elements.push({ list, key, mark })
    }
  }
  for (const { list, key } of removed) {
    if (Object.hasOwn(keyedLists, list)) {
      elements.push({ list, key, mark: 'removed' })
    }
  }
  return elements
}

/** The mark of the element with `key` in `list`, or `undefined` when the layer left it alone. */
export function markOf(elements: LayerElement[], list: string, key: string): LayerMark | undefined {
  return elements.find((element) => element.list === list && element.key === key)?.mark
}
