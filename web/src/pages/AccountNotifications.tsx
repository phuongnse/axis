import { Flex, Select } from 'antd'
import { useEffect, useState } from 'react'
import { Announcement } from '../platform/Announcement'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisCheckboxField } from '../platform/AxisCheckboxField'
import { AxisField } from '../platform/AxisField'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { fetchCurrentUser } from '../platform/currentUser'
import { fillText } from '../platform/fillText'
import { timeOfDayText } from '../platform/formatValue'
import {
  fetchOwnNotificationPreferences,
  replaceOwnNotificationPreferences,
  type NotificationSource,
  type OwnNotificationPreferences,
  type SourceName,
} from '../platform/ownNotificationPreferences'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { browserTimeZone, timeZoneText } from '../platform/timeZones'
import { usePlatform } from './context'

type EmailChoice = 'off' | 'now' | 'digest'

/** One of the four sources, or the administrators' notices, which are always on. */
type Row = SourceName | 'administrators'

/** A row of the table. */
interface TableRow {
  name: Row
}

const sourceRows: SourceName[] = ['task', 'mention', 'process', 'authoringRequest']
const emailChoices: EmailChoice[] = ['off', 'now', 'digest']
const hours = Array.from({ length: 24 }, (_, hour) => hour)

function emailChoice(source: NotificationSource): EmailChoice {
  return !source.channels.email ? 'off' : source.digest ? 'digest' : 'now'
}

// Off keeps `digest`, so the choice shows Off whatever it holds. Right away and the digest set both.
function withEmailChoice(source: NotificationSource, choice: EmailChoice): NotificationSource {
  return choice === 'off'
    ? { ...source, channels: { ...source.channels, email: false } }
    : { ...source, channels: { ...source.channels, email: true }, digest: choice === 'digest' }
}

/**
 * The account page's Notifications tab: a table of the four sources with an in-app switch and an email choice, and the
 * hour of the daily digest while any source is in it. Each change sends the whole preferences at once, with the
 * browser's time zone, and only the changed control is busy while the other controls wait. A `204` is announced, and any
 * other answer puts the last value back and shows an alert. A person who administers users also sees a locked row for
 * notices to administrators. See docs/reference/platform-pages.md#notifications.
 */
