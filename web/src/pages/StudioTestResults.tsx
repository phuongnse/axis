import { Flex, Typography } from 'antd'
import { StatusTag } from '../platform/StatusTag'
import type {
  StudioRequest,
  StudioResourceItem,
  StudioScenarioResult,
  StudioSkippedScenarioResult,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { ResourceReference } from './ResourceReference'

/**
 * The results of a test request, or the contract scenarios of a publish or validate: each scenario with passed or
 * failed, its time and, for a failure, its step and message. A scenario's name is a link that calls `onSelect` with its
 * resource, a failure's with the path of its failing step; `resources` name it. A skipped contract scenario shows the relaxed point it covers and opens
 * nothing. While the request is queued or running it shows a note instead of rows, so an empty list never reads as a
 * pass. An ended request with no scenario results says that no scenario ran.
 */
export function StudioTestResults({
  request,
  resources = [],
  onSelect,
  nested = false,
}: {
  request: StudioRequest
  /** The application's resources, which name each scenario. */
  resources?: readonly StudioResourceItem[]
  onSelect: (resourceId: string, path: string) => void
  /** Proposed: inside a part of a panel, its title is bold text, not a heading, see the review on #3263. */
  nested?: boolean
}) {
  const t = useText()
  const pending = request.status === 'queued' || request.status === 'running'
  const scenarios = request.scenarios ?? []
  return (
    <section data-testid="studio-test-results" data-status={request.status}>
      {nested ? (
        <Typography.Text strong>
          {t(request.kind === 'test' ? 'shell.studio.tests.title' : 'shell.studio.tests.contractTitle')}
        </Typography.Text>
      ) : (
        <Typography.Title level={5}>
          {t(request.kind === 'test' ? 'shell.studio.tests.title' : 'shell.studio.tests.contractTitle')}
        </Typography.Title>
      )}
      {pending ? (
        <Typography.Text type="secondary">{t('shell.studio.tests.pending')}</Typography.Text>
      ) : scenarios.length === 0 ? (
        <Typography.Text type="secondary">{t('shell.studio.tests.none')}</Typography.Text>
      ) : (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {scenarios.map((scenario) => (
            <li key={scenario.file} data-testid="studio-test-result" data-outcome={scenario.outcome}>
              <ScenarioRow scenario={scenario} resources={resources} onSelect={onSelect} />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

/**
 * Proposed: one scenario's result, see the review on #3263: its outcome as a `StatusTag`, then its name as a link to its
 * resource, a failed one's at its failing step, and its time, muted, each a token's gap apart; a failure's step and
 * message under it. A skipped contract scenario is a module's, so its name is text and it opens nothing.
 */
function ScenarioRow({
  scenario,
  resources,
  onSelect,
}: {
  scenario: StudioScenarioResult | StudioSkippedScenarioResult
  resources: readonly StudioResourceItem[]
  onSelect: (resourceId: string, path: string) => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const line = { align: 'baseline', gap: spacing.xs, wrap: true } as const
  if (scenario.outcome === 'skipped') {
    return (
      <Flex vertical align="flex-start" style={{ paddingBlock: spacing.xs / 2 }}>
        <Flex {...line}>
          <StatusTag tone="muted">{t('shell.studio.tests.skipped')}</StatusTag>
          <Typography.Text>{scenario.name}</Typography.Text>
        </Flex>
        <Typography.Text type="secondary">{`${t('shell.studio.tests.relaxedPoint')} ${scenario.point}`}</Typography.Text>
      </Flex>
    )
  }
  const failed = scenario.outcome === 'failed'
  return (
    <Flex vertical align="flex-start" style={{ paddingBlock: spacing.xs / 2 }}>
      <Flex {...line}>
        <StatusTag tone={failed ? 'danger' : 'success'}>
          {t(failed ? 'shell.studio.tests.failed' : 'shell.studio.tests.passed')}
        </StatusTag>
        <ResourceReference
          resourceId={scenario.resourceId}
          file={scenario.file}
          path={failed ? (scenario.path ?? '') : ''}
          resources={resources}
          onSelect={onSelect}
        />
        <Typography.Text style={{ color: colour.textMuted }}>{`${scenario.durationMs} ms`}</Typography.Text>
      </Flex>
      {failed && scenario.step !== null && (
        <Typography.Text>{`${t('shell.studio.tests.step')} ${scenario.step + 1}`}</Typography.Text>
      )}
      {failed && scenario.message !== null && <Typography.Text type="danger">{scenario.message}</Typography.Text>}
    </Flex>
  )
}
