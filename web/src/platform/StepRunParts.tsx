// The Input, Output and Attempts tabs of a step's run panel, see docs/reference/process-inspector-widget.md#instance-page.
import { Flex } from 'antd'
import type { ReactNode } from 'react'
import { AxisTable, type AxisColumn } from './AxisTable'
import { BodyView } from './BodyView'
import { CopyableId } from './CopyableId'
import { EventTime } from './EventTime'
import { EmptyState } from './EmptyState'
import { NotSet } from './EmptyValue'
import { fillText } from './fillText'
import { formatDuration } from './formatValue'
import {
  attemptOutcomeText,
  attemptOutcomeTone,
  problemText,
  type CallAttempt,
  type CallResponse,
  type InstanceDetail,
  type RunChange,
  type RunRecipient,
  type StepRun,
} from './inspection'
import { callOf, hasMembers, settled } from './runPanelParts'
import { PersonName } from './PersonName'
import { RecordLink } from './RecordLink'
import { StatusTag } from './StatusTag'
import { TermList, type Term } from './TermList'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { useEmptyStyle } from './useEmptyStyle'
import { JsonNumber, namedValues } from './valueRows'
import { ValueTable } from './ValueTable'
import { visuallyHidden } from './visuallyHidden'

/** What a pane needs to find its run's call and what the run made. */
interface RunPaneProps {
  instance: InstanceDetail
  runs: readonly StepRun[]
  run: StepRun
  locale: string
}

type Translate = (key: string) => string

/** The word of a kept value that came back `null`: Hidden where the reader may not see it, else Not set. */
function Kept({ hidden }: { hidden: boolean }) {
  const t = useText()
  const empty = useEmptyStyle()
  return hidden ? <span style={empty}>{t('shell.value.hidden')}</span> : <NotSet />
}

/** The pane's terms, or Not set where the run has none to show. */
function Pane({ terms }: { terms: Term[] }) {
  return terms.length === 0 ? <NotSet /> : <TermList terms={terms} />
}

/** A table of names and their texts, or nothing when there are none. */
function textTable(values: Record<string, string> | null | undefined): ReactNode {
  return hasMembers(values) ? <ValueTable value={values} /> : null
}

/**
 * The terms of an outside system's answer: Status as plain text, then Headers and Body. The Output tab shows the answer
 * that settled the call, and a row of the Attempts table shows its own.
 */
function answerTerms(t: Translate, response: CallResponse, prefix: string): Term[] {
  const headers = textTable(response.headers)
  return [
    { key: `${prefix}status`, term: t('shell.inspector.run.status'), value: String(response.status) },
    ...(headers === null ? [] : [{ key: `${prefix}headers`, term: t('shell.inspector.run.headers'), value: headers }]),
    ...(response.body === null || response.body === ''
      ? []
      : [{ key: `${prefix}body`, term: t('shell.inspector.run.body'), value: <BodyView body={response.body} /> }]),
  ]
}

/** The Input tab: a connector call's request, a package call's values or the values a decision read. */
export function RunInput({ instance, runs, run }: RunPaneProps) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const call = callOf(instance, runs, run)
  const terms: Term[] = []
  if (call?.kind === 'package') {
    terms.push({
      key: 'values',
      term: t('shell.inspector.run.values'),
      value:
        call.input === null || call.input === undefined ? (
          <Kept hidden={call.inputHidden} />
        ) : (
          <ValueTable value={call.input} />
        ),
    })
  } else if (call !== undefined) {
    const { request } = call
    if (request === null || request === undefined) {
      terms.push({
        key: 'request',
        term: t('shell.inspector.run.request'),
        value: <Kept hidden={call.inputHidden} />,
      })
    } else {
      const query = textTable(request.query)
      const headers = textTable(request.headers)
      terms.push(
        {
          key: 'request',
          term: t('shell.inspector.run.request'),
          value: (
            <Flex gap={spacing.xs} wrap={false} data-testid="inspector-run-request-line">
              <span>{request.method}</span>
              <span style={{ overflowWrap: 'anywhere' }}>{request.url}</span>
            </Flex>
          ),
        },
        ...(query === null ? [] : [{ key: 'query', term: t('shell.inspector.run.query'), value: query }]),
        ...(headers === null ? [] : [{ key: 'headers', term: t('shell.inspector.run.headers'), value: headers }]),
        ...(request.body === null || request.body === ''
          ? []
          : [{ key: 'body', term: t('shell.inspector.run.body'), value: <BodyView body={request.body} /> }]),
      )
    }
  } else {
    const read = run.entries.at(-1)?.read
    if (read && read.length > 0) {
      terms.push({
        key: 'valuesRead',
        term: t('shell.inspector.run.valuesRead'),
        value: <ValueTable value={namedValues(read)} />,
      })
    }
  }
  return <Pane terms={terms} />
}

