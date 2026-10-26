import { Button, Flex, Menu, Typography } from 'antd'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { visuallyHidden } from './pagePaletteSupport'
import { choiceLabel, type ConnectChoice, connectChoices, connectEdit } from './processConnect'
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
 * and nodes before the panel. A drag from a step's handle to another step sets a transition. A step with one `next`
 * sets it at once. A decision or a task opens a menu at the drop point that lists its branches, `otherwise` or
 * outcomes, and a new branch or outcome. Escape, a click outside the menu or any other edit closes it and changes
 * nothing. Each drag or menu choice is one edit, so one Undo, and the live region says it, such as "submit now goes to
 * done".
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
  const { colour } = useDesignTokens()
  const steps = Array.isArray(draft.steps) ? draft.steps : []
  const [edge, setEdge] = useState<string | null>(null)
  const [announcement, setAnnouncement] = useState('')
  const [requested, setPending] = useState<{
    draft: Record<string, unknown>
    source: number
    target: number
    point: { x: number; y: number }
  } | null>(null)
  // The menu names steps by number, so an edit of the draft closes it.
  const pending = requested?.draft === draft ? requested : null
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

  function apply(source: number, target: number, choice: ConnectChoice) {
    const edit = connectEdit(steps, source, target, choice)
    if (!edit) {
      return
    }
    const nameOf = (index: number) => {
      const step: unknown = steps[index]
      const name = typeof step === 'object' && step !== null ? (step as { name?: unknown }).name : undefined
      return typeof name === 'string' ? name : `/steps/${index}`
    }
    let label = ''
    if (choice.kind === 'newBranch') {
      const count = Array.isArray(edit.value) ? edit.value.length : 1
      label = `${t('shell.studio.process.branchNumber')} ${count}`
    } else if (choice.kind === 'newOutcome') {
      const added: unknown = Array.isArray(edit.value) ? edit.value.at(-1) : undefined
      label = (added as { name: string }).name
    } else if (choice.kind !== 'next') {
      label = choiceLabel(steps[source], choice, t)
    }
    onChange(edit.pointer, edit.value)
    setPending(null)
    setAnnouncement(
      `${nameOf(source)}${label ? ` · ${label}` : ''} ${t('shell.studio.process.nowGoesTo')} ${nameOf(target)}`,
    )
  }

  function connect(source: number, target: number, point: { x: number; y: number }) {
    const choices = connectChoices(steps[source])
    if (choices.length === 0 || source === target) {
      return
    }
    if (choices.length === 1 && choices[0].kind === 'next') {
      apply(source, target, choices[0])
    } else {
      setPending({ draft, source, target, point })
    }
  }

  function removeSelection() {
    if (selectedTransition) {
      removeTransition(selectedTransition)
    } else if (selected !== null && stepSelected) {
      removeStep(selected)
    }
  }

  const menu = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!pending) {
      return
    }
    menu.current?.querySelector<HTMLElement>('[role="menuitem"]')?.focus()
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setPending(null)
      }
    }
    const onPointerDown = (event: PointerEvent) => {
      if (!(event.target instanceof Node) || !menu.current?.contains(event.target)) {
        setPending(null)
      }
    }
    document.addEventListener('keydown', onKeyDown)
    document.addEventListener('pointerdown', onPointerDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      document.removeEventListener('pointerdown', onPointerDown)
    }
  }, [pending])

  return (
    <div data-testid="studio-process-design">
      <Flex gap="middle" align="flex-start">
        <div style={{ flex: 1, minWidth: 0, position: 'relative' }}>
          <StudioProcessGraph
            graph={graph}
            selected={selected}
            selectedEdge={selectedTransition?.path ?? null}
            onSelect={selectStep}
            onSelectEdge={selectEdge}
            onRemove={removeSelection}
            onConnect={connect}
          />
          {pending ? (
            <div
              ref={menu}
              data-testid="studio-process-connect-menu"
              style={{ position: 'absolute', left: pending.point.x, top: pending.point.y, zIndex: 10 }}
            >
              <Menu
                selectable={false}
                style={{ minWidth: 160, border: `1px solid ${colour.border}` }}
                items={connectChoices(steps[pending.source]).map((choice, index) => ({
                  key: String(index),
                  label: choiceLabel(steps[pending.source], choice, t),
                  onClick: () => apply(pending.source, pending.target, choice),
                }))}
              />
            </div>
          ) : null}
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
