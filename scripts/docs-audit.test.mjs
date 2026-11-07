import assert from 'node:assert/strict'
import { describe, test } from 'node:test'
import {
  claudeArgs,
  groupsOf,
  issuesFrom,
  maxIssues,
  pagesFor,
  parseAnswer,
  readers,
  readGroups,
  shares,
} from './docs-audit.mjs'

const finding = (page, line, kind, extra = {}) => ({
  page,
  line,
  kind,
  statement: `Statement at ${line}.`,
  evidence: [{ file: 'src/Axis.Server/Program.cs', line: 10 }],
  ...extra,
})

describe('pagesFor', () => {
  const files = [
    'docs/a.md',
    'docs/b.md',
    'docs/reference/c.md',
    '.claude/skills/x/SKILL.md',
    'AGENTS.md',
    'README.md',
    'docs/x.json',
    '.claude/settings.json',
    'src/notes.md',
  ]
  const kept = ['.claude/skills/x/SKILL.md', 'AGENTS.md', 'docs/a.md', 'docs/b.md', 'docs/reference/c.md']

  test('takes every fourth page of docs and the skills, and AGENTS.md, in order, by the run number', () => {
    assert.deepEqual(pagesFor(files, 0), ['.claude/skills/x/SKILL.md', 'docs/reference/c.md'])
    assert.deepEqual(pagesFor(files, 1), ['AGENTS.md'])
    assert.deepEqual(pagesFor(files, 2), ['docs/a.md'])
    assert.deepEqual(pagesFor(files, 5), ['AGENTS.md'])
  })

  test('reads every kept page once in four runs, a skill and AGENTS.md among them', () => {
    const all = Array.from({ length: shares }, (_, run) => pagesFor(files, run)).flat().sort()
    assert.deepEqual(all, [...kept].sort())
  })

  test('reads a skill and AGENTS.md in exactly one of four runs', () => {
    for (const page of ['.claude/skills/x/SKILL.md', 'AGENTS.md']) {
      const runs = Array.from({ length: shares }, (_, run) => pagesFor(files, run)).filter((pages) => pages.includes(page))
      assert.equal(runs.length, 1, page)
    }
  })
})

describe('parseAnswer', () => {
  test('takes the structured output', () => {
    const text = JSON.stringify({ structured_output: { findings: [finding('docs/a.md', 3, 'wrong')] } })
    assert.equal(parseAnswer(text).length, 1)
  })

  test('takes the JSON of the result when there is no structured output', () => {
    const text = JSON.stringify({ result: JSON.stringify({ findings: [finding('docs/a.md', 3, 'built')] }) })
    assert.equal(parseAnswer(text)[0].kind, 'built')
  })

  test('drops a finding with an unknown kind or no evidence', () => {
    const text = JSON.stringify({
      structured_output: { findings: [finding('docs/a.md', 1, 'opinion'), finding('docs/a.md', 2, 'wrong', { evidence: [] })] },
    })
    assert.deepEqual(parseAnswer(text), [])
  })

  test('fails on an answer without findings', () => {
    assert.throws(() => parseAnswer(JSON.stringify({ result: '{}' })), /no findings/)
  })

  test('fails with the start of the text on an answer that is prose', () => {
    const text = JSON.stringify({ subtype: 'success', result: 'The `modules/` pages were read, and no finding was found.' })
    assert.throws(() => parseAnswer(text), /no structured output.*The `modules\//)
  })

  test('fails with the error text on an answer that Claude Code marks as an error', () => {
    const text = JSON.stringify({ is_error: true, terminal_reason: 'api_error', result: "You've hit your session limit" })
    assert.throws(() => parseAnswer(text), /session limit/)
  })
})

describe('claudeArgs', () => {
  test('gives Claude Code the read-only tools and no subagent tool', () => {
    const args = claudeArgs(['docs/a.md'])
    assert.equal(args[args.indexOf('--tools') + 1], 'Read,Grep,Glob')
    assert.doesNotMatch(args[args.indexOf('-p') + 1], /subagent/i)
  })

  test('lists the pages of the group in the prompt', () => {
    assert.match(claudeArgs(['docs/a.md'])[1], /^- docs\/a\.md$/m)
  })
})

describe('groupsOf and readGroups', () => {
  const pages = Array.from({ length: 20 }, (_, i) => `docs/p${i}.md`)

  test('splits 20 pages into asks of at most eight pages and merges their findings in order', async () => {
    const sizes = []
    const findings = await readGroups(groupsOf(pages), async (group) => {
      sizes.push(group.length)
      return JSON.stringify({ structured_output: { findings: [finding(group[0], 1, 'wrong')] } })
    })
    assert.deepEqual(sizes, [8, 8, 4])
    assert.deepEqual(
      findings.map((f) => f.page),
      ['docs/p0.md', 'docs/p8.md', 'docs/p16.md'],
    )
  })

  test('fails after every group has settled, naming the group whose answer is prose', async () => {
    const calls = []
    const prose = JSON.stringify({ subtype: 'success', result: 'The `modules/` pages were read, and no finding was found.' })
    await assert.rejects(
      readGroups(groupsOf(pages), async (group) => {
        calls.push(group[0])
        return group[0] === 'docs/p8.md' ? prose : JSON.stringify({ structured_output: { findings: [] } })
      }),
      (error) => {
        assert.match(error.message, /^docs\/p8\.md…: .*no structured output.*The `modules\//)
        assert.equal(error.message.split('\n').length, 1)
        return true
      },
    )
    assert.deepEqual(calls, ['docs/p0.md', 'docs/p8.md', 'docs/p16.md'])
  })

  test('never runs more asks at once than readers', async () => {
    let active = 0
    let peak = 0
    const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms))
    const forty = Array.from({ length: 40 }, (_, i) => `docs/p${i}.md`)
    await readGroups(groupsOf(forty), async () => {
      active++
      peak = Math.max(peak, active)
      await delay(10)
      active--
      return JSON.stringify({ structured_output: { findings: [] } })
    })
    assert.equal(peak, readers)
  })
})

