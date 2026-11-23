import { Flex, Tabs } from 'antd'
import dayjs from 'dayjs'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
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
import { SectionPart, SectionParts } from '../platform/SectionParts'
import { StatusTag } from '../platform/StatusTag'
import { recordPageSize } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useAddressTab } from '../platform/useAddressTab'
import { useTableView } from '../platform/useTableView'
import { AccountAi } from './AccountAi'
import { AccountNotifications } from './AccountNotifications'
import { usePlatform } from './context'

const noParameters: string[] = []

const authenticatorFields = [
  { name: 'label', type: 'text' },
  { name: 'createdAt', type: 'date' },
] as const

const sessionFields = [
  { name: 'signedInAt', type: 'date' },
  { name: 'lastRequestAt', type: 'date' },
] as const

/** The account page's tabs, in order. Security, the first, is left out of the address. */
const accountTabs = ['security', 'notifications', 'ai'] as const

/**
 * The person's account page, titled Your account, with a Security tab, which opens first, a Notifications tab, see
 * {@link AccountNotifications}, and an AI tab, see {@link AccountAi}. On Security, for an account of the tenant's realm, Change password and the
 * authenticators come first: each button sends the browser to the identity provider's own step
 * through the sign-in route, which returns here. Remove is disabled on the only authenticator. A
 * brokered user sees neither. Below come the person's live sessions, with an End button on every
 * session but the current one and a button to end all the others. When the last login is too old to
 * end sessions, the buttons are disabled and Sign in again starts a fresh login that returns here.
 * Both tables have a Filter that narrows their own rows, kept in the address under `authenticators.`
 * and `sessions.`, see docs/reference/actions-and-controls.md#tables.
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
  const authenticatorsView = useTableView(
    authenticatorFields,
    recordPageSize,
    noParameters,
    false,
    true,
    false,
    'authenticators.',
  )
  const sessionsView = useTableView(sessionFields, recordPageSize, noParameters, false, true, false, 'sessions.')
  // Proposed (#3926): the open tab is in the address as `tab`, so Studio's Connect your AI opens `/account?tab=ai`.
  const [tab, selectTab] = useAddressTab('tab', accountTabs)

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
    <PageLayout title={t('shell.account.title')} band>
      <Tabs
        activeKey={tab}
        onChange={selectTab}
        items={[
          {
            key: 'security',
            label: t('shell.account.tabs.security'),
            children: (
              <>
                {authenticatorsFailed && (
                  <AxisAlert
                    data-testid="authenticators-error"
                    kind="error"
                    message={t('shell.account.authenticators.loadFailed')}
                  />
                )}
                <SectionParts>
                  {authenticators?.account && (
                    <SectionPart testName="password" heading={{ title: t('shell.account.password.title') }}>
                      <Flex vertical gap="small" align="flex-start">
                        <AxisButton
                          role="secondary"
                          onClick={() => assignLocation(accountActionUrl('/account', 'UPDATE_PASSWORD'))}
                        >
                          {t('shell.account.password.change')}
                        </AxisButton>
                      </Flex>
                    </SectionPart>
                  )}
                  {authenticators?.account && (
                    <SectionPart testName="authenticators" heading={{ title: t('shell.account.authenticators.title') }}>
                      <Flex vertical gap="small" align="flex-start">
                        <AxisButton
                          role="secondary"
                          onClick={() => assignLocation(accountActionUrl('/account', 'CONFIGURE_TOTP'))}
                        >
                          {t('shell.account.authenticators.add')}
                        </AxisButton>
                        <AxisTable<OwnAuthenticator>
                          rowKey={(authenticator) => authenticator.id}
                          view={{ query: authenticatorsView.view, onChange: authenticatorsView.setView, paging: false }}
                          dataSource={authenticators.authenticators}
                          emptyText={t('shell.account.authenticators.empty')}
                          columns={[
                            {
                              key: 'label',
                              kind: 'freeText',
                              title: t('shell.account.authenticators.name'),
                              render: (_value, authenticator) =>
                                authenticator.label ?? t('shell.account.authenticators.unnamed'),
                              filter: { kind: 'text', value: (authenticator) => authenticator.label },
                            },
                            {
                              key: 'createdAt',
                              kind: 'fixed',
                              title: t('shell.account.authenticators.addedAt'),
                              render: (_value, authenticator) =>
                                authenticator.createdAt ? time(authenticator.createdAt) : null,
                              filter: {
                                kind: 'date',
                                value: (authenticator) =>
                                  authenticator.createdAt ? dayjs(authenticator.createdAt).format('YYYY-MM-DD') : null,
                              },
                            },
                          ]}
                          rowActions={{
                            configured: 1,
                            actions: (authenticator) => [
                              {
                                key: 'remove',
                                labelKey: 'shell.account.authenticators.remove',
                                meaning: 'remove',
                                disabled: authenticators.authenticators.length <= 1,
                                onClick: () =>
                                  assignLocation(accountActionUrl('/account', `delete_credential:${authenticator.id}`)),
                              },
                            ],
                          }}
                        />
                      </Flex>
                    </SectionPart>
                  )}
                  <SectionPart testName="sessions" heading={{ title: t('shell.account.sessions.heading') }}>
                    <Flex vertical gap="small">
                      {loadFailed && (
                        <AxisAlert
                          data-testid="sessions-error"
                          kind="error"
                          message={t('shell.account.sessions.loadFailed')}
                        />
                      )}
                      {endFailed && (
                        <AxisAlert
                          data-testid="sessions-end-error"
                          kind="error"
                          message={t('shell.account.sessions.endFailed')}
                        />
                      )}
                      {sessions && !sessions.endAllowed && (
                        <AxisAlert
                          data-testid="sessions-reauthenticate"
                          kind="info"
                          message={t('shell.account.sessions.reauthenticate')}
                          action={
                            <AxisButton
                              role="secondary"
                              size="small"
                              onClick={() => assignLocation(reauthenticateUrl('/account'))}
                            >
                              {t('shell.account.sessions.signInAgain')}
                            </AxisButton>
                          }
                        />
                      )}
                      {sessions && (
                        <Flex vertical gap="small" align="flex-start">
                          <AxisButton
                            role="secondary"
                            destructive
                            disabled={!endAllowed || sessions.sessions.every((session) => session.current)}
                            onClick={() => end(endOtherSessions)}
                          >
                            {t('shell.account.sessions.endOthers')}
                          </AxisButton>
                          <AxisTable<OwnSession>
                            rowKey={(session) => session.id}
                            view={{ query: sessionsView.view, onChange: sessionsView.setView, paging: false }}
                            dataSource={sessions.sessions}
                            emptyText={t('shell.account.sessions.empty')}
                            columns={[
                              {
                                key: 'signedInAt',
                                kind: 'fixed',
                                title: t('shell.account.sessions.signedInAt'),
                                render: (_value, session) => (
                                  <Flex gap="small" align="center">
                                    {time(session.signedInAt)}
                                    {session.current && (
                                      <StatusTag tone="neutral">{t('shell.account.sessions.current')}</StatusTag>
                                    )}
                                  </Flex>
                                ),
                                filter: {
                                  kind: 'date',
                                  value: (session) => dayjs(session.signedInAt).format('YYYY-MM-DD'),
                                },
                              },
                              {
                                key: 'lastRequestAt',
                                kind: 'fixed',
                                title: t('shell.account.sessions.lastRequestAt'),
                                render: (_value, session) => time(session.lastRequestAt),
                                filter: {
                                  kind: 'date',
                                  value: (session) => dayjs(session.lastRequestAt).format('YYYY-MM-DD'),
                                },
                              },
                            ]}
                            rowActions={{
                              configured: 1,
                              actions: (session) =>
                                session.current
                                  ? null
                                  : [
                                      {
                                        key: 'end',
                                        labelKey: 'shell.account.sessions.end',
                                        destructive: true,
                                        disabled: !endAllowed,
                                        onClick: () => end(() => endSession(session.id)),
                                      },
                                    ],
                            }}
                          />
                        </Flex>
                      )}
                    </Flex>
                  </SectionPart>
                </SectionParts>
              </>
            ),
          },
          { key: 'notifications', label: t('shell.account.tabs.notifications'), children: <AccountNotifications /> },
          { key: 'ai', label: t('shell.account.tabs.ai'), children: <AccountAi /> },
        ]}
      />
    </PageLayout>
  )
}
