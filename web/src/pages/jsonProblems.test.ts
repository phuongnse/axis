import Ajv2020 from 'ajv/dist/2020'
import { describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { jsonProblems } from './jsonProblems'

const texts = platformEnglish as Record<string, string>
const t = (key: string) => texts[key] ?? key

function validatorOf(schema: object) {
  return new Ajv2020({ allErrors: true, strict: false, validateFormats: false }).compile(schema)
}

function problems(text: string, schema: object) {
  return jsonProblems(text, validatorOf(schema), t).map(({ from, to, message }) => ({
    mark: text.slice(from, to),
    message,
  }))
}

describe('jsonProblems', () => {
  it('marks the place where the text stops being JSON, even without a schema', () => {
    expect(jsonProblems('{"a": 1,}', undefined, t)).toEqual([
      { from: 8, to: 9, severity: 'error', message: 'This is not valid JSON here.' },
    ])
  })

  it('marks the last character when the text ends too soon', () => {
    expect(jsonProblems('{"a": ', undefined, t)).toMatchObject([{ from: 5, to: 6 }])
    expect(jsonProblems('', undefined, t)).toMatchObject([{ from: 0, to: 0 }])
  })

  it('has no problems for text that fits, or when there is no schema', () => {
    expect(problems('{"a": 1}', { type: 'object' })).toEqual([])
    expect(jsonProblems('{"a": 1}', undefined, t)).toEqual([])
  })

  it('marks a missing property at its object’s key, or at the opening brace when it has none', () => {
    const schema = { type: 'object', properties: { box: { type: 'object', required: ['label'] } }, required: ['id'] }

    expect(problems('{"box": {}}', schema)).toEqual([
      { mark: '{', message: 'Add id.' },
      { mark: '"box"', message: 'Add label.' },
    ])
  })

  it('marks an extra property at its key', () => {
    const schema = { type: 'object', additionalProperties: false, properties: { a: {} } }

    expect(problems('{"a": 1, "extra": {"deep": true}}', schema)).toEqual([
      { mark: '"extra"', message: 'Remove extra. It is not allowed here.' },
    ])
  })

  it('marks a wrong scalar whole, and a wrong array by its key', () => {
    const schema = {
      type: 'object',
      properties: { name: { type: 'string' }, list: { type: 'array', minItems: 2 } },
    }

    expect(problems('{"name": 5, "list": [1]}', schema)).toEqual([
      { mark: '5', message: 'This must be of type string.' },
      { mark: '"list"', message: 'Add items until there are at least 2.' },
    ])
  })

  it('words the limits and the allowed values', () => {
    const schema = {
      type: 'object',
      properties: {
        a: { enum: ['x', 'y'] },
        b: { type: 'string', minLength: 3 },
        c: { type: 'integer', maximum: 5 },
        d: { type: 'integer', exclusiveMinimum: 5 },
        e: { type: 'string', pattern: '^[a-z]+$' },
        f: { type: 'array', uniqueItems: true },
      },
    }

    expect(problems('{"a":"z","b":"ab","c":6,"d":5,"e":"A","f":[1,1]}', schema).map(({ message }) => message)).toEqual([
      'Use one of: "x", "y".',
      'Use at least 3 characters.',
      'Use 5 or less.',
      'Use more than 5.',
      'Change this to match ^[a-z]+$.',
      'Remove the repeated item.',
    ])
  })

  it('says a failed oneOf once, without the errors of its branches', () => {
    const schema = {
      $defs: { text: { type: 'string' } },
      oneOf: [{ $ref: '#/$defs/text' }, { type: 'object', required: ['fields'], additionalProperties: false }],
    }

    expect(problems('{"other": 1}', schema)).toEqual([
      { mark: '{', message: 'This does not fit any of the allowed shapes.' },
    ])
  })

  it('keeps an error that is not a branch of the failed oneOf', () => {
    const schema = {
      type: 'object',
      required: ['id'],
      properties: { step: { oneOf: [{ type: 'string' }, { type: 'number' }] } },
    }

    expect(problems('{"step": true}', schema).map(({ message }) => message)).toEqual([
      'Add id.',
      'This does not fit any of the allowed shapes.',
    ])
  })

  it('shows the error of a then, not the if that came with it', () => {
    const schema = {
      type: 'object',
      if: { properties: { type: { const: 'task' } }, required: ['type'] },
      then: { required: ['assignee'] },
    }

    expect(problems('{"type": "task"}', schema)).toEqual([{ mark: '{', message: 'Add assignee.' }])
  })

  it('words a keyword without a text of its own in general', () => {
    expect(problems('{}', { type: 'object', minProperties: 1 })).toEqual([
      { mark: '{', message: 'This is not allowed here.' },
    ])
  })
})
