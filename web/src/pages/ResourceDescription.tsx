import { Input } from 'antd'
import { useContext } from 'react'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { LabelTexts } from './LabelTexts'
import { LabelTextsContext } from './labelTextsContext'
import type { ChangeHandler } from './SchemaForm'

/**
 * Proposed: the description of a resource in an editor of its own, where the schema form does not show it. It sits after
 * the resource's name and label, as it does on the schema form. It shows the texts when the names have loaded, and
 * otherwise the text key.
 */
export function ResourceDescription({ value, onChange }: { value: unknown; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const label = useContext(LabelTextsContext)
  const key = typeof value === 'object' && value !== null && 'textKey' in value ? value.textKey : undefined
  return (
    <div data-testid="studio-field-/description">
      <div>{t('shell.studio.list.description')}</div>
      {names !== undefined && label !== null ? (
        <LabelTexts pointer="/description" value={value} onChange={onChange} />
      ) : (
        <Input
          aria-label={t('shell.studio.list.description')}
          value={typeof key === 'string' ? key : ''}
          onChange={(event) => onChange('/description/textKey', event.target.value)}
        />
      )}
    </div>
  )
}
