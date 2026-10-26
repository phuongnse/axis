import { Flex, Typography } from 'antd'
import { useText } from '../platform/texts'
import { ProcessStepPanel } from './ProcessEditor'
import type { ChangeHandler } from './SchemaForm'
import StudioProcessGraph from './StudioProcessGraph'

/**
 * The Design tab of a process: the graph of the draft, and beside it the card of the selected step, which is the one
 * the Form tab shows. A node or an edge selects a step, and an edit in the card draws the graph again. With no step
 * selected, the panel says so. The canvas comes first, so Tab reaches the nodes before the panel.
 */
export default function StudioProcessDesign({
  draft,
  diagnosticPaths,
  selected,
  onSelect,
  onChange,
}: {
  draft: Record<string, unknown>
  diagnosticPaths: readonly string[]
  selected: number | null
  onSelect: (index: number | null) => void
  onChange: ChangeHandler
}) {
  const t = useText()
  const steps = Array.isArray(draft.steps) ? draft.steps : []

  return (
    <div data-testid="studio-process-design">
      <Flex gap="middle" align="flex-start">
        <div style={{ flex: 1, minWidth: 0 }}>
          <StudioProcessGraph draft={draft} selected={selected} diagnosticPaths={diagnosticPaths} onSelect={onSelect} />
        </div>
        <aside style={{ width: 420, flexShrink: 0 }}>
          {selected !== null && selected < steps.length ? (
            <ProcessStepPanel value={draft} index={selected} onChange={onChange} onSelect={onSelect} />
          ) : (
            <Typography.Text type="secondary">{t('shell.studio.design.selectStep')}</Typography.Text>
          )}
        </aside>
      </Flex>
    </div>
  )
}
