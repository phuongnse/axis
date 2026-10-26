import { DndContext, DragOverlay, PointerSensor, useSensor, useSensors, type DragEndEvent } from '@dnd-kit/core'
import { Button, Flex, Typography } from 'antd'
import { useMemo, useState } from 'react'
import { useText } from '../platform/texts'
import { innermostCollision, silent } from './designDrag'
import { visuallyHidden } from './pagePaletteSupport'
import { buildProcessGraph, type ProcessGraphTransition, transitionName, transitionPointer } from './processGraph'
import { ProcessStepPanel } from './ProcessEditor'
import { ProcessPalette } from './ProcessPalette'
import { addStep, entryLabel, insertInto, type Added, type PaletteEntry } from './processPalette'
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
 *
 * The palette at the top of the panel adds a step of a type, as one change of `/steps`, and selects the new step. A
 * pointer drag from an entry to the canvas adds an unconnected step, and a drag to an edge inserts the step into that
 * transition. A click, Enter or Space on an entry inserts after the selection: into the selected edge, else into the
 * `next` of the selected step, else unconnected. An `end` step is refused where it would replace a target. The live
 * region says what was added, after which step, or why nothing was.
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
  const [dragging, setDragging] = useState<PaletteEntry>()
  // Only the pointer drags, so Enter and Space reach the palette's buttons.
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }))

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

  function nameOfStep(index: number): string | undefined {
    return graph.steps.find((step) => step.index === index)?.name || undefined
  }

  /** Applies an add as one change, selects the new step and says it, after `source` when there is one. */
  function land(result: Added | 'endRefused', source: number | undefined) {
    if (result === 'endRefused') {
      setAnnouncement(t('shell.studio.process.endOnTransition'))
      return
    }
    onChange('/steps', result.steps)
    selectStep(result.index)
    const after = source === undefined ? undefined : nameOfStep(source)
    const text = `${t('shell.studio.process.stepAdded')} ${result.name}`
    setAnnouncement(
      after === undefined
        ? `${text} ${t('shell.studio.process.added')}`
        : `${text} ${t('shell.studio.process.addedAfter')} ${after}`,
    )
  }

  function insertAfterSelection(entry: PaletteEntry) {
    const step: unknown = selected === null ? undefined : steps[selected]
    const type = (step as { type?: unknown } | undefined)?.type
    if (selectedTransition) {
      land(insertInto(draft, transitionPointer(selectedTransition), entry), selectedTransition.source)
    } else if (selected !== null && (type === 'operation' || type === 'subProcess')) {
      land(insertInto(draft, `/steps/${selected}/next`, entry), selected)
    } else {
      land(addStep(draft, entry), undefined)
    }
  }

  function drop({ active, over }: DragEndEvent) {
    setDragging(undefined)
    const entry = (active.data.current as { entry?: PaletteEntry } | undefined)?.entry
    const pointer = (over?.data.current as { pointer?: unknown } | undefined)?.pointer
    if (entry === undefined || typeof pointer !== 'string') {
      return
    }
    if (pointer === '/steps') {
      land(addStep(draft, entry), undefined)
      return
    }
    const transition = graph.transitions.find((candidate) => candidate.path === pointer)
    if (transition) {
      land(insertInto(draft, transitionPointer(transition), entry), transition.source)
    }
  }

  function removeSelection() {
    if (selectedTransition) {
      removeTransition(selectedTransition)
    } else if (selected !== null && stepSelected) {
      removeStep(selected)
    }
  }

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={innermostCollision}
      accessibility={{ announcements: silent }}
      onDragStart={({ active }) => setDragging((active.data.current as { entry?: PaletteEntry } | undefined)?.entry)}
      onDragCancel={() => setDragging(undefined)}
      onDragEnd={drop}
    >
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
            <Flex vertical gap="middle">
              <ProcessPalette onInsert={insertAfterSelection} />
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
            </Flex>
          </aside>
        </Flex>
        <div role="status" aria-live="polite" data-testid="studio-process-announcement" style={visuallyHidden}>
          {announcement}
        </div>
      </div>
      <DragOverlay dropAnimation={null}>
        {dragging === undefined ? null : (
          <Button>{entryLabel(dragging, t('shell.studio.process.packageOperation'))}</Button>
        )}
      </DragOverlay>
    </DndContext>
  )
}
