import { spawnSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

// The docs audit of D135, in three commands that the docs-audit workflow runs:
//
//   node scripts/docs-audit.mjs pages <run>        prints this run's pages, one per line
//   node scripts/docs-audit.mjs read <run>         reads those pages against the code with Claude Code,
//                                                  which has read-only tools, and prints the findings
//   node scripts/docs-audit.mjs file <findings> <run id>
//                                                  opens a NexKit issue for each group of findings
//                                                  that no open issue covers, at most ten
//
// The read command holds no GitHub token and the file command runs no model, so a page that tries
// to steer the model cannot write to the repository. CLAUDE and GH name the commands, so the tests
// can replace them. The steps of a run are in .claude/skills/axis-docs-audit/SKILL.md.

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repository = 'https://github.com/phuongnse/axis'
export const shares = 4
export const maxIssues = 10
export const kinds = ['wrong', 'built', 'missing', 'unbuilt', 'untested-list', 'standard']
const docsKinds = new Set(['wrong', 'built', 'missing', 'standard'])
const kindNames = {
  wrong: 'Wrong: the page says what the code does not do',
  built: 'Built: marked planned, and the code already does it',
  missing: 'Missing: the code does it, and no page says it',
  unbuilt: 'Unbuilt: an agreed rule that the code does not follow',
  'untested-list': 'Untested list: a list of what the code defines, with no test that compares it',
  standard: 'Standard: the page breaks writing the docs',
}

export const findingsSchema = {
  type: 'object',
  required: ['findings'],
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object',
        required: ['page', 'line', 'kind', 'statement', 'evidence'],
        properties: {
          page: { type: 'string' },
          line: { type: 'integer' },
          kind: { enum: kinds },
          statement: { type: 'string' },
          evidence: {
            type: 'array',
            minItems: 1,
            items: {
              type: 'object',
              required: ['file', 'line'],
              properties: { file: { type: 'string' }, line: { type: 'integer' }, note: { type: 'string' } },
            },
          },
          recommendation: { type: 'string' },
        },
      },
    },
  },
}

/** Returns the pages of one run: every Markdown file under docs/, in order, whose index modulo the shares equals the run's. */
export function pagesFor(files, run) {
  return files.filter((file) => /^docs\/.*\.md$/.test(file)).sort().filter((_, i) => i % shares === run % shares)
}

/** Returns the findings from Claude Code's JSON answer, taking its structured output or else the JSON in its result. */
export function parseAnswer(text) {
  const answer = JSON.parse(text)
  if (answer.is_error) {
    throw new Error(
      `Claude Code answered with an error (${answer.terminal_reason ?? answer.subtype}): ${String(answer.result ?? '').slice(0, 300)}`,
    )
  }
  const output = answer.structured_output ?? (typeof answer.result === 'string' ? resultJson(answer) : answer.result)
  if (!output || !Array.isArray(output.findings)) throw new Error('the answer holds no findings')
  return output.findings.filter((f) => kinds.includes(f.kind) && f.page && f.evidence?.length)
}

function resultJson(answer) {
  try {
    return JSON.parse(answer.result)
  } catch {
    throw new Error(`the answer holds no structured output (${answer.subtype}): ${answer.result.slice(0, 300)}`)
  }
}

const marker = (group, key) => `<!-- docs-audit ${group} ${key} -->`
const link = (file, line) => `[${file}${line ? `:${line}` : ''}](${repository}/blob/main/${file}${line ? `#L${line}` : ''})`

function evidence(finding) {
  return finding.evidence.map((e) => `${link(e.file, e.line)}${e.note ? `, ${e.note}` : ''}`).join('; ')
}

function body(text, group, key, runId, criteria) {
  return [
    text,
    '',
    `Found by the [docs audit](${repository}/blob/main/.claude/skills/axis-docs-audit/SKILL.md) of ` +
      `[D135](${repository}/blob/main/docs/decisions/d135-docs-stay-true-and-an-audit-reads-them-every-hundred-merges.md), ` +
      `run [${runId}](${repository}/actions/runs/${runId}).`,
    '',
    '## Acceptance criteria',
    '',
    ...criteria.map((c) => `- [ ] ${c}`),
    '',
    marker(group, key),
    `<!-- nexkit:workflow docs-audit.yml ${runId} -->`,
  ].join('\n')
}

