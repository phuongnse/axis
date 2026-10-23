import { Alert, Button, Descriptions, Flex, Form, Typography } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { NotFoundPage } from '../pages/NotFoundPage'
import { formatValue } from './formatValue'
import { buildCompleteBody } from './recordBody'
import { fetchRecord } from './records'
import type { WidgetMetadata } from './site'
import { assignedElsewhere, completeTask, fetchTask, type TaskItem } from './tasks'
import { TextProvider } from './TextProvider'
import { useText, useTextCatalogs } from './texts'
import { useReleaseTexts } from './useReleaseTexts'
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
 * already completed, with no form or buttons. A task assigned to someone else shows only that.
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
      />
    </TextProvider>
  )
}

function isTaskItem(task: TaskState | undefined): task is TaskItem {
  return typeof task === 'object' && task !== null
}

interface TaskBodyProps {
  task: TaskItem
  tasksPath: string
  locale: string
  open: boolean
  onDone: () => void
  onAssignedElsewhere: () => void
  onDecided: () => void
}

/** The task's label, subject and due date, and its form while it is open. It reads the release texts. */
function TaskBody({ task, tasksPath, locale, open, onDone, onAssignedElsewhere, onDecided }: TaskBodyProps) {
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
            children: formatValue(dueAtField, task.dueAt, {}, { locale, text: t }),
          },
        ]}
      />
      {open ? (
        <OpenTaskForm
          task={task}
          tasksPath={tasksPath}
          locale={locale}
          onDone={onDone}
          onAssignedElsewhere={onAssignedElsewhere}
          onDecided={onDecided}
        />
      ) : (
        <Alert data-testid="task-completed" type="info" title={t('shell.task.completed')} />
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
    return <Alert data-testid="task-load-error" type="error" title={t('shell.task.loadFailed')} />
  }
  return (
    <Flex vertical gap="middle">
      {form.formErrors.length > 0 && <Alert data-testid="form-error" type="error" title={form.formErrors.join(' ')} />}
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
