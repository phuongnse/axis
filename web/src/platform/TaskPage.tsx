import { Alert, Button, Descriptions, Flex, Form, Typography } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { NotFoundPage } from '../pages/NotFoundPage'
import { formatValue } from './formatValue'
import { buildCompleteBody } from './recordBody'
import { fetchRecord } from './records'
import type { WidgetMetadata } from './site'
import { assignedElsewhere, completeTask, fetchTask, type TaskItem } from './tasks'
import { useText } from './texts'
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
}

/** The task read, `null` when the task API has none with the id, or why it could not be shown. */
type TaskState = TaskItem | null | typeof assignedElsewhere | 'failed'

const dueAtField = { name: 'dueAt', type: 'date-time' } as const

/**
 * Shows one task under the page of its task inbox: the step's label, the subject's label, the due
 * date and the step's form over the subject record, with one button per outcome. A click completes
 * the task with the outcome, the changed editable values and the record's `version`, then returns
 * to the inbox. A task that is not open, or that another completion decided first, shows that it is
 * already completed, with no form or buttons. A task assigned to someone else shows only that.
 */
export function TaskPage({ sitePath, pageName, widget, taskId, locale }: TaskPageProps) {
  const t = useText()
  const navigate = useNavigate()
  // The server sets the path on every task inbox.
  const tasksPath = widget.tasksPath!
  const inboxPath = `/${sitePath}/${pageName.toLowerCase()}`
  const [task, setTask] = useState<TaskState>()
  // Set when completing answered that another completion came first.
  const [decided, setDecided] = useState(false)

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

  if (task === undefined) {
    return null
  }
  if (task === null) {
    return <NotFoundPage />
  }
  if (task === assignedElsewhere) {
    return <Alert data-testid="task-assigned-elsewhere" type="warning" message={t('shell.task.assignedElsewhere')} />
  }
  if (task === 'failed') {
    return <Alert data-testid="task-load-error" type="error" message={t('shell.task.loadFailed')} />
  }

  const open = task.state === 'open' && !decided
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
            children: formatValue(dueAtField, task.dueAt, {}, { locale, text: t }),
          },
        ]}
      />
      {open ? (
        <OpenTaskForm
          task={task}
          tasksPath={tasksPath}
          locale={locale}
          onDone={() => navigate(inboxPath)}
          onAssignedElsewhere={() => setTask(assignedElsewhere)}
          onDecided={() => void onDecided()}
        />
      ) : (
        <Alert data-testid="task-completed" type="info" message={t('shell.task.completed')} />
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
  const form = useRecordForm(entity, sections, locale)
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
        form.showProblem(problem)
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
    return <Alert data-testid="task-load-error" type="error" message={t('shell.task.loadFailed')} />
  }
  return (
    <Flex vertical gap="middle">
      {form.formErrors.length > 0 && (
        <Alert data-testid="form-error" type="error" message={form.formErrors.join(' ')} />
      )}
      <Form layout="vertical">{form.fields}</Form>
      <Flex gap="small">
        {task.outcomes.map((outcome, index) => (
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
