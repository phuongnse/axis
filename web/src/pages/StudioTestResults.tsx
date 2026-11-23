import { Flex, Typography } from 'antd'
import { useContext } from 'react'
import { EmptyState } from '../platform/EmptyState'
import { fillText } from '../platform/fillText'
import { formatDuration } from '../platform/formatValue'
import { StatusTag } from '../platform/StatusTag'
import type {
  StudioPackageTestResult,
  StudioRequest,
  StudioScenarioResult,
  StudioSkippedScenarioResult,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import { FileAtLine } from './FileAtLine'
import { ResourceReference } from './ResourceReference'

/**
 * The results of a test request, or the contract scenarios of a publish or validate. Each scenario is a row: its outcome
 * tag in a column of its own, then its name with its kind and its time. A failure adds one sentence under the name, in
 * the text colour, from its step and message, such as "Failed at step 6: …". The rows share one grid, so the tags line
 * up, see docs/reference/ai-authoring-in-studio.md#scenario-results. A scenario's name is its only link, with "Test
 * scenario" muted after it. The link goes to the scenario at the path of its failing step, built by `scenarioAddressOf`
 * from the scenario's resource, or to its top for a pass, see docs/reference/actions-and-controls.md#opening-things.
 * Without `scenarioAddressOf` the name is plain text. A skipped contract scenario shows the relaxed point it covers and
 * opens nothing. While the request is queued or running it shows a note instead of rows, so an empty list never reads as
 * a pass. An ended request with no scenario results says that no scenario ran.
 *
 * A package's `test` request, which holds `packageTests`, shows one row a test instead: passed or failed, the name, the
 * duration when the host sent it, and for a failure its message with its place after it, `packages/<id>/<file>, line N`.
 * The place is a link through `FileAtLine` when `fileAddressOf` gives an address for the file, which opens the file at
 * the line. An ended run with no test says that no test ran. With `titled` false the section has no heading, for a place
 * that is named already, such as the Tests tab. See docs/reference/studio-packages.md#a-packages-files.
 */
export function StudioTestResults({
  request,
  scenarioAddressOf,
  fileAddressOf,
  titled = true,
}: {
  request: Pick<StudioRequest, 'kind' | 'status' | 'scenarios' | 'package' | 'packageTests'>
  scenarioAddressOf?: (resourceId: string, path: string) => string
  fileAddressOf?: (file: string, line?: number) => string | undefined
  titled?: boolean
}) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const pending = request.status === 'queued' || request.status === 'running'
  const scenarios = request.scenarios ?? []
  const packageTests = request.packageTests ?? null
  return (
    <section data-testid="studio-test-results" data-status={request.status}>
      {titled && (
        <Typography.Title level={5}>
          {t(request.kind === 'test' ? 'shell.studio.tests.title' : 'shell.studio.tests.contractTitle')}
        </Typography.Title>
      )}
      {pending ? (
        <Typography.Text type="secondary">{t('shell.studio.tests.pending')}</Typography.Text>
      ) : packageTests !== null ? (
        packageTests.length === 0 ? (
          <EmptyState text={t('shell.studio.tests.noPackageTests')} />
        ) : (
          <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
            {packageTests.map((test, index) => (
              <li key={`${index}:${test.name}`} data-testid="studio-test-result" data-outcome={test.outcome}>
                <PackageTestRow test={test} packageId={request.package ?? ''} fileAddressOf={fileAddressOf} />
              </li>
            ))}
          </ul>
        )
      ) : scenarios.length === 0 ? (
        <EmptyState text={t('shell.studio.tests.none')} />
      ) : (
        <ul
          style={{
            listStyle: 'none',
            margin: 0,
            padding: 0,
            display: 'grid',
            gridTemplateColumns: 'max-content minmax(0, 1fr)',
            columnGap: spacing.sm,
          }}
        >
          {scenarios.map((scenario) => (
            <li
              key={scenario.file}
              data-testid="studio-test-result"
              data-outcome={scenario.outcome}
              style={{ display: 'grid', gridTemplateColumns: 'subgrid', gridColumn: '1 / -1', padding: '4px 15px' }}
            >
              <ScenarioRow scenario={scenario} scenarioAddressOf={scenarioAddressOf} />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

/** The one sentence a failure reads, from its step and message, or null when it has neither. Steps count from 1. */
function failureSentence(scenario: StudioScenarioResult, t: (key: string) => string): string | null {
  const { step, message } = scenario
  if (step !== null && message !== null) {
    return fillText(t('shell.studio.tests.failedAtStep'), { number: step + 1, message })
  }
  if (step !== null) {
    return fillText(t('shell.studio.tests.failedStepOnly'), { number: step + 1 })
  }
  if (message !== null) {
    return fillText(t('shell.studio.tests.failedBecause'), { message })
  }
  return null
}

/** A scenario's two cells in the list's grid: its outcome tag alone, then its name, kind, time and failure sentence. */
function ScenarioRow({
  scenario,
  scenarioAddressOf,
}: {
  scenario: StudioScenarioResult | StudioSkippedScenarioResult
  scenarioAddressOf?: (resourceId: string, path: string) => string
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour, spacing } = useDesignTokens()
  if (scenario.outcome === 'skipped') {
    return (
      <>
        <div data-testid="studio-test-result-outcome">
          <StatusTag tone="muted">{t('shell.studio.tests.skipped')}</StatusTag>
        </div>
        <Flex vertical align="flex-start" data-testid="studio-test-result-detail">
          <strong>{scenario.name}</strong>
          <Typography.Text type="secondary">
            {fillText(t('shell.studio.tests.relaxedPoint'), { point: scenario.point })}
          </Typography.Text>
        </Flex>
      </>
    )
  }
  const failed = scenario.outcome === 'failed'
  const failure = failed ? failureSentence(scenario, t) : null
  return (
    <>
      <div data-testid="studio-test-result-outcome">
        <StatusTag tone={failed ? 'danger' : 'success'}>
          {t(failed ? 'shell.studio.tests.failed' : 'shell.studio.tests.passed')}
        </StatusTag>
      </div>
      <Flex vertical align="flex-start" data-testid="studio-test-result-detail">
        <Flex gap={spacing.xs} align="center" wrap>
          <ResourceReference
            resource={{ kind: 'scenario', name: scenario.name }}
            file={scenario.file}
            href={scenarioAddressOf?.(scenario.resourceId, scenario.path ?? '')}
          />
          <Typography.Text style={{ color: colour.textMuted }}>
            {formatDuration(scenario.durationMs, locale, t)}
          </Typography.Text>
        </Flex>
        {failure !== null && <Typography.Text style={{ color: colour.text }}>{failure}</Typography.Text>}
      </Flex>
    </>
  )
}

function PackageTestRow({
  test,
  packageId,
  fileAddressOf,
}: {
  test: StudioPackageTestResult
  packageId: string
  fileAddressOf?: (file: string, line?: number) => string | undefined
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const failed = test.outcome === 'failed'
  const file = test.file === undefined ? null : `packages/${packageId}/${test.file}`
  return (
    <div style={{ padding: '4px 15px' }}>
      <Flex vertical align="flex-start">
        <Flex gap="small" align="center" wrap>
          <StatusTag tone={failed ? 'danger' : 'success'}>
            {t(failed ? 'shell.studio.tests.failed' : 'shell.studio.tests.passed')}
          </StatusTag>
          <strong>{test.name}</strong>
          {test.durationMs !== undefined && (
            <Typography.Text type="secondary">{formatDuration(test.durationMs, locale, t)}</Typography.Text>
          )}
        </Flex>
        {(test.message !== null || file !== null) && (
          <Flex gap="small" align="baseline" wrap>
            {test.message !== null && (
              <Typography.Text type={failed ? 'danger' : 'secondary'}>{test.message}</Typography.Text>
            )}
            {file !== null && <FileAtLine file={file} line={test.line} href={fileAddressOf?.(file, test.line)} />}
          </Flex>
        )}
      </Flex>
    </div>
  )
}
