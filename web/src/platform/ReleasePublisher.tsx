import { PersonName } from './PersonName'
import { releasePublisher } from './studio'
import { useText } from './texts'

/**
 * Draws who activated a release: a person by name through `PersonName`, and for a startup or operator activation, or an
 * actor no longer known, the plain words that `releasePublisher` gives. The server names only a user, so a row with no
 * `actorName` is never a link, see docs/reference/actions-and-controls.md#text ("A person by name").
 */
export function ReleasePublisher({ row }: { row: { actor: string; actorName: string | null } }) {
  const t = useText()
  return row.actorName ? <PersonName id={row.actor} name={row.actorName} /> : <>{releasePublisher(row, t)}</>
}
