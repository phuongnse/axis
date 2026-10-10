import { describe, expect, it } from 'vitest'
import { historySummary } from './historySummary'
import type { HistoryItem } from './records'
import type { FieldMetadata, FieldType } from './site'

function field(name: string, type: FieldType, labelKey: string | null): FieldMetadata {
  return {
    name,
    type,
    labelKey,
    required: false,
    unique: false,
    computed: false,
    sequence: false,
    maxLength: null,
    precision: null,
    scale: null,
    values: null,
    target: null,
    fields: null,
  }
}

const fields = [field('title', 'text', 'request.title'), field('comment', 'text', 'request.comment')]

const texts: Record<string, string> = {
  'shell.history.fields': 'Changed',
  'shell.history.outcome': 'Outcome',
  'shell.history.step': 'Step',
  'request.title': 'Title',
  'request.comment': 'Comment',
}
const t = (key: string) => texts[key] ?? key

function item(action: string, details: Record<string, unknown>): HistoryItem {
  return {
    id: '01926b3e-5f40-7c8a-9b1d-2e3f4a5b6c7d',
    occurredAt: '2026-10-06T02:05:00.123456Z',
    actor: 'maria',
    actorName: 'Maria Manager',
    action,
    processInstanceId: null,
    details,
  }
}

describe('historySummary', () => {
  it('lists the labels of the fields, or the name of a field without a label or no longer declared', () => {
    const withUnlabelled = [...fields, field('amount', 'decimal', null)]
    expect(historySummary(item('record.updated', { fields: ['title', 'amount', 'gone'] }), withUnlabelled, t)).toBe(
      'Changed: Title, amount, gone',
    )
  })

  it('joins the fields, the outcome and the step', () => {
    expect(historySummary(item('task.completed', { outcome: 'reject', fields: [], step: 'decide' }), fields, t)).toBe(
      'Outcome: reject · Step: decide',
    )
  })

  it('has no summary when the details name no field, outcome or step', () => {
    expect(historySummary(item('record.created', { version: 1 }), fields, t)).toBeNull()
  })
})
