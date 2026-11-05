import { Select, Tag } from 'antd'
import { asChoice, choiceNames, type ReferenceChoice, type ReferenceOption } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useTextOfKey } from './useTextOfKey'

/**
 * A searchable choice of the names an application defines. A value that is not one of them stays visible and is
 * tagged unknown, so a file that names something gone still shows it. Proposed: a choice shows the label people know it
 * by, which it resolves from the choice's `labelKey`, alone, and the name only in place of a missing label and after the
 * label of choices that share one. The search finds the label or the name, and the value stays the name.
 */
export function ReferencePicker({
  id,
  name,
  value,
  required,
  options,
  block = false,
  onChange,
}: {
  /** Proposed: the id a label's `htmlFor` names. */
  id?: string
  name: string
  value: string | undefined
  required: boolean
  options: readonly ReferenceOption[]
  /** Proposed: fills its column, as an input beside it does, rather than its 240 px. */
  block?: boolean
  onChange: (next: string | undefined) => void
}) {
  const t = useText()
  const textOfKey = useTextOfKey()
  const { colour, spacing } = useDesignTokens()
  const current = value === undefined || value === '' ? undefined : value
  const names = choiceNames(options)
  const all = options.map(asChoice)
  const labelOf = (option: ReferenceChoice) => textOfKey(option.labelKey)
  const match =
    current === undefined ? undefined : names.find((option) => option.toLowerCase() === current.toLowerCase())
  const choices = [
    ...(current !== undefined && match === undefined
      ? [
          {
            value: current,
            label: (
              <span data-testid="studio-reference-unknown">
                {current} <Tag color="warning">{t('shell.studio.unknownName')}</Tag>
              </span>
            ),
          },
        ]
      : []),
    ...all.map((option) => {
      const text = labelOf(option)
      // The name shows beside the label only where the label alone would leave two options alike.
      const alike = text !== null && all.some((other) => other !== option && labelOf(other) === text)
      return {
        value: option.name,
        search: text ? `${text} ${option.name}` : option.name,
        label:
          text !== null && text !== option.name ? (
            <span>
              {text}
              {alike && <span style={{ color: colour.textMuted, marginInlineStart: spacing.xs }}>{option.name}</span>}
            </span>
          ) : (
            option.name
          ),
      }
    }),
  ]

  return (
    <Select
      id={id}
      aria-label={name}
      showSearch
      virtual={false}
      optionFilterProp="search"
      allowClear={!required}
      style={block ? { width: '100%' } : { minWidth: 240 }}
      value={match ?? current}
      options={choices}
      onChange={(next: string | undefined) => onChange(next ?? (required ? '' : undefined))}
    />
  )
}
