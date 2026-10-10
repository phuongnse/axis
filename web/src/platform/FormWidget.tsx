import { Alert, Button, Flex, Form } from 'antd'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { NotFoundPage } from '../pages/NotFoundPage'
import { buildRecordBody } from './recordBody'
import { RecordHistory } from './RecordHistory'
import { fetchRecord, saveRecord, type RecordItem } from './records'
import type { EntityWidgetMetadata } from './site'
import { useText } from './texts'
import { sectionsOf, useRecordForm } from './useRecordForm'

interface FormWidgetProps {
  widget: EntityWidgetMetadata
  /** The record to edit, or `null` to create one. */
  recordId: string | null
  /** Where save and cancel go: the table page the user came from. */
  returnTo: string
  /** The UI locale, for showing computed and sequence values. */
  locale: string
}

type LoadState = 'loading' | 'ready' | 'missing' | 'failed'

/**
 * Creates or edits one record of the widget's entity. A widget that names a form shows the form's
 * sections under their titles, with only the fields they list. Otherwise it shows every field in
 * one section without a title. The form sends only the editable shown fields that differ from the
 * values it started from, plus `version` on edit, and adds no rules of its own: the server
 * validates, and its errors appear on the shown fields their JSON Pointer names, or above the form.
 * A child collection that changed is sent as its whole row list, and a row's errors appear on its
 * cells. A computed field, of the record or of a row, is shown read-only with the value the server
 * returned, and never sent. So is a sequence field, which is empty on a new record because the
 * server numbers it on create, and so is a field the form marks read-only. A saved record shows its
 * history below the form. A new record has none.
 */
export function FormWidget({ widget, recordId, returnTo, locale }: FormWidgetProps) {
  const t = useText()
  const navigate = useNavigate()
  const { entity } = widget
  const sections = useMemo(() => sectionsOf(entity, widget.form), [entity, widget.form])
  const form = useRecordForm(entity, sections, locale)
  const { start: startForm, setFormErrors } = form
  const [load, setLoad] = useState<LoadState>(recordId === null ? 'ready' : 'loading')
  const [conflict, setConflict] = useState(false)
  const [saving, setSaving] = useState(false)

  const start = useCallback(
    (record: RecordItem) => {
      startForm(record)
      setConflict(false)
      setLoad('ready')
    },
    [startForm],
  )

  useEffect(() => {
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

  const save = async () => {
    if (saving) {
      return
    }
    const body = buildRecordBody(entity.fields, form.changed(), recordId === null ? undefined : form.version)
    setSaving(true)
    form.clearErrors()
    setConflict(false)
    try {
      const result = await saveRecord(entity.recordsPath, recordId, body)
      if (result.ok) {
        navigate(returnTo)
        return
      }
      const { problem } = result
      if (Object.keys(problem.errors).length === 0) {
        // The record API answers a stale version with a 409 that names no field.
        if (problem.status === 409 && recordId !== null) {
          setConflict(true)
        } else {
          setFormErrors([t('shell.form.saveFailed')])
        }
        return
      }
      form.showProblem(problem)
    } catch (error) {
      console.warn(`Saving a record of '${entity.name}' failed`, error)
      setFormErrors([t('shell.form.saveFailed')])
    } finally {
      setSaving(false)
    }
  }

  if (load === 'loading') {
    return null
  }
  if (load === 'missing') {
    return <NotFoundPage />
  }
  if (load === 'failed') {
    return <Alert data-testid="form-load-error" type="error" message={t('shell.form.loadFailed')} />
  }

  return (
    <Flex vertical gap="middle">
      {conflict && (
        <Alert
          data-testid="form-conflict"
          type="warning"
          message={t('shell.form.conflict')}
          action={<Button onClick={reload}>{t('shell.form.reload')}</Button>}
        />
      )}
      {form.formErrors.length > 0 && (
        <Alert data-testid="form-error" type="error" message={form.formErrors.join(' ')} />
      )}
      <Form layout="vertical" onFinish={save}>
        {form.fields}
        <Flex gap="small">
          <Button htmlType="submit" type="primary" loading={saving}>
            {t('shell.form.save')}
          </Button>
          <Button onClick={() => navigate(returnTo)}>{t('shell.form.cancel')}</Button>
        </Flex>
      </Form>
      {recordId !== null && (
        <RecordHistory
          key={recordId}
          recordsPath={entity.recordsPath}
          recordId={recordId}
          fields={entity.fields}
          locale={locale}
        />
      )}
    </Flex>
  )
}
