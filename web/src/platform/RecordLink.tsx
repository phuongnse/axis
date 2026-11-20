import { ExportOutlined } from '@ant-design/icons'
import { Link, useLinkClickHandler, useLocation } from 'react-router'
import { AxisButton } from './AxisButton'
import { useRecordOpener } from './recordPages'

interface RecordProps {
  /** The name of the record's entity, which is its key in the site's `recordPages`. */
  entity: string
  id: string
  /** The record's label, or `null` where the person may not read the record. */
  label: string | null
}

/** The address the link leaves, which the record's page offers as its way back. */
function useFrom(): string {
  const { pathname, search } = useLocation()
  return `${pathname}${search}`
}

/**
 * A record's label as a link to its record page, passing the page it leaves as `from`. It is plain text, showing the
 * id where the label is missing, when the entity has no record page or the person may not read the record. See
 * docs/reference/actions-and-controls.md#opening-things.
 */
export function RecordLink({ entity, id, label }: RecordProps) {
  const address = useRecordOpener()(entity, id)
  const from = useFrom()
  if (address === null || label === null) {
    return <>{label ?? id}</>
  }
  return (
    <Link to={address} state={{ from }}>
      {label}
    </Link>
  )
}

/**
 * The icon button joined after a reference's Choose, named "Open {label}", which is a real link to the record's page
 * so Ctrl+click opens a new tab. It is not drawn when there is no record, no label or no record page. See
 * docs/reference/form-widget.md.
 */
export function OpenRecordButton({ entity, id, label }: Omit<RecordProps, 'id'> & { id: string | null }) {
  const address = useRecordOpener()(entity, id ?? '')
  const from = useFrom()
  const go = useLinkClickHandler(address ?? '', { state: { from } })
  if (id === null || label === null || address === null) {
    return null
  }
  return (
    <AxisButton
      role="icon"
      joined
      nameKey="shell.form.openRecord"
      nameSuffix={label}
      icon={<ExportOutlined />}
      href={address}
      onClick={(event) => go(event as never)}
    />
  )
}
