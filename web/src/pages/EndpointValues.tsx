import { Button, Flex, Input, Select, Typography } from 'antd'
import { useContext } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { EmptyState } from '../platform/EmptyState'
import { SectionPart } from '../platform/SectionParts'
import { useText } from '../platform/texts'
import { ExpressionCheckContext } from './expressionCheckContext'
import { ExpressionField } from './ExpressionField'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

function escapeSegment(segment: string): string {
  return segment.replaceAll('~', '~0').replaceAll('/', '~1')
}

/**
 * The `values` of an endpoint as rows of a field of its entity and the expression that gives the field its value. A
 * row's field is picked from `fields`, or typed while they are unknown. The rows edit the draft through `onChange`: a
 * rename keeps the row's place in the file, and a value that is not an object is left to the JSON view. The rows sit in
 * a part headed with the values' title and help, and an endpoint with no values says so. Its Add mapping sits on the right
 * of the part's heading, see docs/reference/studio-resource-form.md#sections.
 */
export function EndpointValues({
  fields,
  values,
  onChange,
}: {
  fields: string[] | undefined
  values: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const check = useContext(ExpressionCheckContext)

  const heading = {
    title: t('shell.studio.field.endpoint.values'),
    line: t('shell.studio.field.endpoint.values.help'),
  }

  if (values !== undefined && (typeof values !== 'object' || values === null || Array.isArray(values))) {
    return (
      <SectionPart testName="values" heading={heading}>
        <div data-testid="studio-field-/values">
          <Typography.Text type="secondary" data-testid="studio-json-only">
            {t('shell.studio.editInJson')}
          </Typography.Text>
        </div>
      </SectionPart>
    )
  }

  const entries = Object.entries((values ?? {}) as Record<string, unknown>)
  const unmapped = (fields ?? []).filter((field) => !entries.some(([key]) => key.toLowerCase() === field.toLowerCase()))

  function rename(from: string, to: string) {
    if (to === from || entries.some(([key]) => key === to)) {
      return
    }
    onChange('/values', Object.fromEntries(entries.map(([key, value]) => [key === from ? to : key, value])))
  }

  // Add mapping sits on the right of the heading, see docs/reference/studio-resource-form.md#sections. An explicit `false`
  // would override a read-only editor's componentDisabled, so the button passes `undefined` unless a blank key holds it.
  const add =
    fields === undefined ? (
      <AxisButton
        role="secondary"
        disabled={entries.some(([key]) => key === '') || undefined}
        onClick={() => onChange('/values/', '')}
      >
        {t('shell.studio.endpoint.addMapping')}
      </AxisButton>
    ) : (
      <Select
        aria-label={t('shell.studio.endpoint.addMapping')}
        placeholder={t('shell.studio.endpoint.addMapping')}
        style={{ minWidth: 240 }}
        showSearch
        virtual={false}
        value={null}
        options={unmapped.map((field) => ({ value: field, label: field }))}
        onChange={(next: string) => onChange(`/values/${escapeSegment(next)}`, '')}
      />
    )

  return (
    <SectionPart testName="values" heading={heading} action={add}>
      <div data-testid="studio-field-/values">
        <Flex vertical gap="small">
          {entries.map(([key, expression], index) => {
            const pointer = `/values/${escapeSegment(key)}`
            const text = typeof expression === 'string' ? expression : ''
            return (
              <Flex key={index} gap="small" align="flex-start" wrap data-testid={`studio-field-${pointer}`}>
                <div>
                  {fields === undefined ? (
                    <Input
                      aria-label={t('shell.studio.endpoint.field')}
                      value={key}
                      onChange={(event) => rename(key, event.target.value)}
                    />
                  ) : (
                    <ReferencePicker
                      name={t('shell.studio.endpoint.field')}
                      value={key}
                      required
                      options={fields}
                      onChange={(next) => rename(key, next ?? '')}
                    />
                  )}
                </div>
                <div style={{ flex: 1, minWidth: 240 }}>
                  <ExpressionField
                    check={check}
                    pointer={pointer}
                    name={t('shell.studio.endpoint.expression')}
                    value={text}
                    required
                    onChange={onChange}
                  />
                </div>
                {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                <Button onClick={() => onChange(pointer, undefined)}>{t('shell.studio.endpoint.removeMapping')}</Button>
              </Flex>
            )
          })}
          {entries.length === 0 && <EmptyState text={t('shell.studio.endpoint.noValues')} />}
        </Flex>
      </div>
    </SectionPart>
  )
}
