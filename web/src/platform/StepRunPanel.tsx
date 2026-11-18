import { Flex, Select, Tabs, Typography } from 'antd'
import { type ReactNode } from 'react'
import { Link } from 'react-router'
import { AxisAlert } from './AxisAlert'
import { AxisButton } from './AxisButton'
import { AxisTable, type AxisColumn } from './AxisTable'
import { BodyView, ValueTable } from './ValueTable'
import { jsonRows, textRows } from './valueRows'
import { EmptyDash, NotSet } from './EmptyValue'
import { EventTime } from './EventTime'
import { formatValue } from './formatValue'
import { inspectorPath } from './inspectorPages'
import type { CallAttempt, InstanceCall, InstanceDetail, RunOutput, StepDefinition } from './inspection'
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

const record = (value: unknown): Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : {}

/** Whether a run made or changed anything its Output shows. */
const hasOutput = (made: RunOutput | undefined) =>
  made !== undefined &&
  ((made.changes?.length ?? 0) > 0 ||
    made.event !== undefined ||
    made.notified !== undefined ||
    made.created !== undefined)

/** The tone of a sub-process's state, as an instance's own state takes. */
const childTones: Record<string, StatusTone> = {
  completed: 'success',
  failed: 'danger',
  waiting: 'neutral',
  running: 'neutral',
  cancelled: 'muted',
}

