import { Alert, Button, Flex, Input, Modal } from 'antd'
import { useEffect, useRef, useState } from 'react'
import {
  fetchStudioReleases,
  fetchStudioRequest,
  fetchStudioResource,
  folderDiffers,
  previewSiteUrl,
  previewStudioApp,
  publishStudioApp,
  rollbackStudioRelease,
  ServerUnreachableError,
  type StudioDiagnostic,
  type StudioReleaseRow,
  type StudioReleases,
  type StudioResourceItem,
  type StudioRequest,
  validateStudioApp,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { StudioReleasesList } from './StudioReleases'
import { StudioRequestStatus } from './StudioRequestStatus'

/** How long Studio waits between two checks of a request. */
const pollDelay = 1000

/**
 * The Validate, Preview and Publish buttons of the open application, with the status of the request they send, and
 * its release history. Validate sends a validate request. Preview shows only when `previewHost` is set, and sends a
 * preview request. When it succeeds, a link to each of the application's `sites` on the preview host shows, built
 * from the page's protocol and port. A preview never shows a release number, because its release belongs to the
 * preview tenant's history. Publish opens a dialog for an optional label and note, then
 * sends a publish request. A Roll back button in the history sends a rollback request.
 * Studio checks the request every second until it is `succeeded` or `failed`, with no time limit, and shows its
 * status meanwhile. When it ends, its diagnostics go to the diagnostics panel. A succeeded publish or rollback
 * shows its release number, found in the release history by the request's `releaseId`. Publish and Roll back show
 * only when `canPublish`. They and Validate are off while a request runs and while the reconnect message shows. The
 * history loads when the application is picked, and again after a request ends, after the reconnect, and when
 * `folderRevision` changes, which the page does after a save or a delete. A warning shows when the folder differs
 * from the active release. When the server
 * does not answer, the checking starts again after the reconnect. `onDiagnostics` runs while the page may have
 * changed, so it must not depend on a stale copy of the page's state.
 */
export function StudioPublishing({
  application,
  canPublish,
  previewHost,
  sites,
  folderRevision,
  onDiagnostics,
}: {
  application: string
  canPublish: boolean
  previewHost: string | null
  sites: StudioResourceItem[]
  folderRevision: number
  onDiagnostics: (diagnostics: StudioDiagnostic[]) => void
}) {
  const t = useText()
  const { generation, reconnecting, lost } = useStudioConnection()
  const [request, setRequest] = useState<StudioRequest | null>(null)
  const [releaseNumber, setReleaseNumber] = useState<number | null>(null)
  const [previewLinks, setPreviewLinks] = useState<string[]>([])
  const [sending, setSending] = useState(false)
  const [sendFailed, setSendFailed] = useState(false)
  const [dialogOpen, setDialogOpen] = useState(false)
  const [label, setLabel] = useState('')
  const [note, setNote] = useState('')
  const [history, setHistory] = useState<StudioReleases | 'failed'>()
  const [historyRevision, setHistoryRevision] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioReleases(application, controller.signal).then(setHistory, (error: unknown) => {
      if (controller.signal.aborted) {
        return
      }
      if (error instanceof ServerUnreachableError) {
        lost()
        return
      }
      console.warn(`Loading the release history of '${application}' failed`, error)
      setHistory('failed')
    })
    return () => controller.abort()
  }, [application, generation, historyRevision, folderRevision, lost])

  const diagnosticsHandler = useRef(onDiagnostics)
  useEffect(() => {
    diagnosticsHandler.current = onDiagnostics
  }, [onDiagnostics])
  const sitesRef = useRef(sites)
  useEffect(() => {
    sitesRef.current = sites
  }, [sites])
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
        const links = await findPreviewLinks(next, controller.signal)
        diagnosticsHandler.current(next.diagnostics)
        setReleaseNumber(number)
        setPreviewLinks(links)
        setRequest(next)
        setHistoryRevision((value) => value + 1)
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
        setHistoryRevision((value) => value + 1)
      }
    }

    async function findReleaseNumber(ended: StudioRequest, signal: AbortSignal): Promise<number | null> {
      if (
        ended.target === 'preview' ||
        (ended.kind !== 'publish' && ended.kind !== 'rollback') ||
        ended.status !== 'succeeded' ||
        ended.releaseId === null
      ) {
        return null
      }
      const releases = await fetchStudioReleases(application, signal)
      return releases.items.find((item) => item.releaseId === ended.releaseId)?.number ?? null
    }

    // The site paths are read from the folder, so a path renamed since the request was sent gives a wrong link.
    async function findPreviewLinks(ended: StudioRequest, signal: AbortSignal): Promise<string[]> {
      if (ended.target !== 'preview' || ended.status !== 'succeeded' || previewHost === null) {
        return []
      }
      const links: string[] = []
      for (const site of sitesRef.current) {
        try {
          const { content } = await fetchStudioResource(application, site.id, signal)
          if (typeof content.path === 'string') {
            links.push(previewSiteUrl(previewHost, content.path))
          }
        } catch (error) {
          if (signal.aborted || error instanceof ServerUnreachableError) {
            throw error
          }
          console.warn(`Loading the site '${site.id}' for the preview links failed`, error)
        }
      }
      return links
    }

    timer = setTimeout(check, pollDelay)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [application, requestId, running, generation, lost, previewHost])

  async function send(start: () => Promise<StudioRequest>) {
    setSending(true)
    setSendFailed(false)
    try {
      const sent = await start()
      setReleaseNumber(null)
      setPreviewLinks([])
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
        {previewHost !== null && (
          <Button
            data-testid="studio-preview"
            disabled={busy}
            onClick={() => send(() => previewStudioApp(application))}
          >
            {t('shell.studio.preview')}
          </Button>
        )}
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
      {previewLinks.length > 0 && (
        <Flex vertical gap={4}>
          {previewLinks.map((link) => (
            <a key={link} data-testid="studio-preview-link" href={link} target="_blank" rel="noreferrer">
              {link}
            </a>
          ))}
        </Flex>
      )}
      {history === 'failed' && (
        <Alert data-testid="studio-releases-error" type="error" message={t('shell.studio.releases.loadFailed')} />
      )}
      {history && history !== 'failed' && (
        <>
          {folderDiffers(history) && (
            <Alert
              data-testid="studio-folder-differs"
              type="warning"
              message={t('shell.studio.releases.folderDiffers')}
            />
          )}
          <StudioReleasesList
            history={history}
            canRollBack={canPublish}
            busy={busy}
            onRollBack={(row: StudioReleaseRow) => send(() => rollbackStudioRelease(application, row.releaseId))}
          />
        </>
      )}
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
