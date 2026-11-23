import { Flex } from 'antd'
import { AxisForm } from './AxisForm'
import { AxisAlert } from './AxisAlert'
import { AxisActionRow, AxisButton, AxisButtonGroup } from './AxisButton'
import { useEffect, useId, useMemo, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import { PageActions, PageLayout } from './PageLayout'
import { formatValue } from './formatValue'
import { buildCompleteBody, buildRecordBody } from './recordBody'
import { fetchRecord, type RecordProblem } from './records'
import { ReassignDialog } from './ReassignDialog'
import { RecordLink } from './RecordLink'
import { RecordList } from './recordList'
import { RecordNotFound } from './RecordNotFound'
import { SaveBar } from './SaveBar'
import { SectionPart, SectionParts } from './SectionParts'
import { StatusTag } from './StatusTag'
import { TermList } from './TermList'
import type { WidgetMetadata } from './site'
import {
  assignedElsewhere,
  completeTask,
  fetchTask,
  reassignTask,
  runTaskCommand,
  saveTask,
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
import { siteAddress } from './siteLinks'
import { leaveWithoutAsking, useUnsavedChanges } from './unsavedChanges'

interface TaskPageProps {
  sitePath: string
  /** The name of the inbox page the task's address is under. */
  pageName: string
  /** The text key of the inbox page's title, which heads the page until the task loads, and names the way back. */
  titleKey: string
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
 * Shows one task under the page of its task inbox, see docs/reference/task-inbox-widget.md#task-page. The page is headed
 * by the step's label, with the Overdue tag beside it when the task is overdue and Task as its line. Its first part is
 * the facts Record, the subject's label as a link where the person may open the record, and Due when the task has a due
 * date. Its second part is the step's form over the subject record. The commands and the outcomes sit on the heading,
 * the outcomes as one group named Decision. A click on an outcome completes the task with the outcome, the changed
 * editable values and the record's `version`, then returns to the inbox. The form follows the task's form state
 * endpoint: a field the answer hides or locks is not sent. A task that is not open, or that another completion decided
 * first, shows that it is already completed, with no form or buttons. An expired task shows that its deadline passed,
 * with no form or buttons.
 *
 * A change to the form shows the save bar, on a wizard's last step. Save sends the changed values and the record's
 * `version` to the task's save route and leaves the task open: the page reads the subject record again and the form
 * starts from it, so the bar goes away and a later outcome sends the new version. Discard asks first, then puts back
 * the values the form started from. A save's 400, and its 409 that names keys, show as a completion's do, under their
 * field or above the form. A 409 that names no key shows that the task is completed, and a 403 that it is assigned to
 * someone else. While the form has a change, a link, Back or the browser asks "Leave without saving?" on every step,
 * but an outcome and a reassign go to the inbox without asking.
 *
 * Until the task loads, and for a task that is not shown, the page is headed by the inbox page's title. A task the task
 * API has none for shows that it does not exist, or that the person cannot open it, with a link back to the inbox. A
 * task assigned to someone else shows only that.
 *
 * An open task shows a Release and a Claim button for each of `release` and `claim` that the task read's `commands`
 * lists. A click shows the task the route returns. A 403 or 409 shows a message and loads the task again.
 *
 * An open task whose `commands` lists `reassign` also shows a Reassign… button. It opens a dialog
 * where the user picks a role of the task read's `reassignRoles` or a person, and gives a reason.
 * On success the page returns to the inbox. A 400 shows in the dialog. A 403 or 409 closes the
 * dialog, shows the same message as Claim and Release and loads the task again.
 *
 * The page shows the texts of the release the task started on, for the current and the fallback
 * locale, before the site texts and the platform texts. So a label a newer release reworded or
 * removed keeps its old text. If those texts fail to load, the page shows its load error.
 */
export function TaskPage({ sitePath, pageName, titleKey, widget, taskId, locale, fallbackLocale }: TaskPageProps) {
  const t = useText()
  const navigate = useNavigate()
  // The server sets the path on every task inbox.
  const tasksPath = widget.tasksPath!
  const inboxPath = siteAddress(sitePath, pageName.toLowerCase())
  // The way back to the inbox, by the inbox page's title, which a missing task's line links to.
  const inbox = useMemo(() => ({ labelKey: titleKey, to: inboxPath }), [titleKey, inboxPath])
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
      navigate(inboxPath, { state: leaveWithoutAsking })
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
  // Until the task loads, and where it is not shown, the page is headed by the inbox page's title.
  const inboxHeading = (content: ReactNode) => (
    <RecordList value={inbox}>
      <PageLayout title={t(titleKey)}>{content}</PageLayout>
    </RecordList>
  )
  if (task === null) {
    return inboxHeading(<RecordNotFound textKey="shell.task.notFound" />)
  }
  if (task === assignedElsewhere) {
    return inboxHeading(
      <AxisAlert data-testid="task-assigned-elsewhere" kind="warning" message={t('shell.task.assignedElsewhere')} />,
    )
  }
  if (task === 'failed' || releaseTexts.failed) {
    return inboxHeading(<AxisAlert data-testid="task-load-error" kind="error" message={t('shell.task.loadFailed')} />)
  }
  // The previous release texts stay on screen while a new locale loads, so the form is not remounted.
  if (!releaseCatalog) {
    return null
  }

  return (
    <RecordList value={inbox}>
      <TextProvider catalogs={catalogs} development={outer.development}>
        <TaskHeading task={task}>
          <TaskBody
            task={task}
            tasksPath={tasksPath}
            locale={locale}
            open={task.state === 'open' && !decided}
            onDone={() => navigate(inboxPath, { state: leaveWithoutAsking })}
            onAssignedElsewhere={() => setTask(assignedElsewhere)}
            onDecided={() => void onDecided()}
            onCommand={onCommand}
            onReassign={onReassign}
          />
        </TaskHeading>
      </TextProvider>
    </RecordList>
  )
}

/** The page of a loaded task: headed by the step's label, with the Overdue tag beside it and Task as its line. */
function TaskHeading({ task, children }: { task: TaskItem; children: ReactNode }) {
  const t = useText()
  return (
    <PageLayout
      title={t(task.labelKey)}
      tags={task.overdue ? <StatusTag tone="attention">{t('shell.tasks.overdue')}</StatusTag> : undefined}
      line={t('shell.task.kind')}
    >
      {children}
    </PageLayout>
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
 * The task's facts, Record and Due, as the page's first part, then its form part: the message that it is expired or
 * completed, or the message of a failed command and the open task's form. The commands and the outcomes sit on the
 * heading. It reads the release texts.
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
  const commands = useTaskCommands({
    commands: open ? task.commands : [],
    reassignRoles: task.reassignRoles,
    usersPath: usersPath(tasksPath),
    onCommand,
    onReassign,
  })
  const terms = [
    {
      key: 'subject',
      term: t('shell.tasks.subject'),
      value: <RecordLink entity={task.subject.entity} id={task.subject.id} label={task.subject.label} />,
    },
    ...(task.dueAt === null
      ? []
      : [
          {
            key: 'dueAt',
            term: t('shell.tasks.dueAt'),
            value: formatValue(dueAtField, task.dueAt, {}, { locale, text: t }),
          },
        ]),
  ]
  return (
    <SectionParts>
      <SectionPart testId="task-facts">
        <TermList terms={terms} />
      </SectionPart>
      <SectionPart testId="task-form">
        {task.state === 'expired' ? (
          <AxisAlert data-testid="task-expired" kind="info" message={t('shell.task.expired')} />
        ) : open ? (
          <Flex vertical gap="middle">
            {commands.panel}
            <OpenTaskForm
              task={task}
              tasksPath={tasksPath}
              locale={locale}
              commandButtons={commands.buttons}
              onDone={onDone}
              onAssignedElsewhere={onAssignedElsewhere}
              onDecided={onDecided}
            />
          </Flex>
        ) : (
          <AxisAlert data-testid="task-completed" kind="info" message={t('shell.task.completed')} />
        )}
      </SectionPart>
    </SectionParts>
  )
}

interface TaskCommandsProps {
  commands: string[]
  reassignRoles: ReassignRole[]
  usersPath: string
  onCommand: (command: 'claim' | 'release') => Promise<CommandMessage | null>
  onReassign: (body: ReassignBody) => Promise<ReassignAnswer>
}

const taskCommands = ['release', 'claim'] as const

/**
 * The command buttons for the heading, in the order Release, Claim, then Reassign…, each of `release`, `claim` and
 * `reassign` that `commands` lists, and the panel to show in the page: the message of a command that failed and the
 * reassign dialog while it is open.
 */
function useTaskCommands({ commands, reassignRoles, usersPath, onCommand, onReassign }: TaskCommandsProps) {
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

  const buttons: ReactNode[] = listed.map((command) => (
    <AxisButton
      key={command}
      role="secondary"
      loading={pending === command}
      disabled={pending !== null && pending !== command}
      onClick={() => void run(command)}
    >
      {t(`shell.task.${command}`)}
    </AxisButton>
  ))
  if (canReassign) {
    buttons.push(
      <AxisButton key="reassign" role="secondary" disabled={pending !== null} onClick={() => setReassigning(true)}>
        {t('shell.task.reassign')}
      </AxisButton>,
    )
  }
  const panel = (
    <>
      {message !== null && <AxisAlert data-testid="task-command-message" kind={message.type} message={message.text} />}
      {reassigning && (
        <ReassignDialog
          roles={reassignRoles}
          usersPath={usersPath}
          onSend={reassign}
          onClose={() => setReassigning(false)}
        />
      )}
    </>
  )
  return { buttons, panel }
}

interface OpenTaskFormProps {
  task: TaskItem
  tasksPath: string
  locale: string
  /** The command buttons, which go on the heading before the outcomes. */
  commandButtons: ReactNode[]
  onDone: () => void
  onAssignedElsewhere: () => void
  onDecided: () => void
}

/**
 * The step's form over the subject record of an open task. The command buttons and the outcomes sit on the page's
 * heading, in one `PageActions`, and a wizard shows the outcomes on its last step only.
 */
function OpenTaskForm({
  task,
  tasksPath,
  locale,
  commandButtons,
  onDone,
  onAssignedElsewhere,
  onDecided,
}: OpenTaskFormProps) {
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
  const [saving, setSaving] = useState(false)
  const unsaved = ready === true && Object.keys(form.changed()).length > 0
  // The guard asks on every step of a wizard, though the save bar shows only on the last.
  useUnsavedChanges(unsaved)

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

  // A 403 means the task is assigned to someone else, and a 409 that names no field that it is no longer open.
  const showProblem = (problem: RecordProblem) => {
    if (problem.status === 403) {
      onAssignedElsewhere()
    } else if (problem.status === 409 && Object.keys(problem.errors).length === 0) {
      onDecided()
    } else {
      steps.openFirstWith(form.showProblem(problem))
    }
  }

  const complete = async (outcome: string) => {
    if (sending !== null || saving) {
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
      showProblem(result.problem)
    } catch (error) {
      console.warn(`Completing the task '${task.id}' failed`, error)
      setFormErrors([t('shell.task.completeFailed')])
    } finally {
      setSending(null)
    }
  }

  // The form starts from the saved record, read again because a save can change computed fields.
  const save = async () => {
    if (saving || sending !== null || !unsaved) {
      return
    }
    const body = buildRecordBody(entity.fields, form.changed(), form.version)
    setSaving(true)
    form.clearErrors()
    try {
      const result = await saveTask(tasksPath, task.id, body)
      if (!result.ok) {
        showProblem(result.problem)
        return
      }
      try {
        const record = await fetchRecord(entity.recordsPath, task.subject.id)
        if (record) {
          start(record)
        } else {
          setFormErrors([t('shell.form.loadFailed')])
        }
      } catch (error) {
        console.warn(`Loading the subject record '${task.subject.id}' of '${entity.name}' failed`, error)
        setFormErrors([t('shell.form.loadFailed')])
      }
    } catch (error) {
      console.warn(`Saving the task '${task.id}' failed`, error)
      setFormErrors([t('shell.form.saveFailed')])
    } finally {
      setSaving(false)
    }
  }

  const actions = (
    <PageActions>
      <Flex gap="small">
        {commandButtons}
        {steps.last && ready === true && (
          <AxisButtonGroup
            role="main"
            nameKey="shell.task.outcomes"
            actions={task.outcomes.map((outcome) => ({
              key: outcome.name,
              label: t(outcome.labelKey),
              loading: sending === outcome.name,
              disabled: saving || (sending !== null && sending !== outcome.name),
              onClick: () => void complete(outcome.name),
            }))}
          />
        )}
      </Flex>
    </PageActions>
  )
  if (ready === false) {
    return actions
  }
  if (ready === 'failed') {
    return (
      <>
        {actions}
        <AxisAlert data-testid="task-load-error" kind="error" message={t('shell.task.loadFailed')} />
      </>
    )
  }
  const back = !steps.first && (
    <AxisButton role="secondary" onClick={steps.back} disabled={steps.checking || sending !== null}>
      {t('shell.form.back')}
    </AxisButton>
  )
  return (
    <>
      {actions}
      <Flex vertical gap="middle">
        {form.formError}
        {steps.progress}
        {/* Enter acts as Next on a step before the last, and does nothing on the last. */}
        <AxisForm id={formId} onFinish={() => void steps.next()}>
          {steps.fields}
        </AxisForm>
        {steps.last ? (
          back && <Flex gap="small">{back}</Flex>
        ) : (
          <AxisActionRow>
            {back}
            {/* A quick check shows no spinner, because antd can leave one half removed after a flash. */}
            <AxisButton role="finish" htmlType="submit" form={formId} loading={steps.checking && { delay: 200 }}>
              {t('shell.form.next')}
            </AxisButton>
          </AxisActionRow>
        )}
      </Flex>
      {steps.last && (
        <SaveBar
          unsaved={unsaved}
          saveable={!saving && sending === null}
          busy={saving || sending !== null}
          onDiscard={form.discard}
          onSave={() => void save()}
          data-testid="task-save-bar"
          saveTestId="task-save"
        />
      )}
    </>
  )
}