/** Groups the findings into issues, leaves out those an open issue already covers, and keeps at most ten. */
export function issuesFrom(findings, openBodies, runId) {
  const covered = (group, key) => openBodies.some((b) => b.includes(marker(group, key)))
  const issues = []
  const byPage = new Map()
  for (const f of findings) {
    if (docsKinds.has(f.kind)) {
      if (!byPage.has(f.page)) byPage.set(f.page, [])
      byPage.get(f.page).push(f)
    }
  }
  for (const [page, list] of byPage) {
    if (covered('docs', page)) continue
    issues.push({
      title: `The docs of ${page} match the code`,
      labels: ['documentation'],
      body: body(
        `The docs audit read ${link(page)} against the code and found statements to fix, as ` +
          `[how the docs stay correct](${repository}/blob/main/docs/writing-docs.md#how-the-docs-stay-correct) says.\n\n` +
          'Concepts: the ones the page names.',
        'docs',
        page,
        runId,
        [
          ...list.map(
            (f) =>
              `${kindNames[f.kind]}. ${link(f.page, f.line)}: ${f.statement} Evidence: ${evidence(f)}.` +
              (f.recommendation ? ` Recommended: ${f.recommendation}` : ''),
          ),
          '`node scripts/check-docs.mjs` and `node scripts/check-links.mjs` pass.',
        ],
      ),
    })
  }
  for (const f of findings.filter((f) => !docsKinds.has(f.kind))) {
    const key = `${f.page}:${f.line}`
    const group = f.kind === 'unbuilt' ? 'code' : 'test'
    if (covered(group, key)) continue
    issues.push({
      title:
        f.kind === 'unbuilt'
          ? `The code follows the rule at ${f.page}:${f.line}`
          : `A test compares the list at ${f.page}:${f.line} with the code`,
      labels: f.kind === 'unbuilt' ? ['bug'] : ['documentation', 'enhancement'],
      body: body(
        `${kindNames[f.kind]}. ${link(f.page, f.line)}: ${f.statement}\n\nEvidence: ${evidence(f)}.` +
          (f.recommendation ? `\n\nRecommended: ${f.recommendation}` : ''),
        group,
        key,
        runId,
        f.kind === 'unbuilt'
          ? ['The code follows the rule, with a test that shows it.', 'The tests of the changed code pass.']
          : [
              'A unit test reads the list from the doc and from the code each run and fails on each entry that one side lacks.',
              'The test runs in `scripts/test.sh` and passes on `main`.',
            ],
      ),
    })
  }
  return issues.slice(0, maxIssues)
}

function run(command, args, options = {}) {
  const result = spawnSync(command, args, { encoding: 'utf8', maxBuffer: 256 * 1024 * 1024, ...options })
  if (result.error) throw new Error(`${command}: ${result.error.message}`)
  if (result.status !== 0) throw new Error(`${command} ${args[0]} failed:\n${result.stdout}${result.stderr}`.trimEnd())
  return result.stdout
}

function trackedDocs() {
  return run('git', ['ls-files', 'docs'], { cwd: root }).split('\n').filter(Boolean)
}

export function promptFor(pages) {
  return [
    'Read .claude/skills/axis-docs-audit/SKILL.md, then follow its section "Reading a page" for these pages:',
    ...pages.map((p) => `- ${p}`),
    'Use subagents for groups of about eight pages. Start each one in the foreground, never in the background. Wait until every subagent has returned. A subagent starts no subagents of its own. Then answer once, with the findings only, in the given schema.',
  ].join('\n')
}

function read(runNumber) {
  const answer = run(
    process.env.CLAUDE ?? 'claude',
    [
      '-p',
      promptFor(pagesFor(trackedDocs(), runNumber)),
      '--output-format',
      'json',
      '--no-session-persistence',
      '--model',
      process.env.DOCS_AUDIT_MODEL ?? 'claude-opus-5-5',
      '--tools',
      'Read,Grep,Glob,Task',
      '--json-schema',
      JSON.stringify(findingsSchema),
    ],
    { cwd: root },
  )
  return parseAnswer(answer)
}

function file(findingsFile, runId) {
  const findings = JSON.parse(readFileSync(findingsFile, 'utf8'))
  const gh = process.env.GH ?? 'gh'
  const open = JSON.parse(run(gh, ['issue', 'list', '--state', 'open', '--limit', '1000', '--json', 'body']))
  const issues = issuesFrom(findings, open.map((i) => i.body ?? ''), runId)
  for (const issue of issues) {
    const args = ['issue', 'create', '--title', issue.title, '--body', issue.body]
    for (const label of issue.labels) args.push('--label', label)
    process.stdout.write(run(gh, args))
  }
  return issues.length
}

function main([command, ...args]) {
  if (command === 'pages') {
    console.log(pagesFor(trackedDocs(), Number(args[0])).join('\n'))
  } else if (command === 'read') {
    console.log(JSON.stringify(read(Number(args[0])), null, 2))
  } else if (command === 'file') {
    console.log(`Opened ${file(args[0], args[1])} issues.`)
  } else {
    console.error('Usage: node scripts/docs-audit.mjs pages <run> | read <run> | file <findings.json> <run id>')
    return 2
  }
  return 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    process.exitCode = main(process.argv.slice(2))
  } catch (error) {
    console.error(error.message)
    process.exitCode = 1
  }
}
