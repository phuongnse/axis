import Ajv2020 from 'ajv/dist/2020'
import { describe, expect, it } from 'vitest'
import {
  createStarter,
  emptyReferences,
  isApplicationName,
  kebabName,
  starterKinds,
  textResourceName,
} from './studioStarters'

// The files the server embeds. A kind that gains a schema file fails here until it gains a starter.
const schemas = import.meta.glob<Record<string, unknown>>('../../../src/Axis.Configuration/Schemas/*.schema.json', {
  eager: true,
  import: 'default',
})

const schemaByKind = new Map(
  Object.entries(schemas).map(([file, schema]) => [
    file.slice(file.lastIndexOf('/') + 1).replace('.schema.json', ''),
    schema,
  ]),
)
// The manifest is not created from Studio.
const notCreatedKinds = ['application']
const createdKinds = [...schemaByKind.keys()].filter((kind) => !notCreatedKinds.includes(kind)).toSorted()

function resolvePointer(root: unknown, pointer: string): unknown {
  let current = root
  for (const part of pointer.split('/').slice(1)) {
    current = (current as Record<string, unknown>)[part]
  }
  return current
}

describe('the starters', () => {
  it('cover every bundled schema except the manifest', () => {
    expect(createdKinds.length).toBeGreaterThan(0)
    expect(starterKinds).toEqual(createdKinds)
  })

  it.each(createdKinds)('start %s with only its empty references failing the schema', (kind) => {
    const ajv = new Ajv2020({ allErrors: true, strict: false, validateFormats: false })
    const validate = ajv.compile(schemaByKind.get(kind)!)
    const body = createStarter(kind, {
      id: crypto.randomUUID(),
      name: kind === 'role' ? 'new-role' : 'NewThing',
      locale: 'de',
    })

    validate(body)

    const empty = emptyReferences(kind)
    for (const error of validate.errors ?? []) {
      expect(empty, `${error.instancePath}: ${error.message}`).toContain(error.instancePath)
    }
    for (const pointer of empty) {
      expect(resolvePointer(body, pointer), pointer).toBe('')
    }
  })

  it('give a new resource its keys in the canonical order', () => {
    const body = createStarter('entity', { id: 'a', name: 'CostCentre' })

    expect(Object.keys(body)).toEqual(['id', 'kind', 'name', 'formatVersion', 'fields'])
    expect(body.formatVersion).toBe(1)
  })

  it('copy the content, so a new resource never changes a starter', () => {
    const first = createStarter('entity', { id: 'a', name: 'One' })
    ;(first.fields as unknown[]).push({ name: 'extra', type: 'text' })

    expect(createStarter('entity', { id: 'b', name: 'Two' }).fields).toHaveLength(1)
  })

  it('give a site the kebab-case name as its path', () => {
    expect(createStarter('site', { id: 'a', name: 'LeaveRequests' }).path).toBe('leave-requests')
  })

  it('copy the texts of the source locale into a new text resource', () => {
    const texts = { 'app.title': 'Title', 'app.save': 'Save' }

    const body = createStarter('text', { id: 'a', name: textResourceName('vi'), locale: 'vi', texts })

    expect(body).toEqual({ id: 'a', kind: 'text', name: 'TextsVi', formatVersion: 1, locale: 'vi', texts })
    expect(body.texts).not.toBe(texts)
  })
})

describe('textResourceName', () => {
  it('joins Texts and each part of the locale with a capital first letter', () => {
    expect(textResourceName('vi')).toBe('TextsVi')
    expect(textResourceName('pt-BR')).toBe('TextsPtBr')
    expect(textResourceName('ZH-hans')).toBe('TextsZhHans')
  })
})

describe('kebabName', () => {
  it.each([
    ['CostCentre', 'cost-centre'],
    ['HTTPCall', 'http-call'],
    ['Step2Approval', 'step2-approval'],
    ['Overview', 'overview'],
  ])('turns %s into %s', (name, expected) => {
    expect(kebabName(name)).toBe(expected)
  })
})

describe('isApplicationName', () => {
  it.each([
    ['Inventory', true],
    ['PurchaseRequests2', true],
    ['a'.repeat(60), true],
    ['1abc', false],
    ['a-b', false],
    ['Api', false],
    ['STUDIO', false],
    ['Account', false],
    ['a'.repeat(61), false],
    ['', false],
  ])('checks the name %j, which is valid: %s', (name, expected) => {
    expect(isApplicationName(name)).toBe(expected)
  })

  it('refuses a name whose kebab-case form is longer than 60 characters', () => {
    const name = 'Ab'.repeat(30)

    expect(name.length).toBe(60)
    expect(kebabName(name).length).toBeGreaterThan(60)
    expect(isApplicationName(name)).toBe(false)
  })
})
