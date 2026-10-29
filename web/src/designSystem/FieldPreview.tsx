import { ConfigProvider, Form } from 'antd'
import { useEffect, useMemo, useRef } from 'react'
import { antLocaleFor } from '../platform/antLocale'
import { requiredMark } from '../platform/requiredMark'
import type { RecordItem } from '../platform/records'
import { sectionsOf, useRecordForm } from '../platform/useRecordForm'
import { entityWithField, locale, savedRecord, type PreviewFieldType } from './fixtures'

interface FieldPreviewProps {
  type: PreviewFieldType
  required?: boolean
  readOnly?: boolean
  /** The text key of the help text under the label. */
  helpKey?: string
  /** The record whose value the input starts with. A caller passes a module-level constant. */
  record?: RecordItem
  /** The messages to show, by JSON Pointer. A caller passes a module-level constant. */
  problem?: Record<string, string[]>
}

/** The form widget's input for one field type, with its label and a record's value, and no buttons or history. */
export function FieldPreview({
  type,
  required = false,
  readOnly = false,
  helpKey,
  record = savedRecord,
  problem,
}: FieldPreviewProps) {
  const entity = useMemo(() => entityWithField(type, required, helpKey ?? null), [type, required, helpKey])
  const sections = useMemo(() => sectionsOf(entity, null, readOnly ? new Set() : undefined), [entity, readOnly])
  const form = useRecordForm(entity, sections, locale)
  const { start, showProblem, version } = form
  const showed = useRef(false)
  useEffect(() => start(record), [start, record])
  // A cell's error needs its row, which the form has once it has started from the record.
  useEffect(() => {
    if (problem && !showed.current && version === record.version) {
      showed.current = true
      showProblem({ status: 400, title: '', errors: problem })
    }
  }, [problem, record.version, showProblem, version])
  return (
    <ConfigProvider locale={antLocaleFor(locale)}>
      <Form layout="vertical" requiredMark={requiredMark}>
        {form.fields}
      </Form>
    </ConfigProvider>
  )
}
