import { Select, Tag } from 'antd'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

/**
 * A searchable choice of the names an application defines. A value that is not one of them stays visible and is
 * tagged unknown, so a file that names something gone still shows it. The picker takes no other text.
 */
export function ReferencePicker({
  name,
  value,
  required,
  options,
  block = false,
  labelOf,
  onChange,
}: {
  name: string
  value: string | undefined
  required: boolean
  options: string[]
  /** Proposed: fills its column, as an input beside it does, rather than its 240 px. */
  block?: boolean
  /**
   * Proposed: the text people know an option by, such as a field's label. The picker shows it first, with the name after
   * it in textMuted, and the search finds either. The value stays the name. An option with no text shows its name.
   */
  labelOf?: (option: string) => string | null
  onChange: (next: string | undefined) => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const current = value === undefined || value === '' ? undefined : value
  const match =
    current === undefined ? undefined : options.find((option) => option.toLowerCase() === current.toLowerCase())
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
    ...options.map((option) => {
      const text = labelOf?.(option)
      return {
        value: option,
        search: text ? `${text} ${option}` : option,
        label:
          text && text !== option ? (
            <span>
              {text} <span style={{ color: colour.textMuted, marginInlineStart: spacing.xs }}>{option}</span>
            </span>
          ) : (
            option
          ),
      }
    }),
  ]

  return (
    <Select
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
