import { spawnSync } from 'node:child_process'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { applyAccepted, parseJson, printReport, readAccepted, todayFromEnv } from './lib/accepted-findings.mjs'

// Scans the .NET solution and the npm packages for known vulnerabilities. It prints every finding
// and fails on a high or critical one, and on any scan that cannot complete. A high or critical
// finding that scripts/scan-accepted.json records with the date it was found passes until its time
// frame ends, then fails again. DOTNET and NPM name the commands, SCAN_ACCEPTED the accepted list
// and SCAN_TODAY (YYYY-MM-DD) the date, so the tests can replace them.
const root = fileURLToPath(new URL('..', import.meta.url))
const dotnet = process.env.DOTNET ?? 'dotnet'
const npm = process.env.NPM ?? 'npm'
const npmFolders = ['web', 'tests/e2e']
const acceptedPath = process.env.SCAN_ACCEPTED ?? path.join(root, 'scripts/scan-accepted.json')

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

const today = todayFromEnv()
const { entries, failures: listFailures } = readAccepted(acceptedPath, today)
failures.push(...listFailures)
applyAccepted(findings, entries, today)
process.exit(printReport(findings, failures))
