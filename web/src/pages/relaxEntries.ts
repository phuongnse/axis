import type { JsonPatchOperation } from '../platform/jsonPatch'

/** A relax entry of a module in `application.json`: a named point of the module's contract, and why it is relaxed. */
export interface RelaxEntry {
  point: string
  reason: string
}

/** The longest reason a relax entry may have, as `application.schema.json` says. */
export const relaxReasonMaxLength = 500

function record(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : {}
}

function list(value: unknown): Record<string, unknown>[] {
  return Array.isArray(value) ? value.map(record) : []
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

/** The index of the module in `modules[]` of `application.json`, or -1 when the application does not declare it directly. */
export function moduleIndex(manifest: Record<string, unknown>, module: string): number {
  return list(manifest.modules).findIndex((entry) => text(entry.id) === module)
}

/** The relax entries of a module, in file order. Empty when the module has none or is not declared directly. */
export function relaxEntries(manifest: Record<string, unknown>, module: string): RelaxEntry[] {
  const entry = list(manifest.modules)[moduleIndex(manifest, module)]
  return list(entry?.relax).map((relax) => ({ point: text(relax.point), reason: text(relax.reason) }))
}

/** Whether any module in `application.json` has a relax entry. An empty `relax` list is none. */
export function hasRelaxEntry(manifest: Record<string, unknown>): boolean {
  return list(manifest.modules).some((entry) => list(entry.relax).length > 0)
}

/** Whether a reason is acceptable: 1 to 500 characters once trimmed. */
export function relaxReasonValid(reason: string): boolean {
  const length = reason.trim().length
  return length >= 1 && length <= relaxReasonMaxLength
}

/**
 * The patch that relaxes a point. It adds the whole `relax` list when the module has none, and otherwise one item at the
 * end of the list, at its current length. The reason is trimmed.
 */
export function relaxPatch(
  manifest: Record<string, unknown>,
  module: string,
  point: string,
  reason: string,
): JsonPatchOperation[] {
  const index = moduleIndex(manifest, module)
  const entry = { point, reason: reason.trim() }
  const existing = list(manifest.modules)[index]?.relax
  const base = `/modules/${index}/relax`
  return Array.isArray(existing)
    ? [{ op: 'add', path: `${base}/${existing.length}`, value: entry }]
    : [{ op: 'add', path: base, value: [entry] }]
}

/** The patch that removes the relax entry of a point. It removes the whole `relax` list when that entry is the only one. */
export function undoRelaxPatch(manifest: Record<string, unknown>, module: string, point: string): JsonPatchOperation[] {
  const index = moduleIndex(manifest, module)
  const entries = relaxEntries(manifest, module)
  const at = entries.findIndex((entry) => entry.point === point)
  if (at < 0) {
    return []
  }
  const base = `/modules/${index}/relax`
  return [{ op: 'remove', path: entries.length === 1 ? base : `${base}/${at}` }]
}

/** `application.json` as it is after {@link relaxPatch}, to show it without loading the file again. */
export function relaxedManifest(
  manifest: Record<string, unknown>,
  module: string,
  point: string,
  reason: string,
): Record<string, unknown> {
  return withRelax(manifest, module, [...relaxEntries(manifest, module), { point, reason: reason.trim() }])
}

/** `application.json` as it is after {@link undoRelaxPatch}. */
export function unrelaxedManifest(
  manifest: Record<string, unknown>,
  module: string,
  point: string,
): Record<string, unknown> {
  const entries = relaxEntries(manifest, module)
  const at = entries.findIndex((entry) => entry.point === point)
  return withRelax(
    manifest,
    module,
    entries.filter((_, position) => position !== at),
  )
}

function withRelax(manifest: Record<string, unknown>, module: string, entries: RelaxEntry[]): Record<string, unknown> {
  const index = moduleIndex(manifest, module)
  return {
    ...manifest,
    modules: list(manifest.modules).map((entry, at) => {
      if (at !== index) {
        return entry
      }
      const { relax: _relax, ...rest } = entry
      return entries.length === 0 ? rest : { ...rest, relax: entries }
    }),
  }
}
