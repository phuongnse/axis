import { Alert, Button, Flex, Input, Modal } from 'antd'
import { useEffect, useRef, useState } from 'react'
import {
  fetchStudioReleases,
  fetchStudioRequest,
  publishStudioApp,
  ServerUnreachableError,
  type StudioDiagnostic,
  type StudioRequest,
  validateStudioApp,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { StudioRequestStatus } from './StudioRequestStatus'

/** How long Studio waits between two checks of a request. */
const pollDelay = 1000

/**
 * The Validate and Publish buttons of the open application, with the status of the request they send. Validate
 * sends a validate request. Publish opens a dialog for an optional label and note, then sends a publish request.
 * Studio checks the request every second until it is `succeeded` or `failed`, with no time limit, and shows its
 * status meanwhile. When it ends, its diagnostics go to the diagnostics panel. A succeeded publish shows its
 * release number, found in the release history by the request's `releaseId`. Publish shows only when
 * `canPublish`. Both buttons are off while a request runs and while the reconnect message shows. When the server
 * does not answer, the checking starts again after the reconnect. `onDiagnostics` runs while the page may have
 * changed, so it must not depend on a stale copy of the page's state.
 */
export function StudioPublishing({
  application,
  canPublish,
  onDiagnostics,
}: {
  application: string
  canPublish: boolean
  onDiagnostics: (diagnostics: StudioDiagnostic[]) => void
}) {
  const t = useText()
  const { generation, reconnecting, lost } = useStudioConnection()
  const [request, setRequest] = useState<StudioRequest | null>(null)
  const [releaseNumber, setReleaseNumber] = useState<number | null>(null)
  const [sending, setSending] = useState(false)
  const [sendFailed, setSendFailed] = useState(false)
  const [dialogOpen, setDialogOpen] = useState(false)
  const [label, setLabel] = useState('')
  const [note, setNote] = useState('')

  const diagnosticsHandler = useRef(onDiagnostics)
  useEffect(() => {
    diagnosticsHandler.current = onDiagnostics
  }, [onDiagnostics])
  const requestId = request?.id
  const running = request !== null && (request.status === 'queued' || request.status === 'running')

  useEffect(() => {
    if (requestId === undefined || !running) {
      return
    }
    const controller = new AbortController()
    let timer: ReturnType<typeof setTimeout>

    async function check() {
      try {
        const next = await fetchStudioRequest(application, requestId!, controller.signal)
        if (next.status === 'queued' || next.status === 'running') {
          setRequest(next)
          timer = setTimeout(check, pollDelay)
          return
        }
        const number = await findReleaseNumber(next, controller.signal)
        diagnosticsHandler.current(next.diagnostics)
        setReleaseNumber(number)
        setRequest(next)
      } catch (error) {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Checking the request '${requestId}' failed`, error)
        setRequest(null)
        setSendFailed(true)
      }
    }

    async function findReleaseNumber(ended: StudioRequest, signal: AbortSignal): Promise<number | null> {
      if (ended.kind !== 'publish' || ended.status !== 'succeeded' || ended.releaseId === null) {
        return null
      }
      const releases = await fetchStudioReleases(application, signal)
      return releases.items.find((item) => item.releaseId === ended.releaseId)?.number ?? null
    }

    timer = setTimeout(check, pollDelay)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [application, requestId, running, generation, lost])

  async function send(start: () => Promise<StudioRequest>) {
    setSending(true)
    setSendFailed(false)
    try {
      const sent = await start()
      setReleaseNumber(null)
      setRequest(sent)
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Sending the request for '${application}' failed`, error)
        setSendFailed(true)
      }
    } finally {
      setSending(false)
    }
  }

  async function publish() {
    setDialogOpen(false)
    await send(() => publishStudioApp(application, { label: label.trim(), note: note.trim() }))
    setLabel('')
    setNote('')
  }

  const busy = running || sending || reconnecting
  return (
    <Flex vertical gap="small">
      <Flex gap="small" align="center">
        <Button disabled={busy} onClick={() => send(() => validateStudioApp(application))}>
          {t('shell.studio.validate')}
        </Button>
        {canPublish && (
          <Button type="primary" data-testid="studio-publish" disabled={busy} onClick={() => setDialogOpen(true)}>
            {t('shell.studio.publish')}
          </Button>
        )}
      </Flex>
      {sendFailed && (
        <Alert data-testid="studio-request-error" type="error" message={t('shell.studio.request.sendFailed')} />
      )}
      {request && <StudioRequestStatus request={request} releaseNumber={releaseNumber} />}
      <Modal
        open={dialogOpen}
        title={t('shell.studio.publish.title')}
        okText={t('shell.studio.publish.confirm')}
        cancelText={t('shell.studio.cancel')}
        onOk={publish}
        onCancel={() => setDialogOpen(false)}
      >
        <Flex vertical gap="small">
          <Input
            aria-label={t('shell.studio.publish.label')}
            placeholder={t('shell.studio.publish.label')}
            maxLength={100}
            value={label}
            onChange={(event) => setLabel(event.target.value)}
          />
          <Input.TextArea
            aria-label={t('shell.studio.publish.note')}
            placeholder={t('shell.studio.publish.note')}
            maxLength={2000}
            autoSize={{ minRows: 3, maxRows: 8 }}
            value={note}
            onChange={(event) => setNote(event.target.value)}
          />
        </Flex>
      </Modal>
    </Flex>
  )
}
