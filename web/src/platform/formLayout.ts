import { labelKey } from './labelKeys'
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

/** Where a section goes: the index of its step, or null without steps, and the position in that step's list or in `sections`. */
export interface SectionPlace {
  step: number | null
  index: number
}

/** The steps of a draft, or `undefined` when it has none. */
function stepsOf(draft: Content): unknown[] | undefined {
  return Array.isArray(draft.steps) && draft.steps.length > 0 ? draft.steps : undefined
}

/** The section names that a step lists, or none. */
function listedBy(step: unknown): unknown[] {
  return isContent(step) && Array.isArray(step.sections) ? step.sections : []
}

function sameName(name: unknown, wanted: string): boolean {
  return typeof name === 'string' && name.toLowerCase() === wanted
}

/**
 * Where a drop on the pointer of an outline section or step moves a section. Without steps, a section takes the place
 * of the section it is dropped on. With steps, it takes the place in the list of the step that lists the dropped-on
 * section, and a step takes it at the end of its list. `undefined` for any other pointer, and for a section that no
 * step lists.
 */
export function sectionMovePlace(draft: Content, pointer: string): SectionPlace | undefined {
  const sections = Array.isArray(draft.sections) ? draft.sections : []
  const steps = stepsOf(draft)
  const section = /^\/sections\/(\d+)$/.exec(pointer)
  if (section !== null && Number(section[1]) < sections.length) {
    if (steps === undefined) {
      return { step: null, index: Number(section[1]) }
    }
    const named = sections[Number(section[1])]
    const wanted = isContent(named) && typeof named.name === 'string' ? named.name.toLowerCase() : undefined
    if (wanted === undefined) {
      return undefined
    }
    const step = steps.findIndex((item) => listedBy(item).some((name) => sameName(name, wanted)))
    return step < 0 ? undefined : { step, index: listedBy(steps[step]).findIndex((name) => sameName(name, wanted)) }
  }
  const step = /^\/steps\/(\d+)$/.exec(pointer)
  if (step !== null && steps !== undefined && isContent(steps[Number(step[1])])) {
    return { step: Number(step[1]), index: listedBy(steps[Number(step[1])]).length }
  }
  return undefined
}

/**
 * Moves the section at `from` to `to`. Without steps, the section is taken out and put at `to.index` of `sections`.
 * With steps, its name leaves every step's list and joins the list of step `to.step` at `to.index`, which is clamped
 * to that list after the removal. Then `sections` is rebuilt to follow the steps: the first step's sections, then the
 * second step's and so on, then the sections that no step lists in their current order. A section moves between steps
 * only when it has a name. Returns the new `sections`, the new `steps` (`undefined` without steps) and the index where
 * the section landed. Only the steps that change are rebuilt. `undefined` when `from` names no section, when the
 * target is missing, and when the section lands where it was.
 */
export function moveSection(
  draft: Content,
  from: number,
  to: SectionPlace,
): { sections: unknown[]; steps: unknown[] | undefined; index: number } | undefined {
  const sections = Array.isArray(draft.sections) ? draft.sections : []
  const moved = sections[from]
  if (moved === undefined) {
    return undefined
  }
  const steps = stepsOf(draft)
  if (steps === undefined) {
    if (to.step !== null) {
      return undefined
    }
    const remaining = sections.filter((_, position) => position !== from)
    const index = Math.max(0, Math.min(to.index, remaining.length))
    if (index === from) {
      return undefined
    }
    return { sections: [...remaining.slice(0, index), moved, ...remaining.slice(index)], steps: undefined, index }
  }
  if (to.step === null || !isContent(moved) || typeof moved.name !== 'string' || moved.name === '') {
    return undefined
  }
  const target = steps[to.step]
  if (!isContent(target)) {
    return undefined
  }
  const wanted = moved.name.toLowerCase()
  const without = (step: unknown) => listedBy(step).filter((name) => !sameName(name, wanted))
  const remaining = without(target)
  const index = Math.max(0, Math.min(to.index, remaining.length))
  const current = sectionMovePlace(draft, `/sections/${from}`)
  if (current?.step === to.step && current.index === index) {
    return undefined
  }
  const nextSteps = steps.map((step, position) => {
    if (!isContent(step)) {
      return step
    }
    if (position === to.step) {
      return { ...step, sections: [...remaining.slice(0, index), moved.name, ...remaining.slice(index)] }
    }
    const kept = without(step)
    return kept.length === listedBy(step).length ? step : { ...step, sections: kept }
  })
  const order: number[] = []
  const used = new Set<number>()
  for (const step of nextSteps) {
    for (const name of listedBy(step)) {
      const position = typeof name === 'string' ? sectionNamed(draft, name) : -1
      if (position >= 0 && !used.has(position)) {
        used.add(position)
        order.push(position)
      }
    }
  }
  const rest = sections.flatMap((_, position) => (used.has(position) ? [] : [position]))
  const next = [...order, ...rest].map((position) => sections[position])
  return { sections: next, steps: nextSteps, index: next.indexOf(moved) }
}