/** A value of a change as text: a number's source digits, a string, else its JSON. A missing value reads Not set. */
function valueText(t: Translate, value: unknown): string {
  if (value === null || value === undefined) {
    return t('shell.value.notSet')
  }
  if (value instanceof JsonNumber) {
    return value.source
  }
  return typeof value === 'string' ? value : JSON.stringify(value)
}

/** The tint of a state colour, which the design tokens give as a seed, as a fill under a line of text. */
function tint(seed: string): string {
  return `color-mix(in srgb, ${seed} 16%, transparent)`
}

/**
 * A changed field as a diff: the value before on a "−" line on the `danger` tint and the value after on a "+" line on the
 * `success` tint. Both lines are hidden from a screen reader, which reads "Changed from X to Y" instead.
 */
function ChangeDiff({ change }: { change: RunChange }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const before = valueText(t, change.before)
  const after = valueText(t, change.after)
  const line = { padding: `0 ${spacing.xs}px`, overflowWrap: 'anywhere' as const }
  return (
    <div>
      <span style={visuallyHidden}>{fillText(t('shell.inspector.run.changedFrom'), { before, after })}</span>
      <div aria-hidden style={{ ...line, background: tint(colour.danger) }}>{`− ${before}`}</div>
      <div aria-hidden style={{ ...line, background: tint(colour.success) }}>{`+ ${after}`}</div>
    </div>
  )
}

/** One recipient of a notification: a user by name, a role by its name, an address, or Hidden. */
function Recipient({ recipient }: { recipient: RunRecipient }) {
  if (recipient.hidden || recipient.name === null) {
    return <Kept hidden />
  }
  return recipient.kind === 'user' && recipient.id !== null ? (
    <PersonName id={recipient.id} name={recipient.name} />
  ) : (
    <>{recipient.name}</>
  )
}

/** The words of a notification's channel, or the channel as stored for one this build does not know. */
function channelText(t: Translate, channel: string): string {
  if (channel === 'inApp') return t('shell.studio.process.channelInApp')
  return channel === 'email' ? t('shell.studio.process.channelEmail') : channel
}

/**
 * The Output tab, each part only when the run made it: the answer of the call, a package call's returned values, the
 * changed fields, the event it raised, the people it notified and the record it created.
 */
