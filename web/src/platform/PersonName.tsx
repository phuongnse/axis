import { Typography } from 'antd'
import { Link } from 'react-router'
import { useCurrentUser } from './currentUser'
import type { Person } from './personText'
import { useText } from './texts'

/**
 * Proposed, see the review on #3650: how every page names a person. The name always shows, so a page read by someone
 * else, or shown in a screenshot, still says who; the signed-in person's own name is followed by "(you)" in
 * `textMuted`, as Slack, Teams and Google Workspace mark it. A name is a link only when there is a page about the
 * person that the reader may open, as every reference opens what it names: today a user's page on the users page, for
 * an administrator; for anyone else it is text.
 */
export function PersonName({ person }: { person: Person }) {
  const t = useText()
  const user = useCurrentUser()
  const own = user && person.id && person.id === user.id
  return (
    <span>
      {user?.users && person.id ? <Link to={`/users/${person.id}`}>{person.name}</Link> : person.name}
      {own && <Typography.Text type="secondary">{` ${t('shell.person.you')}`}</Typography.Text>}
    </span>
  )
}