/**
 * The result of a rename: the new `sections` and `steps` (`undefined` when the draft has no steps), or the reason the
 * name is refused. `undefined` when there is nothing to do.
 */
export type SectionRename =
  { sections: unknown[]; steps: unknown[] | undefined } | { refused: 'empty' | 'taken' } | undefined

/**
 * Renames the section at `index` to `name`, trimmed. Every step entry that lists the old name, without regard to
 * letter case, lists the new name in the same position. The section keeps all its other keys. The name is refused
 * when it is empty, and when another section has it, without regard to letter case. A change of letter case alone is
 * allowed. Only the section and the steps that change are rebuilt. `undefined` when `index` names no section and when
 * the name does not change.
 */
export function renameSection(draft: Content, index: number, name: string): SectionRename {
  const sections = Array.isArray(draft.sections) ? draft.sections : []
  const section = sections[index]
  if (!isContent(section)) {
    return undefined
  }
  const next = name.trim()
  const old = typeof section.name === 'string' ? section.name : ''
  if (next === old) {
    return undefined
  }
  if (next === '') {
    return { refused: 'empty' }
  }
  const wanted = next.toLowerCase()
  if (sections.some((other, position) => position !== index && isContent(other) && sameName(other.name, wanted))) {
    return { refused: 'taken' }
  }
  const before = old.toLowerCase()
  return {
    sections: sections.map((item, position) => (position === index ? { ...section, name: next } : item)),
    steps: Array.isArray(draft.steps)
      ? draft.steps.map((step) =>
          isContent(step) && listedBy(step).some((item) => sameName(item, before))
            ? { ...step, sections: listedBy(step).map((item) => (sameName(item, before) ? next : item)) }
            : step,
        )
      : undefined,
  }
}

/** Removes a section from `sections` and every step that lists it. `step` is the first step that listed it. */
export function removeSection(
  draft: Content,
  index: number,
): { sections: unknown[]; steps: unknown[] | undefined; step: number | undefined } | undefined {
  const sections = Array.isArray(draft.sections) ? draft.sections : []
  const section = sections[index]
  if (!isContent(section)) {
    return undefined
  }
  const wanted = typeof section.name === 'string' ? section.name.toLowerCase() : undefined
  let step: number | undefined
  const steps = Array.isArray(draft.steps)
    ? draft.steps.map((item, position) => {
        if (wanted === undefined || !listedBy(item).some((name) => sameName(name, wanted))) {
          return item
        }
        if (step === undefined) {
          step = position
        }
        return { ...(item as Content), sections: listedBy(item).filter((name) => !sameName(name, wanted)) }
      })
    : undefined
  return { sections: sections.filter((_, position) => position !== index), steps, step }
}

