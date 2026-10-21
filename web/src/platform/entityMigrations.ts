import { fieldSettings } from './entityFields'
import { kebabName } from './studioStarters'
import { newUuid } from './uuid'

type Content = Record<string, unknown>

/** The loaded name of a row of the fields table, or `null` for a row the author added in this session. */
export type FieldOrigin = string | null

/**
 * A question for one field that the author renamed, removed, retyped or made required. A `convert` question is for a
 * loaded field whose column cannot follow its new type, `maxLength` or enum values. A `fill` question is for a field
 * that is now required. `origin` is the loaded name of the field, or `null` for a field added in this session.
 * `whenDropped` marks a `fill` for a loaded field that was already required and was renamed: it only counts when the
 * rename drops the data, because the column is then added again.
 */
export type FieldQuestion =
  | { type: 'rename'; from: string; to: string }
  | { type: 'remove'; field: string; loadedIndex: number }
  | { type: 'convert'; origin: string; field: string; reason: 'type' | 'maxLength' | 'values' }
  | { type: 'fill'; origin: string | null; field: string; whenDropped: boolean }

/**
 * The answer to a question. A rename keeps or drops its data. A removal drops its data, or keeps the field. A
 * `convert` or `fill` question is answered with its expression, or skipped.
 */
export type FieldAnswer = 'keep' | 'drop' | 'skip' | { value: string }

/** One change of a migration that the designer writes. */
export type MigrationChange =
  | { type: 'renameField'; from: string; to: string }
  | { type: 'dropField'; field: string }
  | { type: 'convert' | 'fill'; field: string; value: string }

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function fieldsOf(content: Content): unknown[] {
  return Array.isArray(content.fields) ? content.fields : []
}

function nameOf(field: unknown): string | null {
  return isContent(field) && typeof field.name === 'string' ? field.name : null
}

/** The origins of the fields as Studio loaded them: each row is its own loaded name. */
export function loadedOrigins(content: Content): FieldOrigin[] {
  return fieldsOf(content).map(nameOf)
}

/**
 * The origins of a draft that was edited as JSON, which gives no row identity. A row matches the loaded field of the
 * same name, and each loaded field matches one row at most. Any other row is new.
 */
export function matchOrigins(loaded: Content, draft: Content): FieldOrigin[] {
  const free = new Set(loadedOrigins(loaded).filter((name): name is string => name !== null))
  return fieldsOf(draft).map((field) => {
    const name = nameOf(field)
    if (name !== null && free.has(name)) {
      free.delete(name)
      return name
    }
    return null
  })
}

/**
 * The questions that a save must ask. For each loaded field: a rename or a removal, then a `convert` when its column
 * cannot follow a new type, a smaller `maxLength` or a removed enum value, then a `fill` when it became required.
 * A new required field asks a `fill` too. A `child-collection` field has no column and asks nothing. A row without
 * an origin whose name is that of a loaded field that no other row claims counts as that loaded field. `touched`
 * lists the loaded names in the order the author first edited them. The questions follow that order, then the other
 * loaded fields in loaded order, then the new fields in row order.
 */
