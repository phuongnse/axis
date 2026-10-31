import { readFileSync } from 'node:fs'

// The accepted-list rules that the dependency scan (scripts/scan.mjs) and the image scan
// (scripts/scan-images.mjs) share, so their time frames and messages cannot drift apart. A high or
// critical finding fails unless an entry of the accepted list names its advisory and package and
// its time frame, counted from the date it was found, has not ended.
const failing = new Set(['high', 'critical'])
const timeFrameDays = { critical: 7, high: 30 }
const datePattern = /^\d{4}-\d{2}-\d{2}$/
const dayMs = 24 * 60 * 60 * 1000

export function parseJson(text, what) {
  try {
    return JSON.parse(text)
  } catch {
    throw new Error(`${what} did not print JSON`)
  }
}

// Parses YYYY-MM-DD to the UTC day, or returns null when the text is not a real date.
export function parseDay(text) {
  if (typeof text !== 'string' || !datePattern.test(text)) return null
  const time = Date.parse(`${text}T00:00:00Z`)
  if (Number.isNaN(time) || new Date(time).toISOString().slice(0, 10) !== text) return null
  return time
}

export function formatDay(time) {
  return new Date(time).toISOString().slice(0, 10)
}

// Reads SCAN_TODAY (YYYY-MM-DD), or today's date, as the UTC day.
export function todayFromEnv(env = process.env) {
  const text = env.SCAN_TODAY ?? new Date().toISOString().slice(0, 10)
  const today = parseDay(text)
  if (today === null) throw new Error(`SCAN_TODAY must be a date as YYYY-MM-DD, not ${text}`)
  return today
}

// Reads the accepted list and returns the entries that name a finding, and a failure for each
// entry with a problem. An entry with a problem accepts nothing.
export function readAccepted(file, today) {
  const failures = []
  let list
  try {
    list = parseJson(readFileSync(file, 'utf8'), file).accepted
    if (!Array.isArray(list)) throw new Error('has no "accepted" array')
  } catch (error) {
    failures.push(`accepted list: ${error.message}`)
    return { entries: [], failures }
  }
  const entries = []
  list.forEach((entry, index) => {
    const name = entry?.advisory ? `${entry.advisory} ${entry.package ?? ''}`.trim() : `entry ${index + 1}`
    const found = parseDay(entry?.found)
    if (!entry?.advisory) failures.push(`accepted list: ${name}: has no advisory`)
    else if (!entry.package) failures.push(`accepted list: ${name}: has no package`)
    else if (found === null) failures.push(`accepted list: ${name}: found must be a date as YYYY-MM-DD`)
    else if (found > today) failures.push(`accepted list: ${name}: found is after today`)
    else entries.push({ advisory: entry.advisory, package: entry.package, found })
  })
  return { entries, failures }
}

// Marks each high or critical finding: blocking, or accepted until its deadline.
export function applyAccepted(findings, entries, today) {
  for (const finding of findings) {
    const severity = String(finding.severity).toLowerCase()
    if (!failing.has(severity)) continue
    finding.blocking = true
    const entry = entries.find((e) => e.advisory === finding.advisory && e.package === finding.package)
    if (!entry) continue
    const deadline = entry.found + timeFrameDays[severity] * dayMs
    if (today <= deadline) {
      finding.blocking = false
      finding.note = `accepted until ${formatDay(deadline)}`
    } else {
      finding.note = `past its time frame since ${formatDay(deadline)}`
    }
  }
}

// Prints each finding, the totals and the failures, and returns the exit code: 1 when a finding
// blocks or a scan failed.
export function printReport(findings, failures, { sourceWidth = 12 } = {}) {
  for (const finding of findings) {
    const columns = [
      finding.source.padEnd(sourceWidth),
      String(finding.severity).padEnd(9),
      finding.subject,
      finding.advisory,
      finding.url,
    ]
    if (finding.title) columns.push(finding.title)
    if (finding.note) columns.push(`(${finding.note})`)
    console.log(columns.join('  '))
  }

  const blocking = findings.filter((finding) => finding.blocking)
  const high = findings.filter((finding) => finding.blocking !== undefined)
  const within = high.length - blocking.length
  if (findings.length === 0 && failures.length === 0) console.log('No known vulnerabilities')
  if (findings.length > 0) {
    console.log(
      `${findings.length} known vulnerabilities, ${high.length} high or critical, ${within} accepted within their time frame`,
    )
  }
  for (const failure of failures) console.error(`Scan failed, ${failure}`)
  return blocking.length > 0 || failures.length > 0 ? 1 : 0
}
