import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { useCurrentUser } from './currentUser'
import { studioApplicationAddress, studioReleaseAddress } from './studioAddress'

/**
 * Draws a reference to an application in Studio, or to one of its releases when `release` is given. It is a link only
 * when the reader may use Studio, and plain text until the current user has loaded and for anyone else, see
 * docs/reference/actions-and-controls.md#opening-things. `application` is the application's manifest name.
 */
export function StudioLink({
  application,
  release,
  children,
}: {
  application: string
  release?: number
  children: ReactNode
}) {
  const me = useCurrentUser()
  if (!me?.studio) {
    return <>{children}</>
  }
  return (
    <Link
      to={release === undefined ? studioApplicationAddress(application) : studioReleaseAddress(application, release)}
    >
      {children}
    </Link>
  )
}
