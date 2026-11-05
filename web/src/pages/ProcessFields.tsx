import { Input } from 'antd'
import { useContext } from 'react'
import { referenceOptions, StudioNamesContext } from '../platform/studioNames'
import { ReferencePicker } from './ReferencePicker'

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

/** A picker over the application's names of one kind, or a plain box while the names have not loaded. */
export function NamePicker({
  pointer,
  label,
  kind,
  value,
  onPick,
}: {
  pointer: string
  label: string
  kind: string
  value: unknown
  onPick: (next: string) => void
}) {
  const names = useContext(StudioNamesContext)

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <div>{label}</div>
      {names === undefined ? (
        <Input aria-label={label} value={text(value)} onChange={(event) => onPick(event.target.value)} />
      ) : (
        <ReferencePicker
          name={label}
          value={text(value)}
          required
          options={referenceOptions(names, kind, undefined, pointer)}
          onChange={(next) => onPick(next ?? '')}
        />
      )}
    </div>
  )
}
