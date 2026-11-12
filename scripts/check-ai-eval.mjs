// Checks the cases and the supported models list of the authoring eval against their JSON schemas.
//
// Usage: node scripts/check-ai-eval.mjs [FOLDER]
//
// FOLDER defaults to tests/ai-eval, which also holds the schemas. Every entry of FOLDER/cases must be a .json file that follows
// case.schema.json, and FOLDER/supported-models.json must follow supported-models.schema.json.
// It checks the shape only: it does not check that a named contract point, scenario or starting
// application exists. See docs/reference/model-bindings.md#the-authoring-eval.
// Prints the problems on stderr and exits 1 when there are any.
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
// The SPA's dependencies hold Ajv, as web/schemaValidators.ts loads it.
const Ajv2020 = createRequire(path.join(root, 'web', 'package.json'))('ajv/dist/2020.js').default

export const defaultFolder = path.join(root, 'tests', 'ai-eval')
export const caseSchemaFile = 'case.schema.json'
export const modelsFile = 'supported-models.json'
export const modelsSchemaFile = 'supported-models.schema.json'

const readJson = (file) => JSON.parse(readFileSync(file, 'utf8'))

/** The problems of one instance against a schema, each as `<name>: <where> <message>`. */
function validate(ajv, validator, name, instance) {
  if (validator(instance)) {
    return []
  }
  // An `if` error only repeats the problems of its `then` branch.
  return validator.errors.filter((error) => error.keyword !== 'if').map((error) => `${name}: ${error.instancePath || '/'} ${error.message}`)
}

/**
 * The problems in the cases and the supported models list of FOLDER, as `<path in FOLDER>: <where> <message>`.
 * The schemas come from SCHEMAFOLDER, which is the committed tests/ai-eval so that a test can check a folder of its own.
 */
export function checkAiEval(folder, schemaFolder = defaultFolder) {
  const ajv = new Ajv2020({ allErrors: true })
  const validateCase = ajv.compile(readJson(path.join(schemaFolder, caseSchemaFile)))
  const validateModels = ajv.compile(readJson(path.join(schemaFolder, modelsSchemaFile)))
  const problems = []

  const check = (name, validator) => {
    let instance
    try {
      instance = readJson(path.join(folder, name))
    } catch (error) {
      problems.push(`${name}: ${error.code === 'ENOENT' ? 'is missing' : `is not JSON: ${error.message}`}`)
      return
    }
    problems.push(...validate(ajv, validator, name, instance))
  }

  const casesFolder = path.join(folder, 'cases')
  if (!existsSync(casesFolder)) {
    problems.push('cases: the folder is missing')
  } else {
    for (const entry of readdirSync(casesFolder).sort()) {
      const name = `cases/${entry}`
      if (path.extname(entry) === '.json') {
        check(name, validateCase)
      } else {
        problems.push(`${name}: is not a .json file`)
      }
    }
  }
  check(modelsFile, validateModels)
  return problems
}

function main() {
  const folder = process.argv[2] ? path.resolve(process.argv[2]) : defaultFolder
  const problems = checkAiEval(folder)
  if (problems.length > 0) {
    console.error(problems.join('\n'))
    process.exit(1)
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main()
}
