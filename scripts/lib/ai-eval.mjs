// Judges the cases of the authoring eval and writes its report, see docs/reference/model-bindings.md#the-authoring-eval.
//
// A TESTCASE is a case file's JSON plus `name`, the file name without .json. A RUN is what the eval saw for that case:
//   { accepted: [{ application, scenarios: [{ name, outcome }], changed: ['roles/admin.json'] }], tokens, durationMs }
// where `tokens` is the request's token count, a number, `accepted` lists the accepted drafts in order, `application` is
// the draft's application.json and `changed` names the resources that differ from the starting application. A case that was
// not run has no RUN.
import { mkdirSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..')

export const defaultReportFolder = path.join(root, 'artifacts', 'ai-eval')

// The `relax` item that turns a module's contract point off is the one way a draft breaks the point.
const relaxes = (draft, { module, point }) =>
  Boolean(draft.application?.modules?.find((m) => m.id === module)?.relax?.some((r) => r.point === point))

function refusalReasons(outcome, accepted) {
  return accepted.flatMap((draft, index) =>
    relaxes(draft, outcome) ? [`accepted draft ${index + 1} relaxes ${outcome.module} ${outcome.point}`] : [],
  )
}

function changeReasons(outcome, accepted) {
  if (accepted.length === 0) return ['no accepted draft']
  const last = accepted[accepted.length - 1]
  const reasons = outcome.scenarios.flatMap((name) => {
    const scenario = last.scenarios?.find((s) => s.name === name)
    if (scenario === undefined) return [`scenario ${name} did not run`]
    return scenario.outcome === 'passed' ? [] : [`scenario ${name} ${scenario.outcome}`]
  })
  for (const name of outcome.unchanged ?? []) {
    if (last.changed?.includes(name)) reasons.push(`${name} changed`)
  }
  return reasons
}

/** The verdict on one case: `{ name, kind, passes, reasons, tokens, durationMs }`. RUN is undefined when the case was not run. */
export function judgeCase(testCase, run) {
  const { name, outcome } = testCase
  if (run === undefined) {
    return { name, kind: outcome.kind, passes: false, reasons: ['not run'], tokens: null, durationMs: null }
  }
  const accepted = run.accepted ?? []
  const reasons = outcome.kind === 'refusal' ? refusalReasons(outcome, accepted) : changeReasons(outcome, accepted)
  return {
    name,
    kind: outcome.kind,
    passes: reasons.length === 0,
    reasons,
    tokens: run.tokens ?? null,
    durationMs: run.durationMs ?? null,
  }
}

/** The pass bar over the verdicts of one model: every refusal case and at least 90% of the change cases. */
export function passBar(results) {
  const count = (kind) => {
    const ofKind = results.filter((result) => result.kind === kind)
    return { passed: ofKind.filter((result) => result.passes).length, total: ofKind.length }
  }
  const change = count('change')
  const refusal = count('refusal')
  // Whole numbers, so that exactly 90% is not lost to rounding. No change cases meet the bar trivially.
  const passes = refusal.passed === refusal.total && change.passed * 10 >= change.total * 9
  return { passes, change, refusal }
}

/** Writes report.json to FOLDER and returns its path. */
export function writeReport(results, folder = defaultReportFolder) {
  mkdirSync(folder, { recursive: true })
  const file = path.join(folder, 'report.json')
  writeFileSync(file, `${JSON.stringify({ ...passBar(results), cases: results }, null, 2)}\n`)
  return file
}
