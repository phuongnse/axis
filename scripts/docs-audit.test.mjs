import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { describe, test } from 'node:test'
import {
  chooseHomes,
  claudeArgs,
  groupsOf,
  homesArgs,
  issuesFrom,
  maxIssues,
  milestoneFindings,
  pagesFor,
  parseAnswer,
  parseHomes,
  readers,
  readFindings,
  readGroups,
  shares,
} from './docs-audit.mjs'
import { docWordCounts } from './lib/docs/helpers.mjs'
import { splitIssueWords } from './lib/docs/sizes.mjs'

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

describe('chooseHomes', () => {
  test('asks nothing when there is no link', async () => {
    let asked = 0
    const ask = async () => {
      asked++
      return '{}'
    }
    assert.deepEqual(await chooseHomes([], ask), [])
    assert.equal(asked, 0)
  })

  test('asks once with the links and takes the homes, a missing home as null', async () => {
    const links = [{ issue: 7, link: 'docs/old.md', renamedTo: 'docs/new.md' }]
    const homes = [
      { issue: 7, link: 'docs/old.md', home: 'docs/new.md' },
      { issue: 8, link: 'docs/x.md', home: null },
    ]
    const asked = []
    const answer = await chooseHomes(links, async (list) => {
      asked.push(list)
      return JSON.stringify({ structured_output: { homes } })
    })
    assert.deepEqual(asked, [links])
    assert.deepEqual(answer, homes)
  })

  test('fails on an answer with no homes', () => {
    assert.throws(() => parseHomes(JSON.stringify({ structured_output: {} })), /no homes/)
  })

  test('gives Claude Code the read-only tools and the links as data in the prompt', () => {
    const args = homesArgs([{ issue: 7, link: 'docs/old.md' }])
    assert.equal(args[args.indexOf('--tools') + 1], 'Read,Grep,Glob')
    assert.match(args[1], /"A link's new home"/)
    assert.match(args[1], /"link": "docs\/old\.md"/)
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
    assert.deepEqual(issues[0].labels, ['documentation', 'bug', 'docs-audit'])
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
      'The docs of docs/a.md match the code': ['documentation', 'bug', 'docs-audit'],
      'The docs of docs/b.md match the code': ['documentation', 'docs-audit'],
    })
  })

  test('gives each unbuilt rule a code issue and each untested list a test issue', () => {
    const { issues } = issuesFrom([finding('docs/a.md', 5, 'unbuilt'), finding('docs/b.md', 7, 'untested-list')], [], 1)
    assert.deepEqual(
      issues.map((i) => [i.title, i.labels]),
      [
        ['The code follows the rule at docs/a.md:5', ['bug', 'docs-audit']],
        ['A test compares the list at docs/b.md:7 with the code', ['documentation', 'docs-audit']],
      ],
    )
  })

  test('labels every issue it opens docs-audit, beside its present labels', () => {
    const { issues } = issuesFrom(
      [
        finding('docs/a.md', 3, 'wrong'),
        finding('docs/b.md', 4, 'missing'),
        finding('docs/c.md', 5, 'unbuilt'),
        finding('docs/d.md', 7, 'untested-list'),
      ],
      [],
      1,
    )
    assert.equal(issues.length, 4)
    for (const issue of issues) assert.ok(issue.labels.includes('docs-audit'), issue.title)
    const unbuilt = issues.find((i) => i.title === 'The code follows the rule at docs/c.md:5')
    assert.deepEqual(unbuilt.labels, ['bug', 'docs-audit'])
    const untested = issues.find((i) => i.title === 'A test compares the list at docs/d.md:7 with the code')
    assert.deepEqual(untested.labels, ['documentation', 'docs-audit'])
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

describe('readFindings', () => {
  const withFile = (text, check) => {
    const dir = mkdtempSync(path.join(tmpdir(), 'docs-audit-'))
    try {
      const file = path.join(dir, 'findings.json')
      if (text !== undefined) writeFileSync(file, text)
      check(file)
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  }

  test('an empty file is refused with its name, since the read command always prints a list', () => {
    withFile('\n', (file) => assert.throws(() => readFindings(file), (e) => e.message.includes(file) && e.message.includes('no findings')))
  })

  test('a missing file is refused the same way', () => {
    withFile(undefined, (file) => assert.throws(() => readFindings(file), (e) => e.message.includes(file) && e.message.includes('no findings')))
  })

  test('a cut-off file is refused with its name', () => {
    withFile('[{"page": "docs/a.md"', (file) =>
      assert.throws(
        () => readFindings(file),
        (e) => e.message.includes(file) && e.message.includes('not a JSON list'),
      ),
    )
  })

  test('a file with a list is read as that list', () => {
    withFile('[]', (file) => assert.deepEqual(readFindings(file), []))
    const one = [finding('docs/a.md', 3, 'wrong')]
    withFile(JSON.stringify(one), (file) => assert.deepEqual(readFindings(file), one))
  })
})

describe('split issues', () => {
  const doc = (words) => '# Doc\n' + 'word\n'.repeat(words - 1)
  const splitTitle = (page) => `${page} splits by topic before it meets the size limit`

  // Writes each doc under docs/ of a temporary root, and passes the word count of each doc to check.
  const withDocs = (docs, check) => {
    const root = mkdtempSync(path.join(tmpdir(), 'docs-audit-'))
    try {
      mkdirSync(path.join(root, 'docs'))
      for (const [name, text] of Object.entries(docs)) writeFileSync(path.join(root, 'docs', name), text)
      check(docWordCounts(root))
    } finally {
      rmSync(root, { recursive: true, force: true })
    }
  }

  test('opens one split issue for a doc past splitIssueWords, with its word count and the docs-audit label', () => {
    withDocs({ 'big.md': doc(splitIssueWords + 1) }, (docWords) => {
      const { issues } = issuesFrom([], [], 1, docWords)
      assert.equal(issues.length, 1)
      assert.equal(issues[0].title, splitTitle('docs/big.md'))
      assert.deepEqual(issues[0].labels, ['documentation', 'docs-audit'])
      assert.match(issues[0].body, /9,001 words/)
      assert.match(issues[0].body, /<!-- docs-audit split docs\/big\.md -->/)
    })
  })

  test('opens no split issue for a doc of splitIssueWords words', () => {
    withDocs({ 'edge.md': doc(splitIssueWords) }, (docWords) => {
      assert.deepEqual(issuesFrom([], [], 1, docWords), { issues: [], leftOut: [] })
    })
  })

  test('opens no split issue for a doc that an open issue already asks to split', () => {
    withDocs({ 'big.md': doc(splitIssueWords + 1) }, (docWords) => {
      const open = ['text\n<!-- docs-audit split docs/big.md -->']
      assert.deepEqual(issuesFrom([], open, 1, docWords), { issues: [], leftOut: [] })
    })
  })

  test('still opens a split issue when an open docs fix names the same page', () => {
    withDocs({ 'big.md': doc(splitIssueWords + 1) }, (docWords) => {
      const open = ['text\n<!-- docs-audit docs docs/big.md -->']
      assert.deepEqual(
        issuesFrom([], open, 1, docWords).issues.map((i) => i.title),
        [splitTitle('docs/big.md')],
      )
    })
  })

  test('opens a split issue after the docs issues of the run', () => {
    withDocs({ 'big.md': doc(splitIssueWords + 1) }, (docWords) => {
      const { issues } = issuesFrom([finding('docs/a.md', 3, 'wrong')], [], 1, docWords)
      assert.deepEqual(
        issues.map((i) => i.title),
        ['The docs of docs/a.md match the code', splitTitle('docs/big.md')],
      )
    })
  })

  test('counts a split issue toward the cap of ten, and returns a split past the cap as leftOut', () => {
    const found = Array.from({ length: maxIssues }, (_, i) => finding(`docs/p${i}.md`, 1, 'wrong'))
    const docWords = [{ page: 'docs/big.md', words: splitIssueWords + 1 }]
    const { issues, leftOut } = issuesFrom(found, [], 1, docWords)
    assert.equal(issues.length, maxIssues)
    assert.ok(issues.every((i) => i.title !== splitTitle('docs/big.md')))
    assert.deepEqual(
      leftOut.map((f) => [f.page, f.kind, f.statement]),
      [['docs/big.md', 'split', `${splitIssueWords + 1} words, past ${splitIssueWords}`]],
    )
  })
})

describe('milestone issues', () => {
  const statuses = new Map([
    ['M35', 'In progress'],
    ['M36', 'Done'],
    ['M37', 'Planned'],
  ])
  const titleOf = (milestone) => `The roadmap and the docs of ${milestone} match its close on GitHub`
  const markers = [
    { file: 'docs/a.md', line: 3, milestone: 'M37' },
    { file: 'docs/b.md', line: 9, milestone: 'M37' },
  ]

  test('opens a milestone issue for a milestone closed while its roadmap row is not Done, and none when the row reads Done', () => {
    const found = milestoneFindings(
      statuses,
      [
        { title: 'M35 Shipping', state: 'closed', open_issues: 0 },
        { title: 'M36 Done', state: 'closed', open_issues: 0 },
      ],
      [],
    )
    const { issues } = issuesFrom([], [], 1, [], found)
    assert.deepEqual(
      issues.map((i) => [i.title, i.labels]),
      [[titleOf('M35'), ['documentation', 'docs-audit']]],
    )
    assert.match(issues[0].body, /closed on GitHub, but its roadmap row says In progress/)
  })

  test('opens a milestone issue for planned markers that name a milestone with no open issue, linking each marker', () => {
    const found = milestoneFindings(statuses, [{ title: 'M37 Next', state: 'open', open_issues: 0 }], markers)
    const { issues } = issuesFrom([], [], 1, [], found)
    assert.deepEqual(
      issues.map((i) => i.title),
      [titleOf('M37')],
    )
    assert.match(issues[0].body, /\[docs\/a\.md:3\]\(https:\/\/github\.com\/phuongnse\/axis\/blob\/main\/docs\/a\.md#L3\)/)
    assert.match(issues[0].body, /\[docs\/b\.md:9\]/)
  })

  test('opens no milestone issue for planned markers of a milestone that still has an open issue', () => {
    const found = milestoneFindings(statuses, [{ title: 'M37 Next', state: 'open', open_issues: 2 }], markers)
    assert.deepEqual(issuesFrom([], [], 1, [], found), { issues: [], leftOut: [] })
  })

  test('leaves out a milestone that an open issue already covers by its marker, closed or named by markers', () => {
    const closed = milestoneFindings(statuses, [{ title: 'M35 Shipping', state: 'closed', open_issues: 0 }], [])
    const named = milestoneFindings(statuses, [{ title: 'M37 Next', state: 'open', open_issues: 0 }], markers)
    assert.deepEqual(issuesFrom([], ['text\n<!-- docs-audit milestone M35 -->'], 1, [], closed), { issues: [], leftOut: [] })
    assert.deepEqual(issuesFrom([], ['text\n<!-- docs-audit milestone M37 -->'], 1, [], named), { issues: [], leftOut: [] })
  })

  test('opens the milestone issues before the code and test issues', () => {
    const found = milestoneFindings(statuses, [{ title: 'M35 Shipping', state: 'closed', open_issues: 0 }], [])
    const { issues } = issuesFrom([finding('docs/a.md', 5, 'unbuilt')], [], 1, [], found)
    assert.deepEqual(
      issues.map((i) => i.title),
      [titleOf('M35'), 'The code follows the rule at docs/a.md:5'],
    )
  })

  test('skips a GitHub milestone whose title does not start with M and a number', () => {
    const found = milestoneFindings(
      statuses,
      [
        { title: 'Launch', state: 'closed', open_issues: 0 },
        { title: 'Beta M35', state: 'closed', open_issues: 0 },
      ],
      [{ file: 'docs/a.md', line: 3, milestone: 'M35' }],
    )
    assert.deepEqual(found, [])
  })
})
