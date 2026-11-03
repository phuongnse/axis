import { Flex, Select, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { fetchStudioBindings, type StudioBindingNames } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

let loading: Promise<StudioBindingNames> | null = null

/** Proposed: the tenant's binding names, loaded once a page and shared by every picker on it. */
function useBindingNames(): StudioBindingNames | null {
  const [names, setNames] = useState<StudioBindingNames | null>(null)
  useEffect(() => {
    loading ??= fetchStudioBindings().catch((error: unknown) => {
      loading = null
      throw error
    })
    let live = true
    loading.then(
      (value) => live && setNames(value),
      (error: unknown) => console.warn('Loading the binding names failed', error),
    )
    return () => {
      live = false
    }
  }, [])
  return names
}

/**
 * Proposed: a deployment binding chosen by name from the tenant's bindings of its kind, url, secret or storage, as a
 * release names a binding and never holds its value; the picker shows no value either. An optional binding clears to
 * its placeholder, such as the tenant's database for file storage. A name the tenant does not have stays shown, with
 * a line that says so, as activation would refuse it (AXC0100); the operator adds it with `tenant binding set`.
 */
export function BindingPicker({
  kind,
  label,
  value,
  placeholder,
  onChange,
}: {
  kind: keyof StudioBindingNames
  label: string
  value: string | undefined
  placeholder?: string
  onChange: (next: string | undefined) => void
}) {
  const t = useText()
  const { colour } = useDesignTokens()
  const names = useBindingNames()
  const known = names?.[kind] ?? []
  const missing = value !== undefined && value !== '' && names !== null && !known.includes(value)
  const options = [...known, ...(missing ? [value] : [])].map((name) => ({ value: name, label: name }))
  return (
    <Flex vertical gap={4}>
      <Select
        aria-label={label}
        style={{ width: '100%' }}
        showSearch
        allowClear={placeholder !== undefined}
        loading={names === null}
        placeholder={placeholder ?? t('shell.studio.binding.choose')}
        value={value || undefined}
        options={options}
        notFoundContent={t(`shell.studio.binding.none.${kind}`)}
        onChange={(next: string | undefined) => onChange(next)}
      />
      {missing && (
        <Typography.Text style={{ color: colour.warningText, fontSize: 12 }}>
          {t(`shell.studio.binding.missing.${kind}`).replace('{name}', value)}
        </Typography.Text>
      )}
    </Flex>
  )
}
