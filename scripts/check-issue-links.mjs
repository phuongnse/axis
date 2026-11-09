import { spawnSync } from 'node:child_process'
import { existsSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { slugify } from './check-links.mjs'

// Prints, as a JSON list of { issue, link }, every link of an open issue to a file of this repository
// at main that is missing, or to a heading of a Markdown file that has no such anchor, so that a
// renamed heading or a deleted decision does not leave an issue pointing nowhere. An issue that an
// open issue blocks is left out, because the awaited work may add the section it links. It reads the
// working tree as main. GH names the command, so the tests can replace it. The local agent session runs it
// when the owner asks what waits for them, and points each link at its new home after the owner agrees,
// see .claude/skills/axis-nexkit/what-waits-for-the-owner.md.

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

const openIssuesQuery = `query($owner: String!, $name: String!, $after: String) {
  repository(owner: $owner, name: $name) {
    issues(states: OPEN, first: 100, after: $after) {
      nodes { number body blockedBy(first: 50) { nodes { number state } } }
      pageInfo { hasNextPage endCursor }
    }
  }
}`

function main() {
  const gh = process.env.GH ?? 'gh'
  const issues = []
  let after
  for (;;) {
    const args = ['api', 'graphql', '-f', `query=${openIssuesQuery}`, '-F', 'owner={owner}', '-F', 'name={repo}']
    if (after) args.push('-f', `after=${after}`)
    const result = spawnSync(gh, args, { encoding: 'utf8', maxBuffer: 256 * 1024 * 1024 })
    if (result.status !== 0) {
      console.error(`${gh} api graphql failed:\n${result.stdout}${result.stderr}`.trimEnd())
      return 1
    }
    const { nodes, pageInfo } = JSON.parse(result.stdout).data.repository.issues
    for (const node of nodes) issues.push({ number: node.number, body: node.body, blockedBy: node.blockedBy.nodes })
    if (!pageInfo.hasNextPage) break
    after = pageInfo.endCursor
  }
  console.log(JSON.stringify(brokenLinks(issues, scriptRoot), null, 2))
  return 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main()
}
