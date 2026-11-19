import Ajv2020 from 'ajv/dist/2020'
import { describe, expect, it } from 'vitest'
import { packageLabel, packageLanguages, packageName, packageStarter } from './packageStarters'

// The manifest schema the server reads, so a starter that fails it fails here.
const schemas = import.meta.glob<Record<string, unknown>>(
  '../../../src/Axis.Configuration/Packages/axis-package.schema.json',
  {
    eager: true,
    import: 'default',
  },
)
const manifestSchema = Object.values(schemas)[0]

const placeholder = /\$(id|label|name)\b/

describe('packageStarter', () => {
  it('has a starter for each language, the same one the server test reads', () => {
    expect(packageLanguages).toEqual(['csharp', 'node', 'python'])
  })

  it.each(packageLanguages)('leaves no placeholder in any path or text of the %s starter', (language) => {
    const files = packageStarter(language, 'purchasing-tools')

    for (const [path, text] of Object.entries(files)) {
      expect(path).not.toMatch(placeholder)
      expect(text).not.toMatch(placeholder)
    }
  })

  it.each(packageLanguages)('gives the %s manifest the id, label, version and one echo operation', (language) => {
    const manifest = JSON.parse(packageStarter(language, 'purchasing-tools')['axis-package.json'])

    expect(manifest).toMatchObject({ id: 'purchasing-tools', label: 'Purchasing tools', version: '0.1.0' })
    expect(manifest.operations.map((operation: { name: string }) => operation.name)).toEqual(['echo'])
  })

  it.each(packageLanguages)('passes the manifest of the %s starter to the schema', (language) => {
    const ajv = new Ajv2020({ allErrors: true, strict: false, validateFormats: false })
    const validate = ajv.compile(manifestSchema)
    const manifest = JSON.parse(packageStarter(language, 'purchasing-tools')['axis-package.json'])

    validate(manifest)

    expect(validate.errors ?? []).toEqual([])
  })

  it('throws for a language without a starter', () => {
    expect(() => packageStarter('cobol', 'purchasing-tools')).toThrow(
      "There is no package starter for the language 'cobol'.",
    )
    expect(() => packageStarter('toString', 'purchasing-tools')).toThrow()
  })
})

describe('packageLabel', () => {
  it('turns hyphens into spaces and capitalises the first letter', () => {
    expect(packageLabel('purchasing-tools')).toBe('Purchasing tools')
    expect(packageLabel('ledger')).toBe('Ledger')
  })
})

describe('packageName', () => {
  it('joins the parts of the id in PascalCase', () => {
    expect(packageName('purchasing-tools')).toBe('PurchasingTools')
    expect(packageName('ledger')).toBe('Ledger')
  })
})
