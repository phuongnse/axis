import { Button, Flex, Tag, Typography } from 'antd'
import type { StudioRequest, StudioScenarioResult } from '../platform/studio'
import { useText } from '../platform/texts'

/**
 * The results of a test request: each scenario with passed or failed, its time and, for a failure, its step and
 * message. Selecting a failure calls `onSelect` with the scenario's resource and the path of its failing step. While
 * the request is queued or running it shows a note instead of rows, so an empty list never reads as a pass. An ended
 * request with no scenario results says that no scenario ran.
 */
export function StudioTestResults({
  request,
  onSelect,
}: {
  request: StudioRequest
  onSelect: (resourceId: string, path: string) => void
}) {
  const t = useText()
  const pending = request.status === 'queued' || request.status === 'running'
  const scenarios = request.scenarios ?? []
  return (
    <section data-testid="studio-test-results" data-status={request.status}>
      <Typography.Title level={5}>{t('shell.studio.tests.title')}</Typography.Title>
      {pending ? (
        <Typography.Text type="secondary">{t('shell.studio.tests.pending')}</Typography.Text>
      ) : scenarios.length === 0 ? (
        <Typography.Text type="secondary">{t('shell.studio.tests.none')}</Typography.Text>
      ) : (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {scenarios.map((scenario) => (
            <li key={scenario.resourceId} data-testid="studio-test-result" data-outcome={scenario.outcome}>
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
  scenario: StudioScenarioResult
  onSelect: (resourceId: string, path: string) => void
}) {
  const t = useText()
  const failed = scenario.outcome === 'failed'
  const content = (
    <Flex vertical align="flex-start">
      <span>
        <Tag color={failed ? 'error' : 'success'}>
          {t(failed ? 'shell.studio.tests.failed' : 'shell.studio.tests.passed')}
        </Tag>
        <strong>{scenario.name}</strong>{' '}
        <Typography.Text type="secondary">{`${scenario.durationMs} ms`}</Typography.Text>
      </span>
      {failed && scenario.step !== null && (
        <Typography.Text>{`${t('shell.studio.tests.step')} ${scenario.step + 1}`}</Typography.Text>
      )}
      {failed && scenario.message !== null && <Typography.Text type="danger">{scenario.message}</Typography.Text>}
    </Flex>
  )
  if (!failed) {
    return <div style={{ padding: '4px 15px' }}>{content}</div>
  }
  return (
    <Button
      type="text"
      style={{ height: 'auto', textAlign: 'left' }}
      onClick={() => onSelect(scenario.resourceId, scenario.path ?? '')}
    >
      {content}
    </Button>
  )
}
