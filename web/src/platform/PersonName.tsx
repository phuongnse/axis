import { Link } from 'react-router'
import { useCurrentUser } from './currentUser'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * Draws a person's name. The signed-in person's own name is followed by "(you)" in `textMuted`. The name links to the
 * person's page only when the reader may administer users, and is plain text until the current user has loaded and for
 * anyone else, see docs/reference/actions-and-controls.md#text ("A person by name").
 */
export function PersonName({ id, name }: { id: string; name: string }) {
  const t = useText()
  const { colour } = useDesignTokens()
  const me = useCurrentUser()
  return (
    <>
      {me?.users ? <Link to={`/users/${encodeURIComponent(id)}`}>{name}</Link> : name}
      {me?.id === id && <span style={{ color: colour.textMuted }}> {t('shell.person.you')}</span>}
    </>
  )
}
