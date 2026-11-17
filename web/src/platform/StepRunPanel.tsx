import { Flex, Select, Tabs, Typography } from 'antd'
import { useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { AxisAlert } from './AxisAlert'
import { AxisButton } from './AxisButton'
import { BodyView, ValueTable } from './ValueTable'
import { mappingRows, sentRows, textRows } from './valueRows'
import { EmptyDash } from './EmptyValue'
import { EventTime } from './EventTime'
import { studioResourceAddress } from './studioAddress'
import { inspectorPath } from './inspectorPages'
import type { CallAttempt, InstanceCall, InstanceDetail, StepDefinition } from './inspection'
import { durationText, stepRuns, type StepRun } from './instanceRuns'
import { RecordLink } from './RecordLink'
import { useMinuteClock } from './relativeTime'
import { useRunWords } from './runWords'
import { SidePanel } from './SidePanel'
import { StatusTag, type StatusTone } from './StatusTag'
import { problemText } from './stepProblems'
import type { StepRunTab } from './stepRunTabs'
import { TermList, type Term } from './TermList'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/** An ISO 8601 duration of days, hours, minutes and seconds, such as `P3D` or `PT30S`, in the reader's words. */
function isoDurationText(value: unknown, locale: string): string | null {
  const match = typeof value === 'string' ? /^P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$/.exec(value) : null
  if (match === null) {
    return null
  }
  const [days, hours, minutes, seconds] = match.slice(1).map((part) => Number(part ?? 0))
  const total = ((days * 24 + hours) * 60 + minutes) * 60 + seconds
  return durationText(new Date(0).toISOString(), new Date(total * 1000).toISOString(), locale)
}

const record = (value: unknown): Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : {}
const words = (value: unknown): string => (typeof value === 'string' ? value : JSON.stringify(value))

/** The tone of an attempt's outcome: it ended well, it will be tried again, it failed, or nobody knows yet. */
/** The tone of a sub-process's state, as an instance's own state takes. */
const childTones: Record<string, StatusTone> = {
  completed: 'success',
  failed: 'danger',
  waiting: 'neutral',
  running: 'neutral',
  cancelled: 'muted',
}

const attemptTones: Record<string, StatusTone> = {
  succeeded: 'success',
  found: 'success',
  retryable: 'muted',
  notFound: 'muted',
  failed: 'danger',
  unknown: 'attention',
}

/**
 * Proposed, see the review on #3650: the side panel of one run of a process instance's step, which a click on the step
 * in the graph or on its run in the timeline opens, as AWS Step Functions and Camunda Operate show a state's details
 * beside its diagram. A step run more than once chooses its run on top. Its tabs show what the step needs, and only
 * those: Overview, the run's state, times, where it came from and went to and why, and what it did as facts; Input, what a call
 * sent, its request with its headers and body; Output, what the external system answered and the fields the step
 * wrote; Attempts, each attempt of a call with its status, error, trace ID and answer; Error, why a failed run failed;
 * and Definition, the step as the instance's release defines it. A value the reader may not read is hidden as the
 * call's input is, see docs/reference/process-inspection.md#values-and-masking. The step, its run and its tab live in
 * the address.
 */
export function StepRunPanel({
  instance,
  step,
  run,
  tab,
  onRun,
  onTab,
  onOpen,
  onClose,
  onShowInTimeline,
  stepLabel,
  outcomeLabel,
  inspectors,
  sitePath,
  locale,
}: {
  instance: InstanceDetail
  step: string
  /** The number of the run shown, or `null` for the step's last run. */
  run: number | null
  tab: StepRunTab
  onRun: (number: number) => void
  onTab: (tab: StepRunTab) => void
  /** Opens another step's run, such as the one this run came from, or its last run with `null`. */
  onOpen: (step: string, run: number | null) => void
  onClose: () => void
  onShowInTimeline: (step: string) => void
  stepLabel: (name: string) => string
  outcomeLabel: (step: string, name: string) => string
  inspectors: ReadonlyMap<string, string> | undefined
  sitePath: string
  locale: string
}) {
  const t = useText()
  const now = useMinuteClock()
  const { spacing } = useDesignTokens()
  const { typeText, stateWords, targetOf, assigneeText } = useRunWords({
    instance,
    stepLabel,
    outcomeLabel,
    inspectors,
    sitePath,
  })
  const runs = stepRuns(instance)
  const own = runs.filter((candidate) => candidate.step === step)
  const chosen = own.find((candidate) => candidate.number === run) ?? own.at(-1)
  const graphStep = instance.graph.steps.find((candidate) => candidate.name === step)
  const definition: StepDefinition = graphStep?.definition ?? {}
  const operation = typeof definition.operation === 'string' ? definition.operation : null
  const calls = chosen?.calls ?? []
  const failed = chosen !== undefined && (chosen.error !== null || calls.some((call) => call.state === 'failed'))

  const shown: StepRunTab[] = ['overview']
  if (calls.length > 0) shown.push('input')
  if (calls.length > 0 || operation === 'updateRecord') shown.push('output')
  if (calls.some((call) => call.attempts.length > 0)) shown.push('attempts')
  if (failed) shown.push('error')
  const current = shown.includes(tab) ? tab : 'overview'

  const operationText =
    operation === null
      ? null
      : operation.includes('/')
        ? t('shell.stepRun.operation.package')
        : t(`shell.stepRun.operation.${operation}`)
  const kind = [typeText(graphStep?.type ?? ''), operationText].filter(Boolean).join(' · ')
  // A step a run names opens in the panel, as every reference opens what it names.
  const stepName = (name: unknown) =>
    typeof name === 'string' ? (
      <AxisButton role="link" inline onClick={() => onOpen(name, null)}>
        {stepLabel(name)}
      </AxisButton>
    ) : (
      <EmptyDash />
    )
  const muted = (text: ReactNode) => <Typography.Text type="secondary">{text}</Typography.Text>
  const notSet = muted(t('shell.value.notSet'))
  const heading = (text: ReactNode) => (
    <Typography.Title level={5} style={{ margin: `${spacing.md}px 0 ${spacing.xs}px`, fontSize: 14 }}>
      {text}
    </Typography.Title>
  )
  const stepIndex = instance.graph.steps.findIndex((candidate) => candidate.name === step)
  // Proposed, see the review on #3650: what a run that waits waits for, and until when: an event, and when the wait
  // gives up and where it goes then; or a timer, and when it goes on. The engine keeps the time as the run's due time;
  // the prototype works it out from the step.
  const waitTerms = (shown: StepRun): Term[] => {
    if (shown.finishedAt !== null) {
      return []
    }
    const terms: Term[] = []
    const after = (value: unknown) => {
      const match =
        typeof value === 'string' ? /^P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$/.exec(value) : null
      if (match === null) return null
      const [days, hours, minutes, seconds] = match.slice(1).map((part) => Number(part ?? 0))
      return new Date(
        Date.parse(shown.startedAt) + (((days * 24 + hours) * 60 + minutes) * 60 + seconds) * 1000,
      ).toISOString()
    }
    if (typeof definition.event === 'string') {
      terms.push({ key: 'waitingFor', term: t('shell.stepRun.waitingFor'), value: definition.event })
      const timeout = record(definition.timeout)
      const due = after(timeout.after)
      if (due !== null) {
        terms.push({
          key: 'givesUp',
          term: t('shell.stepRun.givesUp'),
          value: (
            <Flex vertical align="flex-start">
              <EventTime at={due} locale={locale} />
              {muted(<>→ {stepName(timeout.next)}</>)}
            </Flex>
          ),
        })
      }
    }
    const wake = after(definition.wait) ?? null
    if (wake !== null) {
      terms.push({ key: 'goesOn', term: t('shell.stepRun.goesOn'), value: <EventTime at={wake} locale={locale} /> })
    }
    return terms
  }

  const overview = (shown: StepRun) => {
    const index = runs.indexOf(shown)
    const before = runs[index - 1]
    const after = runs[index + 1]
    const transition = graphStep?.transitions.find((candidate) => candidate.next === after?.step)
    const reason =
      transition?.on == null ? null : graphStep?.type === 'task' ? outcomeLabel(step, transition.on) : transition.on
    const runLink = (target: StepRun) => (
      <AxisButton role="link" inline onClick={() => onOpen(target.step, target.number)}>
        {stepLabel(target.step)}
        {runs.filter((candidate) => candidate.step === target.step).length > 1
          ? ` · ${t('shell.stepRun.runNumber').replace('{number}', String(target.number))}`
          : ''}
      </AxisButton>
    )
    const terms: Term[] = [
      {
        key: 'state',
        term: t('shell.stepRun.state'),
        value: stateWords(
          shown.state,
          shown.tasks.some((task) => task.completedAt === null),
        ),
        width: 6,
      },
      {
        key: 'duration',
        term: t('shell.stepRun.duration'),
        value: durationText(shown.startedAt, shown.finishedAt ?? now.toISOString(), locale),
        width: 6,
      },
      {
        key: 'started',
        term: t('shell.stepRun.started'),
        value: <EventTime at={shown.startedAt} locale={locale} />,
        width: 6,
      },
      {
        key: 'ended',
        term: t('shell.stepRun.ended'),
        value: shown.finishedAt === null ? <EmptyDash /> : <EventTime at={shown.finishedAt} locale={locale} />,
        width: 6,
      },
      {
        key: 'cameFrom',
        term: t('shell.stepRun.cameFrom'),
        value: before === undefined ? muted(t('shell.stepRun.start')) : runLink(before),
      },
      {
        key: 'wentTo',
        term: t('shell.stepRun.wentTo'),
        value:
          after === undefined ? (
            muted(shown.finishedAt === null ? t('shell.stepRun.stillHere') : t('shell.stepRun.ended.instance'))
          ) : (
            <Flex vertical align="flex-start">
              {runLink(after)}
              {reason !== null && muted(t('shell.stepRun.because').replace('{reason}', reason))}
            </Flex>
          ),
      },
      // Proposed, see the review on #3650: what the run did, as facts by what it holds, in place of the timeline's
      // sentence: a task's assignee, who completed it and with which outcome; a call's target, state and attempts,
      // whose detail is the Attempts tab; a sub-process's instance and state. A decision's Went to says it all.
      ...shown.tasks.flatMap((task): Term[] => [
        { key: `assignee-${task.id}`, term: t('shell.stepRun.assignedTo'), value: assigneeText(task), width: 6 },
        {
          key: `due-${task.id}`,
          term: t('shell.stepRun.due'),
          value:
            task.dueAt == null ? (
              notSet
            ) : (
              <Flex align="center" gap="small" wrap>
                <EventTime at={task.dueAt} locale={locale} />
                {task.completedAt === null && task.dueAt < now.toISOString() && (
                  <StatusTag tone="attention">{t('shell.stepRun.overdue')}</StatusTag>
                )}
              </Flex>
            ),
          width: 6,
        },
        {
          key: `completedBy-${task.id}`,
          term: t('shell.stepRun.completedBy'),
          value: task.completedBy ?? notSet,
          width: 6,
        },
        {
          key: `outcome-${task.id}`,
          term: t('shell.stepRun.outcomeChosen'),
          value: task.outcome === null ? notSet : outcomeLabel(step, task.outcome),
          width: 6,
        },
      ]),
      ...shown.calls.flatMap((call): Term[] => [
        { key: `called-${call.id}`, term: t('shell.stepRun.called'), value: targetOf(call) },
        {
          key: `callState-${call.id}`,
          term: t('shell.stepRun.callState'),
          value: stateWords(call.state === 'succeeded' ? 'done' : call.state === 'failed' ? 'failed' : 'running'),
          width: 6,
        },
        {
          key: `attempts-${call.id}`,
          term: t('shell.stepRun.attemptCount'),
          value: String(call.attempts.filter((attempt) => attempt.kind === 'attempt').length),
          width: 6,
        },
      ]),
      ...shown.subProcesses.map((child): Term => ({
        key: `child-${child.id}`,
        term: t('shell.stepRun.subProcess'),
        value: (
          <Flex align="center" gap="small" wrap>
            {inspectors?.get(child.process) === undefined ? (
              child.process
            ) : (
              <Link to={inspectorPath(sitePath, inspectors.get(child.process) ?? '', child.id)}>{child.process}</Link>
            )}
            <StatusTag tone={childTones[child.state] ?? 'neutral'}>
              {t(`shell.inspector.states.${child.state}`)}
            </StatusTag>
          </Flex>
        ),
      })),
      ...waitTerms(shown),
      {
        key: 'trace',
        term: t('shell.stepRun.traceId'),
        value:
          shown.traceId === null ? (
            <EmptyDash />
          ) : (
            <Typography.Text code copyable>
              {shown.traceId}
            </Typography.Text>
          ),
      },
    ]
    return (
      <Flex vertical gap={spacing.md}>
        <TermList terms={terms} />
        <Flex gap={spacing.md} wrap>
          <AxisButton role="link" inline onClick={() => onShowInTimeline(step)}>
            {t('shell.inspector.graph.showInTimeline')}
          </AxisButton>
          {/* Proposed: how the step is set lives in Studio's process designer, so an author opens it there, see the
              review on #3650; the server tells only an author where. */}
          {instance.studio && stepIndex >= 0 && (
            <Link
              to={studioResourceAddress(instance.studio.application, instance.studio.resource, `/steps/${stepIndex}`)}
            >
              {t('shell.stepRun.openInStudio')}
            </Link>
          )}
        </Flex>
      </Flex>
    )
  }

  const hidden = muted(t('shell.stepRun.hidden'))
  const callHeading = (call: InstanceCall) => (calls.length > 1 ? heading(targetOf(call)) : null)

  // Proposed, see the review on #3650: names and their values are tables, as Postman and Logic Apps show them: the
  // request's query parameters and headers, a package call's input; a body is a table with a switch to its JSON.
  const input = () =>
    calls.map((call) => (
      <div key={call.id}>
        {callHeading(call)}
        {call.inputHidden ? (
          hidden
        ) : call.request ? (
          <Flex vertical gap={spacing.xs}>
            {requestTerms(call.request.method, call.request.url)}
            {queryRows(call.request.url).length > 0 && (
              <>
                {heading(t('shell.stepRun.query'))}
                <ValueTable label={t('shell.stepRun.query')} rows={queryRows(call.request.url)} />
              </>
            )}
            {heading(t('shell.stepRun.headers'))}
            <ValueTable label={t('shell.stepRun.headers')} rows={textRows(call.request.headers)} />
            {heading(t('shell.stepRun.body'))}
            {call.request.body === null ? (
              muted(t('shell.stepRun.noBody'))
            ) : (
              <BodyView label={t('shell.stepRun.body')} text={call.request.body} mapping={definition.input} />
            )}
          </Flex>
        ) : call.input === null ? (
          muted(t('shell.stepRun.notKept'))
        ) : (
          // A shown input lists its properties, each number with its source digits, as the process inspector shows it.
          <ValueTable label={t('shell.stepRun.tab.input')} rows={sentRows(call.input, definition.input)} />
        )}
      </div>
    ))

  const requestTerms = (method: string, url: string) => {
    const address = url.split('?', 1)[0]
    return (
      <TermList
        terms={[
          { key: 'method', term: t('shell.stepRun.method'), value: method, width: 3 },
          {
            key: 'url',
            term: t('shell.stepRun.url'),
            value: <span style={{ overflowWrap: 'anywhere' }}>{address}</span>,
            width: 9,
          },
        ]}
      />
    )
  }
  const queryRows = (url: string) => {
    const query = url.includes('?') ? url.slice(url.indexOf('?') + 1) : ''
    return textRows(Object.fromEntries(new URLSearchParams(query)))
  }

  const response = (attempt: CallAttempt) =>
    attempt.response === undefined ? null : attempt.response === null ? (
      muted(t('shell.stepRun.noResponse'))
    ) : (
      <Flex vertical gap={spacing.xs}>
        <TermList
          terms={[
            {
              key: 'status',
              term: t('shell.stepRun.status'),
              value: (
                <StatusTag tone={attempt.response.status < 400 ? 'success' : 'danger'}>
                  {String(attempt.response.status)}
                </StatusTag>
              ),
            },
          ]}
        />
        {heading(t('shell.stepRun.headers'))}
        <ValueTable label={t('shell.stepRun.headers')} rows={textRows(attempt.response.headers)} />
        {heading(t('shell.stepRun.body'))}
        {attempt.response.body === null ? (
          muted(t('shell.stepRun.noBody'))
        ) : (
          <BodyView label={t('shell.stepRun.body')} text={attempt.response.body} />
        )}
        {attempt.response.truncated && muted(t('shell.stepRun.truncated'))}
      </Flex>
    )

  // A step's mappings are tables of two columns: a field it sets or an input it sends, and the expression that gives
  // it; or a property of the answer, and the field it writes.
  const settingTable = (mapping: unknown, label: string, nameTitle: string) => (
    <ValueTable
      label={label}
      rows={mappingRows(mapping)}
      nameTitle={nameTitle}
      valueTitle={t('shell.stepRun.expression')}
    />
  )
  const writingTable = (mapping: unknown, label: string) => (
    <ValueTable
      label={label}
      rows={Object.entries(record(mapping)).map(([from, field]) => ({
        key: from,
        name: from,
        value: { kind: 'text', text: words(field) },
      }))}
      nameTitle={t('shell.stepRun.fromAnswer')}
      valueTitle={t('shell.stepRun.field')}
    />
  )

  const output = () => (
    <Flex vertical gap={spacing.xs}>
      {calls.map((call) => {
        const answered = call.attempts.findLast(
          (attempt) => attempt.response !== undefined && attempt.response !== null,
        )
        return (
          <div key={call.id}>
            {callHeading(call)}
            {call.kind === 'connector' &&
              (call.inputHidden ? (
                hidden
              ) : answered === undefined ? (
                muted(t('shell.stepRun.noResponse'))
              ) : (
                <>
                  {heading(t('shell.stepRun.response').replace('{number}', String(answered.number)))}
                  {response(answered)}
                </>
              ))}
          </div>
        )
      })}
      {(definition.output !== undefined || definition.set !== undefined) && (
        <>
          {heading(t(definition.set !== undefined ? 'shell.stepRun.fieldsSet' : 'shell.stepRun.fieldsWritten'))}
          {definition.set !== undefined
            ? settingTable(definition.set, t('shell.stepRun.fieldsSet'), t('shell.stepRun.field'))
            : writingTable(definition.output, t('shell.stepRun.fieldsWritten'))}
          {instance.subject &&
            muted(
              <>
                {t('shell.stepRun.valuesIn')}{' '}
                <RecordLink entity={instance.subject.entity} id={instance.subject.id}>
                  {instance.subject.label ?? instance.subject.id}
                </RecordLink>
              </>,
            )}
        </>
      )}
    </Flex>
  )

  const attempts = () => {
    const retry = record(definition.retry)
    return (
      <Flex vertical gap={spacing.sm}>
        {retry.attempts !== undefined &&
          muted(
            t('shell.stepRun.retry')
              .replace('{attempts}', String(retry.attempts))
              .replace('{backoff}', isoDurationText(retry.backoff, locale) ?? words(retry.backoff)),
          )}
        {calls.map((call) => (
          <div key={call.id}>
            {callHeading(call)}
            {call.attempts.map((attempt) => (
              <AttemptView key={`${attempt.kind}-${attempt.number}`} attempt={attempt} locale={locale}>
                {response(attempt)}
              </AttemptView>
            ))}
          </div>
        ))}
      </Flex>
    )
  }

  const error = () => (
    <Flex vertical gap={spacing.sm}>
      {chosen !== undefined && problemText(t, chosen.problem, chosen.error) !== null && (
        <AxisAlert kind="error" message={problemText(t, chosen.problem, chosen.error)} />
      )}
      {calls
        .filter((call) => call.state === 'failed')
        .map((call) => {
          const last = call.attempts.at(-1)
          return (
            <AxisAlert
              key={call.id}
              kind="error"
              message={targetOf(call)}
              description={[last?.httpStatus, last ? problemText(t, last.problem, last.error) : null]
                .filter((part) => part != null)
                .join(' · ')}
            />
          )
        })}
    </Flex>
  )

  const body: Record<StepRunTab, () => ReactNode> = {
    overview: () => (chosen === undefined ? muted(t('shell.inspector.graph.notReached')) : overview(chosen)),
    input,
    output,
    attempts,
    error,
  }

  return (
    <SidePanel
      id="step-run"
      testId="inspector-step-panel"
      title={stepLabel(step)}
      kind={kind}
      closeKey="shell.graph.closeDetails"
      onClose={onClose}
      top={
        <Flex vertical gap={spacing.xs}>
          {own.length > 1 && chosen !== undefined && (
            <Select
              aria-label={t('shell.stepRun.chooseRun')}
              value={chosen.number}
              onChange={onRun}
              options={own.map((candidate) => ({
                value: candidate.number,
                label: t('shell.stepRun.run')
                  .replace('{number}', String(candidate.number))
                  .replace('{count}', String(own.length)),
              }))}
            />
          )}
          {/* A run with one part, such as a wait's, shows it with no tab bar. */}
          {shown.length > 1 && (
            <Tabs
              activeKey={current}
              onChange={(key) => onTab(key as StepRunTab)}
              items={shown.map((key) => ({ key, label: t(`shell.stepRun.tab.${key}`) }))}
              // Narrow gaps, so the tabs of a call fit a panel `sidePanel` wide.
              tabBarGutter={spacing.md}
              tabBarStyle={{ marginBottom: 0 }}
            />
          )}
        </Flex>
      }
    >
      <div style={{ paddingTop: spacing.xs }}>{body[current]()}</div>
    </SidePanel>
  )
}

/** One attempt of a call: its number, outcome, start and duration, HTTP status, error and trace ID, and its answer. */
function AttemptView({ attempt, locale, children }: { attempt: CallAttempt; locale: string; children: ReactNode }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [open, setOpen] = useState(false)
  return (
    <section
      aria-label={t(`shell.stepRun.${attempt.kind}`).replace('{number}', String(attempt.number))}
      style={{ paddingBlock: spacing.sm, borderTop: `1px solid ${colour.borderSubtle}` }}
    >
      <Flex align="center" gap="small" wrap>
        <Typography.Text strong>
          {t(`shell.stepRun.${attempt.kind}`).replace('{number}', String(attempt.number))}
        </Typography.Text>
        <StatusTag tone={attemptTones[attempt.outcome] ?? 'neutral'}>
          {t(`shell.stepRun.outcome.${attempt.outcome}`)}
        </StatusTag>
        <Typography.Text type="secondary">
          <EventTime at={attempt.startedAt} locale={locale} />
        </Typography.Text>
        <Typography.Text type="secondary">
          {durationText(attempt.startedAt, attempt.finishedAt, locale)}
        </Typography.Text>
      </Flex>
      <div style={{ marginTop: spacing.xs }}>
        <TermList
          terms={[
            {
              key: 'status',
              term: t('shell.stepRun.httpStatus'),
              value: attempt.httpStatus === null ? <EmptyDash /> : String(attempt.httpStatus),
              width: 4,
            },
            {
              key: 'error',
              term: t('shell.stepRun.error'),
              value: problemText(t, attempt.problem, attempt.error) ?? <EmptyDash />,
              width: 8,
            },
            {
              key: 'trace',
              term: t('shell.stepRun.traceId'),
              value:
                attempt.traceId === null ? (
                  <EmptyDash />
                ) : (
                  <Typography.Text code copyable>
                    {attempt.traceId}
                  </Typography.Text>
                ),
            },
          ]}
        />
      </div>
      {children !== null && (
        <div style={{ marginTop: spacing.xs }}>
          <AxisButton role="link" aria-expanded={open} onClick={() => setOpen(!open)}>
            {t(open ? 'shell.stepRun.hideResponse' : 'shell.stepRun.showResponse')}
          </AxisButton>
          {open && <div style={{ marginTop: spacing.xs }}>{children}</div>}
        </div>
      )}
    </section>
  )
}
