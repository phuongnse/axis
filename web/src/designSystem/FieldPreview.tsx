import { ConfigProvider, Form } from 'antd'
import { useEffect, useMemo } from 'react'
import { antLocaleFor } from '../platform/antLocale'
import type { FieldType } from '../platform/site'
import { sectionsOf, useRecordForm } from '../platform/useRecordForm'
import { entityWithField, locale, savedRecord } from './fixtures'

/** The input the form widget draws for one field type, with its label and the fixture request's value, and no buttons or history. */
export function FieldPreview({ type }: { type: FieldType }) {
  const entity = useMemo(() => entityWithField(type), [type])
  const sections = useMemo(() => sectionsOf(entity, null), [entity])
  const form = useRecordForm(entity, sections, locale)
  const { start } = form
  useEffect(() => start(savedRecord), [start])
  return (
    <ConfigProvider locale={antLocaleFor(locale)}>
      <Form layout="vertical">{form.fields}</Form>
    </ConfigProvider>
  )
}
