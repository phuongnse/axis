import starters from './studioStarters.json'

interface Starter {
  content: Record<string, unknown>
  emptyReferences: string[]
}

const byKind: Record<string, Starter> = starters

/** The kinds that have a starter, sorted ordinally. They are the kinds the create route accepts. */
export const starterKinds: string[] = Object.keys(byKind).toSorted()

/**
 * The kebab-case form of a name, as the server's `StudioFileNames.Kebab` forms it: a hyphen goes before a capital that
 * follows a lower-case letter or a digit, or before the last capital of an acronym that a lower-case letter follows.
 */
export function kebabName(name: string): string {
  let result = ''
  for (let index = 0; index < name.length; index++) {
    const character = name[index]
    if (index > 0 && isUpper(character)) {
      const previous = name[index - 1]
      const next = name[index + 1]
      if (isLower(previous) || isDigit(previous) || (isUpper(previous) && next !== undefined && isLower(next))) {
        result += '-'
      }
    }
    result += character
  }
  return result.toLowerCase()
}

const reservedApplicationNames = new Set(['api', 'health', 'assets', 'studio', 'account', 'users'])

/** Whether `name` may name a new application, as the create route checks it: letters and digits, and short enough in both forms. */
export function isApplicationName(name: string): boolean {
  const kebab = kebabName(name)
  return (
    /^[A-Za-z][A-Za-z0-9]*$/.test(name) &&
    name.length <= 60 &&
    kebab.length <= 60 &&
    !reservedApplicationNames.has(kebab)
  )
}

function isUpper(character: string): boolean {
  return character !== character.toLowerCase()
}

function isLower(character: string): boolean {
  return character !== character.toUpperCase()
}

function isDigit(character: string): boolean {
  return character >= '0' && character <= '9'
}

/** The name of a new text resource: `Texts` and the locale, such as `TextsVi` for `vi` and `TextsPtBr` for `pt-BR`. */
export function textResourceName(locale: string): string {
  return (
    'Texts' +
    locale
      .split('-')
      .map((part) => part.charAt(0).toUpperCase() + part.slice(1).toLowerCase())
      .join('')
  )
}

/** The JSON pointers of the references a kind's starter leaves empty, for the author to choose. */
export function emptyReferences(kind: string): string[] {
  return byKind[kind]?.emptyReferences ?? []
}

/** What a new resource needs besides its kind. `locale` and `texts` are for a `text`, and `name` forms a site's path. */
export interface StarterOptions {
  id: string
  name: string
  locale?: string
  texts?: Record<string, string>
}

/**
 * The body of a create for `kind`: `id`, `kind`, `name` and `formatVersion`, then a copy of the starter's content.
 * A `text` gets its `locale` and `texts`, and a `site` gets the kebab-case name as its `path`.
 */
export function createStarter(kind: string, options: StarterOptions): Record<string, unknown> {
  const starter = byKind[kind]
  if (starter === undefined) {
    throw new Error(`There is no starter for the kind '${kind}'.`)
  }
  const content = structuredClone(starter.content)
  const body: Record<string, unknown> = { id: options.id, kind, name: options.name, formatVersion: 1 }
  if (kind === 'text') {
    body.locale = options.locale ?? ''
    body.texts = { ...options.texts }
    return body
  }
  if (kind === 'site') {
    body.path = kebabName(options.name)
  }
  return { ...body, ...content }
}
