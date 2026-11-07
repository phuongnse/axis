import {
  closeSearchPanel,
  findNext,
  findPrevious,
  getSearchQuery,
  SearchQuery,
  setSearchQuery,
} from '@codemirror/search'
import { EditorSelection, type EditorState } from '@codemirror/state'
import { EditorView, type Panel, type ViewUpdate } from '@codemirror/view'

/** The texts of the find bar. Its owner reads them from the platform texts, so the bar needs no text lookup. */
export interface FindTexts {
  /** The accessible name of the find field. */
  field: string
  /** The count of matches, with `{current}` and `{count}` in it. */
  count: string
  /** Shown when the query has no match. */
  none: string
}

interface Match {
  from: number
  to: number
}

/**
 * The find bar of the JSON view, see docs/reference/studio-resource-form.md#json-view. It counts from where the cursor
 * was when it opened, so the first match after that place reads "1 of N".
 */
export function findPanel(texts: () => FindTexts): (view: EditorView) => Panel {
  return (view) => {
    let origin = view.state.selection.main.head
    const dom = document.createElement('div')
    dom.className = 'cm-findBar'
    const input = document.createElement('input')
    input.type = 'text'
    input.setAttribute('main-field', 'true')
    input.setAttribute('aria-label', texts().field)
    input.value = getSearchQuery(view.state).search
    const count = document.createElement('span')
    count.className = 'cm-findCount'
    count.setAttribute('role', 'status')
    count.setAttribute('aria-live', 'polite')
    dom.append(input, count)

    function show(state = view.state) {
      count.textContent = countText(state, origin, texts())
    }

    input.addEventListener('input', () => {
      const query = new SearchQuery({ search: input.value })
      const first = firstMatchFrom(matchesOf(view.state, query), origin)
      view.dispatch({
        effects: setSearchQuery.of(query),
        ...(first && { selection: EditorSelection.single(first.from, first.to), scrollIntoView: true }),
      })
    })
    input.addEventListener('keydown', (event) => {
      if (event.key === 'Enter') {
        event.preventDefault()
        if (event.shiftKey) {
          findPrevious(view)
        } else {
          findNext(view)
        }
      } else if (event.key === 'Escape') {
        event.preventDefault()
        closeSearchPanel(view)
        view.focus()
      }
    })

    show()
    return {
      dom,
      top: true,
      update(update: ViewUpdate) {
        origin = update.changes.mapPos(origin)
        const query = getSearchQuery(update.state)
        const queryChanged = !query.eq(getSearchQuery(update.startState))
        if (queryChanged && input.value !== query.search) {
          input.value = query.search
        }
        if (update.docChanged || update.selectionSet || queryChanged) {
          show(update.state)
        }
      },
    }
  }
}

function matchesOf(state: EditorState, query: SearchQuery): Match[] {
  const matches: Match[] = []
  if (!query.valid) {
    return matches
  }
  const cursor = query.getCursor(state)
  for (let step = cursor.next(); !step.done; step = cursor.next()) {
    matches.push({ from: step.value.from, to: step.value.to })
  }
  return matches
}

/** The first match at or after the place, or the first one when none follows it. */
function firstMatchFrom(matches: Match[], origin: number): Match | undefined {
  return matches.find((match) => match.from >= origin) ?? matches[0]
}

function countText(state: EditorState, origin: number, texts: FindTexts): string {
  const query = getSearchQuery(state)
  if (!query.valid) {
    return ''
  }
  const matches = matchesOf(state, query)
  if (matches.length === 0) {
    return texts.none
  }
  const start = Math.max(
    matches.findIndex((match) => match.from >= origin),
    0,
  )
  const { from, to } = state.selection.main
  const selected = matches.findIndex((match) => match.from === from && match.to === to)
  const index = selected < 0 ? start : selected
  const n = matches.length
  return texts.count.replace('{current}', String(((index - start + n) % n) + 1)).replace('{count}', String(n))
}
