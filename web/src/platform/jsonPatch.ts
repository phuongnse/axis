/** One operation of a JSON Patch (RFC 6902). Studio sends only these three. */
export type JsonPatchOperation = { op: 'add' | 'remove' | 'replace'; path: string; value?: unknown }

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function escapeSegment(segment: string): string {
  return segment.replaceAll('~', '~0').replaceAll('/', '~1')
}

function unescapeSegment(segment: string): string {
  return segment.replaceAll('~1', '/').replaceAll('~0', '~')
}

function segmentsOf(pointer: string): string[] {
  return pointer === '' ? [] : pointer.split('/').slice(1).map(unescapeSegment)
}

function deepEqual(left: unknown, right: unknown): boolean {
  if (left === right) {
    return true
  }
  if (Array.isArray(left) && Array.isArray(right)) {
    return left.length === right.length && left.every((item, index) => deepEqual(item, right[index]))
  }
  if (isObject(left) && isObject(right)) {
    const keys = Object.keys(left)
    return (
      keys.length === Object.keys(right).length &&
      keys.every((key) => Object.hasOwn(right, key) && deepEqual(left[key], right[key]))
    )
  }
  return false
}

/**
 * The JSON Patch that turns `before` into `after`. Objects are compared key by key and arrays of the same
 * length item by item. Any other difference, including an array whose length changed, replaces the whole
 * value at `path`. Added keys come last, so they follow the key order of `after`.
 */
export function diffPatch(before: unknown, after: unknown, path = ''): JsonPatchOperation[] {
  if (deepEqual(before, after)) {
    return []
  }
  if (isObject(before) && isObject(after)) {
    const operations: JsonPatchOperation[] = []
    for (const key of Object.keys(before)) {
      const child = `${path}/${escapeSegment(key)}`
      if (!Object.hasOwn(after, key)) {
        operations.push({ op: 'remove', path: child })
      } else {
        operations.push(...diffPatch(before[key], after[key], child))
      }
    }
    for (const key of Object.keys(after)) {
      if (!Object.hasOwn(before, key)) {
        operations.push({ op: 'add', path: `${path}/${escapeSegment(key)}`, value: after[key] })
      }
    }
    return operations
  }
  if (Array.isArray(before) && Array.isArray(after) && before.length === after.length) {
    return before.flatMap((item, index) => diffPatch(item, after[index], `${path}/${index}`))
  }
  return [{ op: 'replace', path, value: after }]
}

/** The value at a JSON Pointer, or `undefined` when nothing is there. */
export function getAt(value: unknown, pointer: string): unknown {
  let current = value
  for (const segment of segmentsOf(pointer)) {
    if (Array.isArray(current)) {
      current = current[Number(segment)]
    } else if (isObject(current)) {
      current = current[segment]
    } else {
      return undefined
    }
  }
  return current
}

/**
 * A copy of `value` with `next` at the JSON Pointer. Only the objects and arrays along the pointer are copied.
 * An `undefined` `next` deletes an object key and removes an array index.
 */
export function setAt(value: unknown, pointer: string, next: unknown): unknown {
  return setSegments(value, segmentsOf(pointer), next)
}

function setSegments(value: unknown, segments: string[], next: unknown): unknown {
  if (segments.length === 0) {
    return next
  }
  const [head, ...rest] = segments
  if (Array.isArray(value)) {
    const index = Number(head)
    if (rest.length === 0 && next === undefined) {
      return value.filter((_, position) => position !== index)
    }
    return value.map((item, position) => (position === index ? setSegments(item, rest, next) : item))
  }
  const target = isObject(value) ? value : {}
  if (rest.length === 0 && next === undefined) {
    const { [head]: _removed, ...others } = target
    return others
  }
  return { ...target, [head]: setSegments(target[head], rest, next) }
}
