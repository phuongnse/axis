import { Flex, Segmented, Tag } from 'antd'
import { useContext, useEffect, useLayoutEffect, useMemo, useState, type ReactNode, type SyntheticEvent } from 'react'
import { useLocation } from 'react-router'
import { FormCanvasContext, type FormCanvas } from '../platform/formCanvas'
import { FormWidget } from '../platform/FormWidget'
import { getAt } from '../platform/jsonPatch'
import { placeholderApi, placeholderId } from '../platform/placeholderApi'
import { setApiGuard } from '../platform/api'
import {
  fetchStudioFormMetadata,
  ServerUnreachableError,
  type StudioDiagnostic,
  type StudioFormMetadataAnswer,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { StudioNamesContext } from '../platform/studioNames'
import { useText, useTextCatalogs } from '../platform/texts'
import { TextProvider } from '../platform/TextProvider'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import { CanvasAlert } from './CanvasAlert'
import { FormProperties } from './FormProperties'
import type { ChangeHandler } from './SchemaForm'
import { canvasTexts, cancel, debounceMs, diagnosticsAt } from './studioCanvas'

/** The route's latest answer, or `'failed'`. `undefined` before the first. */
type Answer = StudioFormMetadataAnswer | 'failed' | undefined

type ConditionKey = 'visibleWhen' | 'requiredWhen' | 'readOnlyWhen'

const sectionConditions: ConditionKey[] = ['visibleWhen']
const fieldConditions: ConditionKey[] = ['visibleWhen', 'requiredWhen', 'readOnlyWhen']

/**
 * Draws the unsaved draft of a form with the site's own form widget. It posts the draft to the draft page metadata
 * route 300 ms after the last edit and redraws from the answer. While it is mounted, the widget's `/api/apps/` calls
 * get placeholder data in the browser and none goes to the server, and the form opens the placeholder record, so every
 * field holds a placeholder value. A submit on the canvas does nothing, and a click only selects. A form with steps gets a step switch
 * that picks the step the canvas shows. No condition is evaluated, so every field and section shows. An element with
 * `visibleWhen`, `requiredWhen` or `readOnlyWhen` has a mark for each condition with its text from the draft. A
 * section, field or step that a diagnostic names is marked. A click, Enter or Space on a field or a section selects it,
 * and a click on a step selects it too. The selected element has an outline and `aria-current`, and a panel beside the
 * canvas shows its properties. An edit there changes the draft.
 */
export function StudioFormCanvas({
  application,
  resourceId,
  draft,
  selected,
  onSelect,
  onChange,
}: {
  application: string
  resourceId: string
  draft: Record<string, unknown>
  /** The pointer of the selected field, section or step, or null. */
  selected: string | null
  onSelect: (path: string) => void
  onChange: ChangeHandler
}) {
  const outer = useTextCatalogs()
  const names = useContext(StudioNamesContext)
  const platformLocale = useContext(PlatformContext)?.current.locale
  const { pathname } = useLocation()
  const { generation, lost } = useStudioConnection()
  const [answer, setAnswer] = useState<Answer>()
  const [step, setStep] = useState(0)
  const drawn = answer === undefined || answer === 'failed' ? undefined : answer
  const widget = drawn?.widget ?? null
  const diagnostics = useMemo(() => drawn?.diagnostics ?? [], [drawn])
  const steps = widget?.form?.steps ?? []
  const shownStep = Math.min(step, Math.max(steps.length - 1, 0))

  useEffect(() => {
    const controller = new AbortController()
    const timer = setTimeout(() => {
      fetchStudioFormMetadata(application, resourceId, draft, controller.signal).then(
        (result) => setAnswer(result),
        (error: unknown) => {
          if (controller.signal.aborted) {
            return
          }
          if (error instanceof ServerUnreachableError) {
            lost()
            return
          }
          console.warn(`Drawing the form '${resourceId}' failed`, error)
          setAnswer('failed')
        },
      )
    }, debounceMs)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [application, resourceId, draft, generation, lost])

  // A layout effect, because the widget fetches in passive effects. A child's effect runs before its parent's, so a
  // passive effect here would let the widget's first requests reach the server. Layout effects run before any of them.
  useLayoutEffect(() => {
    if (widget) {
      setApiGuard(placeholderApi({ widgets: [widget] }))
      return () => setApiGuard(null)
    }
  }, [widget])

  const texts = useMemo(() => canvasTexts(names, platformLocale), [names, platformLocale])
  const catalogs = useMemo(() => (texts ? [texts.catalog, ...outer.catalogs] : outer.catalogs), [texts, outer.catalogs])
  const pages = useMemo(() => names?.pages.map((entry) => entry.name) ?? [], [names])

  const canvas = useMemo<FormCanvas | null>(() => {
    if (!widget?.form) {
      return null
    }
    const form = widget.form
    const fields = form.sections.flatMap((section) => section.fields)
    return {
      step: shownStep,
      section: (index, content) => {
        const section = form.sections[index]
        return (
          <FormElementFrame
            kind="section"
            label={section.titleKey}
            translate
            path={section.path}
            selected={section.path !== undefined && section.path === selected}
            onSelect={onSelect}
            draft={draft}
            diagnostics={diagnostics}
            conditions={sectionConditions}
          >
            {content}
          </FormElementFrame>
        )
      },
      field: (name, content) => {
        const path = fields.find((field) => field.name === name)?.path
        return (
          <FormElementFrame
            kind="field"
            label={name}
            path={path}
            selected={path !== undefined && path === selected}
            onSelect={onSelect}
            draft={draft}
            diagnostics={diagnostics}
            conditions={fieldConditions}
          >
            {content}
          </FormElementFrame>
        )
      },
    }
  }, [widget, shownStep, draft, diagnostics, selected, onSelect])

  // The innermost frame under the click, so a click in a field selects the field and one on a section's title the section.
  function click(event: SyntheticEvent) {
    cancel(event)
    const frame = (event.target as HTMLElement).closest<HTMLElement>('[data-form-frame]')
    const path = frame?.dataset.path
    if (path !== undefined) {
      onSelect(path)
    }
  }

  return (
    <Flex gap="middle" align="flex-start">
      <div style={{ flex: 1, minWidth: 0 }}>
        <TextProvider catalogs={catalogs} development={outer.development}>
          {steps.length > 0 && (
            <StepSwitch
              value={shownStep}
              selected={selected}
              onChange={(index) => {
                setStep(index)
                const path = steps[index]?.path
                if (path !== undefined) {
                  onSelect(path)
                }
              }}
              steps={steps.map((entry) => ({
                titleKey: entry.titleKey,
                path: entry.path,
                marked: diagnosticsAt(diagnostics, entry.path).length > 0,
              }))}
            />
          )}
          <div data-testid="studio-form-canvas" onClickCapture={click} onSubmitCapture={cancel}>
            {drawn?.widget === null && (
              <CanvasAlert testId="studio-canvas-cannot-draw" textKey="shell.studio.design.formCannotDraw" />
            )}
            {answer === 'failed' && (
              <CanvasAlert testId="studio-canvas-failed" textKey="shell.studio.design.formLoadFailed" />
            )}
            {widget && (
              <FormCanvasContext.Provider value={canvas}>
                <FormWidget
                  widget={widget}
                  sitePath={application}
                  pages={pages}
                  recordId={placeholderId(1)}
                  returnTo={pathname}
                  locale={texts?.locale ?? platformLocale ?? 'en'}
                />
              </FormCanvasContext.Provider>
            )}
          </div>
        </TextProvider>
      </div>
      <div data-testid="studio-design-panel" style={{ width: 400, flexShrink: 0 }}>
        <FormProperties
          draft={draft}
          selected={selected}
          entityFields={widget?.entity.fields ?? []}
          onChange={onChange}
        />
      </div>
    </Flex>
  )
}

function StepSwitch({
  value,
  selected,
  onChange,
  steps,
}: {
  value: number
  selected: string | null
  onChange: (step: number) => void
  steps: { titleKey: string; path: string | undefined; marked: boolean }[]
}) {
  const t = useText()
  return (
    <Segmented
      data-testid="studio-form-steps"
      aria-label={t('shell.studio.design.step')}
      value={value}
      onChange={onChange}
      options={steps.map((step, index) => ({
        value: index,
        label: (
          <span
            data-testid="studio-form-step"
            data-path={step.path}
            data-marked={step.marked ? 'true' : undefined}
            aria-current={step.path !== undefined && step.path === selected ? 'true' : undefined}
          >
            {step.marked ? `${t(step.titleKey)} · ${t('shell.studio.design.hasDiagnostics')}` : t(step.titleKey)}
          </span>
        ),
      }))}
    />
  )
}

/**
 * A section or a field of the canvas. It has a border in the error colour when a diagnostic names it, and
 * `data-marked` then. One with a path is a Tab stop that Enter or Space selects. The selected one has an outline in the
 * primary colour and `aria-current`. Its name is its title or field name, and "has diagnostics" when marked. It shows a mark for each
 * condition the draft has on it.
 */
function FormElementFrame({
  kind,
  label,
  translate,
  path,
  selected,
  onSelect,
  draft,
  diagnostics,
  conditions,
  children,
}: {
  kind: 'section' | 'field'
  /** The field name, or the text key of the section's title with `translate`. */
  label: string
  translate?: boolean
  path: string | undefined
  selected: boolean
  onSelect: (path: string) => void
  draft: Record<string, unknown>
  diagnostics: StudioDiagnostic[]
  conditions: ConditionKey[]
  children: ReactNode
}) {
  const t = useText()
  const { colour, radius } = useDesignTokens()
  const marked = diagnosticsAt(diagnostics, path).length > 0
  const name = translate ? t(label) : label
  const texts = conditions.flatMap((key) => {
    const text = path === undefined ? undefined : getAt(draft, `${path}/${key}`)
    return typeof text === 'string' && text !== '' ? [{ key, text }] : []
  })
  return (
    <div
      role="group"
      data-testid={`studio-form-${kind}`}
      data-form-frame=""
      data-path={path}
      data-marked={marked ? 'true' : undefined}
      aria-label={marked ? `${name} · ${t('shell.studio.design.hasDiagnostics')}` : name}
      aria-current={selected ? 'true' : undefined}
      tabIndex={path === undefined ? undefined : 0}
      onKeyDown={(event) => {
        if (
          path !== undefined &&
          event.target === event.currentTarget &&
          (event.key === 'Enter' || event.key === ' ')
        ) {
          event.preventDefault()
          onSelect(path)
        }
      }}
      style={{
        border: `1px solid ${marked ? colour.danger : 'transparent'}`,
        borderRadius: radius.base,
        outline: selected ? `2px solid ${colour.primary}` : undefined,
        outlineOffset: 2,
      }}
    >
      {texts.map(({ key, text }) => (
        <Tag key={key} data-testid="studio-condition-mark" data-condition={key}>
          {`${t(`shell.studio.design.${key}`)}: ${text}`}
        </Tag>
      ))}
      {children}
    </div>
  )
}
