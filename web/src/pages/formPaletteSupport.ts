import type { FieldPlace } from '../platform/formLayout'
import type { ChangeHandler } from './SchemaForm'

/** The name of the section at `index` for the outline and the live region: its `name`, or `Section N` without one. */
export function sectionLabel(t: (key: string) => string, draft: Record<string, unknown>, index: number): string {
  const section = Array.isArray(draft.sections) ? (draft.sections[index] as unknown) : undefined
  const name = typeof section === 'object' && section !== null ? (section as { name?: unknown }).name : undefined
  return typeof name === 'string' && name !== '' ? name : `${t('shell.studio.design.section')} ${index + 1}`
}

/**
 * Inserts the field `name` at `place`, selects it and says where it landed. The position is clamped to the section's
 * fields, so a stale place still inserts at the end. Nothing happens when the draft has no such section.
 */
export function insertField(
  t: (key: string) => string,
  draft: Record<string, unknown>,
  name: string,
  place: FieldPlace,
  onChange: ChangeHandler,
  onSelect: (pointer: string) => void,
  onAnnounce: (text: string) => void,
) {
  const section = Array.isArray(draft.sections) ? (draft.sections[place.section] as unknown) : undefined
  if (typeof section !== 'object' || section === null) {
    return
  }
  const current = (section as { fields?: unknown }).fields
  const fields = Array.isArray(current) ? (current as unknown[]) : []
  const index = Math.min(place.index, fields.length)
  onChange(`/sections/${place.section}/fields`, [...fields.slice(0, index), { field: name }, ...fields.slice(index)])
  onSelect(`/sections/${place.section}/fields/${index}`)
  onAnnounce(
    `${t('shell.studio.palette.inserted')} ${name} · ${sectionLabel(t, draft, place.section)} · ${t('shell.studio.palette.position')} ${index + 1}`,
  )
}
