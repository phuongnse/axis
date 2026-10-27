import { useText } from './texts'
import { useEmptyStyle } from './useEmptyStyle'

/** The empty value of a table cell, a child collection cell and a KPI, where values are read at a glance. */
export function EmptyDash() {
  return <span style={useEmptyStyle()}>—</span>
}

/** The empty value of a read-only field on a form or a detail, where fields are read one by one. */
export function NotSet() {
  const t = useText()
  return <span style={useEmptyStyle()}>{t('shell.value.notSet')}</span>
}
