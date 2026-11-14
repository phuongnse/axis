import type { StudioAuthoringRequest, StudioAuthoringRequestState } from '../platform/studio'
import { useText } from '../platform/texts'
import type { StatusTone } from '../platform/StatusTag'

/** The states that wait for the author of the request. */
export const waitingForAuthor: readonly StudioAuthoringRequestState[] = ['awaitingApproval', 'ready']

/** The states after which nothing more happens without the author. */
export const settled: readonly StudioAuthoringRequestState[] = [...waitingForAuthor, 'accepted', 'dropped', 'failed']

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