describe('issuesFrom', () => {
  test('groups the docs findings of a page into one docs issue, with each finding as a criterion', () => {
    const { issues } = issuesFrom([finding('docs/a.md', 3, 'wrong'), finding('docs/a.md', 9, 'built')], [], 42)
    assert.equal(issues.length, 1)
    assert.equal(issues[0].title, 'The docs of docs/a.md match the code')
    assert.deepEqual(issues[0].labels, ['documentation', 'bug'])
    assert.match(issues[0].body, /Wrong: .*docs\/a\.md:3/)
    assert.match(issues[0].body, /Built: .*docs\/a\.md:9/)
    assert.match(issues[0].body, /<!-- docs-audit docs docs\/a\.md -->/)
    assert.equal(issues[0].body.split('\n')[0], '<!-- nexkit:workflow docs-audit.yml 42 -->')
  })

  test('labels a docs issue bug only when it holds a wrong finding', () => {
    const { issues } = issuesFrom(
      [finding('docs/a.md', 3, 'wrong'), finding('docs/b.md', 4, 'missing'), finding('docs/b.md', 8, 'missing')],
      [],
      1,
    )
    const labels = Object.fromEntries(issues.map((issue) => [issue.title, issue.labels]))
    assert.deepEqual(labels, {
      'The docs of docs/a.md match the code': ['documentation', 'bug'],
      'The docs of docs/b.md match the code': ['documentation'],
    })
  })

  test('gives each unbuilt rule a code issue and each untested list a test issue', () => {
    const { issues } = issuesFrom([finding('docs/a.md', 5, 'unbuilt'), finding('docs/b.md', 7, 'untested-list')], [], 1)
    assert.deepEqual(
      issues.map((i) => [i.title, i.labels]),
      [
        ['The code follows the rule at docs/a.md:5', ['bug']],
        ['A test compares the list at docs/b.md:7 with the code', ['documentation', 'enhancement']],
      ],
    )
  })

  test('leaves out what an open issue already covers', () => {
    const open = ['text\n<!-- docs-audit docs docs/a.md -->', '<!-- docs-audit code docs/a.md unbuilt statement at 5. -->']
    const { issues } = issuesFrom(
      [finding('docs/a.md', 3, 'wrong'), finding('docs/a.md', 5, 'unbuilt'), finding('docs/b.md', 1, 'missing')],
      open,
      1,
    )
    assert.deepEqual(
      issues.map((i) => i.title),
      ['The docs of docs/b.md match the code'],
    )
  })

  test('leaves out an unbuilt finding that an open issue covers at another line, whatever its case and spacing', () => {
    const open = ['<!-- docs-audit code docs/a.md unbuilt the code does x. -->']
    const moved = finding('docs/a.md', 9, 'unbuilt', { statement: '  The   CODE does X.  ' })
    assert.deepEqual(issuesFrom([moved], open, 1), { issues: [], leftOut: [] })
  })

  test('makes one test issue from two equal untested lists on different lines of one run', () => {
    const same = { statement: 'The list has no test.' }
    const { issues } = issuesFrom(
      [finding('docs/b.md', 7, 'untested-list', same), finding('docs/b.md', 9, 'untested-list', same)],
      [],
      1,
    )
    assert.equal(issues.length, 1)
  })

  test('opens at most ten issues, code and test fixes first, and returns the findings past the cap as leftOut', () => {
    const found = [
      finding('docs/u1.md', 1, 'unbuilt'),
      finding('docs/u2.md', 2, 'unbuilt'),
      finding('docs/t1.md', 3, 'untested-list'),
      finding('docs/t2.md', 4, 'untested-list'),
      ...Array.from({ length: 15 }, (_, i) => finding(`docs/p${i}.md`, 1, 'wrong')),
    ]
    const { issues, leftOut } = issuesFrom(found, [], 1)
    assert.deepEqual(
      issues.map((i) => i.title),
      [
        'The code follows the rule at docs/u1.md:1',
        'The code follows the rule at docs/u2.md:2',
        'A test compares the list at docs/t1.md:3 with the code',
        'A test compares the list at docs/t2.md:4 with the code',
        ...Array.from({ length: 6 }, (_, i) => `The docs of docs/p${i}.md match the code`),
      ],
    )
    assert.equal(issues.length, maxIssues)
    assert.deepEqual(leftOut, found.slice(-9))
  })
})
