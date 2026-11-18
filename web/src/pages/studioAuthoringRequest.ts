import type { StatusTone } from '../platform/StatusTag'
import type { AuthoringRequestState } from '../platform/studio'

/** The word and the tone of each state, from the states in docs/reference/ai-authoring-in-studio.md#the-states. */
export const authoringRequestStates: Record<AuthoringRequestState, { word: string; tone: StatusTone }> = {
  planning: { word: 'shell.studio.aiRequest.state.planning', tone: 'neutral' },
  awaitingApproval: { word: 'shell.studio.aiRequest.state.awaitingApproval', tone: 'attention' },
  working: { word: 'shell.studio.aiRequest.state.working', tone: 'neutral' },
  fixing: { word: 'shell.studio.aiRequest.state.working', tone: 'neutral' },
  checking: { word: 'shell.studio.aiRequest.state.checking', tone: 'neutral' },
  reviewing: { word: 'shell.studio.aiRequest.state.reviewing', tone: 'neutral' },
  ready: { word: 'shell.studio.aiRequest.state.ready', tone: 'attention' },
  accepted: { word: 'shell.studio.aiRequest.state.accepted', tone: 'success' },
  dropped: { word: 'shell.studio.aiRequest.state.dropped', tone: 'muted' },
  failed: { word: 'shell.studio.aiRequest.state.failed', tone: 'danger' },
}

/** The states in which a request waits for its author or ends, so the page stops asking for it. */
export const endedAuthoringRequestStates: ReadonlySet<AuthoringRequestState> = new Set<AuthoringRequestState>([
  'awaitingApproval',
  'ready',
  'accepted',
  'dropped',
  'failed',
])

/** The longest name the heading shows before its ellipsis. */
const maxNameLength = 80

/**
 * The name of a request: its first sentence, or its first line when no sentence ends first, cut at 80 characters with an
 * ellipsis. A sentence ends at a `.`, `!` or `?` that a space or the end follows, and keeps its mark.
 */
export function requestName(text: string): string {
  const trimmed = text.trim()
  const sentenceEnd = /[.!?](?=\s|$)/.exec(trimmed)
  const lineBreak = /\r?\n/.exec(trimmed)
  const end = Math.min(
    sentenceEnd === null ? Infinity : sentenceEnd.index + 1,
    lineBreak === null ? Infinity : lineBreak.index,
  )
  const sentence = trimmed.slice(0, end).trimEnd()
  return sentence.length > maxNameLength ? `${sentence.slice(0, maxNameLength).trimEnd()}…` : sentence
}
