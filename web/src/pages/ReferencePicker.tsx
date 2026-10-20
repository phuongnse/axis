import { Select, Tag } from 'antd'
import { useText } from '../platform/texts'

/**
 * A searchable choice of the names an application defines. A value that is not one of them stays visible and is
 * tagged unknown, so a file that names something gone still shows it. The picker takes no other text.
 */
export function ReferencePicker({
  name,
  value,
  required,
  options,
  onChange,
}: {
  name: string
  value: string | undefined
  required: boolean
  options: string[]
  onChange: (next: string | undefined) => void
}) {
  const t = useText()
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
    ...options.map((option) => ({ value: option, label: option })),
  ]

  return (
    <Select
      aria-label={name}
      showSearch
      virtual={false}
      allowClear={!required}
      style={{ minWidth: 240 }}
      value={match ?? current}
      options={choices}
      onChange={(next: string | undefined) => onChange(next ?? (required ? '' : undefined))}
    />
  )
}
