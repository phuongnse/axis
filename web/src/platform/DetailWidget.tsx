import { Alert, Button, Descriptions, Flex, Typography } from 'antd'
import { Fragment, useEffect, useMemo, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { NotFoundPage } from '../pages/NotFoundPage'
import { ChildCollectionTable } from './ChildCollectionTable'
import { formatValue } from './formatValue'
import { navigatePath } from './navigateAction'
import { newIdempotencyKey, startProcess } from './processes'
import { fetchRecord, type RecordItem, type RecordRow, type RecordValue } from './records'
import type { EntityWidgetMetadata, NavigateAction, StartProcessAction } from './site'
import { useText } from './texts'
import { sectionsOf } from './useRecordForm'

interface DetailWidgetProps {
  /** A detail widget: it has an entity, maybe a form, and its `detail` metadata. */
  widget: EntityWidgetMetadata
  sitePath: string
  /** The id of the record to show, from the page parameter the widget's `detail.record` names. */
  recordId: string
  /** The UI locale, for formatting values. */
  locale: string
}

type LoadState = 'loading' | 'ready' | 'missing' | 'failed'

const noRows: RecordRow[] = []

/**
 * Shows one record of the widget's entity, read-only. A widget that names a form shows the form's
 * sections under their titles, with only the fields they list. Otherwise it shows every field in one
 * section without a title. A value looks as it does in a table cell, and a child collection shows its
 * rows in a read-only table. The server leaves out the fields the user may not read. A record that is
 * missing or unreadable shows the not-found page.
 *
 * A button for each `startProcess` action starts its process for the record straight away, with
 * nothing to save first, and then loads the record again. A button for each `navigate` action opens
 * its target page with the record's values, and is hidden when the SPA cannot work out an expression.
 */
export function DetailWidget({ widget, sitePath, recordId, locale }: DetailWidgetProps) {
  const t = useText()
  const navigate = useNavigate()
  const location = useLocation()
  const { entity } = widget
  const sections = useMemo(() => sectionsOf(entity, widget.form), [entity, widget.form])
  const [load, setLoad] = useState<LoadState>('loading')
  const [record, setRecord] = useState<RecordItem | null>(null)
  const [starting, setStarting] = useState(false)
  const [started, setStarted] = useState(false)
  const [errors, setErrors] = useState<string[]>([])
  // The Idempotency-Key of each action's start that got no final answer, by start endpoint, so a
  // retry cannot start a second instance.
  const startKeys = useRef<Record<string, string>>({})

  useEffect(() => {
    startKeys.current = {}
    const controller = new AbortController()
    fetchRecord(entity.recordsPath, recordId, controller.signal)
      .then((loaded) => {
        setRecord(loaded)
        setLoad(loaded ? 'ready' : 'missing')
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading the record '${recordId}' of '${entity.name}' failed`, error)
          setLoad('failed')
        }
      })
    return () => controller.abort()
  }, [entity.recordsPath, entity.name, recordId])

  const reload = async () => {
    try {
      const loaded = await fetchRecord(entity.recordsPath, recordId)
      setRecord(loaded)
      setLoad(loaded ? 'ready' : 'missing')
    } catch (error) {
      console.warn(`Loading the record '${recordId}' of '${entity.name}' failed`, error)
      setErrors([t('shell.form.loadFailed')])
    }
  }

  const runAction = async (action: StartProcessAction) => {
    if (starting) {
      return
    }
    setStarting(true)
    setStarted(false)
    setErrors([])
    try {
      // A key is kept only while its start has no final answer: a network error or a 5xx.
      const key = (startKeys.current[action.instancesPath] ??= newIdempotencyKey())
      let result
      try {
        result = await startProcess(action.instancesPath, recordId, key)
      } catch (error) {
        console.warn(`Starting the process '${action.process}' for '${recordId}' failed`, error)
        setErrors([t('shell.form.startFailed')])
        return
      }
      delete startKeys.current[action.instancesPath]
      if (result.ok) {
        setStarted(true)
        await reload()
        return
      }
      const { problem } = result
      const messages = Object.values(problem.errors).flat()
      if (messages.length > 0) {
        // A start condition's message is a text key. A fixed message is no key, so it shows as is.
        setErrors(messages.map((message) => t(message)))
      } else if (problem.status === 409) {
        setErrors([t('shell.form.processRunning')])
      } else {
        setErrors([t('shell.form.startFailed')])
      }
    } finally {
      setStarting(false)
    }
  }

  if (load === 'loading') {
    return <div data-testid="detail" aria-busy="true" />
  }
  if (load === 'failed') {
    return <Alert data-testid="detail-load-error" type="error" title={t('shell.form.loadFailed')} />
  }
  if (load === 'missing' || record === null) {
    return <NotFoundPage />
  }

  const startActions = widget.actions.filter((action): action is StartProcessAction => action.type === 'startProcess')
  const navigateActions = widget.actions.filter((action): action is NavigateAction => action.type === 'navigate')
  const buttons = [
    ...startActions.map((action) => (
      <Button key={action.instancesPath} disabled={starting} onClick={() => void runAction(action)}>
        {t(action.labelKey)}
      </Button>
    )),
    ...navigateActions.flatMap((action, index) => {
      const path = navigatePath(sitePath, action, { id: recordId, values: record.values })
      return path
        ? [
            <Button
              key={`navigate-${index}`}
              disabled={starting}
              onClick={() => navigate(path, { state: { from: `${location.pathname}${location.search}` } })}
            >
              {t(action.labelKey)}
            </Button>,
          ]
        : []
    }),
  ]

  return (
    <Flex vertical gap="middle" data-testid="detail" aria-busy="false">
      {started && <Alert data-testid="detail-started" type="success" title={t('shell.form.processStarted')} />}
      {errors.length > 0 && <Alert data-testid="detail-error" type="error" title={errors.join(' ')} />}
      {sections.map((section, index) => (
        <Fragment key={index}>
          {section.titleKey !== null && <Typography.Title level={5}>{t(section.titleKey)}</Typography.Title>}
          <Descriptions
            column={1}
            items={section.fields.map(({ field }) => {
              const value = record.values[field.name] ?? null
              return {
                key: field.name,
                label: field.labelKey ? t(field.labelKey) : field.name,
                children:
                  field.type === 'child-collection' ? (
                    <ChildCollectionTable
                      readOnly
                      field={field}
                      rows={Array.isArray(value) ? value : noRows}
                      onChange={() => {}}
                      errors={{}}
                      locale={locale}
                    />
                  ) : (
                    formatValue(field, value as RecordValue, record.labels, { locale, text: t })
                  ),
              }
            })}
          />
        </Fragment>
      ))}
      {buttons.length > 0 && <Flex gap="small">{buttons}</Flex>}
    </Flex>
  )
}
