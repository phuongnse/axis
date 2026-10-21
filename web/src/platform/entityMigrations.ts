import { kebabName } from './studioStarters'
import { newUuid } from './uuid'

type Content = Record<string, unknown>

/** The loaded name of a row of the fields table, or `null` for a row the author added in this session. */
export type FieldOrigin = string | null

/** A question for one loaded field that the author renamed or removed. */
export type FieldQuestion =
  { type: 'rename'; from: string; to: string } | { type: 'remove'; field: string; loadedIndex: number }

/** The answer to a question. A rename keeps or drops its data. A removal drops its data, or keeps the field. */
export type FieldAnswer = 'keep' | 'drop'

/** One change of a migration that the designer writes. */
export type MigrationChange = { type: 'renameField'; from: string; to: string } | { type: 'dropField'; field: string }

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
 * The questions that a save must ask: one for each loaded field that was renamed or removed. A `child-collection`
 * field has no column and asks nothing. A row without an origin whose name is that of a loaded field that no other
 * row claims counts as that loaded field. `touched` lists the loaded names in the order the author first renamed or
 * removed them. The questions follow that order, then the other fields in loaded order.
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

  const questions = new Map<string, FieldQuestion>()
  loadedFields.forEach((field, loadedIndex) => {
    const name = loadedNames[loadedIndex]
    if (name === null || (isContent(field) && field.type === 'child-collection')) {
      return
    }
    const row = claims.get(name)
    if (row === undefined) {
      questions.set(name, { type: 'remove', field: name, loadedIndex })
      return
    }
    const to = nameOf(draftFields[row])
    if (to !== null && to !== '' && to !== name) {
      questions.set(name, { type: 'rename', from: name, to })
    }
  })

  const order = [...touched, ...loadedNames.filter((name): name is string => name !== null && !touched.includes(name))]
  return order.flatMap((name) => {
    const question = questions.get(name)
    return question ? [question] : []
  })
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
    if (question.type === 'rename') {
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
