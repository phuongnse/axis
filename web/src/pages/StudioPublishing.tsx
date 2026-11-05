import { EllipsisOutlined } from '@ant-design/icons'
import { Alert, Button, Dropdown, Flex, Input, Modal, Tooltip } from 'antd'
import { type Ref, useEffect, useImperativeHandle, useLayoutEffect, useRef, useState } from 'react'
import { createPortal, flushSync } from 'react-dom'
import { QuotaRefusedError } from '../platform/quota'
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
  type StudioReleases,
  type StudioResourceItem,
  type StudioRequest,
  testStudioApp,
  validateStudioApp,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { StudioReleasesList } from './StudioReleases'
import { StudioRequestStatus } from './StudioRequestStatus'
import { StudioTestResults } from './StudioTestResults'
import { dialogWidth } from '../platform/dialogButtons'
import { AxisField } from '../platform/AxisField'

/** How long Studio waits between two checks of a request. */
const pollDelay = 1000

/** What the page calls on this component: a test run of every scenario, or of the named ones. */
export interface StudioPublishingHandle {
  runTests(scenarios?: string[]): void
}

/**
 * The band of the open application, with the status of the request its buttons send and its release history. The
 * band holds the `newResource` button, Validate, Preview, Run tests and Publish. On its left it shows the application's
 * state in words, see `StudioBand`: the problems of `diagnostics`, which `onToggleProblems` opens as the `problems`
 * panel beside the page, then the release state, and `bandState` after it. The request status, test results and
 * preview links show at the top of the content, under the band, then `children`, then the release list. The release
 * list keeps its own title unless `releasesView`, where the page's heading names it. Validate sends a validate request. Preview shows only when `previewHost` is set, and sends a
 * preview request. When it succeeds, a link to each of the application's `sites` on the preview host shows, built
 * from the page's protocol and port. A preview never shows a release number, because its release belongs to the
 * preview tenant's history. Publish opens a dialog for an optional label and note, then
 * sends a publish request. A publish refused by a tenant quota shows the server's title in the error alert.
 * An Activate… button in the history sends a rollback request.
 * Run tests shows only when `canTest`, and sends a test request, which runs every scenario. The ref's `runTests`
 * sends the same request with the names it is given. A test request shows its results under the status line, and
 * selecting a failed scenario calls `onOpenScenario`. A publish, validate or preview shows the contract scenarios it
 * stored under the status line, when it stored any.
 * Studio checks the request every second until it is `succeeded` or `failed`, with no time limit, and shows its
 * status meanwhile. When it ends, its diagnostics go to the Problems panel. A succeeded publish or rollback
 * shows its release number, found in the release history by the request's `releaseId`. Publish and Activate… show
 * only when `canPublish`. They and Validate are off while a request runs and while the reconnect message shows. The
 * history loads when the application is picked, and again after a request ends, after the reconnect, and when
 * `folderRevision` changes, which the page does after a save or a delete. The band says "Changes not live" when the
 * folder differs from the active release. When the server
 * does not answer, the checking starts again after the reconnect. `onDiagnostics` runs while the page may have
 * changed, so it must not depend on a stale copy of the page's state. `onBusyChange` runs when a request is sent,
 * checked or finished, or the reconnect starts or ends.
 */
