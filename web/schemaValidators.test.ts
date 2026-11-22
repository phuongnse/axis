import { readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { describe, expect, it } from 'vitest'
import { leaveOutSchemaValidators, validatorModule } from './schemaValidators.ts'

const schemas = path.resolve(import.meta.dirname, '../src/Axis.Configuration/Schemas')
const files = readdirSync(schemas).filter((file) => file.endsWith('.schema.json'))

describe('validatorModule', () => {
  it('finds the bundled schemas', () => {
    expect(files.length).toBeGreaterThan(0)
  })

  it.each(files)('generates a module for %s that needs no eval and no require', (file) => {
    const code = validatorModule(JSON.parse(readFileSync(path.join(schemas, file), 'utf8')))

    expect(code).not.toContain('new Function')
    expect(code).not.toContain('eval(')
    expect(code).not.toContain('require(')
    expect(code).toContain('export default')
  })

  it('imports the runtime helpers of the generated code', () => {
    const code = validatorModule({ type: 'string', maxLength: 3 })

    expect(code).toContain('import * as module0 from "ajv/dist/runtime/ucs2length"')
  })
})

describe('leaveOutSchemaValidators', () => {
  const load = (id: string) => {
    const { handler } = leaveOutSchemaValidators().load as { handler: (id: string) => unknown }
    return handler(id)
  }

  it('loads a validator module as undefined', () => {
    expect(load('\0/x/entity.schema.json.validator.js')).toBe('export default undefined')
  })

  it('leaves a schema and other modules alone', () => {
    expect(load('/x/entity.schema.json')).toBeNull()
    expect(load('/x/studioSchemas.ts')).toBeNull()
  })
})
