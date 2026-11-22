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
import { PlatformContext } from './context'
import { FileAtLine } from './FileAtLine'
import { ResourceReference } from './ResourceReference'

/**
 * The results of a test request, or the contract scenarios of a publish or validate: each scenario with passed or
 * failed, its time and, for a failure, its step and message. A scenario's name is its only link, with "Test scenario"
 * muted after it. The link goes to the scenario at the path of its failing step, built by `scenarioAddressOf` from the
 * scenario's resource, or to its top for a pass, see docs/reference/actions-and-controls.md#opening-things. Without
 * `scenarioAddressOf` the name is plain text. A skipped contract scenario shows the relaxed point it covers and opens
 * nothing. While the request is queued or running it shows a note instead of rows, so an empty list never reads as a pass.
 * An ended request with no scenario results says that no scenario ran.
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
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {scenarios.map((scenario) => (
            <li key={scenario.file} data-testid="studio-test-result" data-outcome={scenario.outcome}>
              <ScenarioRow scenario={scenario} scenarioAddressOf={scenarioAddressOf} />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function ScenarioRow({
  scenario,
  scenarioAddressOf,
}: {
  scenario: StudioScenarioResult | StudioSkippedScenarioResult
  scenarioAddressOf?: (resourceId: string, path: string) => string
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
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
            href={scenarioAddressOf?.(scenario.resourceId, scenario.path ?? '')}
          />
          <Typography.Text type="secondary">{formatDuration(scenario.durationMs, locale, t)}</Typography.Text>
        </Flex>
        {failed && scenario.step !== null && (
          <Typography.Text>{fillText(t('shell.studio.tests.step'), { number: scenario.step + 1 })}</Typography.Text>
        )}
        {failed && scenario.message !== null && <Typography.Text type="danger">{scenario.message}</Typography.Text>}
      </Flex>
    </div>
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
