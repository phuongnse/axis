import { Flex, Typography } from 'antd'
import type { CSSProperties } from 'react'
import { useEffect, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { fillText } from '../platform/fillText'
import { StatusTag } from '../platform/StatusTag'
import {
  type AuthoringRequest,
  type AuthoringRequestDiffEntry,
  fetchAuthoringRequestDiff,
  ServerUnreachableError,
} from '../platform/studio'
import { studioResourceAddress } from '../platform/studioAddress'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { requestResult } from './studioAuthoringRequest'
import { StudioChanges } from './StudioChanges'
import { StudioProblemList } from './StudioProblems'
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
 * changes. See docs/reference/ai-authoring-in-studio.md#the-diff.
 */
export function StudioAuthoringRequestResult({
  application,
  request,
  isAuthor,
}: {
  application: string
  request: AuthoringRequest
  isAuthor: boolean
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
          <StudioChanges files={files} resources={[]} folded />
        </Flex>
      )}
    </Flex>
  )
}
