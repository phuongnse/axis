import { ConfigProvider, Flex, Form } from 'antd'
import { AxisAlert } from './AxisAlert'
import { useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { antLocaleFor } from './antLocale'
import { AxisActionRow, AxisButton } from './AxisButton'
import { useCanvasActions } from './canvasActions'
import { FormCanvasContext } from './formCanvas'
import { formStatePath } from './formState'
import { navigateTarget, type NavigateOpen } from './navigateAction'
import { buildRecordBody } from './recordBody'
import { RecordHistory } from './RecordHistory'
import { PageActions } from './PageLayout'
import { newIdempotencyKey, startProcess } from './processes'
import { requiredMark } from './requiredMark'
import { RecordNotFound } from './RecordNotFound'
import { SaveBar } from './SaveBar'
import { SectionParts } from './SectionParts'
import { fetchRecord, saveRecord, type RecordItem, type RecordProblem } from './records'
import type { EntityWidgetMetadata, NavigateAction, StartProcessAction } from './site'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { leaveWithoutAsking, useUnsavedChanges } from './unsavedChanges'
import { useFormSteps } from './useFormSteps'
import { sectionsOf, useRecordForm } from './useRecordForm'

interface FormWidgetProps {
  widget: EntityWidgetMetadata
  /** The path of the application site the form is on. */
  sitePath: string
  /** The names of the site's navigation pages, in navigation order. */
  pages: readonly string[]
  /** The record to edit, or `null` to create one. */
  recordId: string | null
  /** Where a save goes: the table page the user came from. */
  returnTo: string
  /** The UI locale, for showing computed and sequence values. */
  locale: string
}

type LoadState = 'loading' | 'ready' | 'missing' | 'failed'

/**
 * The row of a wizard's last step: Back is its only button, so there is no solid button and no action is a text button,
 * see docs/reference/actions-and-controls.md#buttons.
 */
function FooterRow({ children }: { children: ReactNode }) {
  return <Flex gap="small">{children}</Flex>
}

/**
 * Creates or edits one record of the widget's entity. A widget that names a form shows the form's
 * sections under their titles, with only the fields they list. Otherwise it shows every field in
 * one section without a title. The form sends only the editable shown fields that differ from the
 * values it started from, plus `version` on edit, and adds no rules of its own: the server
 * validates, and its errors appear on the shown fields their JSON Pointer names, or above the form.
 * A child collection that changed is sent as its whole row list, and a row's errors appear on its
 * cells. A computed field, of the record or of a row, is shown read-only with the value the server
 * returned, and never sent. So is a sequence field, which is empty on a new record because the
 * server numbers it on create, a creator field, which the server sets to the creating user's id,
 * and a field the form marks read-only. A field the user's grants do not let them write, as the
 * widget's access says, is read-only too: on a new record the `create` fields, and on a saved one
 * the `update` fields. Without the `create` or `update` grant the form has no save bar. The form has
 * no Save or Cancel row: it saves through `SaveBar`, which shows Discard and Save while there is a change, on a
 * wizard's last step, and never on a designer's canvas, see docs/reference/form-widget.md#saving. A saved
 * record shows its history below the form, and an outlined button on the page heading for each of the widget's
 * `startProcess` actions, which lists only the processes the user may start. An action saves the changed fields, then
 * starts its process for the record. A saved record also shows a button for each `navigate` action, after the
 * start buttons. It opens the target page with the record as it was loaded, so unsaved edits are not
 * used, and it is hidden when the SPA cannot work out an expression. A new record has no action buttons. Without
 * a page heading the buttons sit at the top of the form on the right, see
 * docs/reference/form-widget.md#start-process-actions. On a designer's canvas every action is a button, on any step
 * and on a new record, and stays inside the form.
 *
 * A widget that names a form and lets the user save follows the server's form state endpoint as the
 * user edits: a field the answer hides is not shown or sent, a field it requires is marked required,
 * a field it locks is read-only, and its messages show under the fields the user touched. Create and
 * update send the form's name in `?form=`, so the server enforces the form's `requiredWhen`. On a new
 * record the form fills each empty field the user has not touched from the answer's `defaults`, and
 * does not send a default the user leaves unchanged. An error above the form scrolls into view and
 * takes focus, see docs/reference/form-widget.md#saving.
 */
export function FormWidget({ widget, sitePath, pages, recordId, returnTo, locale }: FormWidgetProps) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const navigate = useNavigate()
  const location = useLocation()
  const canvas = useCanvasActions()
  // Both Studio canvases sit inside Studio, whose own save bar uses the shell's slot.
  const formCanvas = useContext(FormCanvasContext)
  const inCanvas = canvas.active || formCanvas !== null
  const { entity } = widget
  const { access } = widget
  const writeAction = recordId === null ? 'create' : 'update'
  const canSave = access === null || access.actions.includes(writeAction)
  const writable = useMemo(() => {
    if (access === null) {
      return undefined
    }
    if (!access.actions.includes(writeAction)) {
      return new Set<string>()
    }
    return new Set(writeAction === 'create' ? access.createFields : access.updateFields)
  }, [access, writeAction])
  const sections = useMemo(() => sectionsOf(entity, widget.form, writable), [entity, widget.form, writable])
  const formName = widget.form?.name
  // The server answers a form state call only to a user who can save the record.
  const conditions = useMemo(
    () =>
      formName !== undefined && canSave
        ? { statePath: formStatePath(entity.recordsPath, formName), recordId }
        : undefined,
    [formName, canSave, entity.recordsPath, recordId],
  )
  const form = useRecordForm(entity, sections, locale, conditions)
  const steps = useFormSteps(widget.form, sections, form)
  const { start: startForm, setFormErrors } = form
  const unsaved = Object.keys(form.changed()).length > 0
  // The guard asks on every step of a wizard, though the save bar shows only on the last.
  useUnsavedChanges(canSave && !inCanvas && unsaved)
  const [load, setLoad] = useState<LoadState>(recordId === null ? 'ready' : 'loading')
  const [conflict, setConflict] = useState(false)
  const [saving, setSaving] = useState(false)
  const [starting, setStarting] = useState(false)
  // The record as it was last loaded or saved, which the navigate buttons read their values from.
  const [record, setRecord] = useState<RecordItem | null>(null)
  const [started, setStarted] = useState(false)
  // The Idempotency-Key of each action's start that got no final answer, by start endpoint, so a
  // retry cannot start a second instance.
  const startKeys = useRef(new Map<string, string>())

  const start = useCallback(
    (record: RecordItem) => {
      startForm(record)
      setRecord(record)
      setConflict(false)
      setLoad('ready')
    },
    [startForm],
  )

  useEffect(() => {
    startKeys.current = new Map()
    if (recordId === null) {
      return
    }
    const controller = new AbortController()
    fetchRecord(entity.recordsPath, recordId, controller.signal)
      .then((record) => (record ? start(record) : setLoad('missing')))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading the record '${recordId}' of '${entity.name}' failed`, error)
          setLoad('failed')
        }
      })
    return () => controller.abort()
  }, [entity.recordsPath, entity.name, recordId, start])

  const reload = async () => {
    if (recordId === null) {
      return
    }
    try {
      const record = await fetchRecord(entity.recordsPath, recordId)
      if (record) {
        start(record)
      } else {
        setLoad('missing')
      }
    } catch (error) {
      console.warn(`Loading the record '${recordId}' of '${entity.name}' failed`, error)
      setFormErrors([t('shell.form.loadFailed')])
    }
  }

  const showSaveProblem = (problem: RecordProblem) => {
    if (Object.keys(problem.errors).length === 0) {
      // The record API answers a stale version with a 409 that names no field.
      if (problem.status === 403) {
        setFormErrors([problem.title])
      } else if (problem.status === 409 && recordId !== null) {
        setConflict(true)
      } else {
        setFormErrors([t('shell.form.saveFailed')])
      }
      return
    }
    steps.openFirstWith(form.showProblem(problem))
  }

  const save = async () => {
    if (!canSave || saving || starting) {
      return
    }
    // A saved record with no change has nothing to send. A new record still saves, so the server's messages show.
    if (recordId !== null && !unsaved) {
      return
    }
    setStarted(false)
    const body = buildRecordBody(entity.fields, form.changed(), recordId === null ? undefined : form.version)
    setSaving(true)
    form.clearErrors()
    setConflict(false)
    try {
      const result = await saveRecord(entity.recordsPath, recordId, body, formName)
      if (result.ok) {
        navigate(returnTo, { state: leaveWithoutAsking })
        return
      }
      showSaveProblem(result.problem)
    } catch (error) {
      console.warn(`Saving a record of '${entity.name}' failed`, error)
      setFormErrors([t('shell.form.saveFailed')])
    } finally {
      setSaving(false)
    }
  }

  // Saves the changed fields, when there are any, as the save button does. Returns whether the
  // record is saved and the start may follow.
  const saveBeforeStart = async (id: string) => {
    const changed = form.changed()
    // An empty update would raise the version and write a history entry for nothing.
    if (Object.keys(changed).length === 0) {
      return true
    }
    try {
      const result = await saveRecord(
        entity.recordsPath,
        id,
        buildRecordBody(entity.fields, changed, form.version),
        formName,
      )
      if (result.ok) {
        start(result.record)
        return true
      }
      showSaveProblem(result.problem)
    } catch (error) {
      console.warn(`Saving a record of '${entity.name}' failed`, error)
      setFormErrors([t('shell.form.saveFailed')])
    }
    return false
  }

  const runAction = async (action: StartProcessAction) => {
    if (recordId === null || saving || starting) {
      return
    }
    setStarting(true)
    setStarted(false)
    form.clearErrors()
    setConflict(false)
    try {
      if (!(await saveBeforeStart(recordId))) {
        return
      }
      // A key is kept only while its start has no final answer: a network error or a 5xx.
      const key = startKeys.current.get(action.instancesPath) ?? newIdempotencyKey()
      startKeys.current.set(action.instancesPath, key)
      let result
      try {
        result = await startProcess(action.instancesPath, recordId, key, action.labelKey)
      } catch (error) {
        console.warn(`Starting the process '${action.process}' for '${recordId}' failed`, error)
        setFormErrors([t('shell.form.startFailed')])
        return
      }
      startKeys.current.delete(action.instancesPath)
      if (result.ok) {
        setStarted(true)
        await reload()
        return
      }
      const { problem } = result
      const messages = Object.values(problem.errors).flat()
      if (messages.length > 0) {
        // A start condition's message is a text key. A fixed message is no key, so it shows as is.
        setFormErrors(messages.map((message) => t(message)))
      } else if (problem.status === 409) {
        setFormErrors([t('shell.form.processRunning')])
      } else {
        setFormErrors([t('shell.form.startFailed')])
      }
    } finally {
      setStarting(false)
    }
  }

  // A refused read on the path of an expression opens nothing and shows a message.
  const runNavigate = async (open: NavigateOpen) => {
    setFormErrors([])
    const address = await open()
    if (address === null) {
      setFormErrors([t('shell.navigate.failed')])
    } else {
      navigate(address, { state: { from: `${location.pathname}${location.search}` } })
    }
  }

  if (load === 'loading') {
    return null
  }
  if (load === 'missing') {
    return <RecordNotFound />
  }
  if (load === 'failed') {
    return <AxisAlert data-testid="form-load-error" kind="error" message={t('shell.form.loadFailed')} />
  }

  // The row under the fields holds a wizard's Back and Next. It is an action row only where Next is a solid button.
  // Without one, Back stays outlined. See docs/reference/actions-and-controls.md#buttons.
  const Row = steps.last ? FooterRow : AxisActionRow
  // A saved record's actions are outlined `main` buttons on the page heading, see docs/reference/form-widget.md#start-process-actions.
  const startButtons =
    canvas.active || (steps.last && recordId !== null)
      ? widget.actions
          .filter((action): action is StartProcessAction => action.type === 'startProcess')
          .map((action) => (
            <AxisButton
              role="main"
              key={action.instancesPath}
              htmlType="button"
              disabled={saving || starting}
              onClick={() => void runAction(action)}
              {...canvas.props(`actions/${widget.actions.indexOf(action)}`)}
            >
              {t(action.labelKey)}
            </AxisButton>
          ))
      : []
  const navigateButtons =
    canvas.active || (recordId !== null && record?.id === recordId)
      ? widget.actions
          .filter((action): action is NavigateAction => action.type === 'navigate')
          .map((action, index) => {
            // The canvas has no record, so its button has no target.
            const open =
              recordId !== null && record
                ? navigateTarget(sitePath, action, { id: recordId, values: record.values })
                : null
            return (
              (canvas.active || open) && (
                <AxisButton
                  role="main"
                  key={index}
                  htmlType="button"
                  disabled={saving || starting}
                  onClick={() => {
                    if (open) {
                      void runNavigate(open)
                    }
                  }}
                  {...canvas.props(`actions/${widget.actions.indexOf(action)}`)}
                >
                  {t(action.labelKey)}
                </AxisButton>
              )
            )
          })
      : []
  const actionButtons = [...startButtons, ...navigateButtons].filter(Boolean)
  return (
    <ConfigProvider locale={antLocaleFor(locale)}>
      {/* The form is the first part and a saved record's history the last, see docs/reference/form-widget.md#history. */}
      <SectionParts>
        <Flex vertical gap="middle">
          {actionButtons.length > 0 && (
            <PageActions inPlace={inCanvas}>
              <Flex gap="small">{actionButtons}</Flex>
            </PageActions>
          )}
          {conflict && (
            <AxisAlert
              data-testid="form-conflict"
              kind="warning"
              message={t('shell.form.conflict')}
              action={
                <AxisButton role="secondary" onClick={reload}>
                  {t('shell.form.reload')}
                </AxisButton>
              }
            />
          )}
          {started && <AxisAlert data-testid="form-started" kind="success" message={t('shell.form.processStarted')} />}
          {form.formError}
          {steps.progress}
          {/* Enter acts as Next on a step before the last, and saves on the last. The form saves through its save bar. */}
          <Form
            layout="vertical"
            requiredMark={requiredMark}
            scrollToFirstError={{ focus: true }}
            onFinish={steps.last ? save : steps.next}
          >
            {/* The form's layout gives the gap below the fields, see docs/reference/form-widget.md#fields-in-columns. */}
            <Flex vertical gap={spacing.lg}>
              {steps.fields}
              {(!steps.first || !steps.last) && (
                <Row>
                  {!steps.first && (
                    <AxisButton role="secondary" onClick={steps.back} disabled={steps.checking}>
                      {t('shell.form.back')}
                    </AxisButton>
                  )}
                  {/* A quick check shows no spinner, because antd can leave one half removed after a flash. */}
                  {!steps.last && (
                    <AxisButton role="finish" htmlType="submit" loading={steps.checking && { delay: 200 }}>
                      {t('shell.form.next')}
                    </AxisButton>
                  )}
                </Row>
              )}
            </Flex>
          </Form>
        </Flex>
        {recordId !== null && (
          <RecordHistory
            key={recordId}
            sitePath={sitePath}
            pages={pages}
            recordsPath={entity.recordsPath}
            recordId={recordId}
            fields={entity.fields}
            locale={locale}
          />
        )}
      </SectionParts>
      {canSave && steps.last && !inCanvas && (
        <SaveBar
          unsaved={unsaved}
          saveable={!saving && !starting}
          busy={saving || starting}
          onDiscard={() => {
            form.discard()
            setConflict(false)
            setStarted(false)
          }}
          onSave={() => void save()}
          data-testid="form-save-bar"
          saveTestId="form-save"
        />
      )}
    </ConfigProvider>
  )
}
