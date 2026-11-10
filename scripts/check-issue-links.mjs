import { spawnSync } from 'node:child_process'
import { existsSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { slugify } from './check-links.mjs'

// The broken links of open issues, in two commands that the docs-audit workflow runs:
//
//   node scripts/check-issue-links.mjs                          prints, as a JSON list of { issue, link }, each
//                                                               link of an open issue to a missing file of main or
//                                                               to a missing heading of a Markdown file, with
//                                                               what git knows of where it went
//   node scripts/check-issue-links.mjs relink <links> <homes>   points each link at the home the audit chose,
//                                                               once the home is checked on main
//
// An issue that an open issue blocks is left out, because the awaited work may add the section it links.
// Both read the working tree as main. The listing job runs with `issues: read`, the audit's read-only Claude
// step chooses the homes, and the `file` job, which runs no model, runs relink and changes nothing in an
// issue but the link. GH names the command, so the tests can replace it. The rules for a home are in
// .claude/skills/axis-docs-audit/broken-links.md, under "A link's new home".

const scriptRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const linkPattern = /github\.com\/phuongnse\/axis\/blob\/main\/([^\s)#"'>]+)(?:#([^\s)"'>]+))?/g

/** Returns the anchors of a Markdown file's headings, numbered as GitHub numbers repeated ones. */
export function anchorsOf(text) {
  const anchors = new Set()
  const seen = new Map()
  let fenced = false
  for (const line of text.split('\n')) {
    if (/^\s*```/.test(line)) fenced = !fenced
    const heading = !fenced && /^#{1,6} (.+)$/.exec(line)
    if (!heading) continue
    const slug = slugify(heading[1])
    const count = seen.get(slug) ?? 0
    seen.set(slug, count + 1)
    anchors.add(count ? `${slug}-${count}` : slug)
  }
  return anchors
}

/** Returns each broken link of the issues that no open issue blocks, reading files from root. */
export function brokenLinks(issues, root) {
  const cache = new Map()
  const broken = []
  for (const { number, body, blockedBy } of issues) {
    if ((blockedBy ?? []).some((blocker) => blocker.state === 'OPEN')) continue
    for (const match of (body ?? '').matchAll(linkPattern)) {
      const relative = decodeURIComponent(match[1])
      const file = path.join(root, relative)
      if (!existsSync(file)) {
        broken.push({ issue: number, link: relative })
        continue
      }
      const anchor = match[2]
      if (!anchor || !relative.endsWith('.md') || /^L\d+/.test(anchor)) continue
      if (!cache.has(file)) cache.set(file, anchorsOf(readFileSync(file, 'utf8')))
      if (!cache.get(file).has(anchor)) broken.push({ issue: number, link: `${relative}#${anchor}` })
    }
  }
  return broken
}

function git(args, root) {
  const result = spawnSync('git', args, {
    cwd: root,
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
  })
  return result.status === 0 ? result.stdout : ''
}

/**
 * Returns what git knows of where a broken link went: the file it was renamed to and the commit that deleted
 * it, for a missing file, or the headings the file has now, for a missing heading. A renamed Markdown file
 * also gives its headings when the link has one.
 */
export function whereWent(link, root) {
  const [relative, anchor] = link.split('#')
  const file = path.join(root, relative)
  if (existsSync(file)) return { headings: [...anchorsOf(readFileSync(file, 'utf8'))] }
  const facts = {}
  const deleted = git(['log', '--no-renames', '--diff-filter=D', '--format=%H %s', '-1', '--', relative], root).trim()
  if (!deleted) return facts
  facts.deletedIn = deleted
  const changes = git(['show', '-M', '--name-status', '--format=', deleted.split(' ')[0]], root)
  for (const line of changes.split('\n')) {
    const [status, from, to] = line.split('\t')
    if (status?.startsWith('R') && from === relative) facts.renamedTo = to
  }
  const renamed = facts.renamedTo && path.join(root, facts.renamedTo)
  if (anchor && renamed && facts.renamedTo.endsWith('.md') && existsSync(renamed)) {
    facts.headings = [...anchorsOf(readFileSync(renamed, 'utf8'))]
  }
  return facts
}

const prefix = 'github.com/phuongnse/axis/blob/main/'
const linkKey = (match) => (match[2] ? `${decodeURIComponent(match[1])}#${match[2]}` : decodeURIComponent(match[1]))

/** Returns the body with the path and anchor of each copy of the link replaced by the home, every other byte kept. */
export function relinkBody(body, link, home) {
  let result = ''
  let at = 0
  for (const match of body.matchAll(linkPattern)) {
    if (linkKey(match) !== link) continue
    result += body.slice(at, match.index + prefix.length) + home
    at = match.index + match[0].length
  }
  return result + body.slice(at)
}

/** Returns why the home is not on main, or null when its file and heading exist under root. */
function homeProblem(home, root) {
  if (typeof home !== 'string') return 'no home'
  if (!/^[\w./-]+(#[\w-]+)?$/.test(home) || home.includes('..') || home.startsWith('/')) return 'home not on main'
  const [relative, anchor] = home.split('#')
  const file = path.join(root, relative)
  if (!existsSync(file) || !statSync(file).isFile()) return 'home not on main'
  if (anchor && relative.endsWith('.md') && !anchorsOf(readFileSync(file, 'utf8')).has(anchor))
    return 'home not on main'
  return null
}

/**
 * Points each listed link at its checked home. An answer for a link the listing did not report is ignored. A
 * link with no home, a home that is not on main or a link gone from the issue is logged and left as it is.
 */
export function relinkIssues(links, homes, { root, view, edit, log }) {
  const listed = new Set(links.map((l) => `${l.issue} ${l.link}`))
  const byIssue = new Map()
  const seen = new Set()
  for (const answer of homes) {
    const key = `${answer.issue} ${answer.link}`
    if (!listed.has(key) || seen.has(key)) continue
    seen.add(key)
    if (!byIssue.has(answer.issue)) byIssue.set(answer.issue, [])
    byIssue.get(answer.issue).push(answer)
  }
  let fixed = 0
  let skipped = 0
  for (const [issue, answers] of byIssue) {
    const skip = (answer, reason) => {
      skipped++
      log(`Not relinked: #${issue} ${answer.link}: ${reason}`)
    }
    const valid = answers.filter((answer) => {
      const problem = homeProblem(answer.home, root)
      if (problem) skip(answer, problem)
      return !problem
    })
    if (!valid.length) continue
    const before = view(issue)
    let body = before
    for (const answer of valid) {
      const next = relinkBody(body, answer.link, answer.home)
      if (next === body) {
        skip(answer, 'link no longer in the issue')
        continue
      }
      body = next
      fixed++
      log(`Relinked #${issue}: ${answer.link} -> ${answer.home}`)
    }
    if (body !== before) edit(issue, body)
  }
  return { fixed, skipped }
}

function readList(file) {
  if (!existsSync(file)) return []
  const text = readFileSync(file, 'utf8').trim()
  return text ? JSON.parse(text) : []
}

function run(gh, args) {
  const result = spawnSync(gh, args, {
    encoding: 'utf8',
    maxBuffer: 256 * 1024 * 1024,
  })
  if (result.status !== 0)
    throw new Error(`${gh} ${args.slice(0, 2).join(' ')} failed:\n${result.stdout}${result.stderr}`.trimEnd())
  return result.stdout
}

function relink(linksFile, homesFile, gh) {
  const folder = mkdtempSync(path.join(tmpdir(), 'relink-'))
  try {
    const { fixed, skipped } = relinkIssues(readList(linksFile), readList(homesFile), {
      root: scriptRoot,
      view: (issue) => JSON.parse(run(gh, ['issue', 'view', String(issue), '--json', 'body'])).body,
      edit: (issue, body) => {
        const file = path.join(folder, `${issue}.md`)
        writeFileSync(file, body)
        run(gh, ['issue', 'edit', String(issue), '--body-file', file])
      },
      log: (line) => console.log(line),
    })
    console.log(`Relinked ${fixed} links, left ${skipped} as they are.`)
  } finally {
    rmSync(folder, { recursive: true, force: true })
  }
  return 0
}

const openIssuesQuery = `query($owner: String!, $name: String!, $after: String) {
  repository(owner: $owner, name: $name) {
    issues(states: OPEN, first: 100, after: $after) {
      nodes { number body blockedBy(first: 50) { nodes { number state } } }
      pageInfo { hasNextPage endCursor }
    }
  }
}`

function main([command, ...args]) {
  const gh = process.env.GH ?? 'gh'
  if (command === 'relink') return relink(args[0], args[1], gh)
  if (command) {
    console.error('Usage: node scripts/check-issue-links.mjs | relink <links.json> <homes.json>')
    return 2
  }
  const issues = []
  let after
  for (;;) {
    const args = ['api', 'graphql', '-f', `query=${openIssuesQuery}`, '-F', 'owner={owner}', '-F', 'name={repo}']
    if (after) args.push('-f', `after=${after}`)
    const result = spawnSync(gh, args, {
      encoding: 'utf8',
      maxBuffer: 256 * 1024 * 1024,
    })
    if (result.status !== 0) {
      console.error(`${gh} api graphql failed:\n${result.stdout}${result.stderr}`.trimEnd())
      return 1
    }
    const { nodes, pageInfo } = JSON.parse(result.stdout).data.repository.issues
    for (const node of nodes)
      issues.push({
        number: node.number,
        body: node.body,
        blockedBy: node.blockedBy.nodes,
      })
    if (!pageInfo.hasNextPage) break
    after = pageInfo.endCursor
  }
  const links = brokenLinks(issues, scriptRoot).map((l) => ({
    ...l,
    ...whereWent(l.link, scriptRoot),
  }))
  console.log(JSON.stringify(links, null, 2))
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
