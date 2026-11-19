import { Checkbox, Flex, Select, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
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

type Choice = 'application' | 'email' | 'digest'

const hours = Array.from({ length: 24 }, (_, hour) => hour)

/** The choices a source's checkbox group holds, from its preference. */
function choicesOf(preference: SourcePreference): Choice[] {
  return [
    ...(preference.channels.application ? (['application'] as const) : []),
    ...(preference.channels.email ? (['email'] as const) : []),
    ...(preference.digest && preference.channels.email ? (['digest'] as const) : []),
  ]
}

/**
 * Proposed: the account page's Notifications part, where a person chooses how each source of notices reaches them, in
 * the application, by email and in the daily digest, and the hour of the digest, see
 * docs/reference/notifications.md#notification-preferences. Tenant notices always show in the application.
 */
export function AccountNotifications() {
  const t = useText()
  const { colour } = useDesignTokens()
  const [saved, setSaved] = useState<NotificationPreferences>()
  const [preferences, setPreferences] = useState<NotificationPreferences>()
  const [loadFailed, setLoadFailed] = useState(false)
  const [saveFailed, setSaveFailed] = useState(false)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchNotificationPreferences(controller.signal).then(
      (loaded) => {
        setSaved(loaded)
        setPreferences(loaded)
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the notification preferences failed', error)
          setLoadFailed(true)
        }
      },
    )
    return () => controller.abort()
  }, [])

  const choose = (source: NotificationSource, choices: Choice[]) =>
    setPreferences((current) =>
      current === undefined
        ? current
        : {
            ...current,
            sources: {
              ...current.sources,
              [source]: {
                channels: { application: choices.includes('application'), email: choices.includes('email') },
                digest: choices.includes('email') && choices.includes('digest'),
              },
            },
          },
    )

  const save = () => {
    if (preferences === undefined) {
      return
    }
    setSaving(true)
    setSaveFailed(false)
    saveNotificationPreferences(preferences).then(
      (status) => {
        setSaving(false)
        if (status === 204) {
          setSaved(preferences)
        } else {
          setSaveFailed(true)
        }
      },
      (error: unknown) => {
        console.warn('Saving the notification preferences failed', error)
        setSaving(false)
        setSaveFailed(true)
      },
    )
  }

  const changed = JSON.stringify(preferences) !== JSON.stringify(saved)
  const anyDigest =
    preferences !== undefined && notificationSources.some((source) => preferences.sources[source].digest)

  return (
    <SectionPart
      testName="notifications"
      heading={{ title: t('shell.account.notifications.title'), line: t('shell.account.notifications.line') }}
    >
      <Flex vertical gap="middle" align="flex-start">
        {loadFailed && <AxisAlert kind="error" message={t('shell.account.notifications.loadFailed')} />}
        {preferences !== undefined &&
          notificationSources.map((source) => {
            const preference = preferences.sources[source]
            return (
              <AxisFieldGroup
                key={source}
                title={t(`shell.account.notifications.source.${source}`)}
                help={t(`shell.account.notifications.source.${source}.help`)}
              >
                <Checkbox.Group<Choice>
                  data-testid={`account-notifications-${source}`}
                  value={choicesOf(preference)}
                  onChange={(choices) => choose(source, choices)}
                  options={[
                    { value: 'application', label: t('shell.account.notifications.application') },
                    { value: 'email', label: t('shell.account.notifications.email') },
                    {
                      value: 'digest',
                      label: t('shell.account.notifications.digest'),
                      disabled: !preference.channels.email,
                    },
                  ]}
                />
              </AxisFieldGroup>
            )
          })}
        {preferences !== undefined && (
          <AxisField
            label={t('shell.account.notifications.digestHour')}
            help={fillText(t('shell.account.notifications.digestHour.help'), { timeZone: preferences.timeZone })}
          >
            <Select
              style={{ width: 160 }}
              disabled={!anyDigest}
              value={preferences.digestHour}
              onChange={(digestHour: number) => setPreferences({ ...preferences, digestHour })}
              options={hours.map((hour) => ({ value: hour, label: `${String(hour).padStart(2, '0')}:00` }))}
            />
          </AxisField>
        )}
        {preferences !== undefined && (
          <Typography.Text style={{ color: colour.textMuted }}>
            {t('shell.account.notifications.tenantNotices')}
          </Typography.Text>
        )}
        {saveFailed && <AxisAlert kind="error" message={t('shell.account.notifications.saveFailed')} />}
        {preferences !== undefined && (
          <AxisButton role="main" disabled={!changed} loading={saving} onClick={save}>
            {t('shell.account.notifications.save')}
          </AxisButton>
        )}
      </Flex>
    </SectionPart>
  )
}
