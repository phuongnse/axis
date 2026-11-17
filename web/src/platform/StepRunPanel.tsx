import { Flex, Select, Tabs, Typography } from 'antd'
import { useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { AxisAlert } from './AxisAlert'
import { AxisButton } from './AxisButton'
import { CodeBlock } from './CodeBlock'
import { EmptyDash } from './EmptyValue'
import { EventTime } from './EventTime'
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
const list = (value: unknown): Record<string, unknown>[] => (Array.isArray(value) ? value.map(record) : [])
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
  if (graphStep?.definition !== undefined) shown.push('definition')
  const current = shown.includes(tab) ? tab : 'overview'

  const operationText =
    operation === null
      ? null
      : operation.includes('/')
        ? t('shell.stepRun.operation.package')
        : t(`shell.stepRun.operation.${operation}`)
  const kind = [typeText(graphStep?.type ?? ''), operationText].filter(Boolean).join(' · ')
  // A step the definition names opens in the panel, as every reference opens what it names.
  const stepName = (name: unknown) =>
    typeof name === 'string' ? (
      <AxisButton role="link" inline onClick={() => onOpen(name, null)}>
        {stepLabel(name)}
      </AxisButton>
    ) : (
      <EmptyDash />
    )
  const leadsTo = (name: unknown) => (
    <Flex align="center" gap={spacing.xs / 2}>
      {muted('→')}
      {stepName(name)}
    </Flex>
  )
  // An expression reads as code; a mapping inside a mapping, such as a list's fields, reads as a block of JSON.
  const code = (value: unknown) =>
    typeof value === 'object' && value !== null ? (
      <CodeBlock label={words(value)} value={value} maxHeight={200} />
    ) : (
      <Typography.Text code>{words(value)}</Typography.Text>
    )
  const muted = (text: ReactNode) => <Typography.Text type="secondary">{text}</Typography.Text>
  const notSet = muted(t('shell.value.notSet'))
  const heading = (text: ReactNode) => (
    <Typography.Title level={5} style={{ margin: `${spacing.md}px 0 ${spacing.xs}px`, fontSize: 14 }}>
      {text}
    </Typography.Title>
  )

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
        { key: `assignee-${task.id}`, term: t('shell.stepRun.assignedTo'), value: assigneeText(task) },
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
        <div>
          <AxisButton role="link" onClick={() => onShowInTimeline(step)}>
            {t('shell.inspector.graph.showInTimeline')}
          </AxisButton>
        </div>
      </Flex>
    )
  }

  const hidden = muted(t('shell.stepRun.hidden'))
  const callHeading = (call: InstanceCall) => (calls.length > 1 ? heading(targetOf(call)) : null)

  const input = () =>
    calls.map((call) => (
      <div key={call.id}>
        {callHeading(call)}
        {call.inputHidden ? (
          hidden
        ) : call.request ? (
          <Flex vertical gap={spacing.xs}>
            {heading(t('shell.stepRun.request'))}
            <CodeBlock label={t('shell.stepRun.request')} value={`${call.request.method} ${call.request.url}`} />
            {heading(t('shell.stepRun.headers'))}
            <CodeBlock label={t('shell.stepRun.headers')} value={call.request.headers} />
            {heading(t('shell.stepRun.body'))}
            {call.request.body === null ? (
              muted(t('shell.stepRun.noBody'))
            ) : (
              <CodeBlock label={t('shell.stepRun.body')} value={call.request.body} />
            )}
          </Flex>
        ) : call.input === null ? (
          muted(t('shell.stepRun.notKept'))
        ) : (
          // A shown input is a list of its top-level properties, each number with its source digits, as the
          // process inspector shows it.
          <TermList
            terms={Object.entries(record(call.input)).map(([name, value]) => ({
              key: name,
              term: name,
              value: typeof value === 'object' && value !== null ? code(value) : words(value),
            }))}
          />
        )}
      </div>
    ))

  const response = (attempt: CallAttempt) =>
    attempt.response === undefined ? null : attempt.response === null ? (
      muted(t('shell.stepRun.noResponse'))
    ) : (
      <Flex vertical gap={spacing.xs}>
        <div>
          <StatusTag tone={attempt.response.status < 400 ? 'success' : 'danger'}>
            {String(attempt.response.status)}
          </StatusTag>
        </div>
        {heading(t('shell.stepRun.headers'))}
        <CodeBlock label={t('shell.stepRun.headers')} value={attempt.response.headers} />
        {heading(t('shell.stepRun.body'))}
        {attempt.response.body === null ? (
          muted(t('shell.stepRun.noBody'))
        ) : (
          <CodeBlock label={t('shell.stepRun.body')} value={attempt.response.body} />
        )}
        {attempt.response.truncated && muted(t('shell.stepRun.truncated'))}
      </Flex>
    )

  const mapping = (value: unknown, reading: boolean): Term[] =>
    Object.entries(record(value)).map(([name, expression]) => ({
      key: name,
      term: reading ? name : code(expression),
      value: reading ? code(expression) : name,
    }))

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
          <TermList
            terms={
              definition.set !== undefined
                ? Object.entries(record(definition.set)).map(([field, expression]) => ({
                    key: field,
                    term: field,
                    value: code(expression),
                  }))
                : Object.entries(record(definition.output)).map(([from, field]) => ({
                    key: from,
                    term: words(field),
                    value: code(from),
                  }))
            }
          />
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

  const definitionView = () => (
    <Flex vertical gap={spacing.md}>
      <TermList terms={definitionTerms()} />
      <JsonFold value={definition} />
    </Flex>
  )

  const definitionTerms = (): Term[] => {
    const terms: Term[] = []
    const add = (key: string, value: ReactNode) =>
      terms.push({ key, term: t(`shell.stepRun.definition.${key}`), value })
    if (Array.isArray(definition.branches)) {
      for (const branch of list(definition.branches)) {
        terms.push({
          key: `branch-${words(branch.name)}`,
          term: words(branch.name),
          value: (
            <Flex vertical align="flex-start">
              {code(branch.when)}
              {leadsTo(branch.next)}
            </Flex>
          ),
        })
      }
      add('otherwise', stepName(definition.otherwise))
    }
    if (definition.assignee !== undefined) {
      const assignee = record(definition.assignee)
      add(
        'assignee',
        typeof assignee.role === 'string'
          ? t('shell.stepRun.definition.role').replace('{role}', assignee.role)
          : code(assignee.user),
      )
    }
    if (definition.form !== undefined) add('form', words(definition.form))
    if (definition.dueIn !== undefined)
      add('dueIn', isoDurationText(definition.dueIn, locale) ?? words(definition.dueIn))
    if (Array.isArray(definition.outcomes)) {
      for (const outcome of list(definition.outcomes)) {
        terms.push({
          key: `outcome-${words(outcome.name)}`,
          term: outcomeLabel(step, words(outcome.name)),
          value: leadsTo(outcome.next),
        })
      }
    }
    const connector = record(definition.connector)
    if (typeof connector.name === 'string') {
      add('connector', connector.name)
      add('operation', words(connector.operation))
    } else if (operation !== null && operation.includes('/')) {
      add('operation', operation)
    }
    if (definition.endpoint !== undefined) {
      add('endpoint', code(`${definition.endpoint.method} ${definition.endpoint.path}`))
    }
    if (definition.set !== undefined) {
      terms.push(...mapping(definition.set, true).map((term) => ({ ...term, key: `set-${term.key}` })))
    }
    if (definition.input !== undefined) {
      add(
        'input',
        <TermList terms={mapping(definition.input, true).map((term) => ({ ...term, key: `input-${term.key}` }))} />,
      )
    }
    if (definition.output !== undefined) {
      add(
        'output',
        <TermList
          terms={Object.entries(record(definition.output)).map(([from, field]) => ({
            key: `output-${from}`,
            term: code(from),
            value: `→ ${words(field)}`,
          }))}
        />,
      )
    }
    const retry = record(definition.retry)
    if (retry.attempts !== undefined) {
      add(
        'retry',
        t('shell.stepRun.retry')
          .replace('{attempts}', String(retry.attempts))
          .replace('{backoff}', isoDurationText(retry.backoff, locale) ?? words(retry.backoff)),
      )
    }
    if (Array.isArray(definition.to)) {
      add(
        'to',
        <Flex vertical>
          {list(definition.to).map((to) => (
            <span key={JSON.stringify(to)}>
              {typeof to.role === 'string'
                ? t('shell.stepRun.definition.role').replace('{role}', to.role)
                : code(Object.values(to)[0])}
            </span>
          ))}
        </Flex>,
      )
    }
    if (Array.isArray(definition.channels)) {
      add('channels', definition.channels.map((channel) => t(`shell.stepRun.channel.${words(channel)}`)).join(', '))
    }
    if (definition.message !== undefined) {
      add('message', code(record(record(definition.message).subject).textKey))
    }
    if (typeof definition.process === 'string') add('process', definition.process)
    if (definition.subject !== undefined && typeof definition.process === 'string')
      add('subject', code(definition.subject))
    if (typeof definition.event === 'string') add('event', definition.event)
    if (definition.when !== undefined && definition.branches === undefined) add('when', code(definition.when))
    if (definition.wait !== undefined) add('wait', isoDurationText(definition.wait, locale) ?? words(definition.wait))
    if (definition.until !== undefined) add('until', code(definition.until))
    if (definition.timeout !== undefined) {
      const timeout = record(definition.timeout)
      add(
        'timeout',
        t('shell.stepRun.definition.after')
          .replace('{duration}', isoDurationText(timeout.after, locale) ?? words(timeout.after))
          .replace('{step}', stepLabel(words(timeout.next))),
      )
    }
    if (typeof definition.next === 'string') add('next', stepName(definition.next))
    if (graphStep?.type === 'end') add('end', t('shell.stepRun.definition.ends'))
    return terms
  }

  const body: Record<StepRunTab, () => ReactNode> = {
    overview: () => (chosen === undefined ? muted(t('shell.inspector.graph.notReached')) : overview(chosen)),
    input,
    output,
    attempts,
    error,
    definition: definitionView,
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
          <Tabs
            activeKey={current}
            onChange={(key) => onTab(key as StepRunTab)}
            items={shown.map((key) => ({ key, label: t(`shell.stepRun.tab.${key}`) }))}
            // Narrow gaps, so the tabs of a call fit a panel `sidePanel` wide.
            tabBarGutter={spacing.md}
            tabBarStyle={{ marginBottom: 0 }}
          />
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

/** The step's definition as JSON, folded until asked for. */
function JsonFold({ value }: { value: unknown }) {
  const t = useText()
  const [open, setOpen] = useState(false)
  return (
    <Flex vertical gap="small">
      <div>
        <AxisButton role="link" aria-expanded={open} onClick={() => setOpen(!open)}>
          {t(open ? 'shell.stepRun.hideJson' : 'shell.stepRun.showJson')}
        </AxisButton>
      </div>
      {open && <CodeBlock label={t('shell.stepRun.tab.definition')} value={value} />}
    </Flex>
  )
}