export function fieldQuestions(
  loaded: Content,
  draft: Content,
  origins: readonly FieldOrigin[],
  touched: readonly string[],
): FieldQuestion[] {
  const loadedFields = fieldsOf(loaded)
  const draftFields = fieldsOf(draft)
  const loadedNames = loadedFields.map(nameOf)

  // The draft row that holds each loaded name.
  const claims = new Map<string, number>()
  draftFields.forEach((_, row) => {
    const origin = origins[row] ?? null
    if (origin !== null && loadedNames.includes(origin) && !claims.has(origin)) {
      claims.set(origin, row)
    }
  })
  draftFields.forEach((field, row) => {
    const name = nameOf(field)
    if ((origins[row] ?? null) === null && name !== null && loadedNames.includes(name) && !claims.has(name)) {
      claims.set(name, row)
    }
  })

  const questions = new Map<string, FieldQuestion[]>()
  loadedFields.forEach((field, loadedIndex) => {
    const name = loadedNames[loadedIndex]
    if (name === null || !isContent(field) || field.type === 'child-collection') {
      return
    }
    const row = claims.get(name)
    if (row === undefined) {
      questions.set(name, [{ type: 'remove', field: name, loadedIndex }])
      return
    }
    const now = draftFields[row]
    const to = nameOf(now)
    const asked: FieldQuestion[] = []
    if (to !== null && to !== '' && to !== name) {
      asked.push({ type: 'rename', from: name, to })
    }
    if (to !== null && to !== '' && isContent(now) && now.type !== 'child-collection') {
      const reason = convertReason(field, now)
      if (reason) {
        asked.push({ type: 'convert', origin: name, field: to, reason })
      }
      if (now.required === true && field.required !== true) {
        asked.push({ type: 'fill', origin: name, field: to, whenDropped: false })
      } else if (now.required === true && to !== name) {
        asked.push({ type: 'fill', origin: name, field: to, whenDropped: true })
      }
    }
    if (asked.length > 0) {
      questions.set(name, asked)
    }
  })

  const order = new Set([
    ...touched,
    ...loadedNames.filter((name): name is string => name !== null && !touched.includes(name)),
  ])
  const loadedAsked = [...order].flatMap((name) => questions.get(name) ?? [])

  const claimed = new Set(claims.values())
  const added = draftFields.flatMap((field, row): FieldQuestion[] => {
    const name = nameOf(field)
    if (
      (origins[row] ?? null) !== null ||
      claimed.has(row) ||
      name === null ||
      name === '' ||
      !isContent(field) ||
      field.required !== true ||
      field.type === 'child-collection'
    ) {
      return []
    }
    return [{ type: 'fill', origin: null, field: name, whenDropped: false }]
  })
  return [...loadedAsked, ...added]
}

/** Why the column of a loaded field cannot follow the field as the draft has it, or `null` when it can. */
function convertReason(loaded: Content, draft: Content): 'type' | 'maxLength' | 'values' | null {
  if (loaded.type !== draft.type) {
    // A change to or from `enum` has no migration type.
    return loaded.type === 'enum' || draft.type === 'enum' ? null : 'type'
  }
  const type = String(draft.type)
  if (fieldSettings(type).includes('maxLength')) {
    const next = draft.maxLength
    const before = loaded.maxLength
    if (typeof next === 'number' && (typeof before !== 'number' || next < before)) {
      return 'maxLength'
    }
  }
  if (type === 'enum' && Array.isArray(loaded.values)) {
    const values = Array.isArray(draft.values) ? draft.values : []
    if (loaded.values.some((value) => !values.includes(value))) {
      return 'values'
    }
  }
  return null
}

/**
 * Whether a save asks the question at `index`, given the answers before it. A `convert` or a `fill` for a loaded
 * field that was renamed with Drop data is not asked, except a `fill` for a required field, which counts as new
 * because the column is added again. A `fill` that only counts when the rename drops the data is not asked when the
 * rename keeps it.
 */
export function asked(questions: readonly FieldQuestion[], answers: readonly FieldAnswer[], index: number): boolean {
  const question = questions[index]
  if (question.type !== 'convert' && question.type !== 'fill') {
    return true
  }
  const rename = questions.findIndex(
    (other, position) => position < index && other.type === 'rename' && other.from === question.origin,
  )
  if (rename < 0) {
    return !(question.type === 'fill' && question.whenDropped)
  }
  const dropped = answers[rename] === 'drop'
  return question.type === 'fill' ? dropped || !question.whenDropped : !dropped
}

/** The change of a loaded field that no migration type follows. */
export type FieldWarning = 'enum' | 'unique'

/**
 * The warnings of the fields table, by draft row. A loaded field whose type changes to or from `enum`, or that was
 * not `unique` and is now, has no migration type that follows it. Validate reports it as `AXC0016`.
 */
