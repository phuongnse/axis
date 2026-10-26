import { Button, Flex, Typography } from 'antd'
import { useMemo, useState } from 'react'
import { useText } from '../platform/texts'
import { visuallyHidden } from './pagePaletteSupport'
import { buildProcessGraph, type ProcessGraphTransition, transitionName, transitionPointer } from './processGraph'
import { ProcessStepPanel } from './ProcessEditor'
import type { ChangeHandler } from './SchemaForm'
import StudioProcessGraph from './StudioProcessGraph'

/**
 * The Design tab of a process: the graph of the draft, and beside it the card of the selected step, which is the one
 * the Form tab shows. A node selects a step, and an edge selects itself and the step it leaves. An edit in the card
 * draws the graph again. With an edge selected, the panel offers Remove transition above the card, which clears the
 * transition's target and keeps its branch or outcome. Removing a step leaves the transitions that named it, for the
 * checker to report. Delete in the canvas removes the selected edge, or else the selected step. A hidden live region
 * says what was removed. With no step selected, the panel says so. The canvas comes first, so Tab reaches its edges
 * and nodes before the panel.
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
  const [edge, setEdge] = useState<string | null>(null)
  const [announcement, setAnnouncement] = useState('')
  const graph = useMemo(() => buildProcessGraph(draft, diagnosticPaths), [draft, diagnosticPaths])
  const selectedTransition =
    edge === null ? undefined : graph.transitions.find((t) => t.path === edge && t.source === selected)
  const stepSelected = selected !== null && selected < steps.length

  function selectStep(index: number | null) {
    setEdge(null)
    onSelect(index)
  }

  function selectEdge(transition: ProcessGraphTransition) {
    setEdge(transition.path)
    onSelect(transition.source)
  }

  function removeStep(index: number) {
    const name = graph.steps.find((step) => step.index === index)?.name
    onChange(
      '/steps',
      steps.filter((_, position) => position !== index),
    )
    selectStep(null)
    setAnnouncement(`${t('shell.studio.process.removedStep')} ${name || `/steps/${index}`}`)
  }

  function removeTransition(transition: ProcessGraphTransition) {
    const name = transitionName(graph, transition, t('shell.studio.processGraph.to'))
    onChange(transitionPointer(transition), '')
    setEdge(null)
    setAnnouncement(`${t('shell.studio.process.removedTransition')} ${name}`)
  }

  function removeSelection() {
    if (selectedTransition) {
      removeTransition(selectedTransition)
    } else if (selected !== null && stepSelected) {
      removeStep(selected)
    }
  }

  return (
    <div data-testid="studio-process-design">
      <Flex gap="middle" align="flex-start">
        <div style={{ flex: 1, minWidth: 0 }}>
          <StudioProcessGraph
            graph={graph}
            selected={selected}
            selectedEdge={selectedTransition?.path ?? null}
            onSelect={selectStep}
            onSelectEdge={selectEdge}
            onRemove={removeSelection}
          />
        </div>
        <aside style={{ width: 420, flexShrink: 0 }}>
          {selected !== null && stepSelected ? (
            <Flex vertical gap="small">
              {selectedTransition ? (
                <Flex gap="small" align="center" justify="space-between" data-testid="studio-process-transition">
                  <Typography.Text>
                    {transitionName(graph, selectedTransition, t('shell.studio.processGraph.to'))}
                  </Typography.Text>
                  <Button danger onClick={() => removeTransition(selectedTransition)}>
                    {t('shell.studio.process.removeTransition')}
                  </Button>
                </Flex>
              ) : null}
              <ProcessStepPanel
                value={draft}
                index={selected}
                onChange={onChange}
                onSelect={selectStep}
                onRemove={() => removeStep(selected)}
              />
            </Flex>
          ) : (
            <Typography.Text type="secondary">{t('shell.studio.design.selectStep')}</Typography.Text>
          )}
        </aside>
      </Flex>
      <div role="status" aria-live="polite" data-testid="studio-process-announcement" style={visuallyHidden}>
        {announcement}
      </div>
    </div>
  )
}
