/** A group of a resource's root properties, shown as one titled panel. Its texts are `shell.studio.section.<kind>.<name>` and `.line`. */
export interface FormSection {
  name: string
  properties: string[]
}

/**
 * The sections of each kind's form, in order, see docs/reference/studio-resource-form.md#sections. A kind with no
 * entry shows its fields in one panel with no heading.
 */
export const sectionsByKind: Record<string, FormSection[]> = {
  application: [
    { name: 'general', properties: ['name', 'label', 'description', 'timeZone'] },
    { name: 'modules', properties: ['modules'] },
    { name: 'packages', properties: ['packages'] },
    { name: 'fileStorage', properties: ['fileStorage'] },
  ],
  role: [{ name: 'general', properties: ['name', 'label', 'description'] }],
  process: [{ name: 'general', properties: ['name', 'description', 'startCondition'] }],
}
