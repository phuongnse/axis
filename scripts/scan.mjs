import { spawnSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

// Scans the .NET solution and the npm packages for known vulnerabilities. It prints every finding
// and fails on a high or critical one, and on any scan that cannot complete. A high or critical
// finding that scripts/scan-accepted.json records with the date it was found passes until its time
// frame ends, then fails again. DOTNET and NPM name the commands, SCAN_ACCEPTED the accepted list
// and SCAN_TODAY (YYYY-MM-DD) the date, so the tests can replace them.
const root = fileURLToPath(new URL('..', import.meta.url))
const dotnet = process.env.DOTNET ?? 'dotnet'
const npm = process.env.NPM ?? 'npm'
const npmFolders = ['web', 'tests/e2e']
const failing = new Set(['high', 'critical'])
const timeFrameDays = { critical: 7, high: 30 }
const acceptedPath = process.env.SCAN_ACCEPTED ?? path.join(root, 'scripts/scan-accepted.json')
const datePattern = /^\d{4}-\d{2}-\d{2}$/
const dayMs = 24 * 60 * 60 * 1000

const findings = []
const failures = []

function run(command, args, cwd = root) {
  const result = spawnSync(command, args, {
    cwd,
    encoding: 'utf8',
    maxBuffer: 256 * 1024 * 1024,
  })
  if (result.error) throw new Error(`${command} ${args.join(' ')}: ${result.error.message}`)
  return result
}

function parseJson(text, what) {
  try {
    return JSON.parse(text)
  } catch {
    throw new Error(`${what} did not print JSON`)
  }
}

// Parses YYYY-MM-DD to the UTC day, or returns null when the text is not a real date.
function parseDay(text) {
  if (typeof text !== 'string' || !datePattern.test(text)) return null
  const time = Date.parse(`${text}T00:00:00Z`)
  if (Number.isNaN(time) || new Date(time).toISOString().slice(0, 10) !== text) return null
  return time
}

function formatDay(time) {
  return new Date(time).toISOString().slice(0, 10)
}

function advisoryId(url) {
  return String(url ?? '')
    .replace(/[/?#]+$/, '')
    .split('/')
    .pop()
}

function scanNuGet() {
  const restore = run(dotnet, ['restore', 'Axis.slnx', '-p:NuGetAudit=false'])
  if (restore.status !== 0) {
    throw new Error(`dotnet restore failed:\n${restore.stdout}${restore.stderr}`.trimEnd())
  }
  const list = run(dotnet, ['list', 'Axis.slnx', 'package', '--vulnerable', '--include-transitive', '--format', 'json'])
  if (list.status !== 0) {
    throw new Error(`dotnet list package failed:\n${list.stdout}${list.stderr}`.trimEnd())
  }
  const report = parseJson(list.stdout, 'dotnet list package')
  const problems = report.problems ?? []
  if (problems.length > 0) {
    throw new Error(
      `dotnet list package reported problems:\n${problems.map((p) => p.text ?? JSON.stringify(p)).join('\n')}`,
    )
  }
  const seen = new Set()
  for (const project of report.projects ?? []) {
    for (const framework of project.frameworks ?? []) {
      for (const pkg of [...(framework.topLevelPackages ?? []), ...(framework.transitivePackages ?? [])]) {
        for (const vulnerability of pkg.vulnerabilities ?? []) {
          const id = advisoryId(vulnerability.advisoryurl)
          const key = `${pkg.id}|${pkg.resolvedVersion}|${id}`
          if (seen.has(key)) continue
          seen.add(key)
          findings.push({
            source: 'nuget',
            severity: vulnerability.severity,
            package: pkg.id,
            subject: `${pkg.id} ${pkg.resolvedVersion}`,
            advisory: id,
            url: vulnerability.advisoryurl,
          })
        }
      }
    }
  }
}

function scanNpm(folder) {
  const audit = run(npm, ['audit', '--audit-level=high', '--json'], path.join(root, folder))
  const report = parseJson(audit.stdout, `npm audit in ${folder}`)
  if (report.error) {
    throw new Error(
      `npm audit in ${folder} failed: ${report.error.summary ?? report.error.code ?? JSON.stringify(report.error)}`,
    )
  }
  const seen = new Set()
  for (const [name, vulnerability] of Object.entries(report.vulnerabilities ?? {})) {
    for (const via of vulnerability.via ?? []) {
      if (typeof via !== 'object' || via === null) continue
      const id = advisoryId(via.url)
      const key = `${name}|${id}`
      if (seen.has(key)) continue
      seen.add(key)
      findings.push({
        source: `npm ${folder}`,
        severity: via.severity,
        package: via.name ?? name,
        subject: via.name ?? name,
        advisory: id,
        url: via.url,
        title: via.title,
      })
    }
  }
}

const scans = [['nuget', scanNuGet], ...npmFolders.map((folder) => [`npm ${folder}`, () => scanNpm(folder)])]
for (const [name, scan] of scans) {
  try {
    scan()
  } catch (error) {
    failures.push(`${name}: ${error.message}`)
  }
}

// Reads the accepted list and returns the entries that name a finding. An entry with a problem is
// a failure and accepts nothing.
function readAccepted(today) {
  let list
  try {
    list = parseJson(readFileSync(acceptedPath, 'utf8'), acceptedPath).accepted
    if (!Array.isArray(list)) throw new Error('has no "accepted" array')
  } catch (error) {
    failures.push(`accepted list: ${error.message}`)
    return []
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
  return entries
}

const todayText = process.env.SCAN_TODAY ?? new Date().toISOString().slice(0, 10)
const today = parseDay(todayText)
if (today === null) throw new Error(`SCAN_TODAY must be a date as YYYY-MM-DD, not ${todayText}`)
const accepted = readAccepted(today)

// Marks each high or critical finding: blocking, or accepted until its deadline.
for (const finding of findings) {
  const severity = String(finding.severity).toLowerCase()
  if (!failing.has(severity)) continue
  finding.blocking = true
  const entry = accepted.find((e) => e.advisory === finding.advisory && e.package === finding.package)
  if (!entry) continue
  const deadline = entry.found + timeFrameDays[severity] * dayMs
  if (today <= deadline) {
    finding.blocking = false
    finding.note = `accepted until ${formatDay(deadline)}`
  } else {
    finding.note = `past its time frame since ${formatDay(deadline)}`
  }
}

for (const finding of findings) {
  const columns = [
    finding.source.padEnd(12),
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

process.exit(blocking.length > 0 || failures.length > 0 ? 1 : 0)
