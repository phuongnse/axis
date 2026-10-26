import { Alert, Button, Descriptions, Flex, Form, Space, Tag, Typography } from 'antd'
import { useEffect, useId, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { NotFoundPage } from '../pages/NotFoundPage'
import { formatValue } from './formatValue'
import { buildCompleteBody } from './recordBody'
import { fetchRecord, type RecordProblem } from './records'
import { ReassignDialog } from './ReassignDialog'
import type { WidgetMetadata } from './site'
import {
  assignedElsewhere,
  completeTask,
  fetchTask,
  reassignTask,
  runTaskCommand,
  usersPath,
  type ReassignBody,
  type ReassignRole,
  type TaskItem,
} from './tasks'
import { TextProvider } from './TextProvider'
import { useText, useTextCatalogs } from './texts'
import { useReleaseTexts } from './useReleaseTexts'
import { useFormSteps } from './useFormSteps'
import { sectionsOf, useRecordForm } from './useRecordForm'

interface TaskPageProps {
  sitePath: string
  /** The name of the inbox page the task's address is under. */
  pageName: string
  /** The task inbox widget of that page. */
  widget: WidgetMetadata
  taskId: string
  /** The UI locale, for formatting the due date and read-only values. */
  locale: string
  /** The site's fallback locale, for the texts of the task's release. */
  fallbackLocale: string
}

/** The task read, `null` when the task API has none with the id, or why it could not be shown. */
type TaskState = TaskItem | null | typeof assignedElsewhere | 'failed'

const dueAtField = { name: 'dueAt', type: 'date-time' } as const

/**
 * Shows one task under the page of its task inbox: the step's label, the subject's label, the due
 * date and the step's form over the subject record, with one button per outcome. A click completes
 * the task with the outcome, the changed editable values and the record's `version`, then returns
 * to the inbox. The form follows the task's form state endpoint: a field the answer hides or locks
 * is not sent. A task that is not open, or that another completion decided first, shows that it is
 * already completed, with no form or buttons. An expired task shows that its deadline passed, with
 * no form or buttons. A task assigned to someone else shows only that.
 *
 * An overdue task shows an "Overdue" tag beside the due date. An open task shows a Claim and a
 * Release button for each of `claim` and `release` that the task read's `commands` lists. A click
 * shows the task the route returns. A 403 or 409 shows a message and loads the task again.
 *
 * An open task whose `commands` lists `reassign` also shows a Reassign button. It opens a dialog
 * where the user picks a role of the task read's `reassignRoles` or a person, and gives a reason.
 * On success the page returns to the inbox. A 400 shows in the dialog. A 403 or 409 closes the
 * dialog, shows the same message as Claim and Release and loads the task again.
 *
 * The page shows the texts of the release the task started on, for the current and the fallback
 * locale, before the site texts and the platform texts. So a label a newer release reworded or
 * removed keeps its old text. If those texts fail to load, the page shows its load error.
 */
export function TaskPage({ sitePath, pageName, widget, taskId, locale, fallbackLocale }: TaskPageProps) {
  const t = useText()
  const navigate = useNavigate()
  // The server sets the path on every task inbox.
  const tasksPath = widget.tasksPath!
  const inboxPath = `/${sitePath}/${pageName.toLowerCase()}`
  const [task, setTask] = useState<TaskState>()
  // Set when completing answered that another completion came first.
  const [decided, setDecided] = useState(false)
  const outer = useTextCatalogs()
  const releaseIds = useMemo(() => (isTaskItem(task) ? [task.releaseId] : []), [task])
  const releaseTexts = useReleaseTexts(sitePath, releaseIds, locale, fallbackLocale)
  const releaseCatalog = isTaskItem(task) ? releaseTexts.catalogs.get(task.releaseId) : undefined
  const catalogs = useMemo(
    () => (releaseCatalog ? [releaseCatalog, ...outer.catalogs] : outer.catalogs),
    [releaseCatalog, outer.catalogs],
  )

  useEffect(() => {
    const controller = new AbortController()
    fetchTask(tasksPath, taskId, controller.signal)
      .then(setTask)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading the task '${taskId}' failed`, error)
          setTask('failed')
        }
      })
    return () => controller.abort()
  }, [tasksPath, taskId])

  // Another completion came first, so the task is loaded again to show its state.
  const onDecided = async () => {
    setDecided(true)
    try {
      setTask(await fetchTask(tasksPath, taskId))
    } catch (error) {
      console.warn(`Loading the task '${taskId}' failed`, error)
    }
  }

  // The task the route returns replaces the read, but the form stays: a new form object would load
  // the subject record again and lose what the user typed.
  const onCommand = async (command: 'claim' | 'release'): Promise<CommandMessage | null> => {
    try {
      const result = await runTaskCommand(tasksPath, taskId, command)
      if (result.ok) {
        setTask((current) => (isTaskItem(current) ? { ...result.task, form: current.form } : result.task))
        return null
      }
      setTask(await fetchTask(tasksPath, taskId))
      return { type: 'warning', text: t('shell.task.commandRefused') }
    } catch (error) {
      console.warn(`Running '${command}' on the task '${taskId}' failed`, error)
      return { type: 'error', text: t('shell.task.commandFailed') }
    }
  }

  // A 400 goes back to the dialog, and a 403 or 409 comes back as the message to show.
  const onReassign = async (body: ReassignBody): Promise<ReassignAnswer> => {
    const result = await reassignTask(tasksPath, taskId, body)
    if (result.ok) {
      navigate(inboxPath)
      return null
    }
    if ('problem' in result) {
      return { problem: result.problem }
    }
    setTask(await fetchTask(tasksPath, taskId))
    return { message: { type: 'warning', text: t('shell.task.commandRefused') } }
  }

  if (task === undefined) {
    return null
  }
  if (task === null) {
    return <NotFoundPage />
  }
  if (task === assignedElsewhere) {
    return <Alert data-testid="task-assigned-elsewhere" type="warning" title={t('shell.task.assignedElsewhere')} />
  }
  if (task === 'failed' || releaseTexts.failed) {
    return <Alert data-testid="task-load-error" type="error" title={t('shell.task.loadFailed')} />
  }
  // The previous release texts stay on screen while a new locale loads, so the form is not remounted.
  if (!releaseCatalog) {
    return null
  }

  return (
    <TextProvider catalogs={catalogs} development={outer.development}>
      <TaskBody
        task={task}
        tasksPath={tasksPath}
        locale={locale}
        open={task.state === 'open' && !decided}
        onDone={() => navigate(inboxPath)}
        onAssignedElsewhere={() => setTask(assignedElsewhere)}
        onDecided={() => void onDecided()}
        onCommand={onCommand}
        onReassign={onReassign}
      />
    </TextProvider>
  )
}

function isTaskItem(task: TaskState | undefined): task is TaskItem {
  return typeof task === 'object' && task !== null
}

/** The answer to a command that did not succeed, shown above the command buttons. */
interface CommandMessage {
  type: 'warning' | 'error'
  text: string
}

/**
 * What the page answers a reassign with: the problem of a 400 for the dialog to show, the message of
 * a 403 or 409 that closes the dialog, or `null` when it succeeded.
 */
type ReassignAnswer = { problem: RecordProblem } | { message: CommandMessage } | null

interface TaskBodyProps {
  task: TaskItem
  tasksPath: string
  locale: string
  open: boolean
  onDone: () => void
  onAssignedElsewhere: () => void
  onDecided: () => void
  /** Runs the command and returns the message to show, or `null` when it succeeded. */
  onCommand: (command: 'claim' | 'release') => Promise<CommandMessage | null>
  onReassign: (body: ReassignBody) => Promise<ReassignAnswer>
}

/**
 * The task's label, subject and due date, then its Claim, Release and Reassign buttons and form
 * while it is open, or the message that it is expired or completed. It reads the release texts.
 */
function TaskBody({
  task,
  tasksPath,
  locale,
  open,
  onDone,
  onAssignedElsewhere,
  onDecided,
  onCommand,
  onReassign,
}: TaskBodyProps) {
  const t = useText()
  return (
    <Flex vertical gap="middle">
      <Typography.Title level={4}>{t(task.labelKey)}</Typography.Title>
      <Descriptions
        column={1}
        items={[
          { key: 'subject', label: t('shell.tasks.subject'), children: task.subject.label ?? '' },
          {
            key: 'dueAt',
            label: t('shell.tasks.dueAt'),
            children: (
              <Space size="small">
                {formatValue(dueAtField, task.dueAt, {}, { locale, text: t })}
                {task.overdue && <Tag color="error">{t('shell.tasks.overdue')}</Tag>}
              </Space>
            ),
          },
        ]}
      />
      {task.state === 'expired' ? (
        <Alert data-testid="task-expired" type="info" title={t('shell.task.expired')} />
      ) : open ? (
        <>
          <TaskCommands
            commands={task.commands}
            reassignRoles={task.reassignRoles}
            usersPath={usersPath(tasksPath)}
            onCommand={onCommand}
            onReassign={onReassign}
          />
          <OpenTaskForm
            task={task}
            tasksPath={tasksPath}
            locale={locale}
            onDone={onDone}
            onAssignedElsewhere={onAssignedElsewhere}
            onDecided={onDecided}
          />
        </>
      ) : (
        <Alert data-testid="task-completed" type="info" title={t('shell.task.completed')} />
      )}
    </Flex>
  )
}

interface TaskCommandsProps {
  commands: string[]
  reassignRoles: ReassignRole[]
  usersPath: string
  onCommand: (command: 'claim' | 'release') => Promise<CommandMessage | null>
  onReassign: (body: ReassignBody) => Promise<ReassignAnswer>
}

const taskCommands = ['claim', 'release'] as const

/**
 * A button for each of `claim` and `release` that `commands` lists, a Reassign button with its
 * dialog when it lists `reassign`, and the message of a command that failed.
 */
function TaskCommands({ commands, reassignRoles, usersPath, onCommand, onReassign }: TaskCommandsProps) {
  const t = useText()
  // The command being sent, so both buttons wait for its answer.
  const [pending, setPending] = useState<string | null>(null)
  const [message, setMessage] = useState<CommandMessage | null>(null)
  const [reassigning, setReassigning] = useState(false)
  const listed = taskCommands.filter((command) => commands.includes(command))
  const canReassign = commands.includes('reassign')

  const run = async (command: (typeof taskCommands)[number]) => {
    if (pending !== null) {
      return
    }
    setPending(command)
    setMessage(null)
    try {
      setMessage(await onCommand(command))
    } finally {
      setPending(null)
    }
  }

  // A 403 or 409 closes the dialog and shows its message. A 400 goes back to the dialog.
  const reassign = async (body: ReassignBody): Promise<RecordProblem | null> => {
    const answer = await onReassign(body)
    if (answer !== null && 'message' in answer) {
      setReassigning(false)
      setMessage(answer.message)
      return null
    }
    return answer === null ? null : answer.problem
  }

  if (listed.length === 0 && !canReassign && message === null) {
    return null
  }
  return (
    <Flex vertical gap="small">
      {message !== null && <Alert data-testid="task-command-message" type={message.type} title={message.text} />}
      <Flex gap="small">
        {listed.map((command) => (
          <Button
            key={command}
            loading={pending === command}
            disabled={pending !== null && pending !== command}
            onClick={() => void run(command)}
          >
            {t(`shell.task.${command}`)}
          </Button>
        ))}
        {canReassign && (
          <Button disabled={pending !== null} onClick={() => setReassigning(true)}>
            {t('shell.task.reassign')}
          </Button>
        )}
      </Flex>
      {reassigning && (
        <ReassignDialog
          roles={reassignRoles}
          usersPath={usersPath}
          onSend={reassign}
          onClose={() => setReassigning(false)}
        />
      )}
    </Flex>
  )
}

interface OpenTaskFormProps {
  task: TaskItem
  tasksPath: string
  locale: string
  onDone: () => void
  onAssignedElsewhere: () => void
  onDecided: () => void
}

/** The step's form over the subject record of an open task, and its outcome buttons. */
function OpenTaskForm({ task, tasksPath, locale, onDone, onAssignedElsewhere, onDecided }: OpenTaskFormProps) {
  const t = useText()
  const { entity } = task.form
  const sections = useMemo(() => sectionsOf(entity, task.form), [entity, task.form])
  const conditions = useMemo(() => ({ statePath: `${tasksPath}/${task.id}/form-state` }), [tasksPath, task.id])
  const form = useRecordForm(entity, sections, locale, conditions)
  const steps = useFormSteps(task.form, sections, form)
  // The Next button is outside the form, so it names the form it submits.
  const formId = useId()
  const { start, setFormErrors } = form
  const [ready, setReady] = useState<boolean | 'failed'>(false)
  // The outcome being sent, so every button waits for its answer.
  const [sending, setSending] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetchRecord(entity.recordsPath, task.subject.id, controller.signal)
      .then((record) => {
        if (record) {
          start(record)
          setReady(true)
        } else {
          setReady('failed')
        }
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading the subject record '${task.subject.id}' of '${entity.name}' failed`, error)
          setReady('failed')
        }
      })
    return () => controller.abort()
  }, [entity.recordsPath, entity.name, task.subject.id, start])

  const complete = async (outcome: string) => {
    if (sending !== null) {
      return
    }
    const body = buildCompleteBody(outcome, entity.fields, form.changed(), form.version)
    setSending(outcome)
    form.clearErrors()
    try {
      const result = await completeTask(tasksPath, task.id, body)
      if (result.ok) {
        onDone()
        return
      }
      const { problem } = result
      if (problem.status === 403) {
        onAssignedElsewhere()
      } else if (problem.status === 409 && Object.keys(problem.errors).length === 0) {
        // The task API answers a task that is no longer open with a 409 that names no field.
        onDecided()
      } else {
        steps.openFirstWith(form.showProblem(problem))
      }
    } catch (error) {
      console.warn(`Completing the task '${task.id}' failed`, error)
      setFormErrors([t('shell.task.completeFailed')])
    } finally {
      setSending(null)
    }
  }

  if (ready === false) {
    return null
  }
  if (ready === 'failed') {
    return <Alert data-testid="task-load-error" type="error" title={t('shell.task.loadFailed')} />
  }
  return (
    <Flex vertical gap="middle">
      {form.formErrors.length > 0 && <Alert data-testid="form-error" type="error" title={form.formErrors.join(' ')} />}
      {steps.progress}
      {/* Enter acts as Next on a step before the last, and does nothing on the last. */}
      <Form id={formId} layout="vertical" onFinish={() => void steps.next()}>
        {steps.fields}
      </Form>
      <Flex gap="small">
        {!steps.first && (
          <Button onClick={steps.back} disabled={steps.checking || sending !== null}>
            {t('shell.form.back')}
          </Button>
        )}
        {/* A quick check shows no spinner, because antd can leave one half removed after a flash. */}
        {!steps.last && (
          <Button htmlType="submit" form={formId} type="primary" loading={steps.checking && { delay: 200 }}>
            {t('shell.form.next')}
          </Button>
        )}
        {steps.last &&
          task.outcomes.map((outcome, index) => (
            <Button
              key={outcome.name}
              type={index === 0 ? 'primary' : 'default'}
              loading={sending === outcome.name}
              disabled={sending !== null && sending !== outcome.name}
              onClick={() => void complete(outcome.name)}
            >
              {t(outcome.labelKey)}
            </Button>
          ))}
      </Flex>
    </Flex>
  )
}
