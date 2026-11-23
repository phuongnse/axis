// - Every row of docs/capabilities.md, under its '| Capability | Where |' header, names a capability once in
//   the page and says where it lives: a link to the doc section that holds its rule, or, alone in the cell,
//   a planned marker: '_(planned for Mx)_' for a milestone that docs/roadmap.md lists, or '_(planned, #n)_'
//   with the open issue that builds the rule. A bare '_(planned)_' is still a home here: planned-markers.mjs reports it.
//   So no capability is left without a home, see D154.
import { existsSync } from 'node:fs'
import path from 'node:path'
import { cellSeparator, lines } from './helpers.mjs'
import { plannedMarker } from './planned-markers.mjs'

const header = '| Capability | Where |'
const roadmapRowPattern = /^\| \*\*(M\d+)\*\* \|/
const markerPattern = new RegExp(`^${plannedMarker}$`)
const linkPattern = /\]\([^)\s]+\)/

/** Checks that each capability of the capability map has one row, and that the row says where it lives. */
export function checkCapabilities(root, report) {
  const page = path.join(root, 'docs', 'capabilities.md')
  if (!existsSync(page)) return
  const roadmap = path.join(root, 'docs', 'roadmap.md')
  const milestones = new Set(
    existsSync(roadmap) ? lines(roadmap).flatMap((line) => roadmapRowPattern.exec(line)?.[1] ?? []) : [],
  )
  const seen = new Map()
  const text = lines(page)
  for (let start = 0; start < text.length; start++) {
    if (text[start].trim() !== header) continue
    for (let i = start + 1; i < text.length && text[i].startsWith('|'); i++) {
      const cells = text[i].split(cellSeparator).slice(1, 3).map((cell) => cell.trim())
      if (/^:?-+:?$/.test(cells[0])) continue
      const [capability, where] = cells
      const line = i + 1
      if (capability === '') {
        report(page, line, 'a row names no capability')
        continue
      }
      const key = capability.toLowerCase()
      if (seen.has(key)) report(page, line, `'${capability}' has a row already, on line ${seen.get(key)}`)
      else seen.set(key, line)
      const marker = markerPattern.exec(where ?? '')
      if (marker) {
        for (const milestone of marker[1]?.match(/M\d+/g) ?? []) {
          if (!milestones.has(milestone)) report(page, line, `'${capability}' is planned for ${milestone}, which the roadmap does not list`)
        }
      } else if (!linkPattern.test(where ?? '')) {
        report(
          page,
          line,
          `'${capability}' has no home: link the doc section that holds its rule, or put a planned marker alone in the cell`,
        )
      }
    }
  }
}
