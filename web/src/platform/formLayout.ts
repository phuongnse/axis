import type { StudioNameField, StudioNames } from './studio'

type Content = Record<string, unknown>

/** Where a field goes: the section by its index and the position in the section's fields. */
export interface FieldPlace {
  section: number
  index: number
}

/** A step of the outline, or `null` for the sections that no step lists, and the section indexes in it. */
export interface OutlineGroup {
  step: { pointer: string; titleKey: string } | null
  sections: number[]
}

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function itemsOf(draft: Content, key: string): Content[] {
  const items = draft[key]
  return Array.isArray(items) ? items.filter(isContent) : []
}

/** The fields of a section, or none. */
function fieldsOf(section: unknown): unknown[] {
  return isContent(section) && Array.isArray(section.fields) ? section.fields : []
}

/**
 * The fields of the entity that the draft names, without those that a section lists. Names match without regard to
 * letter case. A child collection counts as a field. Nothing is listed while the names or the entity are missing.
 */
export function paletteFields(draft: Content, names: StudioNames | undefined): StudioNameField[] {
  if (names === undefined || typeof draft.entity !== 'string') {
    return []
  }
  const wanted = draft.entity.toLowerCase()
  const entity = names.entities.find((item) => item.name.toLowerCase() === wanted)
  if (entity === undefined) {
    return []
  }
  const listed = new Set(
    (Array.isArray(draft.sections) ? draft.sections : []).flatMap((section) =>
      fieldsOf(section).flatMap((item) =>
        isContent(item) && typeof item.field === 'string' ? [item.field.toLowerCase()] : [],
      ),
    ),
  )
  return entity.fields.filter((field) => !listed.has(field.name.toLowerCase()))
}

function sectionCount(draft: Content): number {
  return Array.isArray(draft.sections) ? draft.sections.length : 0
}

/** The index of the first section that has the name, without regard to letter case, or -1. */
function sectionNamed(draft: Content, name: string): number {
  const wanted = name.toLowerCase()
  return (Array.isArray(draft.sections) ? draft.sections : []).findIndex(
    (section) => isContent(section) && typeof section.name === 'string' && section.name.toLowerCase() === wanted,
  )
}

/** The end of a section, or the position after one of its fields when `field` is given. */
function placeIn(draft: Content, section: number, field?: number): FieldPlace | undefined {
  const sections = Array.isArray(draft.sections) ? draft.sections : []
  if (section >= sections.length) {
    return undefined
  }
  const count = fieldsOf(sections[section]).length
  return { section, index: field === undefined ? count : Math.min(field + 1, count) }
}

/**
 * Where the palette inserts a field, given the pointer of the selected element. A section takes it at its end, a
 * field after that field, and a step at the end of the step's first section. Any other selection, and one that names
 * nothing in the draft, falls back to the end of the first section. `undefined` when the draft has no section.
 */
export function insertionPlace(draft: Content, selected: string | null): FieldPlace | undefined {
  if (sectionCount(draft) === 0) {
    return undefined
  }
  const fallback = placeIn(draft, 0)
  if (selected === null) {
    return fallback
  }
  const field = /^\/sections\/(\d+)\/fields\/(\d+)$/.exec(selected)
  if (field !== null) {
    return placeIn(draft, Number(field[1]), Number(field[2])) ?? fallback
  }
  const section = /^\/sections\/(\d+)$/.exec(selected)
  if (section !== null) {
    return placeIn(draft, Number(section[1])) ?? fallback
  }
  const step = /^\/steps\/(\d+)$/.exec(selected)
  if (step !== null) {
    const named = itemsOf(draft, 'steps')[Number(step[1])]?.sections
    const first = Array.isArray(named)
      ? named.map((name) => (typeof name === 'string' ? sectionNamed(draft, name) : -1)).find((index) => index >= 0)
      : undefined
    return (first === undefined ? undefined : placeIn(draft, first)) ?? fallback
  }
  return fallback
}

