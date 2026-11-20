import { ExportOutlined } from '@ant-design/icons'
import { Select, Space, Tag } from 'antd'
import { useContext } from 'react'
import { useHref, useLinkClickHandler } from 'react-router'
import { AxisButton } from '../platform/AxisButton'
import { studioResourceAddress } from '../platform/studioAddress'
import { labelText, StudioApplicationContext, StudioNamesContext, type ReferenceOption } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'

/**
 * A searchable choice of the names an application defines. Each choice shows its label in the author's language, else
 * in `en`, else its name, and muted after the label when choices share one. The search finds a choice by its label or
 * its name, and the value written is the name, see [reference pickers](docs/reference/studio.md#reference-pickers). A
 * plain string is a choice with no label. A choice may carry a ready `label`, already in the reader's language, which
 * shows in place of the names' label. A value that is not one of the choices stays visible and is tagged unknown,
 * so a file that names something gone still shows it. A chosen resource of the application Studio has open gets an Open
 * link joined after the select, see [reference pickers](docs/reference/studio.md#reference-pickers).
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
  width,
  placeholder,
}: {
  name: string
  /** The id, description, and required mark and state that `AxisField` gives its input. */
  id?: string
  'aria-describedby'?: string
  'aria-required'?: boolean
  value: string | undefined
  required: boolean
  options: readonly (string | ReferenceOption)[]
  onChange: (next: string | undefined) => void
  /** The select's width in px, at least 240 by default. */
  width?: number
  placeholder?: string
}) {
  const t = useText()
  const { colour } = useDesignTokens()
  const names = useContext(StudioNamesContext)
  const application = useContext(StudioApplicationContext)
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const entries = options.map((option) => {
    const { name, labelKey, id, label } =
      typeof option === 'string' ? { name: option, labelKey: undefined, id: undefined, label: undefined } : option
    return {
      name,
      id,
      label: label ?? (names === undefined ? undefined : labelText(names, labelKey, locale)),
    }
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
  const chosen =
    current === undefined ? undefined : entries.find((entry) => entry.name.toLowerCase() === current.toLowerCase())
  const match = chosen?.name
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

  const select = (
    <Select
      aria-label={name}
      id={id}
      aria-describedby={describedBy}
      aria-required={ariaRequired}
      showSearch
      virtual={false}
      allowClear={!required}
      style={width === undefined ? { minWidth: 240 } : { width }}
      placeholder={placeholder}
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
  if (application === undefined || chosen?.id === undefined) {
    return select
  }
  return (
    <Space.Compact>
      {select}
      <OpenResource application={application} id={chosen.id} name={name} />
    </Space.Compact>
  )
}

/** The Open button joined after the select: a real link to the resource in Studio, so Ctrl+click opens a new tab. */
function OpenResource({ application, id, name }: { application: string; id: string; name: string }) {
  const address = studioResourceAddress(application, id)
  const href = useHref(address)
  const open = useLinkClickHandler<HTMLElement>(address)
  return (
    <AxisButton
      role="icon"
      joined
      nameKey="shell.studio.reference.open"
      nameSuffix={name}
      icon={<ExportOutlined />}
      href={href}
      onClick={open}
    />
  )
}