/** Removes a step and leaves `sections` alone. `steps` is `undefined` when no step is left. */
export function removeStep(draft: Content, index: number): { steps: unknown[] | undefined } | undefined {
  const steps = stepsOf(draft)
  if (steps === undefined || !isContent(steps[index])) {
    return undefined
  }
  const remaining = steps.filter((_, position) => position !== index)
  return { steps: remaining.length > 0 ? remaining : undefined }
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

/**
 * The field indexes of each row of a section, given the widths of its fields in twelfths. A row fills left to right
 * while its widths add up to 12 at most, and a field that does not fit starts the next row. At `narrow` width every
 * field has its own row. A width outside 1 to 12 counts as 12.
 */
export function fieldRows(widths: readonly number[], narrow = false): number[][] {
  const rows: number[][] = []
  let used = 0
  widths.forEach((width, index) => {
    const part = width >= 1 && width <= 12 ? width : 12
    if (narrow || rows.length === 0 || used + part > 12) {
      rows.push([index])
      used = part
    } else {
      rows[rows.length - 1].push(index)
      used += part
    }
  })
  return rows
}

/** What a new title key needs: the resource's file and name, and the keys that texts already own. */
export interface TitleKeys {
  file: string
  name: string | null
  texts: ReadonlySet<string>
}

/**
 * The result of an add: the new `sections` and `steps` lists, the pointer to select and a name for the live region.
 * `steps` is `undefined` when the draft has none and the add does not create them.
 */
export interface LayoutAdd {
  sections: unknown[]
  steps: unknown[] | undefined
  select: string
  name: string
}

/** The lowest `section{n}` from 1 whose lower case is not in `used`. */
function unusedSectionName(used: ReadonlySet<string>): string {
  for (let number = 1; ; number++) {
    if (!used.has(`section${number}`)) {
      return `section${number}`
    }
  }
}

/** The lowest `step{n}` from 1 that no step of `steps` uses, ignoring letter case as the compiler does. */
export function unusedStepName(steps: unknown[]): string {
  const used = new Set(
    steps.flatMap((step) => (isContent(step) && typeof step.name === 'string' ? [step.name.toLowerCase()] : [])),
  )
  for (let number = 1; ; number++) {
    if (!used.has(`step${number}`)) {
      return `step${number}`
    }
  }
}

function sectionNames(sections: unknown[]): Set<string> {
  return new Set(
    sections.flatMap((section) =>
      isContent(section) && typeof section.name === 'string' ? [section.name.toLowerCase()] : [],
    ),
  )
}

/** The texts' keys plus every title key that the draft's sections and steps hold. */
function takenKeys(draft: Content, keys: TitleKeys): Set<string> {
  const taken = new Set(keys.texts)
  for (const item of [
    ...(Array.isArray(draft.sections) ? draft.sections : []),
    ...(Array.isArray(draft.steps) ? draft.steps : []),
  ]) {
    if (isContent(item) && isContent(item.title) && typeof item.title.textKey === 'string') {
      taken.add(item.title.textKey)
    }
  }
  return taken
}

/** The index of the step that the selected step, section or field belongs to, or `undefined`. */
function selectedStep(draft: Content, selected: string | null): number | undefined {
  const steps = Array.isArray(draft.steps) ? draft.steps : []
  const step = selected === null ? null : /^\/steps\/(\d+)$/.exec(selected)
  if (step !== null) {
    return Number(step[1]) < steps.length ? Number(step[1]) : undefined
  }
  const section = selected === null ? null : /^\/sections\/(\d+)(?:\/fields\/\d+)?$/.exec(selected)
  const sections = Array.isArray(draft.sections) ? draft.sections : []
  const named = section === null ? undefined : sections[Number(section[1])]
  if (!isContent(named) || typeof named.name !== 'string') {
    return undefined
  }
  const wanted = named.name.toLowerCase()
  const index = steps.findIndex(
    (item) =>
      isContent(item) &&
      Array.isArray(item.sections) &&
      item.sections.some((name) => typeof name === 'string' && name.toLowerCase() === wanted),
  )
  return index >= 0 ? index : undefined
}

/**
 * Appends an empty section with the lowest unused name `section{n}` and a new title key. With steps, it joins the
 * selected step, or the step of the selected section or field, or the last step. The new section is selected.
 */
export function addSection(draft: Content, selected: string | null, keys: TitleKeys): LayoutAdd {
  const sections = Array.isArray(draft.sections) ? draft.sections : []
  const name = unusedSectionName(sectionNames(sections))
  const textKey = labelKey(keys.file, keys.name, `/sections/${sections.length}/title/textKey`, takenKeys(draft, keys))
  const steps = Array.isArray(draft.steps) ? draft.steps : undefined
  const target =
    steps === undefined || steps.length === 0 ? undefined : (selectedStep(draft, selected) ?? steps.length - 1)
  return {
    sections: [...sections, { name, title: { textKey }, fields: [] }],
    steps:
      steps === undefined
        ? undefined
        : steps.map((step, index) =>
            index === target && isContent(step)
              ? { ...step, sections: [...(Array.isArray(step.sections) ? step.sections : []), name] }
              : step,
          ),
    select: `/sections/${sections.length}`,
    name,
  }
}

/**
 * Adds a step. The first step names every unnamed section and lists all sections in order. A later step is empty.
 * The new step is selected, and its title key names the add in the live region.
 */
export function addStep(draft: Content, keys: TitleKeys): LayoutAdd {
  const sections = Array.isArray(draft.sections) ? draft.sections : []
  const steps = Array.isArray(draft.steps) ? draft.steps : []
  const textKey = labelKey(keys.file, keys.name, `/steps/${steps.length}/title/textKey`, takenKeys(draft, keys))
  if (steps.length > 0) {
    return {
      sections,
      steps: [...steps, { name: unusedStepName(steps), title: { textKey }, sections: [] }],
      select: `/steps/${steps.length}`,
      name: textKey,
    }
  }
  const used = sectionNames(sections)
  const named = sections.map((section) => {
    if (isContent(section) && typeof section.name === 'string' && section.name !== '') {
      return section
    }
    const name = unusedSectionName(used)
    used.add(name)
    return { ...(isContent(section) ? section : {}), name }
  })
  return {
    sections: named,
    steps: [
      { name: unusedStepName(steps), title: { textKey }, sections: named.map((section) => section.name as string) },
    ],
    select: '/steps/0',
    name: textKey,
  }
}
