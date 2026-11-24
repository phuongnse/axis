import { Flex, Typography } from 'antd'
import type { CSSProperties } from 'react'
import { useEffect, useRef, useState } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { fillText } from '../platform/fillText'
import { StatusTag } from '../platform/StatusTag'
import {
  type AuthoringRequest,
  type AuthoringRequestDiffEntry,
  fetchAuthoringRequestDiff,
  fetchStudioRequest,
  findPreviewLinks,
  type PreviewLink,
  previewStudioDraft,
  ServerUnreachableError,
  type StudioRequest,
  type StudioResourceItem,
} from '../platform/studio'
import { studioResourceAddress } from '../platform/studioAddress'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useFollowJob } from '../platform/useFollowJob'
import { requestResult } from './studioAuthoringRequest'
import { StudioChanges } from './StudioChanges'
import { StudioProblemList } from './StudioProblems'
import { StudioRequestStatus } from './StudioRequestStatus'
import { StudioTestResults } from './StudioTestResults'

/** Every page of the request's diff, in order, or null when the request has neither a draft nor an accept. */
async function allDiffEntries(
  application: string,
  id: string,
  signal: AbortSignal,
): Promise<AuthoringRequestDiffEntry[] | null> {
  const entries: AuthoringRequestDiffEntry[] = []
  let cursor: string | null | undefined
  do {
    const page = await fetchAuthoringRequestDiff(application, id, cursor, signal)
    if (page === null) {
      return null
    }
    entries.push(...page.resources)
    cursor = page.cursor
  } while (cursor)
  return entries
}

/**
 * The body of the Result item of a ready, failed or accepted request: the Checks and Review lines with their verdicts,
 * then the request's changes, folded resource by resource. Show the checks opens the compile's problems and the scenario
 * results, whose names link to Studio. The changes are read again when the request moves on, and a failed read shows no
 * changes. A ready request's author sees Preview the draft after the changes when `previewHost` is set. It sends the draft's
 * preview route and follows the request with `fetchStudioRequest`, kept in memory for this visit, and is off while it runs.
 * Its status line ends with a link to each of `sites` on the preview host, as the band's Preview does. See
 * docs/reference/ai-authoring-in-studio.md#the-diff and #preview-the-draft.
 */
