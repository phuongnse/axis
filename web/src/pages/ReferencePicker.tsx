import { Select, Tag } from 'antd'
import { useContext } from 'react'
import { labelText, StudioNamesContext, type ReferenceOption } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'

/**
 * A searchable choice of the names an application defines. Each choice shows its label in the author's language, else
 * in `en`, else its name, and muted after the label when choices share one. The search finds a choice by its label or
 * its name, and the value written is the name, see [reference pickers](docs/reference/studio.md#reference-pickers). A
 * plain string is a choice with no label. A value that is not one of the choices stays visible and is tagged unknown,
 * so a file that names something gone still shows it. The picker takes no other text.
 */
export function ReferencePicker({
  name,
  id,
  'aria-describedby': describedBy,
  'aria-required': ariaRequired,
  value,
  required,
  options,
  onChange,
}: {
  name: string
  /** The id, description and required mark that `AxisField` gives its input. */
  id?: string
  'aria-describedby'?: string
  'aria-required'?: boolean
  value: string | undefined
  required: boolean
  options: readonly (string | ReferenceOption)[]
  onChange: (next: string | undefined) => void
}) {
  const t = useText()
  const { colour } = useDesignTokens()
  const names = useContext(StudioNamesContext)
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const entries = options.map((option) => {
    const { name, labelKey } = typeof option === 'string' ? { name: option, labelKey: undefined } : option
    return { name, label: names === undefined ? undefined : labelText(names, labelKey, locale) }
  })
  const shared = new Set<string>()
  const seen = new Set<string>()
  for (const { label } of entries) {
    if (label !== undefined) {
      if (seen.has(label)) {
        shared.add(label)
      }
      seen.add(label)
    }
  }
  const current = value === undefined || value === '' ? undefined : value
  const match =
    current === undefined
      ? undefined
      : entries.find((entry) => entry.name.toLowerCase() === current.toLowerCase())?.name
  const choices = [
    ...(current !== undefined && match === undefined
      ? [
          {
            value: current,
            label: (
              <span data-testid="studio-reference-unknown">
                {/* oxlint-disable-next-line axis/no-preset-tag-colour -- Predates the colour tokens; colour it with a token. */}
                {current} <Tag color="warning">{t('shell.studio.unknownName')}</Tag>
              </span>
            ),
          },
        ]
      : []),
    ...entries.map(({ name, label }) => ({
      value: name,
      text: label ?? '',
      label:
        label === undefined ? (
          name
        ) : shared.has(label) ? (
          <>
            {label} <span style={{ color: colour.textMuted }}>{name}</span>
          </>
        ) : (
          label
        ),
    })),
  ]

  return (
    <Select
      aria-label={name}
      id={id}
      aria-describedby={describedBy}
      aria-required={ariaRequired}
      showSearch
      virtual={false}
      allowClear={!required}
      style={{ minWidth: 240 }}
      value={match ?? current}
      options={choices}
      filterOption={(input, option) => {
        const typed = input.toLowerCase()
        return (
          String(option?.value ?? '')
            .toLowerCase()
            .includes(typed) || ((option as { text?: string } | undefined)?.text ?? '').toLowerCase().includes(typed)
        )
      }}
      onChange={(next: string | undefined) => onChange(next ?? (required ? '' : undefined))}
    />
  )
}
