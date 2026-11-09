import { useText } from '../platform/texts'

/**
 * The texts of the fields of a resource of `kind`, by the path of the property from the root, with list indices left out,
 * see docs/reference/studio-resource-form.md#fields: a field's label and help, and a group's title and help.
 */
export function useFieldTexts(kind: string) {
  const t = useText()
  const help = (path: string) => t(`shell.studio.field.${kind}.${path}.help`)
  return {
    field: (path: string) => ({ label: t(`shell.studio.field.${kind}.${path}`), help: help(path) }),
    group: (path: string) => ({ title: t(`shell.studio.field.${kind}.${path}`), help: help(path) }),
  }
}