/** The tone of an attempt's outcome: it ended well, it will be tried again, it failed, or nobody knows yet. */
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
 * beside its diagram. Its title carries the run's state. A step run more than once chooses its run on top. Its tabs
 * show what the step needs, and only those: Overview, the run's facts; Input, what a call sent or a decision read;
 * Output, what the run made or changed; Attempts, each attempt of a call; and Error, why a failed run failed. A value the reader may not read is hidden as the
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
  stepLabel: (name: string) => string
  outcomeLabel: (step: string, name: string) => string
  inspectors: ReadonlyMap<string, string> | undefined
  sitePath: string
  locale: string
}) {
  const t = useText()
  const now = useMinuteClock()
  const { spacing } = useDesignTokens()
  const { typeText, stateWords, targetOf, assigneeName, completedName } = useRunWords({
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
  const made = chosen?.made
  const read = chosen?.read
  const failed = chosen !== undefined && (chosen.error !== null || calls.some((call) => call.state === 'failed'))

  const shown: StepRunTab[] = ['overview']
  if (calls.length > 0 || (read?.length ?? 0) > 0) shown.push('input')
  if (calls.length > 0 || hasOutput(made)) shown.push('output')
  if (calls.some((call) => call.attempts.length > 0)) shown.push('attempts')
  if (failed) shown.push('error')
  const current = shown.includes(tab) ? tab : 'overview'

  const operationText =
    operation === null
      ? null
      : operation.includes('/')
        ? t('shell.stepRun.operation.package')
        : t(`shell.stepRun.operation.${operation}`)
  // Proposed, see D155 and the review on #3650: a step on a branch of a block names its branch.
  const branch = instance.graph.steps
    .find((candidate) => candidate.name === graphStep?.inBlock?.block)
    ?.parallel?.branches.find((candidate) => candidate.name === graphStep?.inBlock?.branch)
  const branchText = branch === undefined ? null : branch.labelKey ? t(branch.labelKey) : branch.name
  const kind = [typeText(graphStep?.type ?? ''), operationText, branchText].filter(Boolean).join(' · ')
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
  const notSet = <NotSet />
  // A time set ahead, such as a due time, is data, not an event that happened, so it keeps the system's date and time
  // alone and never takes a time ago or ahead, as `EventTime` says.
  const dueTime = (at: string) => formatValue({ name: 'due', type: 'date-time' }, at, {}, { locale, text: t }) as string
  // Proposed, see the review on #3650: a trace ID shows its first 8 characters and copies whole, as the instance's ID.
  const traceId = (id: string | null) =>
    id === null ? (
      notSet
    ) : (
      <Typography.Text copyable={{ text: id }} style={{ fontFamily: 'monospace' }}>
        {id.slice(0, 8)}
      </Typography.Text>
    )
  // Proposed, see the review on #3650: what a wait's run waits for, and until when, whether it still waits or not: an
  // event, and when the wait gives up and where it goes then; or a timer, and when it goes on. The engine keeps the time as the run's due time;
  // the prototype works it out from the step.
  const waitTerms = (shown: StepRun): Term[] => {
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
      terms.push({
        key: 'waitingFor',
        term: t('shell.stepRun.waitingFor'),
        // Proposed: an event is named by its label, as a step is, see the review on #3650.
        value: typeof definition.eventLabelKey === 'string' ? t(definition.eventLabelKey) : definition.event,
      })
      const timeout = record(definition.timeout)
      const due = after(timeout.after)
      if (due !== null) {
        // Where the run goes when it gives up is a fact of its own, a step that opens in the panel as Went to's does.
        terms.push(
          { key: 'givesUp', term: t('shell.stepRun.givesUp'), value: dueTime(due), width: 6 },
          { key: 'thenGoesTo', term: t('shell.stepRun.thenGoesTo'), value: stepName(timeout.next), width: 6 },
        )
      }
    }
    const wake = after(definition.wait) ?? null
    if (wake !== null) {
      terms.push({ key: 'goesOn', term: t('shell.stepRun.goesOn'), value: dueTime(wake) })
    }
    return terms
  }

  // Proposed, see the review on #3650: Overview lists the run's facts as terms, each label above its value and each on a
  // row of its own, as every page that shows one thing lists its facts: when it started and how long it took, then what it did by what it holds, a
  // task's assignee, due time, who completed it and with which outcome, a call's target and attempts, a sub-process's
  // instance, what a wait waits for; then where it came from and went to and why, and its trace ID. Its state is the tag
  // beside the panel's title, and its end is its start and its duration, so neither is a line of its own.
  const overview = (shown: StepRun) => {
    const index = runs.indexOf(shown)
    // Proposed, see D155 and the review on #3650: a run on a branch came from the run before it on that branch, or
    // from the last step before the block, and goes on along that branch; a run outside a block skips the branches.
    const along = (candidate: StepRun) => candidate.path === shown.path || candidate.path === undefined
    const before = runs.slice(0, index).findLast(along)
    const after = runs.slice(index + 1).find(along)
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
      // A fact every run has always shows, Not set while it is empty; only what does not apply to the run is left out,
      // see the rule on #3650. Facts that belong together share a row: when it started and ended, when a wait gives up
      // and where it goes then, where the run came from and where it went; every other fact has its own.
      {
        key: 'started',
        term: t('shell.stepRun.started'),
        value: <EventTime at={shown.startedAt} locale={locale} />,
        width: 6,
      },
      {
        key: 'ended',
        term: t('shell.stepRun.ended'),
        value: shown.finishedAt === null ? notSet : <EventTime at={shown.finishedAt} locale={locale} />,
        width: 6,
      },
      {
        key: 'duration',
        term: t('shell.stepRun.duration'),
        value: durationText(shown.startedAt, shown.finishedAt ?? now.toISOString(), locale),
      },
      ...shown.tasks.flatMap((task): Term[] => [
        { key: `assignee-${task.id}`, term: t('shell.stepRun.assignedTo'), value: assigneeName(task) },
        {
          key: `due-${task.id}`,
          term: t('shell.stepRun.due'),
          value:
            task.dueAt == null ? (
              notSet
            ) : (
              <Flex align="center" gap="small" wrap>
                {dueTime(task.dueAt)}
                {task.completedAt === null && task.dueAt < now.toISOString() && (
                  <StatusTag tone="attention">{t('shell.stepRun.overdue')}</StatusTag>
                )}
              </Flex>
            ),
        },
        {
          key: `completedBy-${task.id}`,
          term: t('shell.stepRun.completedBy'),
          value: task.completedBy === null ? notSet : completedName(task),
        },
        {
          key: `outcome-${task.id}`,
          term: t('shell.stepRun.outcomeChosen'),
          value: task.outcome === null ? notSet : outcomeLabel(step, task.outcome),
        },
      ]),
      ...shown.calls.flatMap((call): Term[] => [
        {
          key: `called-${call.id}`,
          term: t('shell.stepRun.called'),
          // The state of a step's one call is the step's; a step with several calls says each one's.
          value:
            shown.calls.length > 1 ? (
              <Flex align="center" gap="small" wrap>
                {targetOf(call)}
                {stateWords(call.state === 'succeeded' ? 'done' : call.state === 'failed' ? 'failed' : 'running')}
              </Flex>
            ) : (
              targetOf(call)
            ),
        },
        {
          key: `attempts-${call.id}`,
          term: t('shell.stepRun.attemptCount'),
          value: String(call.attempts.filter((attempt) => attempt.kind === 'attempt').length),
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
        key: 'cameFrom',
        term: t('shell.stepRun.cameFrom'),
        value: before === undefined ? muted(t('shell.stepRun.start')) : runLink(before),
        width: 6,
      },
      // The outcome or branch that chose the next step follows it on its line.
      {
        key: 'wentTo',
        term: t('shell.stepRun.wentTo'),
        width: 6,
        value:
          shown.finishedAt === null ? (
            notSet
          ) : after === undefined ? (
            muted(t('shell.stepRun.ended.instance'))
          ) : (
            <span>
              {runLink(after)}
              {reason !== null && muted(` — ${reason}`)}
            </span>
          ),
      },
      { key: 'trace', term: t('shell.stepRun.traceId'), value: traceId(shown.traceId) },
    ]
    return <TermList terms={terms} />
  }

  const hidden = muted(t('shell.stepRun.hidden'))
  // A step with several calls names each call's target first.
  const calledTerm = (call: InstanceCall): Term[] =>
    calls.length > 1 ? [{ key: 'called', term: t('shell.stepRun.called'), value: targetOf(call) }] : []
  const bodyValue = (body: string | null) =>
    body === null ? muted(t('shell.stepRun.noBody')) : <BodyView label={t('shell.stepRun.body')} text={body} />

  // Proposed, see the review on #3650: Input and Output are terms, as the instance's Data tab is, each name above its
  // value, since a value is a table: the request on one line, its method then its address `spacing-xs` apart, as the
  // developer tools show it, its query parameters, headers and body; a package call's input; the values a decision's
  // conditions read.
  const input = () => (
    <Flex vertical gap={spacing.lg}>
      {calls.map((call) => {
        const terms: Term[] = [...calledTerm(call)]
        if (call.inputHidden) {
          terms.push({ key: 'input', term: t('shell.stepRun.tab.input'), value: hidden })
        } else if (call.request) {
          const { method, url, headers, body } = call.request
          const query = url.includes('?') ? url.slice(url.indexOf('?') + 1) : ''
          const queryRows = textRows(Object.fromEntries(new URLSearchParams(query)))
          terms.push({
            key: 'request',
            term: t('shell.stepRun.request'),
            value: (
              <span style={{ overflowWrap: 'anywhere' }}>
                {/* The method stands `spacing-xs` apart from its address, past the space that keeps them two words. */}
                <Typography.Text strong style={{ marginInlineEnd: spacing.xs }}>
                  {method}
                </Typography.Text>{' '}
                {url.split('?', 1)[0]}
              </span>
            ),
          })
          if (queryRows.length > 0) {
            terms.push({
              key: 'query',
              term: t('shell.stepRun.query'),
              value: <ValueTable label={t('shell.stepRun.query')} rows={queryRows} />,
            })
          }
          terms.push(
            {
              key: 'headers',
              term: t('shell.stepRun.headers'),
              value: <ValueTable label={t('shell.stepRun.headers')} rows={textRows(headers)} />,
            },
            { key: 'body', term: t('shell.stepRun.body'), value: bodyValue(body) },
          )
        } else {
          terms.push({
            key: 'input',
            term: t('shell.stepRun.tab.input'),
            // A shown input lists its properties, each number with its source digits, as the process inspector shows it.
            value:
              call.input === null ? (
                muted(t('shell.stepRun.notKept'))
              ) : (
                <ValueTable label={t('shell.stepRun.tab.input')} rows={jsonRows(call.input)} />
              ),
          })
        }
        return <TermList key={call.id} terms={terms} />
      })}
      {read !== undefined && read.length > 0 && (
        <TermList
          terms={[
            {
              key: 'read',
              term: t('shell.stepRun.read'),
              value: (
                <ValueTable
                  label={t('shell.stepRun.read')}
                  rows={read.map((value) => ({
                    key: value.field,
                    name: value.labelKey ? t(value.labelKey) : value.field,
                    value:
                      value.hidden === true
                        ? { kind: 'change', before: null, after: null, hidden: true }
                        : value.value === null
                          ? { kind: 'null' }
                          : { kind: 'text', text: value.value },
                  }))}
                />
              ),
            },
          ]}
        />
      )}
    </Flex>
  )

  /** The status, headers and body of an attempt's answer, or why there is none. */
  const responseTerms = (attempt: CallAttempt, from: 'output' | 'attempt'): Term[] =>
    attempt.response === undefined
      ? []
      : attempt.response === null
        ? [{ key: 'response', term: t('shell.stepRun.response'), value: muted(t('shell.stepRun.noResponse')) }]
        : [
            // An attempt's row already says its status.
            ...(from === 'attempt'
              ? []
              : [
                  {
                    key: 'status',
                    term: t('shell.stepRun.status'),
                    value: (
                      <StatusTag tone={attempt.response.status < 400 ? 'success' : 'danger'}>
                        {String(attempt.response.status)}
                      </StatusTag>
                    ),
                  },
                ]),
            {
              key: 'headers',
              term: t('shell.stepRun.headers'),
              value: <ValueTable label={t('shell.stepRun.headers')} rows={textRows(attempt.response.headers)} />,
            },
            {
              key: 'body',
              term: t('shell.stepRun.body'),
              value: (
                <Flex vertical gap={spacing.xs}>
                  {bodyValue(attempt.response.body)}
                  {attempt.response.truncated && muted(t('shell.stepRun.truncated'))}
                </Flex>
              ),
            },
          ]

  // Proposed, see the review on #3650: Output is what the run made or changed, with its values: a connector's answer,
  // the values a package gave back, the fields it or its task's form changed on the record before and after, an event
  // it raised, the people it notified and a record it created. A step that makes nothing has no Output.
  const output = () => {
    const terms: Term[] = calls.flatMap((call): Term[] => {
      if (call.kind !== 'connector') {
        return [
          ...calledTerm(call),
          {
            key: `returned-${call.id}`,
            term: t('shell.stepRun.returned'),
            value:
              call.returned === null || call.returned === undefined ? (
                call.inputHidden ? (
                  hidden
                ) : (
                  muted(t('shell.stepRun.nothingReturned'))
                )
              ) : (
                <ValueTable label={t('shell.stepRun.returned')} rows={jsonRows(call.returned)} />
              ),
          },
        ]
      }
      // A call's Output is the answer of the attempt that settled it, its last; an earlier one is in Attempts.
      const settled = call.attempts.filter((attempt) => attempt.kind === 'attempt').at(-1)
      const answered = settled?.response == null ? undefined : settled
      return [
        ...calledTerm(call),
        ...(call.inputHidden
          ? [{ key: `response-${call.id}`, term: t('shell.stepRun.response'), value: hidden }]
          : answered === undefined
            ? [
                {
                  key: `response-${call.id}`,
                  term: t('shell.stepRun.response'),
                  value: muted(t('shell.stepRun.noResponse')),
                },
              ]
            : responseTerms(answered, 'output').map((term) => ({ ...term, key: `${term.key}-${call.id}` }))),
      ]
    })
    if (made?.changes !== undefined && made.changes.length > 0) {
      terms.push({
        key: 'changes',
        term: t('shell.stepRun.changes'),
        // A step changes only the instance's record, which the page names, so the changes need no record of their own.
        value: (
          <ValueTable
            label={t('shell.stepRun.changes')}
            rows={made.changes.map((change) => ({
              key: change.field,
              name: change.labelKey ? t(change.labelKey) : change.field,
              value: { kind: 'change', before: change.before, after: change.after, hidden: change.hidden === true },
            }))}
          />
        ),
      })
    }
    if (made?.event !== undefined) {
      // The event and its fields are facts of their own, as a notification's are.
      terms.push(
        {
          key: 'event',
          term: t('shell.stepRun.eventRaised'),
          value: made.event.labelKey ? t(made.event.labelKey) : made.event.name,
        },
        {
          key: 'eventFields',
          term: t('shell.stepRun.eventFields'),
          value: <ValueTable label={t('shell.stepRun.eventFields')} rows={textRows(made.event.fields)} />,
        },
      )
    }
    // The people a step notified, by which channels and with which subject, are facts of their own, as every other.
    if (made?.notified !== undefined) {
      terms.push(
        { key: 'notified', term: t('shell.stepRun.notified'), value: made.notified.to.join(', ') },
        {
          key: 'channels',
          term: t('shell.stepRun.channels'),
          value: made.notified.channels.map((channel) => t(`shell.stepRun.channel.${channel}`)).join(', '),
        },
        { key: 'subject', term: t('shell.stepRun.subject'), value: made.notified.subject },
      )
    }
    if (made?.created !== undefined) {
      terms.push({
        key: 'created',
        term: t('shell.stepRun.created'),
        value: (
          <RecordLink entity={made.created.entity} id={made.created.id}>
            {made.created.label ?? made.created.id}
          </RecordLink>
        ),
      })
    }
    return <TermList terms={terms} />
  }

  // Proposed, see the review on #3650: the attempts of a call are one compact table, each row its number, result, HTTP
  // status and duration, and a row folds open to when it started, its error, trace ID and answer, as Stripe and GitHub
  // list a webhook's deliveries. The tab shows the attempts made, never the step's retry policy, which is the author's
  // setting, as the agreed rule of no Definition tab says.
  const attempts = () => {
    return (
      <Flex vertical gap={spacing.sm}>
        {calls.map((call) => (
          <Flex key={call.id} vertical gap={spacing.xs}>
            {calls.length > 1 && <Typography.Text strong>{targetOf(call)}</Typography.Text>}
            <AttemptTable
              // Records in time are newest first, as the timeline lists a call's attempts, see
              // docs/reference/actions-and-controls.md.
              attempts={[...call.attempts].reverse()}
              locale={locale}
              details={(attempt) => (
                <TermList
                  terms={[
                    {
                      key: 'started',
                      term: t('shell.stepRun.started'),
                      value: <EventTime at={attempt.startedAt} locale={locale} />,
                      width: 6,
                    },
                    { key: 'trace', term: t('shell.stepRun.traceId'), value: traceId(attempt.traceId), width: 6 },
                    ...(problemText(t, attempt.problem, attempt.error) === null
                      ? []
                      : [
                          {
                            key: 'error',
                            term: t('shell.stepRun.error'),
                            value: problemText(t, attempt.problem, attempt.error),
                          },
                        ]),
                    ...responseTerms(attempt, 'attempt'),
                  ]}
                />
              )}
            />
          </Flex>
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
      status={
        chosen === undefined
          ? undefined
          : stateWords(
              chosen.state,
              chosen.tasks.some((task) => task.completedAt === null),
            )
      }
      kind={kind}
      closeKey="shell.graph.closeDetails"
      onClose={onClose}
      // A run with one part and one run, such as a wait's, has nothing above its facts, and so no room kept for it.
      top={
        (own.length > 1 && chosen !== undefined) || shown.length > 1 ? (
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
        ) : undefined
      }
    >
      {body[current]()}
    </SidePanel>
  )
}

/** The attempts of one call as a compact table, each row folding open to its `details`. */
function AttemptTable({
  attempts,
  locale,
  details,
}: {
  attempts: readonly CallAttempt[]
  locale: string
  details: (attempt: CallAttempt) => ReactNode
}) {
  const t = useText()
  const columns: AxisColumn<CallAttempt>[] = [
    {
      key: 'number',
      kind: 'fixed',
      title: t('shell.stepRun.attemptColumn'),
      // An attempt is its number; a lookup of a call whose answer was lost says it is one.
      render: (_value, attempt) =>
        attempt.kind === 'attempt'
          ? String(attempt.number)
          : t(`shell.stepRun.${attempt.kind}`).replace('{number}', String(attempt.number)),
    },
    {
      key: 'outcome',
      kind: 'fixed',
      title: t('shell.stepRun.result'),
      render: (_value, attempt) => (
        <StatusTag tone={attemptTones[attempt.outcome] ?? 'neutral'}>
          {t(`shell.stepRun.outcome.${attempt.outcome}`)}
        </StatusTag>
      ),
    },
    {
      key: 'status',
      kind: 'fixed',
      title: t('shell.stepRun.httpStatus'),
      render: (_value, attempt) => (attempt.httpStatus === null ? null : String(attempt.httpStatus)),
    },
    {
      key: 'took',
      kind: 'fixed',
      title: t('shell.stepRun.duration'),
      render: (_value, attempt) => durationText(attempt.startedAt, attempt.finishedAt, locale),
    },
  ]
  return (
    <AxisTable<CallAttempt>
      rowKey={(attempt) => `${attempt.kind}-${attempt.number}`}
      dataSource={attempts}
      columns={columns}
      emptyText={t('shell.valueTable.none')}
      compact
      details={details}
    />
  )
}
