// - No '(planned for Mx)' marker under docs/ names a milestone that docs/roadmap.md calls Done, and each
//   marker '_(planned for Mx)_' sits on a line of its own under a heading, right after a bold lead-in,
//   or in a table row, never in the middle of a sentence.
import { existsSync } from 'node:fs'
import path from 'node:path'
import { lines, markdownFiles } from './helpers.mjs'

const roadmapRowPattern = /^\| \*\*(M\d+)\*\* \| [^|]+\| ([^|]+?) \|/
const plannedPattern = /planned for (M\d+(?:(?:, | and )M\d+)*)/g
const plannedMarkerPattern = /_\(planned for M\d+(?:(?:, | and )M\d+)*\)_/g

/** Reports the planned markers that name a Done milestone, and those that do not sit where a marker belongs. */
export function checkPlannedMarkers(root, report) {
  const files = markdownFiles(path.join(root, 'docs')).sort()
  const roadmap = path.join(root, 'docs', 'roadmap.md')
  if (existsSync(roadmap)) {
    const done = new Set()
    for (const line of lines(roadmap)) {
      const row = roadmapRowPattern.exec(line)
      if (row && row[2].trim() === 'Done') done.add(row[1])
    }
    for (const file of files) {
      lines(file).forEach((line, i) => {
        for (const match of line.matchAll(plannedPattern)) {
          for (const milestone of match[1].match(/M\d+/g)) {
            if (done.has(milestone)) {
              report(file, i + 1, `planned for ${milestone}, which the roadmap calls Done: remove the marker, or move it to the milestone that builds the rest`)
            }
          }
        }
      })
    }
  }
  for (const file of files) checkPlannedPlacement(file, report)
}

/** Reports each planned marker that is not on a line of its own under a heading, after a bold lead-in or in a table row. */
function checkPlannedPlacement(file, report) {
  const text = lines(file)
  let fenced = false
  text.forEach((line, i) => {
    if (/^\s*```/.test(line)) {
      fenced = !fenced
      return
    }
    if (fenced || /^\s*\|/.test(line)) return
    const prose = line.replace(/`[^`]*`/g, '')
    for (const match of prose.matchAll(plannedMarkerPattern)) {
      if (prose.trim() === match[0]) {
        let j = i - 1
        while (j >= 0 && text[j].trim() === '') j--
        if (j >= 0 && /^#{1,6} /.test(text[j])) continue
      } else if (prose.slice(0, match.index).endsWith('** ')) {
        continue
      }
      report(file, i + 1, `${match[0]} sits in a sentence: put it on a line of its own under the heading of a planned section, or right after the bold lead-in of a planned rule`)
    }
  })
}
