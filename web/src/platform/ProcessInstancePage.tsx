import { Alert, Button, Descriptions, Empty, Flex, Table, Tag, Typography, type TableColumnsType } from 'antd'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { formatValue } from './formatValue'
import {
  fetchInstance,
  forbidden,
  instanceCommands,
  retryWarns,
  sendCommand,
  stateText,
  timerDueAt,
  type CallAttempt,
  type InstanceCommand,
  type InstanceCall,
  type InstanceDetail,
  type InstanceTask,
  type PathEntry,
  type SubProcess,
} from './inspection'
import { inspectorPath, useInspectorPages } from './inspectorPages'
import { ReasonDialog } from './ReasonDialog'
import { ResolveDialog } from './ResolveDialog'
import type { WidgetMetadata } from './site'
import { TextProvider } from './TextProvider'
import { useText, useTextCatalogs } from './texts'
import { useDesignTokens } from './themeMode'
import { useReleaseTexts } from './useReleaseTexts'

interface ProcessInstancePageProps {
  sitePath: string
  /** The names of the site's navigation pages, in navigation order, for the links to other inspector pages. */
  pages: readonly string[]
  /** The process inspector widget of the page the instance's address is under. */
  widget: WidgetMetadata
  instanceId: string
  /** The UI locale, for formatting the times. */
  locale: string
  /** The site's fallback locale, for the texts of the instance's release. */
  fallbackLocale: string
}

/** The instance read, `null` when the process has none with the id, or why it could not be shown. */
type InstanceState = InstanceDetail | null | typeof forbidden | 'failed'

const dateTime = (name: string) => ({ name, type: 'date-time' }) as const

/**
 * Shows one instance under the page of its process inspector: its state and step, the steps of
 * the release it is pinned to with the path marked, the history, the tasks, the calls with their
 * attempts and the sub-processes. An instance that waits at a timer shows when it is due. The parent
 * and each sub-process link to their instance on the first navigation page whose process inspector
 * names their process, and show without a link when there is none. A step the path holds is marked as taken, and the step the instance is at is marked as
 * current. A call whose input is hidden shows the fixed "hidden" text and no value.
 *
 * Step labels come from the texts of the instance's release, then the site texts and the platform
 * texts. A step without a label shows its name. If the release texts fail to load, the page shows
 * its load error.
 */
