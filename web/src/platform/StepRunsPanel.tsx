import { CloseOutlined } from '@ant-design/icons'
import { Flex, Typography } from 'antd'
import { AxisButton } from './AxisButton'
import { EventTime } from './EventTime'
import type { InstanceDetail } from './inspection'
import { durationText, stepRuns } from './instanceRuns'
import { useMinuteClock } from './relativeTime'
import { useRunWords } from './runWords'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * Proposed, see the review on #3650: the panel beside a process instance's graph for the step chosen on it, as
 * Camunda Operate shows a flow node's details beside its diagram: the step's label and type, each of its runs newest
 * first with its state, start, duration and what it did, in the timeline's words, and a link to its runs in the
 * timeline. A step the instance has not reached says so. Close, or Esc on the graph, clears the choice.
 */
export function StepRunsPanel({
  instance,
  step,
  stepLabel,
  outcomeLabel,
  inspectors,
  sitePath,
  locale,
  maxHeight,
  onClose,
  onShowInTimeline,
}: {
  instance: InstanceDetail
  step: string
  stepLabel: (name: string) => string
  outcomeLabel: (step: string, name: string) => string
  inspectors: ReadonlyMap<string, string> | undefined
  sitePath: string
  locale: string
  maxHeight: number
  onClose: () => void
  onShowInTimeline: (step: string) => void
}) {
  const t = useText()
  const now = useMinuteClock()
  const { colour, radius, spacing, shadow } = useDesignTokens()
  const { typeOf, typeText, summary, stateWords } = useRunWords({
    instance,
    stepLabel,
    outcomeLabel,
    inspectors,
    sitePath,
  })
  const runs = stepRuns(instance)
    .filter((run) => run.step === step)
    .reverse()
  return (
    <section
      aria-label={stepLabel(step)}
      data-testid="inspector-step-panel"
      style={{
        width: 360,
        maxHeight,
        overflow: 'auto',
        padding: spacing.md,
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
        boxShadow: shadow.overlay,
      }}
    >
      <Flex justify="space-between" align="flex-start" gap="small">
        <Flex vertical style={{ minWidth: 0 }}>
          <Typography.Title level={2} style={{ margin: 0, fontSize: 16, lineHeight: '24px' }}>
            {stepLabel(step)}
          </Typography.Title>
          <Typography.Text type="secondary">{typeText(typeOf(step))}</Typography.Text>
        </Flex>
        <AxisButton role="icon" nameKey="shell.graph.closeDetails" icon={<CloseOutlined />} onClick={onClose} />
      </Flex>
      <Flex vertical style={{ marginTop: spacing.sm }}>
        {runs.length === 0 ? (
          <Typography.Text type="secondary">{t('shell.inspector.graph.notReached')}</Typography.Text>
        ) : (
          runs.map((run) => (
            <Flex
              key={run.key}
              vertical
              gap={spacing.xs / 2}
              style={{ paddingBlock: spacing.sm, borderTop: `1px solid ${colour.borderSubtle}` }}
            >
              <Flex align="center" gap="small" wrap>
                {runs.length > 1 && (
                  <Typography.Text strong>
                    {t('shell.inspector.graph.run').replace('{number}', String(run.number))}
                  </Typography.Text>
                )}
                {stateWords(
                  run.state,
                  run.tasks.some((task) => task.completedAt === null),
                )}
                <Typography.Text type="secondary">
                  <EventTime at={run.startedAt} locale={locale} />
                </Typography.Text>
                <Typography.Text type="secondary">
                  {durationText(run.startedAt, run.finishedAt ?? now.toISOString(), locale)}
                </Typography.Text>
              </Flex>
              {summary(run)}
            </Flex>
          ))
        )}
      </Flex>
      {runs.length > 0 && (
        <div style={{ marginTop: spacing.xs }}>
          <AxisButton role="link" onClick={() => onShowInTimeline(step)}>
            {t('shell.inspector.graph.showInTimeline')}
          </AxisButton>
        </div>
      )}
    </section>
  )
}
