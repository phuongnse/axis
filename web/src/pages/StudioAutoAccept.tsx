import { Alert, Flex, Switch, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { fetchStudioSettings, saveStudioSettings } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

/**
 * The "Let AI accept its own drafts" switch of an application, in a panel of its own below the manifest's Save row.
 * The setting is kept in the tenant database and not in the manifest, so the switch saves as soon as it is switched,
 * apart from the manifest's Save. It is off until a publisher turns it on. With `publisher` false the switch is
 * disabled and a line says that only a publisher can change it. A failed save puts the value back and shows an
 * error, and a failed load shows a warning in place of the switch.
 */
export function StudioAutoAccept({ application, publisher }: { application: string; publisher: boolean }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [autoAccept, setAutoAccept] = useState<boolean>()
  const [loadFailed, setLoadFailed] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saveFailed, setSaveFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioSettings(application, controller.signal).then(
      (settings) => setAutoAccept(settings.autoAccept),
      () => {
        if (!controller.signal.aborted) {
          setLoadFailed(true)
        }
      },
    )
    return () => controller.abort()
  }, [application])

  function change(checked: boolean) {
    const before = autoAccept
    setAutoAccept(checked)
    setSaveFailed(false)
    setSaving(true)
    saveStudioSettings(application, checked).then(
      (saved) => {
        setAutoAccept(saved.autoAccept)
        setSaving(false)
      },
      () => {
        setAutoAccept(before)
        setSaveFailed(true)
        setSaving(false)
      },
    )
  }

  return (
    <section
      data-testid="studio-auto-accept"
      style={{
        padding: spacing.md,
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
      }}
    >
      <Flex vertical gap="small">
        <Flex align="center" gap="small">
          <Switch
            aria-label={t('shell.studio.autoAccept.label')}
            checked={autoAccept === true}
            disabled={!publisher || autoAccept === undefined}
            loading={saving}
            onChange={change}
          />
          <Typography.Text strong>{t('shell.studio.autoAccept.label')}</Typography.Text>
        </Flex>
        <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.autoAccept.help')}</Typography.Text>
        {!publisher && (
          <Typography.Text data-testid="studio-auto-accept-publisher-only" style={{ color: colour.textMuted }}>
            {t('shell.studio.autoAccept.publisherOnly')}
          </Typography.Text>
        )}
        {loadFailed && (
          // oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert.
          <Alert
            data-testid="studio-auto-accept-load-error"
            type="warning"
            title={t('shell.studio.autoAccept.loadFailed')}
          />
        )}
        {saveFailed && (
          // oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert.
          <Alert
            data-testid="studio-auto-accept-save-error"
            type="error"
            title={t('shell.studio.autoAccept.saveFailed')}
          />
        )}
      </Flex>
    </section>
  )
}
