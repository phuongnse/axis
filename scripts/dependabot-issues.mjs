import { spawnSync } from 'node:child_process'

// Prints the NexKit issues the hourly driver opens for Dependabot alerts: one for each advisory of
// a high or critical alert that no open issue names by its advisory id (GHSA id). It prints a JSON
// list of { title, body, labels } and opens nothing. It fails with exit code 1 when it cannot read
// the alerts or the issues. GH names the command, so the tests can replace it.
const gh = process.env.GH ?? 'gh'
const repository = 'https://github.com/phuongnse/axis'
const timeFrameDays = { critical: 7, high: 30 }
const dayMs = 24 * 60 * 60 * 1000

function run(args) {
  const result = spawnSync(gh, args, { encoding: 'utf8', maxBuffer: 256 * 1024 * 1024 })
  if (result.error) throw new Error(`${gh} ${args.join(' ')}: ${result.error.message}`)
  if (result.status !== 0) {
    throw new Error(`${gh} ${args.join(' ')} failed:\n${result.stdout}${result.stderr}`.trimEnd())
  }
  try {
    return JSON.parse(result.stdout)
  } catch {
    throw new Error(`${gh} ${args.join(' ')} did not print JSON`)
  }
}

function formatDay(time) {
  return new Date(time).toISOString().slice(0, 10)
}

function addDays(day, days) {
  return formatDay(Date.parse(`${day}T00:00:00Z`) + days * dayMs)
}

function issueFor(ghsaId, alerts) {
  const severity = alerts.some((a) => a.severity === 'critical') ? 'critical' : 'high'
  const first = alerts.map((a) => a.createdAt).sort()[0]
  const alertDate = first.slice(0, 10)
  const deadline = addDays(alertDate, timeFrameDays[severity])
  const packages = [...new Set(alerts.map((a) => a.package))]
  const summary = alerts.find((a) => a.summary)?.summary ?? ''
  const lines = [
    `Update or remove the vulnerable ${packages.length === 1 ? 'package' : 'packages'} of the ${severity} Dependabot alert for ${ghsaId}.`,
    '',
    'Concepts: **Dependabot alert**, **hourly driver**, **NexKit issue**, **time frame**. Docs: ' +
      `[dependency updates](${repository}/blob/main/docs/development.md#dependency-updates), ` +
      `[D107](${repository}/blob/main/docs/decisions/d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md).`,
    '',
    `- Advisory: [${ghsaId}](https://github.com/advisories/${ghsaId})${summary ? `, ${summary}` : ''}`,
    `- Severity: ${severity}`,
    `- Alert date: ${alertDate}`,
    `- The time frame ends ${deadline} (${timeFrameDays[severity]} days)`,
    '',
    ...alerts.map(
      (a) =>
        `- ${a.ecosystem} \`${a.package}\` in \`${a.manifest}\`, vulnerable ${a.range}, ` +
        `${a.patched ? `first patched ${a.patched}` : 'no fixed version yet'}, [alert](${a.url})`,
    ),
    '',
    '## Acceptance criteria',
    '',
    ...alerts.map(
      (a) =>
        `- [ ] ${a.manifest} no longer resolves ${a.package} in ${a.range}: it is updated to ` +
        `${a.patched ? `${a.patched} or later` : 'a fixed version'}, or removed.`,
    ),
    ...(alerts.some((a) => a.ecosystem === 'npm' || a.ecosystem === 'nuget')
      ? [`- [ ] scripts/scan.sh prints no finding of ${ghsaId}.`]
      : []),
    '',
    'A package with no fixed version yet follows the rule "A fix that does not exist yet" of ' +
      `[dependency updates](${repository}/blob/main/docs/development.md#dependency-updates).`,
  ]
  return {
    deadline,
    ghsaId,
    issue: {
      title: `Update or remove ${packages.map((p) => `\`${p}\``).join(', ')} for ${severity} advisory ${ghsaId}`,
      body: lines.join('\n'),
      labels: ['security', 'dependencies'],
    },
  }
}

function main() {
  const pages = run(['api', '--paginate', '--slurp', 'repos/{owner}/{repo}/dependabot/alerts?state=open&per_page=100'])
  const rawAlerts = (Array.isArray(pages) ? pages : []).flat()
  const issues = run(['issue', 'list', '--state', 'open', '--limit', '1000', '--json', 'number,title,body'])
  const openText = (Array.isArray(issues) ? issues : []).map((i) => `${i.title ?? ''}\n${i.body ?? ''}`.toLowerCase())

  const groups = new Map()
  for (const alert of rawAlerts) {
    const advisory = alert.security_advisory ?? {}
    const severity = String(advisory.severity ?? '').toLowerCase()
    if (!(severity in timeFrameDays) || !advisory.ghsa_id || !alert.created_at) continue
    const ghsaId = String(advisory.ghsa_id)
    const dependency = alert.dependency ?? {}
    const entry = {
      severity,
      summary: advisory.summary,
      createdAt: String(alert.created_at),
      ecosystem: dependency.package?.ecosystem ?? 'unknown',
      package: dependency.package?.name ?? 'unknown',
      manifest: dependency.manifest_path ?? 'unknown',
      range: alert.security_vulnerability?.vulnerable_version_range ?? 'unknown',
      patched: alert.security_vulnerability?.first_patched_version?.identifier ?? null,
      url: alert.html_url ?? '',
    }
    if (!groups.has(ghsaId)) groups.set(ghsaId, [])
    groups.get(ghsaId).push(entry)
  }

  const result = []
  for (const [ghsaId, alerts] of groups) {
    if (openText.some((text) => text.includes(ghsaId.toLowerCase()))) continue
    result.push(issueFor(ghsaId, alerts))
  }
  result.sort((a, b) => a.deadline.localeCompare(b.deadline) || a.ghsaId.localeCompare(b.ghsaId))
  console.log(JSON.stringify(result.map((r) => r.issue), null, 2))
}

try {
  main()
} catch (error) {
  console.error(`Dependabot issues failed, ${error.message}`)
  process.exit(1)
}
