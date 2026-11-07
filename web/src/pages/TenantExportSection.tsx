import { Alert, Flex, Modal, Typography } from 'antd'
import { useEffect, useId, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { fetchCurrentUser } from '../platform/currentUser'
import { saveFile } from '../platform/exports'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { downloadTenantExport, readTenantExport, startTenantExport, type TenantExport } from '../platform/tenantExports'
import { usePlatform } from './context'

/** The local storage key that holds the id of the last export, so a reload shows it again. */
const storageKey = 'axis.tenantExport'

/** How long the section waits between two checks of a queued or running export. */
const pollDelay = 2000

type Notice = 'expired' | 'startFailed' | 'downloadFailed'

function storedId(): string | null {
  try {
    return localStorage.getItem(storageKey)
  } catch {
    return null
  }
}

function remember(id: string | null): void {
  try {
    if (id) {
      localStorage.setItem(storageKey, id)
    } else {
      localStorage.removeItem(storageKey)
    }
  } catch (error: unknown) {
    console.warn('Keeping the last export failed', error)
  }
}

/**
 * The "Export data" section of the users page for an administrator in a cell tenant, which `tenantExports` of
 * `GET /api/me` says. It renders nothing without it. It warns that the file holds every value in clear, and a
 * confirmation repeats the warning before the Start button sends the start. It follows the export every 2 seconds while
 * it is queued or running, and offers the ZIP for download once it has succeeded. The id of the last export stays in
 * local storage, so a reload shows the same export. An export that has expired or is gone is forgotten.
 */
export function TenantExportSection() {
  const t = useText()
  const { size } = useDesignTokens()
  const { locale } = usePlatform().current
  const headingId = useId()
  const [enabled, setEnabled] = useState(false)
  const [exported, setExported] = useState<TenantExport>()
  // The export to follow. Setting it again to the same id does not restart the check, so a start sets it once.
  const [following, setFollowing] = useState<string | null>(storedId)
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<Notice>()

  useEffect(() => {
    const controller = new AbortController()
    fetchCurrentUser(controller.signal).then(
      (user) => setEnabled(user?.tenantExports === true),
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the current user failed', error)
        }
      },
    )
    return () => controller.abort()
  }, [])

  useEffect(() => {
    if (!enabled || !following) {
      return
    }
    const controller = new AbortController()
    let timer: ReturnType<typeof setTimeout> | undefined
    async function check(id: string) {
      let next: TenantExport | undefined
      try {
        const result = await readTenantExport(id, controller.signal)
        if (controller.signal.aborted) {
          return
        }
        if (result.kind === 'gone') {
          remember(null)
          setExported(undefined)
          setFollowing(null)
          return
        }
        next = result.export
        setExported(next)
      } catch (error: unknown) {
        if (controller.signal.aborted) {
          return
        }
        console.warn('Reading the export failed', error)
      }
      // A failed read tries again, like a running export.
      if (!next || next.state === 'queued' || next.state === 'running') {
        timer = setTimeout(() => void check(id), pollDelay)
      }
    }
    void check(following)
    return () => {
      controller.abort()
      clearTimeout(timer)
    }
  }, [enabled, following])

  if (!enabled) {
    return null
  }

  async function start() {
    setBusy(true)
    setNotice(undefined)
    try {
      const started = await startTenantExport()
      remember(started.id)
      setExported(started)
      setFollowing(started.id)
    } catch (error: unknown) {
      console.warn('Starting the export failed', error)
      setNotice('startFailed')
    } finally {
      setBusy(false)
      setConfirming(false)
    }
  }

  async function download(id: string) {
    setBusy(true)
    setNotice(undefined)
    try {
      const file = await downloadTenantExport(id)
      if (file.ok) {
        saveFile(file.blob, file.fileName)
      } else if (file.status === 404 || file.status === 410) {
        remember(null)
        setExported(undefined)
        setFollowing(null)
        setNotice('expired')
      } else {
        setNotice('downloadFailed')
      }
    } catch (error: unknown) {
      console.warn('Downloading the export failed', error)
      setNotice('downloadFailed')
    } finally {
      setBusy(false)
    }
  }

  const running = exported?.state === 'queued' || exported?.state === 'running'
  const offerStart = !exported || exported.state === 'failed'

  return (
    <section aria-labelledby={headingId} data-testid="tenant-export">
      <Flex vertical gap="small">
        <Typography.Title level={3} id={headingId}>
          {t('shell.users.export.title')}
        </Typography.Title>
        <Alert type="warning" showIcon title={t('shell.users.export.warning')} />
        {running && (
          <Typography.Text data-testid="tenant-export-running">{t('shell.users.export.running')}</Typography.Text>
        )}
        {exported?.state === 'failed' && (
          <Alert data-testid="tenant-export-failed" type="error" title={t('shell.users.export.failed')} />
        )}
        {exported?.state === 'succeeded' && (
          <Flex vertical gap="small" align="flex-start">
            {exported.expiresAt && (
              <Typography.Text data-testid="tenant-export-ready">
                {t('shell.users.export.ready').replace(
                  '{expiresAt}',
                  new Date(exported.expiresAt).toLocaleString(locale),
                )}
              </Typography.Text>
            )}
            <AxisButton role="main" loading={busy} onClick={() => void download(exported.id)}>
              {t('shell.users.export.download')}
            </AxisButton>
          </Flex>
        )}
        {notice && <Alert data-testid="tenant-export-notice" type="error" title={t(`shell.users.export.${notice}`)} />}
        {offerStart && (
          <div>
            <AxisButton
              role="secondary"
              onClick={() => {
                setNotice(undefined)
                setConfirming(true)
              }}
            >
              {t('shell.users.export.start')}
            </AxisButton>
          </div>
        )}
      </Flex>
      <Modal
        open={confirming}
        width={size.dialog.small}
        title={t('shell.users.export.confirm')}
        okText={t('shell.users.export.ok')}
        cancelText={t('shell.users.roles.cancel')}
        confirmLoading={busy}
        onOk={() => void start()}
        onCancel={() => setConfirming(false)}
      >
        <Typography.Text>{t('shell.users.export.warning')}</Typography.Text>
      </Modal>
    </section>
  )
}
