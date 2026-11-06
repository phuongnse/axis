import { spawnSync } from 'node:child_process'
import { existsSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { slugify } from './check-links.mjs'

// Prints, as a JSON list of { issue, link }, every link of an open issue to a file of this repository
// at main that is missing, or to a heading of a Markdown file that has no such anchor, so that a
// renamed heading or a deleted decision does not leave an issue pointing nowhere. It reads the
// working tree as main. GH names the command, so the tests can replace it. The hourly driver runs it
// and points each link at its new home, see .claude/skills/axis-nexkit-routine/SKILL.md.

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

/** Returns each broken link of the issues, reading files from root. */
export function brokenLinks(issues, root) {
  const cache = new Map()
  const broken = []
  for (const { number, body } of issues) {
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

function main() {
  const gh = process.env.GH ?? 'gh'
  const result = spawnSync(gh, ['issue', 'list', '--state', 'open', '--limit', '1000', '--json', 'number,body'], {
    encoding: 'utf8',
    maxBuffer: 256 * 1024 * 1024,
  })
  if (result.status !== 0) {
    console.error(`${gh} issue list failed:\n${result.stdout}${result.stderr}`.trimEnd())
    return 1
  }
  console.log(JSON.stringify(brokenLinks(JSON.parse(result.stdout), scriptRoot), null, 2))
  return 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main()
}
