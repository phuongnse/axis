import assert from 'node:assert/strict'
import { describe, test } from 'node:test'
import { issuesFrom, maxIssues, pagesFor, parseAnswer, promptFor, shares } from './docs-audit.mjs'

const finding = (page, line, kind, extra = {}) => ({
  page,
  line,
  kind,
  statement: `Statement at ${line}.`,
  evidence: [{ file: 'src/Axis.Server/Program.cs', line: 10 }],
  ...extra,
})

describe('pagesFor', () => {
  const files = ['docs/b.md', 'docs/a.md', 'docs/reference/c.md', 'docs/d.md', 'docs/e.md', 'docs/x.json', 'README.md']

  test('takes every fourth Markdown page under docs, in order, by the run number', () => {
    assert.deepEqual(pagesFor(files, 0), ['docs/a.md', 'docs/reference/c.md'])
    assert.deepEqual(pagesFor(files, 1), ['docs/b.md'])
    assert.deepEqual(pagesFor(files, 5), ['docs/b.md'])
  })

  test('reads every page once in four runs', () => {
    const all = Array.from({ length: shares }, (_, run) => pagesFor(files, run)).flat().sort()
    assert.deepEqual(all, ['docs/a.md', 'docs/b.md', 'docs/d.md', 'docs/e.md', 'docs/reference/c.md'])
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

describe('promptFor', () => {
  test('asks for foreground subagents, a wait for all of them, and no subagents of subagents', () => {
    const prompt = promptFor(['docs/a.md'])
    assert.match(prompt, /foreground/)
    assert.match(prompt, /wait/i)
    assert.match(prompt, /starts no subagents/)
    assert.match(prompt, /^- docs\/a\.md$/m)
  })
})

describe('issuesFrom', () => {
  test('groups the docs findings of a page into one docs issue, with each finding as a criterion', () => {
    const issues = issuesFrom([finding('docs/a.md', 3, 'wrong'), finding('docs/a.md', 9, 'built')], [], 42)
    assert.equal(issues.length, 1)
    assert.equal(issues[0].title, 'The docs of docs/a.md match the code')
    assert.deepEqual(issues[0].labels, ['documentation'])
    assert.match(issues[0].body, /Wrong: .*docs\/a\.md:3/)
    assert.match(issues[0].body, /Built: .*docs\/a\.md:9/)
    assert.match(issues[0].body, /<!-- docs-audit docs docs\/a\.md -->/)
    assert.equal(issues[0].body.split('\n')[0], '<!-- nexkit:workflow docs-audit.yml 42 -->')
  })

  test('gives each unbuilt rule a code issue and each untested list a test issue', () => {
    const issues = issuesFrom([finding('docs/a.md', 5, 'unbuilt'), finding('docs/b.md', 7, 'untested-list')], [], 1)
    assert.deepEqual(
      issues.map((i) => [i.title, i.labels]),
      [
        ['The code follows the rule at docs/a.md:5', ['bug']],
        ['A test compares the list at docs/b.md:7 with the code', ['documentation', 'enhancement']],
      ],
    )
  })

  test('leaves out what an open issue already covers', () => {
    const open = ['text\n<!-- docs-audit docs docs/a.md -->', '<!-- docs-audit code docs/a.md:5 -->']
    const issues = issuesFrom([finding('docs/a.md', 3, 'wrong'), finding('docs/a.md', 5, 'unbuilt'), finding('docs/b.md', 1, 'missing')], open, 1)
    assert.deepEqual(issues.map((i) => i.title), ['The docs of docs/b.md match the code'])
  })

  test('opens at most ten issues in a run', () => {
    const many = Array.from({ length: 15 }, (_, i) => finding(`docs/p${i}.md`, 1, 'wrong'))
    assert.equal(issuesFrom(many, [], 1).length, maxIssues)
  })
})
