import { Alert, Button, Flex, Table, Tag } from 'antd'
import { useCallback, useEffect, useState } from 'react'
import { reauthenticateUrl } from '../platform/api'
import { assignLocation } from '../platform/navigation'
import {
  endOtherSessions,
  endSession,
  fetchOwnSessions,
  type OwnSession,
  type OwnSessions,
} from '../platform/ownSessions'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import { usePlatform } from './context'

/**
 * The person's live sessions, with an End button on every session but the current one and a button
 * to end all the others. When the last login is too old to end sessions, the buttons are disabled
 * and Sign in again starts a fresh login that returns here.
 */
export function AccountPage() {
  const t = useText()
  const { locale } = usePlatform().current
  const [sessions, setSessions] = useState<OwnSessions>()
  const [loadFailed, setLoadFailed] = useState(false)
  const [endFailed, setEndFailed] = useState(false)
  const [reload, setReload] = useState(0)

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
    <PageLayout title={t('shell.account.sessions.title')}>
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
            <Button size="small" onClick={() => assignLocation(reauthenticateUrl('/account'))}>
              {t('shell.account.sessions.signInAgain')}
            </Button>
          }
        />
      )}
      {sessions && (
        <Flex vertical gap="small" align="flex-start">
          <Button
            disabled={!endAllowed || sessions.sessions.every((session) => session.current)}
            onClick={() => end(endOtherSessions)}
          >
            {t('shell.account.sessions.endOthers')}
          </Button>
          <Table<OwnSession>
            style={{ width: '100%' }}
            rowKey="id"
            pagination={false}
            dataSource={sessions.sessions}
            columns={[
              { title: t('shell.account.sessions.signedInAt'), dataIndex: 'signedInAt', render: time },
              { title: t('shell.account.sessions.lastRequestAt'), dataIndex: 'lastRequestAt', render: time },
              {
                key: 'actions',
                render: (_, session) =>
                  session.current ? (
                    <Tag>{t('shell.account.sessions.current')}</Tag>
                  ) : (
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