export function RunOutput({ instance, runs, run, locale }: RunPaneProps) {
  const t = useText()
  const call = callOf(instance, runs, run)
  const made = run.entries.at(-1)?.made
  const terms: Term[] = []

  const answer = call && settled(call) ? call.attempts.findLast((attempt) => attempt.kind === 'attempt') : undefined
  if (answer?.response) {
    terms.push(...answerTerms(t, answer.response, 'answer-'))
  }
  if (call && hasMembers(call.returned)) {
    terms.push({
      key: 'returned',
      term: t('shell.inspector.run.returned'),
      value: <ValueTable value={call.returned} />,
    })
  }
  if (made?.changes && made.changes.length > 0) {
    for (const change of made.changes) {
      terms.push({
        key: `change-${change.field}`,
        term: change.field,
        value: change.hidden ? <Kept hidden /> : <ChangeDiff change={change} />,
      })
    }
  }
  if (made?.event) {
    terms.push({ key: 'event', term: t('shell.inspector.run.eventRaised'), value: made.event.name })
    if (made.event.fields.length > 0) {
      terms.push({
        key: 'eventFields',
        term: t('shell.inspector.run.eventFields'),
        value: <ValueTable value={namedValues(made.event.fields)} />,
      })
    }
  }
  if (made?.notified) {
    const { to, channels, subject } = made.notified
    terms.push(
      {
        key: 'notified',
        term: t('shell.inspector.run.notified'),
        value: (
          <Flex vertical>
            {to.map((recipient, index) => (
              <span key={index}>
                <Recipient recipient={recipient} />
              </span>
            ))}
          </Flex>
        ),
      },
      {
        key: 'channels',
        term: t('shell.inspector.run.channels'),
        value:
          channels.length === 0 ? (
            <NotSet />
          ) : (
            new Intl.ListFormat(locale, { type: 'conjunction' }).format(channels.map((c) => channelText(t, c)))
          ),
      },
      { key: 'subject', term: t('shell.inspector.run.subject'), value: t(subject) },
    )
  }
  if (made?.created) {
    terms.push({
      key: 'created',
      term: t('shell.inspector.run.created'),
      value: <RecordLink entity={made.created.entity} id={made.created.id} label={t('shell.inspector.open')} />,
    })
  }
  if (terms.length === 0 && call?.inputHidden) {
    return <Kept hidden />
  }
  return <Pane terms={terms} />
}

/** The details of an attempt's row: Started, Trace ID, the error and the answer. */
function attemptDetails(t: Translate, locale: string, attempt: CallAttempt): ReactNode {
  const error = problemText(t, locale, attempt.problem, attempt.error)
  const terms: Term[] = [
    { key: 'startedAt', term: t('shell.inspector.startedAt'), value: <EventTime value={attempt.startedAt} /> },
    {
      key: 'traceId',
      term: t('shell.inspector.traceId'),
      value: attempt.traceId === null ? <NotSet /> : <CopyableId id={attempt.traceId} />,
    },
    ...(error === null ? [] : [{ key: 'error', term: t('shell.inspector.error'), value: error }]),
    ...(attempt.response ? answerTerms(t, attempt.response, 'answer-') : []),
  ]
  return <TermList terms={terms} />
}

/**
 * The Attempts tab: the call's attempts and reconciliation lookups as a compact table, newest first. A row folds open to
 * its details. A call with no attempts says so in place of the table.
 */
export function RunAttempts({ instance, runs, run, locale }: RunPaneProps) {
  const t = useText()
  const call = callOf(instance, runs, run)
  if (call === undefined) {
    return null
  }
  if (call.attempts.length === 0) {
    return <EmptyState text={t('shell.inspector.noAttempts')} />
  }
  const columns: AxisColumn<CallAttempt>[] = [
    { key: 'number', title: '#', dataIndex: 'number', kind: 'fixed' },
    {
      key: 'result',
      title: t('shell.inspector.run.result'),
      kind: 'fixed',
      render: (_value, attempt) => (
        <StatusTag tone={attemptOutcomeTone(attempt.outcome)}>{attemptOutcomeText(t, attempt.outcome)}</StatusTag>
      ),
    },
    { key: 'http', title: t('shell.inspector.run.http'), dataIndex: 'httpStatus', kind: 'fixed' },
    {
      key: 'duration',
      title: t('shell.inspector.duration'),
      kind: 'fixed',
      render: (_value, attempt) =>
        formatDuration(Date.parse(attempt.finishedAt) - Date.parse(attempt.startedAt), locale, t),
    },
  ]
  return (
    <AxisTable<CallAttempt>
      compact
      testId="inspector-run-attempts"
      columns={columns}
      rowKey={(attempt) => `${attempt.kind}|${attempt.number}|${attempt.startedAt}`}
      dataSource={[...call.attempts].reverse()}
      details={(attempt) => attemptDetails(t, locale, attempt)}
    />
  )
}