export function StudioPublishing({
  application,
  canPublish,
  canTest,
  previewHost,
  sites,
  folderRevision,
  onDiagnostics,
  onOpenScenario,
  onBusyChange,
  actionsSlot,
  showHistory = true,
  onHistory,
  release,
  releaseAddress,
  historyAddress,
  headingSlot,
  ref,
}: {
  application: string
  canPublish: boolean
  canTest: boolean
  previewHost: string | null
  sites: StudioResourceItem[]
  folderRevision: number
  onDiagnostics: (diagnostics: StudioDiagnostic[]) => void
  onOpenScenario: (resourceId: string, path: string) => void
  onBusyChange?: (busy: boolean) => void
  /** Proposed: where the buttons draw, such as the workspace's header band. Without it they draw here. */
  actionsSlot?: HTMLElement | null
  /** Proposed: whether the release history draws here. The workspace shows it on its Releases view only. */
  showHistory?: boolean
  /** Proposed: called with the release history each time it loads, for the workspace's overview. */
  onHistory?: (history: StudioReleases | 'failed') => void
  /** Proposed: the release whose own page shows in place of the history, its address and its heading's right side. */
  release?: number
  releaseAddress?: (number: number) => string
  historyAddress?: string
  headingSlot?: HTMLElement | null
  ref?: Ref<StudioPublishingHandle>
}) {
  const t = useText()
  const { generation, reconnecting, lost } = useStudioConnection()
  const [request, setRequest] = useState<StudioRequest | null>(null)
  const [releaseNumber, setReleaseNumber] = useState<number | null>(null)
  const [previewLinks, setPreviewLinks] = useState<string[]>([])
  const [sending, setSending] = useState(false)
  const [sendError, setSendError] = useState<string | null>(null)
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

  const historyHandler = useRef(onHistory)
  useEffect(() => {
    historyHandler.current = onHistory
  }, [onHistory])
  useEffect(() => {
    if (history !== undefined) {
      historyHandler.current?.(history)
    }
  }, [history])

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
        setSendError(t('shell.studio.request.sendFailed'))
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
  }, [application, requestId, running, generation, lost, previewHost, t])

  async function send(start: () => Promise<StudioRequest>) {
    setSending(true)
    setSendError(null)
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
        setSendError(error instanceof QuotaRefusedError ? error.message : t('shell.studio.request.sendFailed'))
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

  // Proposed: when the band has too little room, the actions that check fold into one "…" menu. Publish stays. A new
  // width starts over, as they may fit again.
  const [folded, setFolded] = useState(false)
  const band = actionsSlot?.closest<HTMLElement>('[data-axis-band]') ?? null
  useLayoutEffect(() => {
    if (!band) {
      return
    }
    let width = band.clientWidth
    // Unfolded first, as only then the band can say whether all of it fits.
    const measure = () => {
      flushSync(() => setFolded(false))
      setFolded(band.scrollWidth > band.clientWidth)
    }
    const observer = new ResizeObserver(() => {
      if (band.clientWidth !== width) {
        width = band.clientWidth
        measure()
      }
    })
    observer.observe(band)
    queueMicrotask(measure)
    return () => observer.disconnect()
  }, [band])

  useEffect(() => {
    onBusyChange?.(busy)
  }, [busy, onBusyChange])

  useImperativeHandle(ref, () => ({
    runTests: (scenarios) => send(() => testStudioApp(application, scenarios)),
  }))

  // Proposed: the actions that check, as text buttons in the band, so Publish is its one solid button.
  const checks = [
    {
      key: 'validate',
      labelKey: 'shell.studio.validate',
      testId: undefined,
      run: () => validateStudioApp(application),
    },
    ...(previewHost !== null
      ? [
          {
            key: 'preview',
            labelKey: 'shell.studio.preview',
            testId: 'studio-preview',
            run: () => previewStudioApp(application),
          },
        ]
      : []),
    ...(canTest
      ? [
          {
            key: 'test',
            labelKey: 'shell.studio.runTests',
            testId: 'studio-run-tests',
            run: () => testStudioApp(application),
          },
        ]
      : []),
  ]

  const actions = (
    <Flex gap="small" align="center">
      {folded ? (
        <Dropdown
          trigger={['click']}
          autoFocus
          menu={{
            'aria-label': t('shell.studio.moreActions'),
            items: checks.map((check) => ({
              key: check.key,
              label: t(check.labelKey),
              disabled: busy,
              onClick: () => send(check.run),
            })),
          }}
        >
          <Tooltip title={t('shell.studio.moreActions')} trigger={['hover', 'focus']}>
            <Button
              type="text"
              data-testid="studio-more-actions"
              aria-label={t('shell.studio.moreActions')}
              icon={<EllipsisOutlined />}
            />
          </Tooltip>
        </Dropdown>
      ) : (
        checks.map((check) => (
          <Button
            key={check.key}
            type="text"
            data-testid={check.testId}
            disabled={busy}
            onClick={() => send(check.run)}
          >
            {t(check.labelKey)}
          </Button>
        ))
      )}
      {canPublish && (
        <Button type="primary" data-testid="studio-publish" disabled={busy} onClick={() => setDialogOpen(true)}>
          {t('shell.studio.publish')}
        </Button>
      )}
    </Flex>
  )

  return (
    <Flex vertical gap="small">
      {actionsSlot ? createPortal(actions, actionsSlot) : actions}
      {sendError !== null && <Alert data-testid="studio-request-error" type="error" title={sendError} />}
      {request && <StudioRequestStatus request={request} releaseNumber={releaseNumber} />}
      {request && (request.kind === 'test' || (request.scenarios?.length ?? 0) > 0) && (
        <StudioTestResults request={request} onSelect={onOpenScenario} />
      )}
      {previewLinks.length > 0 && (
        <Flex vertical gap={4}>
          {previewLinks.map((link) => (
            <a key={link} data-testid="studio-preview-link" href={link} target="_blank" rel="noreferrer">
              {link}
            </a>
          ))}
        </Flex>
      )}
      {showHistory && history === 'failed' && (
        <Alert data-testid="studio-releases-error" type="error" title={t('shell.studio.releases.loadFailed')} />
      )}
      {showHistory && history && history !== 'failed' && (
        <>
          {release === undefined && folderDiffers(history) && (
            <Alert
              data-testid="studio-folder-differs"
              type="warning"
              title={t('shell.studio.releases.folderDiffers')}
            />
          )}
          <StudioReleasesList
            application={application}
            history={history}
            canRollBack={canPublish}
            busy={busy}
            onRollBack={(target) => send(() => rollbackStudioRelease(application, target.releaseId))}
            release={release}
            releaseAddress={releaseAddress}
            historyAddress={historyAddress}
            headingSlot={headingSlot}
          />
        </>
      )}
      <Modal
        width={dialogWidth('medium')}
        open={dialogOpen}
        title={t('shell.studio.publish.title')}
        okText={t('shell.studio.publish.confirm')}
        cancelText={t('shell.studio.cancel')}
        onOk={publish}
        onCancel={() => setDialogOpen(false)}
      >
        {/* Proposed: the release's label and note are fields like any other, with a heading and help, not a placeholder. */}
        <Flex vertical gap="middle">
          <AxisField label={t('shell.studio.publish.label')} help={t('shell.studio.publish.label.help')}>
            <Input
              aria-label={t('shell.studio.publish.label')}
              maxLength={100}
              value={label}
              onChange={(event) => setLabel(event.target.value)}
            />
          </AxisField>
          <AxisField label={t('shell.studio.publish.note')} help={t('shell.studio.publish.note.help')}>
            <Input.TextArea
              aria-label={t('shell.studio.publish.note')}
              maxLength={2000}
              autoSize={{ minRows: 3, maxRows: 8 }}
              value={note}
              onChange={(event) => setNote(event.target.value)}
            />
          </AxisField>
        </Flex>
      </Modal>
    </Flex>
  )
}
