import { Flex, Select, Switch, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { Announcement } from '../platform/Announcement'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisField } from '../platform/AxisField'
import { AxisTable } from '../platform/AxisTable'
import { fetchCurrentUser } from '../platform/currentUser'
import { fillText } from '../platform/fillText'
import {
  fetchNotificationPreferences,
  notificationSources,
  saveNotificationPreferences,
  type NotificationPreferences,
  type NotificationSource,
  type SourcePreference,
} from '../platform/ownNotificationPreferences'
import { SectionPart } from '../platform/SectionParts'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { usePlatform } from './context'

/** How a source's emails go: not at all, as each notice comes, or gathered in the daily digest. */
type EmailChoice = 'off' | 'now' | 'digest'

/** A row of the table: a source the person chooses for, or the tenant notices, which always show in the application. */
type Row = { key: NotificationSource; locked: false } | { key: 'tenant'; locked: true }

const hours = Array.from({ length: 24 }, (_, hour) => hour)

function emailChoice(preference: SourcePreference): EmailChoice {
  return !preference.channels.email ? 'off' : preference.digest ? 'digest' : 'now'
}

/** The time zone in words in the page's locale, such as "Indochina Time", else its IANA name. */
function timeZoneName(timeZone: string, locale: string): string {
  try {
    const part = new Intl.DateTimeFormat(locale, { timeZone, timeZoneName: 'longGeneric' })
      .formatToParts(new Date())
      .find((candidate) => candidate.type === 'timeZoneName')
    return part?.value ?? timeZone
  } catch {
    return timeZone
  }
}

/**
 * Proposed: the account page's Notifications tab, see docs/reference/notifications.md#notification-preferences. A table
 * has a row for each source, a switch for the application and a choice for email: off, as each notice comes, or in the
 * daily digest. A change is saved at once. The digest's hour shows while a source uses the digest. An administrator also
 * sees the tenant notices, locked on in the application.
 */
