// Shared by the StudioEditor test files, which are split so that Vitest runs them in parallel.
import { EditorView } from '@codemirror/view'
import { render, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioNames, StudioResourceItem } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { loadKindSchema } from '../platform/studioSchemas'
import { StudioEditor } from '../pages/StudioEditor'

export const folder = '/api/studio/apps/Shop'
export const seedId = '46e28f6b-ce3b-4a17-829e-92fb3a5930ad'
export const entityId = 'ebe6bc20-4772-4d6c-839b-65d426ebeeca'

export const resources: StudioResourceItem[] = [
  { id: seedId, kind: 'seed', name: 'Orders', file: 'seeds/orders.json', version: 's1', usedBy: 0 },
  { id: entityId, kind: 'entity', name: 'Order', file: 'entities/order.json', version: 'e1', usedBy: 1 },
]

export function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

// The first lookup waits for the schema chunk and two fetches, which is slow while other test files run.
export const slow = { timeout: 5000 }

/** A fetch stub that serves one resource and answers its PATCH, so the test can read what was sent. */
export function stubResource(item: StudioResourceItem, content: unknown) {
  const url = `${folder}/resources/${item.id}`
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input === url && init?.method === 'PATCH') {
      return json({ version: 'next', diagnostics: [] })
    }
    if (input === `${folder}/changes` && init?.method === 'POST') {
      return json({
        items: [
          { id: item.id, version: 'next' },
          { id: 'created', version: 'created-version' },
        ],
        diagnostics: [],
      })
    }
    if (input === url) {
      return json({ ...item, content })
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return { fetchMock, url }
}

export function renderResource(
  item: StudioResourceItem,
  names?: StudioNames,
  onTextsWritten = vi.fn(),
  path: string | null = null,
) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioNamesContext.Provider value={names}>
          <StudioEditor
            application="Shop"
            item={item}
            resources={[item]}
            path={path}
            onSaved={vi.fn()}
            onTextsWritten={onTextsWritten}
            onDeleted={vi.fn()}
          />
        </StudioNamesContext.Provider>
      </TextProvider>
    </ThemeModeProvider>,
  )
}

export type PatchOperation = { op: string; path: string; value?: unknown }

export type SentChanges = { changes: ({ create: Record<string, unknown> } | { id: string; patch: PatchOperation[] })[] }

export function sentChanges(fetchMock: ReturnType<typeof vi.fn>): SentChanges {
  const [, init] = fetchMock.mock.calls.find(
    ([input, call]) => input === `${folder}/changes` && call?.method === 'POST',
  )!
  return JSON.parse(init!.body as string) as SentChanges
}

export function requests(fetchMock: ReturnType<typeof vi.fn>, method: string): number {
  return fetchMock.mock.calls.filter(([, call]) => call?.method === method).length
}

export function sentPatch(fetchMock: ReturnType<typeof vi.fn>): PatchOperation[] {
  const [, init] = fetchMock.mock.calls.find(([, call]) => call?.method === 'PATCH')!
  return JSON.parse(init!.body as string) as PatchOperation[]
}

/** The CodeMirror view of the expression editor that shows inside `field`. */
export async function expressionView(field: HTMLElement): Promise<EditorView> {
  let view: EditorView | null = null
  // The editor chunk loads after the row shows a read-only box.
  await waitFor(() => {
    view = EditorView.findFromDOM(within(field).getByRole('textbox', { name: 'When' }))
    expect(view).not.toBeNull()
  }, slow)
  return view as unknown as EditorView
}

/** The option of the dropdown opened last. A title also marks a closed select that shows the same value. */
export async function optionOf(name: string): Promise<HTMLElement> {
  return waitFor(() => {
    const found = [...document.querySelectorAll<HTMLElement>('.ant-select-item-option')].filter(
      (element) => element.title === name && !element.closest('.ant-select-dropdown-hidden'),
    )
    expect(found.length).toBeGreaterThan(0)
    return found[found.length - 1]
  })
}

export async function pick(field: HTMLElement, option: string) {
  await userEvent.click(within(field).getByRole('combobox'))
  await userEvent.click(await optionOf(option))
}

/** Loads the schema chunks the editor reads, so the first test of a file does not race its first lookup. */
export function loadStudioSchemas() {
  return Promise.all([
    loadKindSchema('seed'),
    loadKindSchema('connector'),
    loadKindSchema('endpoint'),
    loadKindSchema('text'),
    loadKindSchema('page'),
    loadKindSchema('entity'),
  ])
}
