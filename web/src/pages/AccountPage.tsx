import { Alert, Button, Flex, Tag, Typography } from 'antd'
import { useCallback, useEffect, useState } from 'react'
import { accountActionUrl, reauthenticateUrl } from '../platform/api'
import { AxisTable } from '../platform/AxisTable'
import { assignLocation } from '../platform/navigation'
import { fetchOwnAuthenticators, type OwnAuthenticator, type OwnAuthenticators } from '../platform/ownAuthenticators'
import {
  endOtherSessions,
  endSession,
  fetchOwnSessions,
  type OwnSession,
  type OwnSessions,
} from '../platform/ownSessions'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import { visuallyHidden } from '../platform/visuallyHidden'
import { usePlatform } from './context'

/**
 * The person's account page. For an account of the tenant's realm, Change password and the
 * authenticators come first: each button sends the browser to the identity provider's own step
 * through the sign-in route, which returns here. Remove is disabled on the only authenticator. A
 * brokered user sees neither. Below come the person's live sessions, with an End button on every
 * session but the current one and a button to end all the others. When the last login is too old to
 * end sessions, the buttons are disabled and Sign in again starts a fresh login that returns here.
 */
export function AccountPage() {
  const t = useText()
  const platform = usePlatform()
  const { locale } = platform.current
  const [sessions, setSessions] = useState<OwnSessions>()
  const [loadFailed, setLoadFailed] = useState(false)
  const [endFailed, setEndFailed] = useState(false)
  const [reload, setReload] = useState(0)
  const [authenticators, setAuthenticators] = useState<OwnAuthenticators>()
  const [authenticatorsFailed, setAuthenticatorsFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchOwnAuthenticators(controller.signal).then(setAuthenticators, (error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the authenticators failed', error)
        setAuthenticatorsFailed(true)
      }
    })
    return () => controller.abort()
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    fetchOwnSessions(controller.signal).then(
      (loaded) => {
        setSessions(loaded)
        setLoadFailed(false)
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the sessions failed', error)
          setLoadFailed(true)
        }
      },
    )
    return () => controller.abort()
  }, [reload])

  // A 403 means the 5-minute window closed, so the fresh list shows Sign in again.
  const end = useCallback((request: () => Promise<number>) => {
    setEndFailed(false)
    request().then(
      (status) => {
        setEndFailed(status !== 204 && status !== 403)
        if (status === 204 || status === 403) {
          setReload((count) => count + 1)
        }
      },
      (error: unknown) => {
        console.warn('Ending the session failed', error)
        setEndFailed(true)
      },
    )
  }, [])

  const endAllowed = sessions?.endAllowed ?? false
  const time = (value: string) => new Date(value).toLocaleString(locale)

  return (
    <PageLayout
      title={t('shell.account.sessions.title')}
      breadcrumb={[{ label: t(platform.site.titleKey), to: '/' }, { label: t('shell.account.sessions.title') }]}
    >
      {authenticatorsFailed && (
        <Alert data-testid="authenticators-error" type="error" title={t('shell.account.authenticators.loadFailed')} />
      )}
      {authenticators?.account && (
        <>
          <Flex vertical gap="small" align="flex-start">
            <Typography.Title level={5}>{t('shell.account.password.title')}</Typography.Title>
            {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
            <Button onClick={() => assignLocation(accountActionUrl('/account', 'UPDATE_PASSWORD'))}>
              {t('shell.account.password.change')}
            </Button>
          </Flex>
          <Flex vertical gap="small" align="flex-start">
            <Typography.Title level={5}>{t('shell.account.authenticators.title')}</Typography.Title>
            {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
            <Button onClick={() => assignLocation(accountActionUrl('/account', 'CONFIGURE_TOTP'))}>
              {t('shell.account.authenticators.add')}
            </Button>
            <AxisTable<OwnAuthenticator>
              rowKey={(authenticator) => authenticator.id}
              pagination={false}
              dataSource={authenticators.authenticators}
              emptyText={t('shell.account.authenticators.empty')}
              columns={[
                {
                  key: 'label',
                  kind: 'freeText',
                  title: t('shell.account.authenticators.name'),
                  render: (_value, authenticator) => authenticator.label ?? t('shell.account.authenticators.unnamed'),
                },
                {
                  key: 'createdAt',
                  kind: 'fixed',
                  title: t('shell.account.authenticators.addedAt'),
                  render: (_value, authenticator) => (authenticator.createdAt ? time(authenticator.createdAt) : null),
                },
                {
                  key: 'remove',
                  kind: 'fixed',
                  title: <span style={visuallyHidden}>{t('shell.table.rowActions')}</span>,
                  render: (_value, authenticator) => (
                    // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
                    <Button
                      size="small"
                      disabled={authenticators.authenticators.length <= 1}
                      onClick={() =>
                        assignLocation(accountActionUrl('/account', `delete_credential:${authenticator.id}`))
                      }
                    >
                      {t('shell.account.authenticators.remove')}
                    </Button>
                  ),
                },
              ]}
            />
          </Flex>
        </>
      )}
      {loadFailed && <Alert data-testid="sessions-error" type="error" title={t('shell.account.sessions.loadFailed')} />}
      {endFailed && (
        <Alert data-testid="sessions-end-error" type="error" title={t('shell.account.sessions.endFailed')} />
      )}
      {sessions && !sessions.endAllowed && (
        <Alert
          data-testid="sessions-reauthenticate"
          type="info"
          showIcon
          title={t('shell.account.sessions.reauthenticate')}
          action={
            // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
            <Button size="small" onClick={() => assignLocation(reauthenticateUrl('/account'))}>
              {t('shell.account.sessions.signInAgain')}
            </Button>
          }
        />
      )}
      {sessions && (
        <Flex vertical gap="small" align="flex-start">
          {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
          <Button
            disabled={!endAllowed || sessions.sessions.every((session) => session.current)}
            onClick={() => end(endOtherSessions)}
          >
            {t('shell.account.sessions.endOthers')}
          </Button>
          <AxisTable<OwnSession>
            rowKey={(session) => session.id}
            pagination={false}
            dataSource={sessions.sessions}
            emptyText={t('shell.account.sessions.empty')}
            columns={[
              {
                key: 'signedInAt',
                kind: 'fixed',
                title: t('shell.account.sessions.signedInAt'),
                render: (_value, session) => time(session.signedInAt),
              },
              {
                key: 'lastRequestAt',
                kind: 'fixed',
                title: t('shell.account.sessions.lastRequestAt'),
                render: (_value, session) => time(session.lastRequestAt),
              },
              {
                key: 'end',
                kind: 'fixed',
                title: <span style={visuallyHidden}>{t('shell.table.rowActions')}</span>,
                render: (_value, session) =>
                  session.current ? (
                    <Tag>{t('shell.account.sessions.current')}</Tag>
                  ) : (
                    // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
                    <Button size="small" disabled={!endAllowed} onClick={() => end(() => endSession(session.id))}>
                      {t('shell.account.sessions.end')}
                    </Button>
                  ),
              },
            ]}
          />
        </Flex>
      )}
    </PageLayout>
  )
}
