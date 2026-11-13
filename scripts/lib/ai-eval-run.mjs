// Runs the authoring eval against one model binding and writes its report, see
// docs/reference/model-bindings.md#the-authoring-eval. Run it with scripts/ai-eval.sh, which says what it does and exits with.
//
// It starts the eval stack with the binding, and for each case in tests/ai-eval/cases, one at a time, it creates a tenant,
// runs the case as the tenant's author, and judges it. Importing this file starts nothing.
import { readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { defaultReportFolder, judgeCase, passBar, writeReport } from './ai-eval.mjs'
import { runCase } from './ai-eval-case.mjs'
import { createTenant, startStack } from './ai-eval-stack.mjs'
import { cookieHeader } from './keycloak-sign-in.mjs'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..')

export const casesFolder = path.join(root, 'tests', 'ai-eval', 'cases')

const usage = 'Usage: scripts/ai-eval.sh --binding <name>, with the settings of the binding in Ai__Bindings__<name>__* variables.'

/**
 * The binding that ARGV asks for, as `{ binding }`, or `{ error }` with the reason. The arguments are exactly `--binding <name>`,
 * and ENVIRONMENT must hold at least one `Ai__Bindings__<name>__*` variable, because a binding without settings cannot call a model.
 */
export function parseArguments(argv, environment) {
  if (argv.length !== 2 || argv[0] !== '--binding' || argv[1] === '') {
    return { error: `Give exactly --binding <name>, not: ${argv.join(' ') || 'no arguments'}` }
  }
  const binding = argv[1]
  const prefix = `Ai__Bindings__${binding}__`
  if (!Object.keys(environment).some((name) => name.startsWith(prefix))) {
    return { error: `The binding ${binding} has no settings: set ${prefix}Kind, ${prefix}Model and the rest of its settings.` }
  }
  return { binding }
}

/** The TESTCASEs of FOLDER, one for each .json file in the order of the file names, with `name` set to the file name without .json. */
export function readCases(folder = casesFolder) {
  return readdirSync(folder)
    .filter((entry) => path.extname(entry) === '.json')
    .sort()
    .map((entry) => ({ ...JSON.parse(readFileSync(path.join(folder, entry), 'utf8')), name: path.basename(entry, '.json') }))
}

// A fetch that sends each call through the stack's request, which names the tenant in the Host header, because tenant hosts
// such as eval-1.eval.localhost resolve only on some machines.
const throughStack = (stack, tenant) => async (url, { method, headers, body }) => {
  const { pathname, search } = new URL(url)
  const answer = await stack.request(tenant.host, pathname + search, { method, headers, body })
  if (answer.status === 0) throw new Error(answer.body)
  // A Response refuses a body with a status such as 204.
  return new Response(answer.body === '' ? null : answer.body, { status: answer.status })
}

async function main() {
  const parsed = parseArguments(process.argv.slice(2), process.env)
  if (parsed.error) {
    console.error(`${parsed.error}\n${usage}`)
    process.exit(2)
  }
  const cases = readCases()

  let stack
  try {
    stack = await startStack({ binding: parsed.binding })
  } catch (error) {
    console.error(error.message)
    process.exit(1)
  }
  for (const signal of ['SIGINT', 'SIGTERM']) {
    process.once(signal, () => stack.stop().finally(() => process.exit(1)))
  }

  let passes = false
  try {
    const results = []
    for (const [index, testCase] of cases.entries()) {
      let run
      try {
        const tenant = await createTenant(stack, { id: `eval-${index + 1}`, start: testCase.start })
        const session = { server: `http://${tenant.host}:${stack.serverPort}`, cookie: cookieHeader(tenant.jar).cookie }
        run = await runCase(session, tenant.application, testCase.request, { fetch: throughStack(stack, tenant) })
      } catch (error) {
        // One broken case does not hide the results of the others.
        console.error(`${testCase.name}: ${error.message}`)
      }
      const result = judgeCase(testCase, run)
      console.log(`${result.passes ? 'passed' : 'failed'} ${result.name}${result.reasons.length ? `: ${result.reasons.join('; ')}` : ''}`)
      results.push(result)
    }
    const file = writeReport(results, process.env.AXIS_AI_EVAL_REPORT_DIR || defaultReportFolder)
    const bar = passBar(results)
    passes = bar.passes
    console.log(`Report: ${file}`)
    console.log(
      `Refusal cases ${bar.refusal.passed}/${bar.refusal.total}, change cases ${bar.change.passed}/${bar.change.total}: ${passes ? 'meets' : 'is below'} the pass bar.`,
    )
  } finally {
    await stack.stop()
  }
  process.exit(passes ? 0 : 1)
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch((error) => {
    console.error(error)
    process.exit(1)
  })
}
