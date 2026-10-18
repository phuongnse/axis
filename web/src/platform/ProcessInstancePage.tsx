import {
  Alert,
  Button,
  Descriptions,
  Flex,
  Form,
  Input,
  List,
  Modal,
  Table,
  Tag,
  Typography,
  type TableColumnsType,
} from 'antd'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { formatValue } from './formatValue'
import {
  fetchInstance,
  forbidden,
  instanceCommands,
  retryWarns,
  sendCommand,
  stateText,
  type CallAttempt,
  type GraphStep,
  type InstanceCommand,
  type InstanceCall,
  type InstanceDetail,
  type InstanceTask,
  type PathEntry,
} from './inspection'
import type { WidgetMetadata } from './site'
import { TextProvider } from './TextProvider'
import { useText, useTextCatalogs } from './texts'
import { useReleaseTexts } from './useReleaseTexts'

interface ProcessInstancePageProps {
  sitePath: string
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
 * the release it is pinned to with the path marked, the history, the tasks, and the calls with their
 * attempts. A step the path holds is marked as taken, and the step the instance is at is marked as
 * current. A call whose input is hidden shows the fixed "hidden" text and no value.
 *
 * Step labels come from the texts of the instance's release, then the site texts and the platform
 * texts. A step without a label shows its name. If the release texts fail to load, the page shows
 * its load error.
 */
export function ProcessInstancePage({
  sitePath,
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
    return <Alert data-testid="inspector-not-found" type="warning" message={t('shell.inspector.notFound')} />
  }
  if (instance === forbidden) {
    return <Alert data-testid="inspector-forbidden" type="warning" message={t('shell.inspector.forbidden')} />
  }
  if (instance === 'failed' || releaseTexts.failed) {
    return <Alert data-testid="inspector-error" type="error" message={t('shell.inspector.loadFailed')} />
  }
  // The previous release texts stay on screen while a new locale loads.
  if (!releaseCatalog) {
    return null
  }

  return (
    <TextProvider catalogs={catalogs} development={outer.development}>
      <InstanceBody instance={instance} instancesPath={instancesPath} locale={locale} onChanged={reload} />
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
  locale: string
  /** Called after a command was accepted, so the page reads the instance again. */
  onChanged: () => void
}

/** The sections of an instance. It reads the release texts. */
function InstanceBody({ instance, instancesPath, locale, onChanged }: InstanceBodyProps) {
  const t = useText()
  const stepsByName = useMemo(
    () => new Map(instance.graph.steps.map((step) => [step.name, step])),
    [instance.graph.steps],
  )
  const stepLabel = (name: string) => {
    const labelKey = stepsByName.get(name)?.labelKey
    return labelKey ? t(labelKey) : name
  }
  const time = (name: string, value: string | null) => formatValue(dateTime(name), value, {}, { locale, text: t })

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

  const taken = new Set(instance.path.map((entry) => entry.step))
  return (
    <Flex vertical gap="large" data-testid="inspector-instance">
      <InstanceCommands instance={instance} instancesPath={instancesPath} onChanged={onChanged} />
      <Descriptions
        column={1}
        items={[
          { key: 'state', label: t('shell.inspector.state'), children: stateText(t, instance.state) },
          { key: 'step', label: t('shell.inspector.step'), children: stepLabel(instance.step) },
          { key: 'subject', label: t('shell.inspector.subject'), children: instance.subjectId },
          { key: 'release', label: t('shell.inspector.release'), children: instance.releaseId },
          { key: 'createdAt', label: t('shell.inspector.createdAt'), children: time('createdAt', instance.createdAt) },
          { key: 'endedAt', label: t('shell.inspector.endedAt'), children: time('endedAt', instance.endedAt) },
        ]}
      />
      <section>
        <Typography.Title level={5}>{t('shell.inspector.steps')}</Typography.Title>
        <List<GraphStep>
          bordered
          dataSource={instance.graph.steps}
          renderItem={(step) => (
            <List.Item data-testid={`inspector-step-${step.name}`}>
              <Flex vertical gap="small">
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
            </List.Item>
          )}
        />
      </section>
      <section>
        <Typography.Title level={5}>{t('shell.inspector.history')}</Typography.Title>
        <Table<PathEntry>
          rowKey={(row) => `${row.step}|${row.startedAt}|${row.finishedAt}`}
          columns={historyColumns}
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
          dataSource={instance.tasks}
          pagination={false}
          locale={{ emptyText: t('shell.inspector.noTasks') }}
        />
      </section>
      <section>
        <Typography.Title level={5}>{t('shell.inspector.calls')}</Typography.Title>
        <List<InstanceCall>
          bordered
          dataSource={instance.calls}
          locale={{ emptyText: t('shell.inspector.noCalls') }}
          renderItem={(call) => <CallItem call={call} stepLabel={stepLabel} attemptColumns={attemptColumns} />}
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
        <CommandDialog
          command={open}
          instance={instance}
          instancesPath={instancesPath}
          onClose={() => setOpen(undefined)}
          onChanged={onChanged}
        />
      )}
    </Flex>
  )
}

