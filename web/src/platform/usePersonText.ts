import { useCurrentUser } from './currentUser'
import { useText } from './texts'

/**
 * A person's name as words alone, with "(you)" after it for the signed-in person, as search and accessible names read
 * them, see docs/reference/actions-and-controls.md#text ("A person by name").
 */
export function usePersonText(id: string, name: string): string {
  const t = useText()
  return useCurrentUser()?.id === id ? `${name} ${t('shell.person.you')}` : name
}
