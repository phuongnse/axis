import { Flex } from 'antd'
import type { ResourceAddressOf } from '../platform/studioAddress'
import { AxisAlert } from '../platform/AxisAlert'
import { type ReactNode, type Ref, useEffect, useImperativeHandle, useRef, useState } from 'react'
import { AxisActionRow, AxisButton } from '../platform/AxisButton'
import { QuotaRefusedError } from '../platform/quota'
import {
  fetchStudioReleases,
  fetchStudioRequest,
  fetchStudioResource,
  previewSiteUrl,
  previewStudioApp,
  publishStudioApp,
  type PublishDetails,
  activateStudioRelease,
  ServerUnreachableError,
  type StudioDiagnostic,
  type StudioReleaseListRow,
  type StudioReleases,
  type StudioResourceItem,
  type StudioRequest,
  testStudioApp,
  validateStudioApp,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { useFollowJob } from '../platform/useFollowJob'
import { askAiPanelId } from './StudioAskAi'
import { StudioBand } from './StudioBand'
import { StudioReleasesList } from './StudioReleases'
import { StudioRequestStatus } from './StudioRequestStatus'
import { StudioTestResults } from './StudioTestResults'

/** What the page calls on this component: a test run of every scenario, or of the named ones, a publish and an activation. */
export interface StudioPublishingHandle {
  runTests(scenarios?: string[]): void
  publish(details: PublishDetails): void
  activate(releaseId: string): void
}

async function findReleaseNumber(
  application: string,
  ended: StudioRequest,
  signal: AbortSignal,
): Promise<number | null> {
  if (
    ended.target === 'preview' ||
    (ended.kind !== 'publish' && ended.kind !== 'activate') ||
    ended.status !== 'succeeded' ||
    ended.releaseId === null
  ) {
    return null
  }
  const releases = await fetchStudioReleases(application, signal)
  return releases.items.find((item) => item.releaseId === ended.releaseId)?.number ?? null
}

// The site paths are read from the folder, so a path renamed since the request was sent gives a wrong link.
async function findPreviewLinks(
  application: string,
  previewHost: string | null,
  sites: StudioResourceItem[],
  ended: StudioRequest,
  signal: AbortSignal,
): Promise<string[]> {
  if (ended.target !== 'preview' || ended.status !== 'succeeded' || previewHost === null) {
    return []
  }
  const links: string[] = []
  for (const site of sites) {
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

/**
 * The band of the open application, with the status of the request its buttons send and its release history. The
 * band holds the `newResource` button, Validate, Preview, Run tests, Ask AI and Publish. Ask AI shows only when `canAskAi`, sits just
 * before Publish and never folds, and `onToggleAskAi` opens and closes the request panel, see `StudioAskAi`, with `askAiOpen` saying whether it is open. They keep one line: when they do not
 * fit, Validate, Preview and Run tests fold into one "…" menu, see `AxisActionRow`. On its left it shows the application's
 * state in words, see `StudioBand`: the problems of `diagnostics`, which `onToggleProblems` opens as the Problems
 * panel beside the page, then the release state. The request status, test results and
 * preview links show at the top of the content, under the band, then `children`, which is called with the release history
 * when it is a function. The release list follows only on `releasesView`, under the page's heading. Validate sends a validate request. Preview shows only when `previewHost` is set, and sends a
 * preview request. When it succeeds, a link to each of the application's `sites` on the preview host shows, built
 * from the page's protocol and port. A preview never shows a release number, because its release belongs to the
 * preview tenant's history. Publish… calls `onOpenPublish`, which opens the publish page, see `StudioPublishPage`, and
 * hides while `publishView` is set, because that page has its own Publish. The ref's `publish` sends the publish
 * request with the label and note the page gives it, see docs/reference/studio.md#publishing, and the status line shows
 * it wherever the author is then. A publish refused by a tenant quota shows the server's title in the error alert.
 * An Activate… button in the history sends an activate request, and so does the ref's `activate`, which a release
 * page's Activate… calls.
 * Run tests shows only when `canTest`, and sends a test request, which runs every scenario. The ref's `runTests`
 * sends the same request with the names it is given. A test request shows its results under the status line, and
 * a scenario links to the address `scenarioAddressOf` gives. A publish, validate or preview shows the contract scenarios it
 * stored under the status line, when it stored any.
 * Studio checks the request every second until it is `succeeded` or `failed`, with no time limit, and shows its
 * status meanwhile. When it ends, its diagnostics go to the Problems panel. A succeeded publish or activate
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
  diagnostics,
  problemsOpen,
  onToggleProblems,
  canAskAi,
  askAiOpen,
  onToggleAskAi,
  newResource,
  releasesView,
  publishView,
  onOpenPublish,
  onDiagnostics,
  scenarioAddressOf,
  onBusyChange,
  ref,
  children,
}: {
  application: string
  canPublish: boolean
  canTest: boolean
  previewHost: string | null
  sites: StudioResourceItem[]
  folderRevision: number
  diagnostics: StudioDiagnostic[]
  problemsOpen: boolean
  onToggleProblems: () => void
  canAskAi: boolean
  askAiOpen: boolean
  onToggleAskAi: () => void
  newResource: ReactNode
  releasesView: boolean
  publishView: boolean
  onOpenPublish: () => void
  onDiagnostics: (diagnostics: StudioDiagnostic[]) => void
  scenarioAddressOf: ResourceAddressOf
  onBusyChange?: (busy: boolean) => void
  ref?: Ref<StudioPublishingHandle>
  children?: ReactNode | ((history: StudioReleases | 'failed' | undefined) => ReactNode)
}) {
  const t = useText()
  const { generation, reconnecting, lost } = useStudioConnection()
  const [request, setRequest] = useState<StudioRequest | null>(null)
  const [releaseNumber, setReleaseNumber] = useState<number | null>(null)
  const [previewLinks, setPreviewLinks] = useState<string[]>([])
  const [sending, setSending] = useState(false)
  const [sendError, setSendError] = useState<string | null>(null)
  const [history, setHistory] = useState<StudioReleases | 'failed'>()
  const [historyRevision, setHistoryRevision] = useState(0)

  // The publish page needs the history as the folder is now, since a file edited on disk or a change
  // from another session does not reach this page, see studio.md#publishing.
  const [historyForPublishView, setHistoryForPublishView] = useState(publishView)
  if (historyForPublishView !== publishView) {
    setHistoryForPublishView(publishView)
    if (publishView) {
      setHistory(undefined)
    }
  }

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
  }, [application, generation, historyRevision, folderRevision, publishView, lost])

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

  useFollowJob(running && requestId !== undefined ? `${application}/${requestId}/${generation}` : null, {
    read: (signal) => fetchStudioRequest(application, requestId!, signal),
    ends: (next) => next.status !== 'queued' && next.status !== 'running',
    interval: 1000,
    waitFirst: true,
    onAnswer: setRequest,
    onEnd: async (next, signal) => {
      const number = await findReleaseNumber(application, next, signal)
      const links = await findPreviewLinks(application, previewHost, sitesRef.current, next, signal)
      diagnosticsHandler.current(next.diagnostics)
      setReleaseNumber(number)
      setPreviewLinks(links)
      setHistoryRevision((value) => value + 1)
      setRequest(next)
    },
    onError: (error) => {
      if (error instanceof ServerUnreachableError) {
        lost()
        return 'stop'
      }
      console.warn(`Checking the request '${requestId}' failed`, error)
      setRequest(null)
      setSendError(t('shell.studio.request.sendFailed'))
      setHistoryRevision((value) => value + 1)
      return 'stop'
    },
  })

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

  function activateRelease(releaseId: string) {
    return send(() => activateStudioRelease(application, releaseId))
  }

  const busy = running || sending || reconnecting

  useEffect(() => {
    onBusyChange?.(busy)
  }, [busy, onBusyChange])

  useImperativeHandle(ref, () => ({
    runTests: (scenarios) => send(() => testStudioApp(application, scenarios)),
    publish: (details) => send(() => publishStudioApp(application, details)),
    activate: activateRelease,
  }))

  return (
    <Flex vertical gap="middle">
      <StudioBand
        diagnostics={diagnostics}
        history={history}
        problemsOpen={problemsOpen}
        onToggleProblems={onToggleProblems}
      >
        <AxisActionRow
          align="center"
          justify="flex-end"
          fold={[
            {
              key: 'validate',
              labelKey: 'shell.studio.validate',
              disabled: busy,
              onClick: () => send(() => validateStudioApp(application)),
            },
            ...(previewHost !== null
              ? [
                  {
                    key: 'preview',
                    labelKey: 'shell.studio.preview',
                    disabled: busy,
                    onClick: () => send(() => previewStudioApp(application)),
                    attributes: { 'data-testid': 'studio-preview' },
                  },
                ]
              : []),
            ...(canTest
              ? [
                  {
                    key: 'run-tests',
                    labelKey: 'shell.studio.runTests',
                    disabled: busy,
                    onClick: () => send(() => testStudioApp(application)),
                    attributes: { 'data-testid': 'studio-run-tests' },
                  },
                ]
              : []),
          ]}
          end={
            <>
              {canAskAi && (
                <AxisButton
                  role="secondary"
                  data-testid="studio-ask-ai"
                  aria-expanded={askAiOpen}
                  aria-controls={askAiPanelId}
                  onClick={onToggleAskAi}
                >
                  {t('shell.studio.askAi')}
                </AxisButton>
              )}
              {canPublish && !publishView && (
                <AxisButton role="finish" data-testid="studio-publish" disabled={busy} onClick={onOpenPublish}>
                  {t('shell.studio.publish')}
                </AxisButton>
              )}
            </>
          }
        >
          {newResource}
        </AxisActionRow>
      </StudioBand>
      <Flex vertical gap="middle" style={{ minWidth: 0 }}>
        {sendError !== null && <AxisAlert data-testid="studio-request-error" kind="error" message={sendError} />}
        {request && <StudioRequestStatus request={request} releaseNumber={releaseNumber} />}
        {request && (request.kind === 'test' || (request.scenarios?.length ?? 0) > 0) && (
          <StudioTestResults request={request} addressOf={scenarioAddressOf} />
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
        {typeof children === 'function' ? children(history) : children}
        {releasesView && history && history !== 'failed' && (
          <StudioReleasesList
            application={application}
            history={history}
            canActivate={canPublish}
            busy={busy}
            onActivate={(row: StudioReleaseListRow) => activateRelease(row.releaseId)}
          />
        )}
      </Flex>
    </Flex>
  )
}
