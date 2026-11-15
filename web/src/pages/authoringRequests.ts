import type { StudioAuthoringRequest, StudioAuthoringRequestState } from '../platform/studio'
import type { FilterGroup } from '../platform/tableFilter'
import { useText } from '../platform/texts'
import type { StatusTone } from '../platform/StatusTag'

/** The states that wait for the author of the request. */
export const waitingForAuthor: readonly StudioAuthoringRequestState[] = ['awaitingApproval', 'ready']

/** The states after which nothing more happens without the author. */
export const settled: readonly StudioAuthoringRequestState[] = [...waitingForAuthor, 'accepted', 'dropped', 'failed']

/**
 * Proposed: Needs action, a view of the AI requests table that the platform gives, see the review on #3263: the filter
 * that keeps the reader's own requests that wait for them, in Awaiting approval or Ready. A view is only a filter kept in
 * the address, so the table shows its conditions as any filter's and the person may change them.
 */
export function needsActionFilter(userId: string): FilterGroup {
  return {
    all: [
      { field: 'who', op: 'eq', value: userId },
      { any: waitingForAuthor.map((state) => ({ field: 'state', op: 'eq' as const, value: state })) },
    ],
  }
}

/** Proposed: the address of the application's AI requests in the Needs action view. */
export function needsActionAddress(application: string, userId: string): string {
  return `/studio?${new URLSearchParams({ app: application, view: 'requests', filter: JSON.stringify(needsActionFilter(userId)) })}`
}

/** Proposed: the address of the application's AI requests, or of one request's page. */
export function authoringRequestAddress(application: string, id?: string): string {
  return `/studio?${new URLSearchParams({ app: application, view: 'requests', ...(id ? { request: id } : {}) })}`
}

/** Proposed: a request's state in one word, the same for every reader, see docs/reference/authoring-requests.md#states and the review on #3263. */
export function useRequestStateWords(): (request: StudioAuthoringRequest) => string {
  const t = useText()
  // A fix round is AI working on what the checks or the review found, so it reads as working.
  return (request) => t(`shell.studio.ai.state.${request.state === 'fixing' ? 'working' : request.state}`)
}

/** Proposed: the tone of a request's state, see `StatusTag` and the review on #3263. */
export function requestStateTone(state: StudioAuthoringRequestState): StatusTone {
  switch (state) {
    case 'awaitingApproval':
    case 'ready':
      return 'attention'
    case 'accepted':
      return 'success'
    case 'failed':
      return 'danger'
    case 'dropped':
      return 'muted'
    default:
      return 'neutral'
  }
}

/** The longest a request's name runs, in characters, before it is cut. */
const titleLength = 80

/**
 * Proposed: a request's name, its title, or until AI has given it one, the first sentence of its words, cut at 80
 * characters, see the review on #3263.
 */
export function requestTitle(request: StudioAuthoringRequest): string {
  if (request.title !== null && request.title.trim() !== '') {
    return request.title
  }
  const words = request.text.trim()
  const end = words.search(/[.!?\n]/)
  const sentence = end === -1 ? words : words.slice(0, end)
  return sentence.length > titleLength ? `${sentence.slice(0, titleLength - 1).trimEnd()}…` : sentence
}