export function AccountNotifications() {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const { locale } = usePlatform().current
  const [preferences, setPreferences] = useState<NotificationPreferences>()
  const [administrator, setAdministrator] = useState(false)
  const [loadFailed, setLoadFailed] = useState(false)
  const [saveFailed, setSaveFailed] = useState(false)
  const [saving, setSaving] = useState<string>()
  const [announcement, setAnnouncement] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    fetchNotificationPreferences(controller.signal).then(setPreferences, (error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the notification preferences failed', error)
        setLoadFailed(true)
      }
    })
    fetchCurrentUser(controller.signal).then(
      (user) => setAdministrator(user?.users ?? false),
      () => setAdministrator(false),
    )
    return () => controller.abort()
  }, [])

  /** Saves the next preferences at once, and puts the last ones back when the save is refused. */
  const save = (next: NotificationPreferences, what: string) => {
    const previous = preferences
    setPreferences(next)
    setSaving(what)
    setSaveFailed(false)
    setAnnouncement('')
    saveNotificationPreferences(next).then(
      (status) => {
        setSaving(undefined)
        if (status === 204) {
          setAnnouncement(t('shell.account.notifications.saved'))
        } else {
          setPreferences(previous)
          setSaveFailed(true)
        }
      },
      (error: unknown) => {
        console.warn('Saving the notification preferences failed', error)
        setSaving(undefined)
        setPreferences(previous)
        setSaveFailed(true)
      },
    )
  }

  const choose = (source: NotificationSource, preference: SourcePreference, what: string) =>
    preferences && save({ ...preferences, sources: { ...preferences.sources, [source]: preference } }, what)

  const rows: Row[] = [
    ...notificationSources.map((key): Row => ({ key, locked: false })),
    ...(administrator ? [{ key: 'tenant', locked: true } as const] : []),
  ]
  const name = (row: Row) => t(`shell.account.notifications.source.${row.key}`)
  const cellName = (row: Row, channel: string) =>
    fillText(t('shell.account.notifications.cell'), { notice: name(row), channel })
  const anyDigest =
    preferences !== undefined &&
    notificationSources.some((source) => emailChoice(preferences.sources[source]) === 'digest')

  return (
    <SectionPart testName="notifications">
      <Announcement>{announcement}</Announcement>
      <Flex vertical gap="middle">
        <Typography.Text style={{ color: colour.textMuted }}>{t('shell.account.notifications.line')}</Typography.Text>
        {loadFailed && <AxisAlert kind="error" message={t('shell.account.notifications.loadFailed')} />}
        {saveFailed && <AxisAlert kind="error" message={t('shell.account.notifications.saveFailed')} />}
        {preferences !== undefined && (
          <AxisTable<Row>
            testId="account-notifications"
            rowKey={(row) => row.key}
            dataSource={rows}
            pagination={false}
            columns={[
              {
                key: 'notice',
                kind: 'freeText',
                title: t('shell.account.notifications.notice'),
                render: (_value, row) => (
                  <Flex vertical>
                    <span>{name(row)}</span>
                    <Typography.Text style={{ color: colour.textMuted, fontSize: 12, lineHeight: '20px' }}>
                      {t(`shell.account.notifications.source.${row.key}.help`)}
                    </Typography.Text>
                  </Flex>
                ),
              },
              {
                key: 'application',
                kind: 'fixed',
                title: t('shell.account.notifications.application'),
                render: (_value, row) =>
                  row.locked ? (
                    <Flex gap="small" align="center">
                      <Switch
                        checked
                        disabled
                        size="small"
                        aria-label={cellName(row, t('shell.account.notifications.application'))}
                      />
                      <span style={{ color: colour.textMuted }}>{t('shell.account.notifications.alwaysOn')}</span>
                    </Flex>
                  ) : (
                    <Switch
                      size="small"
                      checked={preferences.sources[row.key].channels.application}
                      loading={saving === `${row.key}.application`}
                      aria-label={cellName(row, t('shell.account.notifications.application'))}
                      onChange={(application) => {
                        const current = preferences.sources[row.key]
                        choose(
                          row.key,
                          { ...current, channels: { ...current.channels, application } },
                          `${row.key}.application`,
                        )
                      }}
                    />
                  ),
              },
              {
                key: 'email',
                kind: 'fixed',
                title: t('shell.account.notifications.email'),
                render: (_value, row) =>
                  row.locked ? null : (
                    <Select<EmailChoice>
                      style={{ width: 220 }}
                      value={emailChoice(preferences.sources[row.key])}
                      loading={saving === `${row.key}.email`}
                      aria-label={cellName(row, t('shell.account.notifications.email'))}
                      onChange={(choice) => {
                        const current = preferences.sources[row.key]
                        choose(
                          row.key,
                          { channels: { ...current.channels, email: choice !== 'off' }, digest: choice === 'digest' },
                          `${row.key}.email`,
                        )
                      }}
                      options={[
                        { value: 'off', label: t('shell.account.notifications.email.off') },
                        { value: 'now', label: t('shell.account.notifications.email.now') },
                        { value: 'digest', label: t('shell.account.notifications.email.digest') },
                      ]}
                    />
                  ),
              },
            ]}
          />
        )}
        {preferences !== undefined && anyDigest && (
          <div style={{ marginTop: spacing.xs }}>
            <AxisField
              label={t('shell.account.notifications.digestHour')}
              help={fillText(t('shell.account.notifications.digestHour.help'), {
                timeZone: timeZoneName(preferences.timeZone, locale),
              })}
            >
              <Select<number>
                style={{ width: 120 }}
                value={preferences.digestHour}
                loading={saving === 'digestHour'}
                onChange={(digestHour) => save({ ...preferences, digestHour }, 'digestHour')}
                options={hours.map((hour) => ({ value: hour, label: `${String(hour).padStart(2, '0')}:00` }))}
              />
            </AxisField>
          </div>
        )}
      </Flex>
    </SectionPart>
  )
}
