import { spawnSync } from 'node:child_process'
import { homedir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { applyAccepted, parseJson, printReport, readAccepted, todayFromEnv } from './lib/accepted-findings.mjs'
import { buildImage, runDocker } from './lib/docker-build.mjs'
import { findImages } from './lib/images.mjs'

// Builds the production Axis images, the Dockerfile's axis-* stages, pulls the identity provider image
// that KeycloakFixture.Image pins, and scans each one with Trivy. It prints every finding and fails on
// a high or critical one, and on any build, pull or scan that cannot complete. A high or critical
// finding that scripts/scan-images-accepted.json records with the date it was found passes until its
// time frame ends, with the same rules as the dependency scan. TRIVY names the scanner,
// SCAN_IMAGES_ACCEPTED the accepted list and SCAN_TODAY (YYYY-MM-DD) the date, so the tests can
// replace them. AXIS_RETRY_DELAY_SECONDS sets the wait between retries.
const root = fileURLToPath(new URL('..', import.meta.url))
const trivy = process.env.TRIVY ?? 'trivy'
const acceptedPath = process.env.SCAN_IMAGES_ACCEPTED ?? path.join(root, 'scripts/scan-images-accepted.json')
const attempts = 3

const findings = []
const failures = []

// Runs Trivy on the image and parses its report. Trivy downloads its vulnerability database, which
// fails at times, so a failed run or one that prints no JSON is tried again after a pause.
async function scanImage(reference) {
  const args = ['image', '--scanners', 'vuln', '--format', 'json', '--quiet', '--exit-code', '0']
  args.push('--cache-dir', path.join(homedir(), '.cache/trivy'), reference)
  let problem
  for (let attempt = 1; attempt <= attempts; attempt++) {
    const result = spawnSync(trivy, args, {
      cwd: root,
      encoding: 'utf8',
      maxBuffer: 256 * 1024 * 1024,
    })
    if (result.error) problem = `${trivy}: ${result.error.message}`
    else if (result.status !== 0) problem = `trivy exited with status ${result.status}:\n${result.stderr}`.trimEnd()
    else {
      try {
        return parseJson(result.stdout, 'trivy')
      } catch (error) {
        problem = error.message
      }
    }
    if (attempt === attempts) break
    console.error(`Attempt ${attempt} of ${attempts} failed: trivy image ${reference}`)
    const seconds = Number(process.env.AXIS_RETRY_DELAY_SECONDS ?? 15) * 2 ** (attempt - 1)
    await new Promise((resolve) => setTimeout(resolve, seconds * 1000))
  }
  throw new Error(`${problem} (after ${attempts} attempts)`)
}

function collect(reference, report) {
  const seen = new Set()
  for (const result of report.Results ?? []) {
    for (const vulnerability of result.Vulnerabilities ?? []) {
      const key = `${vulnerability.PkgName}|${vulnerability.InstalledVersion}|${vulnerability.VulnerabilityID}`
      if (seen.has(key)) continue
      seen.add(key)
      findings.push({
        source: reference,
        severity: String(vulnerability.Severity).toLowerCase(),
        package: vulnerability.PkgName,
        subject: `${vulnerability.PkgName} ${vulnerability.InstalledVersion}`,
        advisory: vulnerability.VulnerabilityID,
        url: vulnerability.PrimaryURL,
        title: vulnerability.Title,
      })
    }
  }
}

let images
try {
  const { targets, identity } = findImages(root)
  images = [
    ...targets.map((target) => ({
      reference: `${target}:scan`,
      get: () => buildImage({ target, version: '0.0.0-scan', tag: `${target}:scan` }),
    })),
    { reference: identity, get: () => runDocker(['pull', identity]) },
  ]
} catch (error) {
  failures.push(`images: ${error.message}`)
  images = []
}

for (const { reference, get } of images) {
  try {
    await get()
    collect(reference, await scanImage(reference))
  } catch (error) {
    failures.push(`${reference}: ${error.message}`)
  }
}

const today = todayFromEnv()
const { entries, failures: listFailures } = readAccepted(acceptedPath, today)
failures.push(...listFailures)
applyAccepted(findings, entries, today)
const sourceWidth = Math.max(12, ...images.map((image) => image.reference.length))
process.exit(printReport(findings, failures, { sourceWidth }))