export function ProcessInstancePage({
  sitePath,
  pages,
  widget,
  instanceId,
  locale,
  fallbackLocale,
}: ProcessInstancePageProps) {
  const t = useText()
  // The server sets the path on every process inspector.
  const instancesPath = widget.instancesPath!
  const [instance, setInstance] = useState<InstanceState>()
  const outer = useTextCatalogs()
  const releaseIds = useMemo(() => (isInstanceDetail(instance) ? [instance.releaseId] : []), [instance])
  const releaseTexts = useReleaseTexts(sitePath, releaseIds, locale, fallbackLocale)
  const releaseCatalog = isInstanceDetail(instance) ? releaseTexts.catalogs.get(instance.releaseId) : undefined
  const catalogs = useMemo(
    () => (releaseCatalog ? [releaseCatalog, ...outer.catalogs] : outer.catalogs),
    [releaseCatalog, outer.catalogs],
  )

  const hasRelatives = isInstanceDetail(instance) && (instance.parent !== null || instance.subProcesses.length > 0)
  const inspectors = useInspectorPages(sitePath, pages, hasRelatives)

  useEffect(() => {
    const controller = new AbortController()
    fetchInstance(instancesPath, instanceId, controller.signal)
      .then(setInstance)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading the instance '${instanceId}' failed`, error)
          setInstance('failed')
        }
      })
    return () => controller.abort()
  }, [instancesPath, instanceId])

  // After a command, the instance is read again. The page keeps showing the old one until it arrives.
  const reload = useCallback(() => {
    fetchInstance(instancesPath, instanceId)
      .then(setInstance)
      .catch((error: unknown) => {
        console.warn(`Loading the instance '${instanceId}' failed`, error)
        setInstance('failed')
      })
  }, [instancesPath, instanceId])

  if (instance === undefined) {
    return null
  }
  if (instance === null) {
    return <Alert data-testid="inspector-not-found" type="warning" title={t('shell.inspector.notFound')} />
  }
  if (instance === forbidden) {
    return <Alert data-testid="inspector-forbidden" type="warning" title={t('shell.inspector.forbidden')} />
  }
  if (instance === 'failed' || releaseTexts.failed) {
    return <Alert data-testid="inspector-error" type="error" title={t('shell.inspector.loadFailed')} />
  }
  // The previous release texts stay on screen while a new locale loads.
  if (!releaseCatalog) {
    return null
  }

  return (
    <TextProvider catalogs={catalogs} development={outer.development}>
      <InstanceBody
        instance={instance}
        instancesPath={instancesPath}
        sitePath={sitePath}
        inspectors={inspectors}
        locale={locale}
        onChanged={reload}
      />
    </TextProvider>
  )
}

function isInstanceDetail(instance: InstanceState | undefined): instance is InstanceDetail {
  return typeof instance === 'object' && instance !== null
}

/** The top-level properties of a call's input as text. Objects are JSON, and numbers keep their digits. */
function inputEntries(input: unknown): [string, string][] {
  if (typeof input !== 'object' || input === null || Array.isArray(input)) {
    return []
  }
  return Object.entries(input).map(([key, value]) => [
    key,
    typeof value === 'object' && value !== null ? JSON.stringify(value) : String(value),
  ])
}

interface InstanceBodyProps {
  instance: InstanceDetail
  instancesPath: string
  sitePath: string
  /** The inspector page of each process, once loaded. */
  inspectors: ReadonlyMap<string, string> | undefined
  locale: string
  /** Called after a command was accepted, so the page reads the instance again. */
  onChanged: () => void
}

/** The sections of an instance. It reads the release texts. */
function InstanceBody({ instance, instancesPath, sitePath, inspectors, locale, onChanged }: InstanceBodyProps) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const rowStyle = { paddingBlock: spacing.sm, borderBlockEnd: `1px solid ${colour.divider}` }
  const stepsByName = useMemo(
    () => new Map(instance.graph.steps.map((step) => [step.name, step])),
    [instance.graph.steps],
  )
  const stepLabel = (name: string) => {
    const labelKey = stepsByName.get(name)?.labelKey
    return labelKey ? t(labelKey) : name
  }
  const time = (name: string, value: string | null) => formatValue(dateTime(name), value, {}, { locale, text: t })

  const due = timerDueAt(instance)
  const parentPage = instance.parent === null ? undefined : inspectors?.get(instance.parent.process)

  const historyColumns: TableColumnsType<PathEntry> = [
    { key: 'step', title: t('shell.inspector.step'), render: (_v: unknown, row) => stepLabel(row.step) },
    {
      key: 'startedAt',
      title: t('shell.inspector.startedAt'),
      render: (_v: unknown, row) => time('startedAt', row.startedAt),
    },
    {
      key: 'finishedAt',
      title: t('shell.inspector.finishedAt'),
      render: (_v: unknown, row) => time('finishedAt', row.finishedAt),
    },
    { key: 'decision', title: t('shell.inspector.decision'), render: (_v: unknown, row) => row.decision ?? '' },
    { key: 'error', title: t('shell.inspector.error'), render: (_v: unknown, row) => row.error ?? '' },
  ]
  const taskColumns: TableColumnsType<InstanceTask> = [
    { key: 'step', title: t('shell.inspector.step'), render: (_v: unknown, row) => stepLabel(row.step) },
    {
      key: 'assignee',
      title: t('shell.inspector.assignee'),
      render: (_v: unknown, row) => `${row.assigneeKind}: ${row.assignee}`,
    },
    { key: 'state', title: t('shell.inspector.state'), render: (_v: unknown, row) => row.state },
    { key: 'outcome', title: t('shell.inspector.outcome'), render: (_v: unknown, row) => row.outcome ?? '' },
    {
      key: 'completedBy',
      title: t('shell.inspector.completedBy'),
      render: (_v: unknown, row) => row.completedBy ?? '',
    },
    {
      key: 'createdAt',
      title: t('shell.inspector.createdAt'),
      render: (_v: unknown, row) => time('createdAt', row.createdAt),
    },
    {
      key: 'completedAt',
      title: t('shell.inspector.completedAt'),
      render: (_v: unknown, row) => time('completedAt', row.completedAt),
    },
  ]
  const attemptColumns: TableColumnsType<CallAttempt> = [
    { key: 'kind', title: t('shell.inspector.kind'), render: (_v: unknown, row) => row.kind },
    { key: 'number', title: t('shell.inspector.number'), render: (_v: unknown, row) => row.number },
    {
      key: 'startedAt',
      title: t('shell.inspector.startedAt'),
      render: (_v: unknown, row) => time('startedAt', row.startedAt),
    },
    {
      key: 'finishedAt',
      title: t('shell.inspector.finishedAt'),
      render: (_v: unknown, row) => time('finishedAt', row.finishedAt),
    },
    { key: 'outcome', title: t('shell.inspector.outcome'), render: (_v: unknown, row) => row.outcome },
    { key: 'httpStatus', title: t('shell.inspector.httpStatus'), render: (_v: unknown, row) => row.httpStatus ?? '' },
    { key: 'error', title: t('shell.inspector.error'), render: (_v: unknown, row) => row.error ?? '' },
  ]

  const subProcessColumns: TableColumnsType<SubProcess> = [
    { key: 'process', title: t('shell.inspector.process'), render: (_v: unknown, row) => row.process },
    { key: 'step', title: t('shell.inspector.step'), render: (_v: unknown, row) => stepLabel(row.step) },
    { key: 'state', title: t('shell.inspector.state'), render: (_v: unknown, row) => stateText(t, row.state) },
    {
      key: 'createdAt',
      title: t('shell.inspector.createdAt'),
      render: (_v: unknown, row) => time('createdAt', row.createdAt),
    },
    {
      key: 'endedAt',
      title: t('shell.inspector.endedAt'),
      render: (_v: unknown, row) => time('endedAt', row.endedAt),
    },
    {
      key: 'open',
      render: (_v: unknown, row) => {
        const page = inspectors?.get(row.process)
        return page === undefined ? null : (
          <Link to={inspectorPath(sitePath, page, row.id)} data-testid={`inspector-subprocess-${row.id}`}>
            {t('shell.inspector.open')}
          </Link>
        )
      },
    },
  ]

  const taken = new Set(instance.path.map((entry) => entry.step))
  return (
    <Flex vertical gap="large" data-testid="inspector-instance">
      <InstanceCommands instance={instance} instancesPath={instancesPath} onChanged={onChanged} />
      <Descriptions
        column={1}
        items={[
          { key: 'state', label: t('shell.inspector.state'), children: stateText(t, instance.state) },
          { key: 'step', label: t('shell.inspector.step'), children: stepLabel(instance.step) },
          {
            key: 'subject',
            label: t('shell.inspector.subject'),
            children: instance.subjectId ?? t('shell.inspector.noSubject'),
          },
          ...(instance.parent === null
            ? []
            : [
                {
                  key: 'parent',
                  label: t('shell.inspector.parent'),
                  children:
                    parentPage === undefined ? (
                      instance.parent.process
                    ) : (
                      <Link
                        to={inspectorPath(sitePath, parentPage, instance.parent.id)}
                        data-testid="inspector-parent-link"
                      >
                        {instance.parent.process}
                      </Link>
                    ),
                },
              ]),
          { key: 'release', label: t('shell.inspector.release'), children: instance.releaseId },
          { key: 'createdAt', label: t('shell.inspector.createdAt'), children: time('createdAt', instance.createdAt) },
          { key: 'endedAt', label: t('shell.inspector.endedAt'), children: time('endedAt', instance.endedAt) },
          ...(due === null ? [] : [{ key: 'dueAt', label: t('shell.inspector.dueAt'), children: time('dueAt', due) }]),
        ]}
      />
      <section>
        <Typography.Title level={5}>{t('shell.inspector.steps')}</Typography.Title>
        <Flex vertical>
          {instance.graph.steps.map((step) => (
            <Flex key={step.name} vertical gap="small" data-testid={`inspector-step-${step.name}`} style={rowStyle}>
              <Flex gap="small" align="center">
                <Typography.Text strong>{stepLabel(step.name)}</Typography.Text>
                <Typography.Text type="secondary">{step.type}</Typography.Text>
                {taken.has(step.name) && <Tag color="green">{t('shell.inspector.taken')}</Tag>}
                {step.name === instance.step && <Tag color="blue">{t('shell.inspector.current')}</Tag>}
              </Flex>
              {step.transitions.map((transition) => (
                <Typography.Text key={`${transition.next}|${transition.on ?? ''}`} type="secondary">
                  {`→ ${stepLabel(transition.next)}${transition.on === null ? '' : ` (${transition.on})`}`}
                </Typography.Text>
              ))}
            </Flex>
          ))}
        </Flex>
      </section>
      <section>
        <Typography.Title level={5}>{t('shell.inspector.history')}</Typography.Title>
        <Table<PathEntry>
          rowKey={(row) => `${row.step}|${row.startedAt}|${row.finishedAt}`}
          columns={historyColumns}
          scroll={{ x: 'max-content' }}
          dataSource={instance.path}
          pagination={false}
          locale={{ emptyText: t('shell.inspector.noHistory') }}
        />
      </section>
      <section>
        <Typography.Title level={5}>{t('shell.inspector.tasks')}</Typography.Title>
        <Table<InstanceTask>
          rowKey="id"
          columns={taskColumns}
          scroll={{ x: 'max-content' }}
          dataSource={instance.tasks}
          pagination={false}
          locale={{ emptyText: t('shell.inspector.noTasks') }}
        />
      </section>
      <section>
        <Typography.Title level={5}>{t('shell.inspector.calls')}</Typography.Title>
        {instance.calls.length === 0 ? (
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('shell.inspector.noCalls')} />
        ) : (
          <Flex vertical>
            {instance.calls.map((call) => (
              <CallItem
                key={call.id}
                call={call}
                instanceId={instance.id}
                instancesPath={instancesPath}
                onChanged={onChanged}
                stepLabel={stepLabel}
                attemptColumns={attemptColumns}
              />
            ))}
          </Flex>
        )}
      </section>
      <section>
        <Typography.Title level={5}>{t('shell.inspector.subProcesses')}</Typography.Title>
        <Table<SubProcess>
          rowKey="id"
          columns={subProcessColumns}
          scroll={{ x: 'max-content' }}
          dataSource={instance.subProcesses}
          pagination={false}
          locale={{ emptyText: t('shell.inspector.noSubProcesses') }}
        />
      </section>
    </Flex>
  )
}

interface InstanceCommandsProps {
  instance: InstanceDetail
  instancesPath: string
  onChanged: () => void
}

/** One button for each command the instance detail allows. A command this build does not know is left out. */
function InstanceCommands({ instance, instancesPath, onChanged }: InstanceCommandsProps) {
  const t = useText()
  const [open, setOpen] = useState<InstanceCommand>()
  const allowed = instanceCommands.filter((command) => instance.commands.includes(command))
  if (allowed.length === 0) {
    return null
  }
  return (
    <Flex gap="small">
      {allowed.map((command) => (
        <Button key={command} data-testid={`inspector-command-${command}`} onClick={() => setOpen(command)}>
          {t(`shell.inspector.commands.${command}`)}
        </Button>
      ))}
      {open && (
        <ReasonDialog
          title={t(`shell.inspector.commands.${open}`)}
          onSend={(reason) => sendCommand(instancesPath, instance.id, open, reason)}
          onClose={() => setOpen(undefined)}
          onChanged={onChanged}
        >
          {open === 'retry' && retryWarns(instance) && (
            <Alert
              data-testid="inspector-retry-warning"
              type="warning"
              title={t('shell.inspector.commands.retryWarning')}
            />
          )}
        </ReasonDialog>
      )}
    </Flex>
  )
}

interface CallItemProps {
  call: InstanceCall
  instanceId: string
  instancesPath: string
  /** Called after a resolve was accepted, so the page reads the instance again. */
  onChanged: () => void
  stepLabel: (name: string) => string
  attemptColumns: TableColumnsType<CallAttempt>
}

/**
 * One call: where it goes, its state, its input and its attempts. A call the server marks resolvable
 * also has a Resolve button. The page never decides who may resolve.
 */
function CallItem({ call, instanceId, instancesPath, onChanged, stepLabel, attemptColumns }: CallItemProps) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [resolving, setResolving] = useState(false)
  const target = call.connector ?? `${call.package}@${call.version}`
  const input = call.inputHidden ? null : inputEntries(call.input)
  return (
    <Flex
      vertical
      gap="small"
      data-testid={`inspector-call-${call.id}`}
      style={{ width: '100%', paddingBlock: spacing.sm, borderBlockEnd: `1px solid ${colour.divider}` }}
    >
      <Flex gap="small" align="center" wrap>
        <Tag>{call.kind}</Tag>
        <Typography.Text strong>{stepLabel(call.step)}</Typography.Text>
        <Typography.Text>{`${target} · ${call.operation}`}</Typography.Text>
        <Tag>{call.state}</Tag>
        {call.resolvable && call.outputs && (
          <Button size="small" data-testid={`inspector-resolve-${call.id}`} onClick={() => setResolving(true)}>
            {t('shell.inspector.commands.resolve')}
          </Button>
        )}
      </Flex>
      {resolving && call.outputs && (
        <ResolveDialog
          call={call}
          outputs={call.outputs}
          instanceId={instanceId}
          instancesPath={instancesPath}
          onClose={() => setResolving(false)}
          onChanged={onChanged}
        />
      )}
      {call.inputHidden && (
        <Typography.Text type="secondary">{`${t('shell.inspector.input')}: ${t('shell.inspector.hidden')}`}</Typography.Text>
      )}
      {input !== null && input.length > 0 && (
        <Descriptions
          size="small"
          column={1}
          title={t('shell.inspector.input')}
          items={input.map(([key, value]) => ({ key, label: key, children: value }))}
        />
      )}
      <Typography.Text type="secondary">{t('shell.inspector.attempts')}</Typography.Text>
      <Table<CallAttempt>
        size="small"
        rowKey={(row) => `${row.kind}|${row.number}|${row.startedAt}`}
        columns={attemptColumns}
        scroll={{ x: 'max-content' }}
        dataSource={call.attempts}
        pagination={false}
      />
    </Flex>
  )
}
