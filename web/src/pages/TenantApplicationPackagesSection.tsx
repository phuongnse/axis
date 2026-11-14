import { Flex, Modal, Typography } from 'antd'
import { useEffect, useId, useState } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import {
  fetchTenantApplicationPackages,
  setTenantApplicationPackages,
  type TenantApplicationPackages,
} from '../platform/tenantApplicationPackages'

/**
 * The "Application packages" section of the users page. It renders nothing unless
 * `GET /api/tenant/application-packages` answers `200`, so it stays hidden from a non-administrator and for a tenant with no
 * directory row or a preview tenant. It says whether application packages are on and has one button that opens a
 * confirmation, and only the confirmation's button sends the `PUT`. After a change the section reads the switch again, so it
 * shows what the server stored. A refusal past the quota shows the problem's title.
 * See docs/reference/platform-pages.md#application-packages.
 */
export function TenantApplicationPackagesSection() {
  const t = useText()
  const { size } = useDesignTokens()
  const headingId = useId()
  const [packages, setPackages] = useState<TenantApplicationPackages | null>(null)
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<string>()

  useEffect(() => {
    const controller = new AbortController()
    fetchTenantApplicationPackages(controller.signal).then(
      (answer) => {
        if (!controller.signal.aborted) {
          setPackages(answer)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the application packages switch failed', error)
        }
      },
    )
    return () => controller.abort()
  }, [])

  if (!packages) {
    return null
  }

  const next = packages.on ? 'turnOff' : 'turnOn'

  async function change(on: boolean) {
    setBusy(true)
    setFailure(undefined)
    try {
      const result = await setTenantApplicationPackages(on)
      if (result.ok) {
        setPackages(await fetchTenantApplicationPackages())
      } else {
        setFailure(result.title ?? t('shell.users.applicationPackages.changeFailed'))
      }
    } catch (error: unknown) {
      console.warn('Changing the application packages switch failed', error)
      setFailure(t('shell.users.applicationPackages.changeFailed'))
    } finally {
      setBusy(false)
      setConfirming(false)
    }
  }

  return (
    <section aria-labelledby={headingId} data-testid="tenant-application-packages">
      <Flex vertical gap="small">
        <Typography.Title level={3} id={headingId}>
          {t('shell.users.applicationPackages.title')}
        </Typography.Title>
        <Typography.Text>{t(`shell.users.applicationPackages.${packages.on ? 'on' : 'off'}`)}</Typography.Text>
        {failure && <AxisAlert data-testid="tenant-application-packages-notice" kind="error" message={failure} />}
        <Flex gap="small">
          <AxisButton
            role="secondary"
            onClick={() => {
              setFailure(undefined)
              setConfirming(true)
            }}
          >
            {t(`shell.users.applicationPackages.${next}`)}
          </AxisButton>
        </Flex>
      </Flex>
      <Modal
        open={confirming}
        width={size.dialog.small}
        title={t(`shell.users.applicationPackages.${next}Confirm`)}
        okText={t(`shell.users.applicationPackages.${next}.ok`)}
        cancelText={t('shell.users.roles.cancel')}
        confirmLoading={busy}
        onOk={() => void change(!packages.on)}
        onCancel={() => setConfirming(false)}
      >
        <Typography.Text>{t(`shell.users.applicationPackages.${next}Effect`)}</Typography.Text>
      </Modal>
    </section>
  )
}
