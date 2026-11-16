import { ExportOutlined } from '@ant-design/icons'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { AxisButton } from './AxisButton'
import { useRecordOpener } from './recordPages'

/**
 * Proposed, see the review on #3650: a reference to a record anywhere on a screen opens it. `children`, the record's
 * label, is a real link to the record's page in the site, so it can open in a new tab. When the person cannot open
 * the record there, it is plain text.
 */
export function RecordLink({ entity, id, children }: { entity: string; id: string; children: ReactNode }) {
  const opener = useRecordOpener(entity, id)
  if (opener === null) {
    return <>{children}</>
  }
  return (
    <Link to={opener.href} state={{ from: opener.from }}>
      {children}
    </Link>
  )
}

/**
 * Proposed, see the review on #3650: the Open button of a field that holds a reference, joined after its other
 * buttons, a real link to the chosen record's page. `name` is the record's label, which completes the button's
 * accessible name. When the person cannot open the record in the site, there is none.
 */
export function OpenRecordButton({ entity, id, name }: { entity: string; id: string; name: string }) {
  const opener = useRecordOpener(entity, id)
  if (opener === null) {
    return null
  }
  return (
    <AxisButton
      role="icon"
      joined
      nameKey="shell.form.openRecord"
      nameSuffix={name}
      icon={<ExportOutlined />}
      href={opener.href}
      onClick={opener.onClick}
    />
  )
}