export function fieldWarnings(
  loaded: Content,
  draft: Content,
  origins: readonly FieldOrigin[],
): Map<number, FieldWarning> {
  const before = new Map<string, unknown>()
  for (const field of fieldsOf(loaded)) {
    const name = nameOf(field)
    if (name !== null && isContent(field)) {
      before.set(name, field)
    }
  }
  const warnings = new Map<number, FieldWarning>()
  const seen = new Set<string>()
  fieldsOf(draft).forEach((field, row) => {
    const origin = origins[row] ?? null
    const was = origin === null ? undefined : before.get(origin)
    if (origin === null || seen.has(origin) || !isContent(was) || !isContent(field)) {
      return
    }
    seen.add(origin)
    if (was.type !== field.type && (was.type === 'enum' || field.type === 'enum')) {
      warnings.set(row, 'enum')
    } else if (was.unique !== true && field.unique === true) {
      warnings.set(row, 'unique')
    }
  })
  return warnings
}

/**
 * The migration changes and the final draft for a set of answers. A removal answered Keep field puts the loaded field
 * back at its place, or at the end when the fields are fewer now. The origins of the final draft come with it.
 */
export function applyAnswers(
  loaded: Content,
  draft: Content,
  origins: readonly FieldOrigin[],
  questions: readonly FieldQuestion[],
  answers: readonly FieldAnswer[],
): { draft: Content; origins: FieldOrigin[]; changes: MigrationChange[] } {
  const changes: MigrationChange[] = []
  const kept: { field: unknown; name: string; loadedIndex: number }[] = []
  questions.forEach((question, index) => {
    if (question.type === 'convert' || question.type === 'fill') {
      const answer = answers[index]
      if (typeof answer === 'object' && asked(questions, answers, index)) {
        changes.push({ type: question.type, field: question.field, value: answer.value })
      }
    } else if (question.type === 'rename') {
      changes.push(
        answers[index] === 'keep'
          ? { type: 'renameField', from: question.from, to: question.to }
          : { type: 'dropField', field: question.from },
      )
    } else if (answers[index] === 'drop') {
      changes.push({ type: 'dropField', field: question.field })
    } else {
      kept.push({
        field: fieldsOf(loaded)[question.loadedIndex],
        name: question.field,
        loadedIndex: question.loadedIndex,
      })
    }
  })
  if (kept.length === 0) {
    return { draft, origins: [...origins], changes }
  }
  const fields = [...fieldsOf(draft)]
  const nextOrigins = fields.map((_, row) => origins[row] ?? null)
  for (const { field, name, loadedIndex } of kept.toSorted((left, right) => left.loadedIndex - right.loadedIndex)) {
    const at = Math.min(loadedIndex, fields.length)
    fields.splice(at, 0, field)
    nextOrigins.splice(at, 0, name)
  }
  return { draft: { ...draft, fields }, origins: nextOrigins, changes }
}

function pad(value: number, length = 2): string {
  return String(value).padStart(length, '0')
}

/** `<Entity>Change<UTC yyyyMMddHHmmss>`, the name of the migration that one save of an entity adds. */
export function migrationName(entity: string, now: Date): string {
  const stamp =
    pad(now.getUTCFullYear(), 4) +
    pad(now.getUTCMonth() + 1) +
    pad(now.getUTCDate()) +
    pad(now.getUTCHours()) +
    pad(now.getUTCMinutes()) +
    pad(now.getUTCSeconds())
  return `${entity}Change${stamp}`
}

/** The file of a migration, such as `migrations/order-change20261020093000.json`. */
export function migrationFile(name: string): string {
  return `migrations/${kebabName(name)}.json`
}

/** The migration resource that holds the changes of one save. */
export function migrationResource(
  entity: string,
  changes: readonly MigrationChange[],
  now: Date,
  id: string = newUuid(),
): Content {
  return { id, kind: 'migration', name: migrationName(entity, now), formatVersion: 1, entity, changes }
}