interface CommandDialogProps {
  command: InstanceCommand
  instance: InstanceDetail
  instancesPath: string
  onClose: () => void
  onChanged: () => void
}

const reasonMaxLength = 500

/**
 * The dialog that asks for the reason of a command and sends it. A refusal shows the server's problem
 * title, and any other failure shows a generic text. Retry warns first when the failed call may have
 * acted before it failed.
 */
function CommandDialog({ command, instance, instancesPath, onClose, onChanged }: CommandDialogProps) {
  const t = useText()
  const [form] = Form.useForm<{ reason: string }>()
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string>()

  const send = async () => {
    const { reason } = await form.validateFields()
    setSending(true)
    setError(undefined)
    try {
      const result = await sendCommand(instancesPath, instance.id, command, reason)
      if (result.ok) {
        onClose()
        onChanged()
      } else {
        setError(result.problem.title)
      }
    } catch (failure) {
      console.warn(`Sending '${command}' to the instance '${instance.id}' failed`, failure)
      setError(t('shell.inspector.commands.failed'))
    } finally {
      setSending(false)
    }
  }

  return (
    <Modal
      open
      title={t(`shell.inspector.commands.${command}`)}
      okText={t('shell.inspector.commands.send')}
      cancelText={t('shell.inspector.commands.close')}
      confirmLoading={sending}
      onOk={() => void send().catch(() => undefined)}
      onCancel={onClose}
      destroyOnHidden
    >
      <Flex vertical gap="middle">
        {command === 'retry' && retryWarns(instance) && (
          <Alert
            data-testid="inspector-retry-warning"
            type="warning"
            message={t('shell.inspector.commands.retryWarning')}
          />
        )}
        <Form form={form} layout="vertical">
          <Form.Item
            name="reason"
            label={t('shell.inspector.commands.reason')}
            rules={[
              { required: true, whitespace: true, message: t('shell.inspector.commands.reasonRequired') },
              { max: reasonMaxLength, message: t('shell.inspector.commands.reasonTooLong') },
            ]}
          >
            <Input.TextArea rows={3} showCount maxLength={reasonMaxLength} />
          </Form.Item>
        </Form>
        {error !== undefined && <Alert data-testid="inspector-command-error" type="error" message={error} />}
      </Flex>
    </Modal>
  )
}

interface CallItemProps {
  call: InstanceCall
  stepLabel: (name: string) => string
  attemptColumns: TableColumnsType<CallAttempt>
}

/** One call: where it goes, its state, its input and its attempts. */
function CallItem({ call, stepLabel, attemptColumns }: CallItemProps) {
  const t = useText()
  const target = call.connector ?? `${call.package}@${call.version}`
  const input = call.inputHidden ? null : inputEntries(call.input)
  return (
    <List.Item data-testid={`inspector-call-${call.id}`}>
      <Flex vertical gap="small" style={{ width: '100%' }}>
        <Flex gap="small" align="center" wrap>
          <Tag>{call.kind}</Tag>
          <Typography.Text strong>{stepLabel(call.step)}</Typography.Text>
          <Typography.Text>{`${target} · ${call.operation}`}</Typography.Text>
          <Tag>{call.state}</Tag>
        </Flex>
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
          dataSource={call.attempts}
          pagination={false}
        />
      </Flex>
    </List.Item>
  )
}
