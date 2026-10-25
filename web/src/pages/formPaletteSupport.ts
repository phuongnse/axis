import { moveField, moveSection, type FieldPlace, type SectionPlace } from '../platform/formLayout'
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

/**
 * Moves the field at `from` to `to`, selects it where it landed and says where that is. The move is one change of the
 * draft, so one undo reverses it. Nothing happens when `moveField` finds nothing to move.
 */
export function moveFieldTo(
  t: (key: string) => string,
  draft: Record<string, unknown>,
  from: FieldPlace,
  to: FieldPlace,
  onChange: ChangeHandler,
  onSelect: (pointer: string) => void,
  onAnnounce: (text: string) => void,
) {
  const result = moveField(draft, from, to)
  if (result === undefined) {
    return
  }
  const { section, index } = result.place
  const field = (result.sections[section] as { fields: { field?: unknown }[] }).fields[index]
  const name = typeof field.field === 'string' ? field.field : `/sections/${section}/fields/${index}`
  onChange('/sections', result.sections)
  onSelect(`/sections/${section}/fields/${index}`)
  onAnnounce(
    `${t('shell.studio.palette.moved')} ${name} · ${sectionLabel(t, draft, section)} · ${t('shell.studio.palette.position')} ${index + 1}`,
  )
}

/**
 * Takes the field at `place` out of its section, keeps the other fields in order, selects the section and says which
 * field left. The removal is one change of the draft, so one undo puts the field back. Nothing happens when the draft
 * has no such field.
 */
export function removeField(
  t: (key: string) => string,
  draft: Record<string, unknown>,
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
  const fields = Array.isArray(current) ? (current as { field?: unknown }[]) : []
  if (place.index >= fields.length) {
    return
  }
  const field = fields[place.index].field
  const name = typeof field === 'string' && field !== '' ? field : `/sections/${place.section}/fields/${place.index}`
  onChange(
    `/sections/${place.section}/fields`,
    fields.filter((_, index) => index !== place.index),
  )
  onSelect(`/sections/${place.section}`)
  onAnnounce(`${t('shell.studio.design.removed')} ${name} · ${sectionLabel(t, draft, place.section)}`)
}

/**
 * Moves the section at `from` to `to`, selects it where it landed and says where that is: its name, its step when the
 * draft has steps, and its position in the step or in `sections`. `sections` and `steps` change in the same event, so
 * one undo reverses the move. Nothing happens when `moveSection` finds nothing to move.
 */
export function moveSectionTo(
  t: (key: string) => string,
  draft: Record<string, unknown>,
  from: number,
  to: SectionPlace,
  onChange: ChangeHandler,
  onSelect: (pointer: string) => void,
  onAnnounce: (text: string) => void,
) {
  const result = moveSection(draft, from, to)
  if (result === undefined) {
    return
  }
  const label = sectionLabel(t, { ...draft, sections: result.sections }, result.index)
  const step = to.step === null ? undefined : (result.steps?.[to.step] as Record<string, unknown> | undefined)
  const textKey = (step?.title as { textKey?: unknown } | undefined)?.textKey
  const named = step === undefined || !Array.isArray(step.sections) ? [] : (step.sections as unknown[])
  const position = step === undefined ? result.index : named.findIndex((name) => name === label)
  const where =
    to.step === null ? '' : ` · ${typeof textKey === 'string' && textKey !== '' ? t(textKey) : `/steps/${to.step}`}`
  onChange('/sections', result.sections)
  if (result.steps !== undefined) {
    onChange('/steps', result.steps)
  }
  onSelect(`/sections/${result.index}`)
  onAnnounce(
    `${t('shell.studio.palette.moved')} ${label}${where} · ${t('shell.studio.palette.position')} ${position + 1}`,
  )
}
