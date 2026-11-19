import { Flex, Typography } from 'antd'
import { EmptyState } from '../platform/EmptyState'
import { fillText } from '../platform/fillText'
import { StatusTag } from '../platform/StatusTag'
import type { StudioRequest, StudioScenarioResult, StudioSkippedScenarioResult } from '../platform/studio'
import { useText } from '../platform/texts'
import { ResourceReference } from './ResourceReference'

/**
 * The results of a test request, or the contract scenarios of a publish or validate: each scenario with passed or
 * failed, its time and, for a failure, its step and message. A scenario's name is its only link, with "Test scenario"
 * muted after it, see docs/reference/actions-and-controls.md#buttons. Selecting it calls `onSelect` with the scenario's
 * resource and the path of its failing step, or an empty path for a pass, so a pass opens at its top. Without `onSelect`
 * the name is plain text. A skipped contract scenario shows the relaxed point it covers and opens nothing. While the
 * request is queued or running it shows a note instead of rows, so an empty list never reads as a pass. An ended request
 * with no scenario results says that no scenario ran.
 */
export function StudioTestResults({
  request,
  onSelect,
}: {
  request: Pick<StudioRequest, 'kind' | 'status' | 'scenarios'>
  onSelect?: (resourceId: string, path: string) => void
}) {
  const t = useText()
  const pending = request.status === 'queued' || request.status === 'running'
  const scenarios = request.scenarios ?? []
  return (
    <section data-testid="studio-test-results" data-status={request.status}>
      <Typography.Title level={5}>
        {t(request.kind === 'test' ? 'shell.studio.tests.title' : 'shell.studio.tests.contractTitle')}
      </Typography.Title>
      {pending ? (
        <Typography.Text type="secondary">{t('shell.studio.tests.pending')}</Typography.Text>
      ) : scenarios.length === 0 ? (
        <EmptyState text={t('shell.studio.tests.none')} />
      ) : (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {scenarios.map((scenario) => (
            <li key={scenario.file} data-testid="studio-test-result" data-outcome={scenario.outcome}>
              <ScenarioRow scenario={scenario} onSelect={onSelect} />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function ScenarioRow({
  scenario,
  onSelect,
}: {
  scenario: StudioScenarioResult | StudioSkippedScenarioResult
  onSelect?: (resourceId: string, path: string) => void
}) {
  const t = useText()
  if (scenario.outcome === 'skipped') {
    return (
      <div style={{ padding: '4px 15px' }}>
        <Flex vertical align="flex-start">
          <Flex gap="small" align="center" wrap>
            <StatusTag tone="muted">{t('shell.studio.tests.skipped')}</StatusTag>
            <strong>{scenario.name}</strong>
          </Flex>
          <Typography.Text type="secondary">
            {fillText(t('shell.studio.tests.relaxedPoint'), { point: scenario.point })}
          </Typography.Text>
        </Flex>
      </div>
    )
  }
  const failed = scenario.outcome === 'failed'
  return (
    <div style={{ padding: '4px 15px' }}>
      <Flex vertical align="flex-start">
        <Flex gap="small" align="center" wrap>
          <StatusTag tone={failed ? 'danger' : 'success'}>
            {t(failed ? 'shell.studio.tests.failed' : 'shell.studio.tests.passed')}
          </StatusTag>
          <ResourceReference
            resource={{ kind: 'scenario', name: scenario.name }}
            file={scenario.file}
            onOpen={onSelect && (() => onSelect(scenario.resourceId, scenario.path ?? ''))}
          />
          <Typography.Text type="secondary">{`${scenario.durationMs} ms`}</Typography.Text>
        </Flex>
        {failed && scenario.step !== null && (
          <Typography.Text>{fillText(t('shell.studio.tests.step'), { number: scenario.step + 1 })}</Typography.Text>
        )}
        {failed && scenario.message !== null && <Typography.Text type="danger">{scenario.message}</Typography.Text>}
      </Flex>
    </div>
  )
}