/** Where a drop on the pointer of an outline section or field inserts a field. `undefined` for any other pointer. */
export function dropPlace(draft: Content, pointer: string): FieldPlace | undefined {
  const field = /^\/sections\/(\d+)\/fields\/(\d+)$/.exec(pointer)
  if (field !== null) {
    return placeIn(draft, Number(field[1]), Number(field[2]))
  }
  const section = /^\/sections\/(\d+)$/.exec(pointer)
  return section === null ? undefined : placeIn(draft, Number(section[1]))
}

/** Where a drop on the pointer of an outline section or field moves a field. A field takes the place of the field it is dropped on, and a section takes it at its end. `undefined` for any other pointer. */
export function movePlace(draft: Content, pointer: string): FieldPlace | undefined {
  const sections = Array.isArray(draft.sections) ? draft.sections : []
  const field = /^\/sections\/(\d+)\/fields\/(\d+)$/.exec(pointer)
  if (field !== null) {
    return Number(field[1]) < sections.length ? { section: Number(field[1]), index: Number(field[2]) } : undefined
  }
  const section = /^\/sections\/(\d+)$/.exec(pointer)
  if (section !== null && Number(section[1]) < sections.length) {
    return { section: Number(section[1]), index: fieldsOf(sections[Number(section[1])]).length }
  }
  return undefined
}

/**
 * Moves the field at `from` to `to`: the field is taken out, then put at `to.index` of the target section, which is
 * clamped to that section's fields after the removal. The moved field keeps all its keys, and every other field keeps
 * its order. Returns the new sections and the place where the field landed. Only the sections that change are
 * rebuilt. `undefined` when `from` names no field, when `to.section` is missing, and when the field lands where it was.
 */
export function moveField(
  draft: Content,
  from: FieldPlace,
  to: FieldPlace,
): { sections: unknown[]; place: FieldPlace } | undefined {
  const sections = Array.isArray(draft.sections) ? draft.sections : []
  const source = sections[from.section]
  const target = sections[to.section]
  const moved = fieldsOf(source)[from.index]
  if (moved === undefined || !isContent(source) || !isContent(target)) {
    return undefined
  }
  const remaining = fieldsOf(source).filter((_, index) => index !== from.index)
  const into = from.section === to.section ? remaining : [...fieldsOf(target)]
  const index = Math.max(0, Math.min(to.index, into.length))
  if (from.section === to.section && index === from.index) {
    return undefined
  }
  const landed = [...into.slice(0, index), moved, ...into.slice(index)]
  const next = sections.map((section, position) => {
    if (position === to.section) {
      return { ...(target as Content), fields: landed }
    }
    return position === from.section ? { ...(source as Content), fields: remaining } : section
  })
  return { sections: next, place: { section: to.section, index } }
}

/**
 * The layout of a draft: each step with the indexes of the sections it lists, in the order the step lists them, then
 * the sections that no step lists. A draft without steps has all its sections in one group with no step.
 */
export function outline(draft: Content): OutlineGroup[] {
  const total = sectionCount(draft)
  const steps = itemsOf(draft, 'steps')
  const groups: OutlineGroup[] = steps.map((step, index) => {
    const title = isContent(step.title) && typeof step.title.textKey === 'string' ? step.title.textKey : ''
    const named = Array.isArray(step.sections) ? step.sections : []
    return {
      step: { pointer: `/steps/${index}`, titleKey: title },
      sections: named.flatMap((name) => {
        const section = typeof name === 'string' ? sectionNamed(draft, name) : -1
        return section >= 0 ? [section] : []
      }),
    }
  })
  const listed = new Set(groups.flatMap((group) => group.sections))
  const rest = Array.from({ length: total }, (_, index) => index).filter((index) => !listed.has(index))
  return rest.length > 0 || groups.length === 0 ? [...groups, { step: null, sections: rest }] : groups
}
