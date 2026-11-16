import { Flex } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisCheckboxField } from '../platform/AxisCheckboxField'
import { SectionPart } from '../platform/SectionParts'
import { useEffect, useState } from 'react'
import { fetchStudioSettings, saveStudioSettings } from '../platform/studio'
import { useText } from '../platform/texts'

/**
 * The "Let AI accept its own drafts" switch of an application, the last part of the application's Form tab, after File
 * storage, with no heading of its own, see docs/reference/studio-resource-form.md#sections. The setting is kept in the
 * tenant database and not in the manifest, so the switch saves as soon as it is switched, apart from the manifest's
 * Save. It is off until a publisher turns it on. It is drawn by the shared yes-or-no field,
 * see docs/reference/actions-and-controls.md#fields. With `publisher` false the switch is disabled and its help ends
 * with that only a publisher can change it. A failed save puts the value back and shows an
 * error, and a failed load shows a warning in place of the switch.
 */
export function StudioAutoAccept({ application, publisher }: { application: string; publisher: boolean }) {
  const t = useText()
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
    <SectionPart testName="autoAccept">
      <Flex vertical gap="small" data-testid="studio-auto-accept">
        <AxisCheckboxField
          kind="switch"
          label={t('shell.studio.autoAccept.label')}
          help={
            publisher
              ? t('shell.studio.autoAccept.help')
              : `${t('shell.studio.autoAccept.help')} ${t('shell.studio.autoAccept.publisherOnly')}`
          }
          checked={autoAccept === true}
          disabled={!publisher || autoAccept === undefined}
          loading={saving}
          onChange={change}
        />
        {loadFailed && (
          <AxisAlert
            data-testid="studio-auto-accept-load-error"
            kind="warning"
            message={t('shell.studio.autoAccept.loadFailed')}
          />
        )}
        {saveFailed && (
          <AxisAlert
            data-testid="studio-auto-accept-save-error"
            kind="error"
            message={t('shell.studio.autoAccept.saveFailed')}
          />
        )}
      </Flex>
    </SectionPart>
  )
}