export function StudioAuthoringRequestResult({
  application,
  request,
  isAuthor,
  previewHost,
  sites,
}: {
  application: string
  request: AuthoringRequest
  isAuthor: boolean
  previewHost: string | null
  sites: StudioResourceItem[]
}) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const { generation, lost } = useStudioConnection()
  // `key` names the request, its state, its round and the connection. A change of any of them reads the changes again, and
  // until then the changes of the old key are not shown.
  const [loaded, setLoaded] = useState<{ key: string; files: AuthoringRequestDiffEntry[] }>()
  const key = `${application}/${request.id}/${request.state}/${request.round}/${generation}`
  const files = loaded?.key === key ? loaded.files : []
  const [checksOpen, setChecksOpen] = useState(false)
  // The preview that the author sent in this visit and the links it ended with. It is never read back from the server.
  const [previewed, setPreviewed] = useState<{ requestId: string; value: StudioRequest; links: PreviewLink[] }>()
  const [sending, setSending] = useState(false)
  const [previewFailed, setPreviewFailed] = useState(false)
  const preview = previewed?.requestId === request.id ? previewed : null
  const previewing = preview !== null && (preview.value.status === 'queued' || preview.value.status === 'running')
  const previewId = previewing ? preview.value.id : null
  const sitesRef = useRef(sites)
  useEffect(() => {
    sitesRef.current = sites
  })
  const result = requestResult(request, isAuthor)
  const grid: CSSProperties = {
    display: 'grid',
    gridTemplateColumns: 'max-content max-content',
    columnGap: spacing.md,
    rowGap: spacing.xs,
    alignItems: 'baseline',
  }
  const whole: CSSProperties = { gridColumn: '1 / -1' }

  useEffect(() => {
    const controller = new AbortController()
    allDiffEntries(application, request.id, controller.signal).then(
      (entries) => {
        if (!controller.signal.aborted) {
          setLoaded({ key, files: entries ?? [] })
        }
      },
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the changes of the authoring request '${request.id}' of '${application}' failed`, error)
        setLoaded({ key, files: [] })
      },
    )
    return () => controller.abort()
  }, [key, application, request.id, lost])

  useFollowJob(previewId === null ? null : `${application}/${previewId}/${generation}`, {
    read: (signal) => fetchStudioRequest(application, previewId!, signal),
    ends: (answer) => answer.status !== 'queued' && answer.status !== 'running',
    interval: 1000,
    waitFirst: true,
    onAnswer: (value) => setPreviewed({ requestId: request.id, value, links: [] }),
    onEnd: async (value, signal) => {
      const links = await findPreviewLinks(application, previewHost, sitesRef.current, value, signal)
      setPreviewed({ requestId: request.id, value, links })
    },
    onError: (error) => {
      if (error instanceof ServerUnreachableError) {
        lost()
        return 'stop'
      }
      console.warn(`Reading the preview '${previewId}' of the authoring request '${request.id}' failed`, error)
      return 'retry'
    },
  })

  async function previewDraft(draft: string) {
    setSending(true)
    setPreviewFailed(false)
    try {
      setPreviewed({ requestId: request.id, value: await previewStudioDraft(application, draft), links: [] })
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Previewing the draft of the authoring request '${request.id}' of '${application}' failed`, error)
        setPreviewFailed(true)
      }
    } finally {
      setSending(false)
    }
  }

  const { checks, review } = result
  // A passed review stores no text, so it has no summary. A review with findings shows its own text.
  const reviewSummary = review === null ? null : review.summaryKey === null ? review.findings : t(review.summaryKey)
  return (
    <Flex vertical gap={spacing.sm} data-testid="studio-ai-request-result">
      {checks !== null && (
        <div data-testid="studio-ai-request-checks" style={grid}>
          <Typography.Text strong>{t('shell.studio.aiRequest.checks')}</Typography.Text>
          <StatusTag tone={checks.passed ? 'success' : 'danger'}>
            {t(checks.passed ? 'shell.studio.aiRequest.checks.passed' : 'shell.studio.aiRequest.checks.failed')}
          </StatusTag>
          <Flex wrap gap="small" align="center" style={whole}>
            <Typography.Text>{fillText(t(checks.summaryKey), checks.values)}</Typography.Text>
            <AxisButton role="secondary" aria-expanded={checksOpen} onClick={() => setChecksOpen((open) => !open)}>
              {t(checksOpen ? 'shell.studio.aiRequest.checks.hide' : 'shell.studio.aiRequest.checks.show')}
            </AxisButton>
          </Flex>
          {checksOpen && (
            <Flex
              vertical
              gap={spacing.xs}
              data-testid="studio-ai-request-checks-detail"
              style={{ ...whole, marginLeft: spacing.md }}
            >
              {checks.checks.compile.diagnostics.length > 0 && (
                <StudioProblemList diagnostics={checks.checks.compile.diagnostics} />
              )}
              {checks.checks.scenarios.outcome !== 'notRun' && (
                <StudioTestResults
                  request={{
                    kind: 'test',
                    status: checks.checks.scenarios.outcome === 'passed' ? 'succeeded' : 'failed',
                    scenarios: checks.checks.scenarios.items,
                  }}
                  scenarioAddressOf={(id, path) => studioResourceAddress(application, id, path)}
                />
              )}
            </Flex>
          )}
        </div>
      )}
      {review !== null && (
        <div data-testid="studio-ai-request-review" style={grid}>
          <Typography.Text strong>{t('shell.studio.aiRequest.review')}</Typography.Text>
          <StatusTag tone={review.tone}>{t(`shell.studio.aiRequest.review.${review.verdict}`)}</StatusTag>
          {reviewSummary !== null && (
            <Typography.Paragraph style={{ ...whole, margin: 0, whiteSpace: 'pre-wrap' }}>
              {reviewSummary}
            </Typography.Paragraph>
          )}
        </div>
      )}
      {files.length > 0 && (
        <Flex vertical gap={spacing.xs}>
          <Typography.Text strong>{t('shell.studio.aiRequest.changes')}</Typography.Text>
          <StudioChanges files={files} resources={[]} />
        </Flex>
      )}
      {isAuthor && request.state === 'ready' && previewHost !== null && request.draft !== null && (
        <Flex vertical gap={spacing.xs} align="flex-start">
          <AxisButton
            role="secondary"
            data-testid="studio-ai-request-preview"
            disabled={sending || previewing}
            onClick={() => void previewDraft(request.draft!)}
          >
            {t('shell.studio.aiRequest.previewDraft')}
          </AxisButton>
          {previewFailed && (
            <AxisAlert
              data-testid="studio-ai-request-preview-error"
              kind="error"
              message={t('shell.studio.request.sendFailed')}
            />
          )}
          {preview !== null && (
            <StudioRequestStatus request={preview.value} releaseNumber={null} previewLinks={preview.links} />
          )}
        </Flex>
      )}
    </Flex>
  )
}
