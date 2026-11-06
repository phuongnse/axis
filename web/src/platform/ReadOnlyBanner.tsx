import { Alert } from 'antd'
import { useEffect, useState } from 'react'
import { fetchCurrentUser, type CurrentUser } from './currentUser'
import { useText } from './texts'

/**
 * The banner at the top of the shell's content while the tenant is read-only: a warning with the alert role that says
 * the workspace is read-only and why. When the user has a billing address, which only an administrator in a cell has,
 * it links to the billing portal. It renders nothing while the user loads, when loading fails, with nobody signed in
 * and while the tenant is active.
 */
export function ReadOnlyBanner() {
  const t = useText()
  const [user, setUser] = useState<CurrentUser | null | undefined>(undefined)

  useEffect(() => {
    const controller = new AbortController()
    fetchCurrentUser(controller.signal).then(setUser, (error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the current user failed', error)
      }
    })
    return () => controller.abort()
  }, [])

  if (user?.tenantStatus !== 'readOnly') {
    return null
  }

  return (
    <Alert
      banner
      type="warning"
      data-testid="read-only-banner"
      title={t('shell.readOnly.title')}
      description={t('shell.readOnly.description')}
      action={user.billingUrl ? <a href={user.billingUrl}>{t('shell.readOnly.billing')}</a> : undefined}
    />
  )
}
