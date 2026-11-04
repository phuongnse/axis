import { getAt, unescapePointerSegment } from './jsonPatch'
import { setOwn } from './ownProperty'
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

/** The keyed list of a policy: its grants, each keyed by name. */
export const policyKeyedLists: Record<string, string> = { '/grants': 'name' }

/** The keyed list of a process: its steps, each keyed by name. */
export const processKeyedLists: Record<string, string> = {
  '/steps': 'name',
}

/**
 * The keyed lists of a form: its sections and steps, each keyed by name, and the fields of each section of `content`,
 * keyed by `field`. The read route names a field's mark by the section's position in the form with the layer applied,
 * so there is one list for each section.
 */
export function formKeyedLists(content: unknown): Record<string, string> {
  const sections =
    typeof content === 'object' && content !== null ? (content as { sections?: unknown }).sections : undefined
  const lists: Record<string, string> = { '/sections': 'name', '/steps': 'name' }
  if (Array.isArray(sections)) {
    sections.forEach((_, index) => {
      setOwn(lists, `/sections/${index}/fields`, 'field')
    })
  }
  return lists
}

/** The maps of a text resource whose keys the layer marks: `/texts`, each text keyed by its property name. */
export const textKeyedMaps: readonly string[] = ['/texts']

/**
 * The elements that the read route's `marks` and `removed` name. Only a whole element counts, a pointer such as
 * `/fields/1`, and its key is read from `content`, the resource with the layer applied. A pointer to a property
 * of an element or of the root, and a list that is not in `keyedLists`, are left out. The key stays with the
 * element when the author moves it, which a position would not. A map in `maps`, such as `/texts`, has no positions:
 * a mark on `/texts/<key>` names that key, read from the pointer, when `content` has it.
 */
export function layerMarks(
  content: unknown,
  marks: StudioModuleResource['marks'],
  removed: StudioModuleResource['removed'],
  keyedLists: Record<string, string>,
  maps: readonly string[] = [],
): LayerElement[] {
  const elements: LayerElement[] = []
  for (const { path, mark } of marks) {
    const slash = path.lastIndexOf('/')
    const list = path.slice(0, slash)
    if (slash >= 0 && maps.includes(list)) {
      const key = unescapePointerSegment(path.slice(slash + 1))
      const map = getAt(content, list)
      if (typeof map === 'object' && map !== null && Object.hasOwn(map, key)) {
        elements.push({ list, key, mark })
      }
      continue
    }
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
