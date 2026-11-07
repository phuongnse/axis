import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import type { Plugin } from 'vite'

// Ajv publishes CommonJS with `module.exports` set to the class and to the function.
const require = createRequire(import.meta.url)
const Ajv2020: typeof import('ajv/dist/2020.js').default = require('ajv/dist/2020.js')
const standaloneCode: typeof import('ajv/dist/standalone/index.js').default = require('ajv/dist/standalone/index.js')

const suffix = '.schema.json?validator'
const virtualEnd = '.validator.js'

/**
 * The module code of a validator that Ajv generates from a JSON Schema. It has no `require`, so the browser and Vitest
 * both load it as an ES module. The browser runs it without `eval`, see
 * docs/reference/headers-and-rate-limits.md#security-headers.
 */
export function validatorModule(schema: object): string {
  const ajv = new Ajv2020({ allErrors: true, strict: false, validateFormats: false, code: { source: true, esm: true } })
  const code = standaloneCode(ajv, ajv.compile(schema))
  const imports: string[] = []
  const body = code.replace(/require\("([^"]+)"\)\.default/g, (_match, specifier: string) => {
    let index = imports.indexOf(specifier)
    if (index < 0) {
      index = imports.push(specifier) - 1
    }
    return `runtime${index}`
  })
  if (body.includes('require(')) {
    throw new Error('The generated validator needs a require this plugin does not rewrite.')
  }
  // A CommonJS module is its exports object under Node and its `default` in the bundle. The runtime helpers are
  // functions, so the first function on the way down is the helper.
  const header = [
    ...imports.map((specifier, index) => `import * as module${index} from ${JSON.stringify(specifier)}`),
    'const helper = (namespace) => { let value = namespace; while (typeof value !== "function" && value?.default) value = value.default; return value }',
    ...imports.map((_specifier, index) => `const runtime${index} = helper(module${index})`),
  ].join('\n')
  return `${header}\n${body}`
}

/**
 * Turns `<kind>.schema.json?validator` into the module of the kind's validator, generated when the SPA is built.
 * Ajv would otherwise compile it in the browser, which needs `eval`.
 */
export function schemaValidators(): Plugin {
  return {
    name: 'axis-schema-validators',
    enforce: 'pre',
    async resolveId(id, importer) {
      if (!id.endsWith(suffix)) {
        return null
      }
      const file = await this.resolve(id.slice(0, -'?validator'.length), importer, { skipSelf: true })
      // The id ends in `.js` so that Vite's JSON plugin leaves it alone.
      return file ? `\0${file.id}${virtualEnd}` : null
    },
    load(id) {
      if (!id.startsWith('\0') || !id.endsWith(virtualEnd)) {
        return null
      }
      const file = id.slice(1, -virtualEnd.length)
      this.addWatchFile(file)
      return validatorModule(JSON.parse(readFileSync(file, 'utf8')))
    },
  }
}
