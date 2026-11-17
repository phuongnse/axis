import { useCurrentUser } from './currentUser'
import { useText } from './texts'

/** A person a page names: their user id when the server knows it, and the name they go by. */
export interface Person {
  id?: string | null
  name: string
}

/**
 * Proposed, see the review on #3650: a person's name as text, followed by "(you)" when it is the signed-in person, for
 * where a name is words only, such as the words a search finds.
 */
export function usePersonText(): (person: Person) => string {
  const t = useText()
  const user = useCurrentUser()
  return (person) =>
    user && person.id && person.id === user.id ? `${person.name} ${t('shell.person.you')}` : person.name
}
