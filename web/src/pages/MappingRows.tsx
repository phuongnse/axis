import { ConfigProvider, Flex, Input, Select, Typography } from 'antd'
import { useContext } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { RowActions } from '../platform/AxisTable'
import { fillText } from '../platform/fillText'
import { useText } from '../platform/texts'
import { ExpressionCheckContext } from './expressionCheckContext'
import { ExpressionField } from './ExpressionField'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

function escapeSegment(segment: string): string {
  return segment.replaceAll('~', '~0').replaceAll('/', '~1')
}

/** What a row maps its key to: an expression, or a field picked from `options`, or typed while they are unknown. */
export type MappingTarget = { kind: 'expression' } | { kind: 'field'; label: string; options?: string[] }

/**
 * A map of the draft as rows of a key and its target, at `pointer`. A row's key is picked from `keyOptions`, or typed
 * while they are unknown. The rows edit the draft through `onChange`: a rename keeps the row's place in the file, and
 * a value that is not an object is left to the JSON view, and so is a row whose entry is an object. Add sits on the
 * title row as a light text button, or as the add choice of the unmapped keys, and each row's Remove is in its "…" menu.
 * A read-only editor, Ant Design's `componentDisabled`, shows neither, see
 * docs/reference/studio-editors-by-kind.md#process-editor.
 */
export function MappingRows({
  pointer,
  title,
  help,
  value,
  keyLabel,
  keyOptions,
  target,
  onChange,
}: {
  pointer: string
  title: string
  help: string
  value: unknown
  keyLabel: string
  keyOptions?: string[]
  target: MappingTarget
  onChange: ChangeHandler
}) {
  const t = useText()
  const check = useContext(ExpressionCheckContext)
  const { componentDisabled } = ConfigProvider.useConfig()

  if (value !== undefined && (typeof value !== 'object' || value === null || Array.isArray(value))) {
    return (
      <div data-testid={`studio-field-${pointer}`}>
        <AxisFieldGroup title={title} help={help}>
          <Typography.Text type="secondary" data-testid="studio-json-only">
            {t('shell.studio.editInJson')}
          </Typography.Text>
        </AxisFieldGroup>
      </div>
    )
  }

  const entries = Object.entries((value ?? {}) as Record<string, unknown>)
  const unmapped = (keyOptions ?? []).filter(
    (option) => !entries.some(([key]) => key.toLowerCase() === option.toLowerCase()),
  )
  const expressionName = t('shell.studio.process.expression')

  function rename(from: string, to: string) {
    if (to === from || entries.some(([key]) => key === to)) {
      return
    }
    onChange(pointer, Object.fromEntries(entries.map(([key, entry]) => [key === from ? to : key, entry])))
  }

  const add =
    keyOptions === undefined ? (
      <AxisButton
        role="secondary"
        disabled={entries.some(([key]) => key === '')}
        onClick={() => onChange(`${pointer}/`, '')}
      >
        {t('shell.studio.process.addMapping')}
      </AxisButton>
    ) : (
      <Select
        aria-label={fillText(t('shell.studio.list.addTitle'), { name: title })}
        placeholder={t('shell.studio.process.addMapping')}
        style={{ minWidth: 240 }}
        showSearch
        virtual={false}
        value={null}
        options={unmapped.map((option) => ({ value: option, label: option }))}
        onChange={(next: string) => onChange(`${pointer}/${escapeSegment(next)}`, '')}
      />
    )

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <AxisFieldGroup title={title} help={help} action={componentDisabled ? undefined : add}>
        <Flex vertical gap="small">
          {entries.map(([key, entry], index) => {
            const rowPointer = `${pointer}/${escapeSegment(key)}`
            const text = typeof entry === 'string' ? entry : ''
            return (
              <Flex key={index} gap="small" align="flex-start" data-testid={`studio-field-${rowPointer}`}>
                <Flex gap="small" align="flex-start" wrap style={{ flex: 1 }}>
                  <div>
                    {keyOptions === undefined ? (
                      <Input aria-label={keyLabel} value={key} onChange={(event) => rename(key, event.target.value)} />
                    ) : (
                      <ReferencePicker
                        name={keyLabel}
                        value={key}
                        required
                        options={keyOptions}
                        onChange={(next) => rename(key, next ?? '')}
                      />
                    )}
                  </div>
                  {typeof entry === 'object' && entry !== null ? (
                    <Typography.Text type="secondary" data-testid="studio-json-only" style={{ flex: 1, minWidth: 240 }}>
                      {t('shell.studio.editInJson')}
                    </Typography.Text>
                  ) : (
                    <div style={{ flex: 1, minWidth: 240 }}>
                      {target.kind === 'field' ? (
                        target.options === undefined ? (
                          <Input
                            aria-label={target.label}
                            value={text}
                            onChange={(event) => onChange(rowPointer, event.target.value)}
                          />
                        ) : (
                          <ReferencePicker
                            name={target.label}
                            value={text}
                            required
                            options={target.options}
                            onChange={(next) => onChange(rowPointer, next ?? '')}
                          />
                        )
                      ) : (
                        <ExpressionField
                          check={check}
                          pointer={rowPointer}
                          name={expressionName}
                          value={text}
                          required
                          onChange={onChange}
                        />
                      )}
                    </div>
                  )}
                </Flex>
                {componentDisabled ? null : (
                  <RowActions
                    fold
                    actions={[
                      {
                        key: 'remove',
                        labelKey: 'shell.studio.process.removeMapping',
                        destructive: true,
                        onClick: () => onChange(rowPointer, undefined),
                      },
                    ]}
                  />
                )}
              </Flex>
            )
          })}
        </Flex>
      </AxisFieldGroup>
    </div>
  )
}