export function AccountNotifications() {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const { locale } = usePlatform().current
  const [preferences, setPreferences] = useState<OwnNotificationPreferences>()
  const [loadFailed, setLoadFailed] = useState(false)
  const [users, setUsers] = useState(false)
  const [saving, setSaving] = useState<string>()
  const [refused, setRefused] = useState(false)
  const [saved, setSaved] = useState<string>()

  useEffect(() => {
    const controller = new AbortController()
    fetchOwnNotificationPreferences(controller.signal).then(setPreferences, (error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the notification preferences failed', error)
        setLoadFailed(true)
      }
    })
    fetchCurrentUser(controller.signal).then(
      (user) => setUsers(user?.users === true),
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the current user failed', error)
        }
      },
    )
    return () => controller.abort()
  }, [])

  const change = (control: string, update: (current: OwnNotificationPreferences) => OwnNotificationPreferences) => {
    if (preferences === undefined) {
      return
    }
    const previous = preferences
    const next = { ...update(previous), timeZone: browserTimeZone().id }
    setPreferences(next)
    setRefused(false)
    setSaved(undefined)
    setSaving(control)
    const putBack = () => {
      setPreferences(previous)
      setRefused(true)
    }
    replaceOwnNotificationPreferences(next)
      .then(
        (status) => {
          if (status === 204) {
            setSaved(t('shell.account.notifications.saved'))
          } else {
            putBack()
          }
        },
        (error: unknown) => {
          console.warn('Saving the notification preferences failed', error)
          putBack()
        },
      )
      .finally(() => setSaving(undefined))
  }

  const changeSource = (
    control: string,
    name: SourceName,
    update: (source: NotificationSource) => NotificationSource,
  ) =>
    change(control, (current) => ({
      ...current,
      sources: { ...current.sources, [name]: update(current.sources[name]) },
    }))

  const busy = saving !== undefined
  const applicationTitle = t('shell.account.notifications.application')
  const emailTitle = t('shell.account.notifications.email')
  const sourceTitle = (row: Row) => t(`shell.account.notifications.${row}.title`)
  const controlName = (row: Row, column: string) =>
    fillText(t('shell.account.notifications.controlName'), { source: sourceTitle(row), column })

  const columns: AxisColumn<TableRow>[] = [
    {
      key: 'source',
      kind: 'freeText',
      title: t('shell.account.notifications.source'),
      render: (_value, { name: row }) => (
        <div>
          <div>{sourceTitle(row)}</div>
          <div style={{ color: colour.textMuted }}>{t(`shell.account.notifications.${row}.description`)}</div>
        </div>
      ),
    },
    {
      key: 'application',
      kind: 'fixed',
      title: applicationTitle,
      render: (_value, { name: row }) =>
        row === 'administrators' ? (
          <Flex align="center" gap={spacing.sm}>
            <AxisCheckboxField
              kind="switch"
              hideLabel
              label={controlName(row, applicationTitle)}
              help=""
              checked
              disabled
              onChange={() => {}}
            />
            <span>{t('shell.account.notifications.alwaysOn')}</span>
          </Flex>
        ) : (
          <AxisCheckboxField
            kind="switch"
            hideLabel
            label={controlName(row, applicationTitle)}
            help=""
            checked={preferences?.sources[row].channels.application === true}
            disabled={busy && saving !== `${row}.application`}
            loading={saving === `${row}.application`}
            onChange={(application) =>
              changeSource(`${row}.application`, row, (source) => ({
                ...source,
                channels: { ...source.channels, application },
              }))
            }
          />
        ),
    },
    {
      key: 'email',
      kind: 'fixed',
      title: emailTitle,
      render: (_value, { name: row }) =>
        row === 'administrators' || preferences === undefined ? null : (
          <AxisField label={controlName(row, emailTitle)} help="" hideLabel>
            <Select<EmailChoice>
              value={emailChoice(preferences.sources[row])}
              disabled={busy && saving !== `${row}.email`}
              loading={saving === `${row}.email`}
              style={{ minWidth: 200 }}
              options={emailChoices.map((choice) => ({
                value: choice,
                label: t(`shell.account.notifications.email.${choice}`),
              }))}
              onChange={(choice) => changeSource(`${row}.email`, row, (source) => withEmailChoice(source, choice))}
            />
          </AxisField>
        ),
    },
  ]

  const digesting =
    preferences !== undefined && sourceRows.some((name) => emailChoice(preferences.sources[name]) === 'digest')

  return (
    <Flex vertical gap="middle">
      <Announcement>{saved}</Announcement>
      {loadFailed && (
        <AxisAlert
          data-testid="notifications-error"
          kind="error"
          message={t('shell.account.notifications.loadFailed')}
        />
      )}
      {refused && (
        <AxisAlert
          data-testid="notifications-refused"
          kind="error"
          message={t('shell.account.notifications.refused')}
        />
      )}
      {preferences && (
        <>
          <AxisTable<TableRow>
            rowKey={(row) => row.name}
            dataSource={(users ? [...sourceRows, 'administrators' as const] : sourceRows).map((name) => ({ name }))}
            columns={columns}
          />
          {digesting && (
            <AxisField
              label={t('shell.account.notifications.digestHour')}
              help={fillText(t('shell.account.notifications.digestHour.help'), {
                zone: timeZoneText(browserTimeZone()),
              })}
            >
              <Select<number>
                value={preferences.digestHour}
                disabled={busy && saving !== 'digestHour'}
                loading={saving === 'digestHour'}
                style={{ minWidth: 200 }}
                options={hours.map((hour) => ({ value: hour, label: timeOfDayText(hour, locale) }))}
                onChange={(digestHour) => change('digestHour', (current) => ({ ...current, digestHour }))}
              />
            </AxisField>
          )}
        </>
      )}
    </Flex>
  )
}
