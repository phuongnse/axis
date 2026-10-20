/** `my-tasks` as `myTasks`, and `MyTasks` as `myTasks`. */
function camelCase(text: string): string {
  const [first, ...rest] = text.split(/[-_ ]+/).filter((part) => part !== '')
  if (first === undefined) {
    return ''
  }
  const head = first.charAt(0).toLowerCase() + first.slice(1)
  return head + rest.map((part) => part.charAt(0).toUpperCase() + part.slice(1)).join('')
}

/**
 * A text key for the label at `pointer`, which ends with `/textKey`. It is the kind's folder, the resource's name and
 * the path to the label, such as `pages.myTasks.title`. The folder is the first segment of `file`, and the manifest at
 * the root is `application`. A number follows the key while `taken` holds it.
 */
export function labelKey(file: string, name: string | null, pointer: string, taken: ReadonlySet<string>): string {
  const segments = file.split('/')
  const folder = segments.length > 1 ? camelCase(segments[0]) : 'application'
  const path = pointer
    .split('/')
    .slice(1)
    .map((segment) => segment.replaceAll('~1', '/').replaceAll('~0', '~'))
  if (path[path.length - 1] === 'textKey') {
    path.pop()
  }
  const parts = [folder, ...(name ? [camelCase(name)] : []), ...path].filter((part) => part !== '')
  const key = parts.join('.')
  if (!taken.has(key)) {
    return key
  }
  for (let number = 2; ; number++) {
    if (!taken.has(`${key}${number}`)) {
      return `${key}${number}`
    }
  }
}
