import { describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
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
    creator: false,
    maxLength: null,
    precision: null,
    scale: null,
    min: null,
    max: null,
    minLength: null,
    values: null,
    target: null,
    fields: null,
  }
}

const fields = [field('title', 'text', 'request.title'), field('comment', 'text', 'request.comment')]

const texts: Record<string, string> = {
  'shell.history.fields': 'Changed: {fields}',
  'shell.history.outcome': 'Outcome: {outcome}',
  'shell.history.step': 'Step: {step}',
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
    inspectPath: null,
  }
}

describe('historySummary', () => {
  it('lists the labels of the fields, or the name of a field without a label or no longer declared', () => {
    const withUnlabelled = [...fields, field('amount', 'decimal', null)]
    expect(
      historySummary(item('record.updated', { fields: ['title', 'amount', 'gone'] }), withUnlabelled, t, 'en'),
    ).toEqual(['Changed: Title, amount, and gone'])
  })

  it('gives the outcome and the step a line each', () => {
    expect(
      historySummary(item('task.completed', { outcome: 'reject', fields: [], step: 'decide' }), fields, t, 'en'),
    ).toEqual(['Outcome: reject', 'Step: decide'])
  })

  it('has no lines when the details name no field, outcome or step', () => {
    expect(historySummary(item('record.created', { version: 1 }), fields, t, 'en')).toEqual([])
  })
})

describe.each([
  {
    locale: 'English',
    code: 'en',
    catalog: platformEnglish,
    labels: { 'request.title': 'Title', 'request.comment': 'Comment' },
    changed: 'Changed: Title, amount, and gone',
    outcome: 'Outcome: reject',
    step: 'Step: decide',
  },
  {
    locale: 'Vietnamese',
    code: 'vi',
    catalog: platformVietnamese,
    labels: { 'request.title': 'Tiêu đề', 'request.comment': 'Ghi chú' },
    changed: 'Đã thay đổi: Tiêu đề, amount và gone',
    outcome: 'Kết quả: reject',
    step: 'Bước: decide',
  },
])('historySummary in $locale', ({ code, catalog, labels, changed, outcome, step }) => {
  const all: Record<string, string> = { ...catalog, ...labels }
  const translate = (key: string) => all[key] ?? key

  it('fills each line from one text of the catalog', () => {
    expect(
      historySummary(
        item('task.completed', { fields: ['title', 'amount', 'gone'], outcome: 'reject', step: 'decide' }),
        [...fields, field('amount', 'decimal', null)],
        translate,
        code,
      ),
    ).toEqual([changed, outcome, step])
  })
})
