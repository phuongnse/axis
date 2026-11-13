import type { StudioAuthoringRequest, StudioAuthoringRequestState } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

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

/** The colour of a state's words: muted once it is over, the danger text when it failed. */
export function useRequestStateColour(): (state: StudioAuthoringRequestState) => string {
  const { colour } = useDesignTokens()
  return (state) =>
    state === 'failed'
      ? colour.dangerText
      : state === 'accepted' || state === 'dropped'
        ? colour.textMuted
        : colour.text
}
