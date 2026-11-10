import { readFileSync } from 'node:fs'
import path from 'node:path'

// The production images the Dockerfile builds: every `axis-*` stage. A new production stage is built and
// scanned with no change to the scan or the smoke tests.
export function productionTargets(root) {
  const dockerfile = path.join(root, 'Dockerfile')
  const targets = [...readFileSync(dockerfile, 'utf8').matchAll(/^FROM\s+\S+\s+AS\s+(axis-[\w-]+)\s*$/gim)].map(
    (match) => match[1],
  )
  if (targets.length === 0) throw new Error(`${dockerfile} has no axis-* stage`)
  return targets
}

// The text of one Dockerfile stage, from its `FROM <base> AS <name>` line up to the next `FROM`, without comments and
// blank lines, so a change to a comment does not change it. Its first line names the base image.
export function stageText(root, name) {
  const dockerfile = path.join(root, 'Dockerfile')
  const stage = readFileSync(dockerfile, 'utf8')
    .split(/^(?=FROM\s)/im)
    .find((text) => new RegExp(`^FROM\\s+\\S+\\s+AS\\s+${name}\\s*$`, 'im').test(text.split('\n')[0]))
  if (!stage) throw new Error(`${dockerfile} has no ${name} stage`)
  return stage
    .split('\n')
    .filter((line) => line.trim() !== '' && !line.startsWith('#'))
    .join('\n')
}

// Finds what the image scan scans from the repository itself: the production images, and the identity provider
// image that KeycloakFixture.Image pins.
export function findImages(root) {
  const targets = productionTargets(root)
  const fixture = path.join(root, 'tests/Axis.Integration.Tests/KeycloakFixture.cs')
  const identity = readFileSync(fixture, 'utf8').match(/public const string Image = "([^"]+)";/)?.[1]
  if (!identity) throw new Error(`${fixture} has no Image constant`)
  return { targets, identity }
}

// The Keycloak image every other reference must name.
export function keycloakPin(root) {
  return findImages(root).identity
}

// Whether the image names an exact version, such as 26.8.1, and not a moving tag such as 26.8.
export function isExactKeycloakImage(image) {
  return /^quay\.io\/keycloak\/keycloak:\d+\.\d+\.\d+$/.test(image)
}

// Every Keycloak image reference in files (paths relative to root), as { file, line, image }. The
// scripts' own test files are skipped because they hold made-up references as test input.
export function findKeycloakReferences(root, files) {
  const references = []
  for (const file of files) {
    if (/^scripts\/.*\.test\.mjs$/.test(file)) continue
    let text
    try {
      text = readFileSync(path.join(root, file), 'utf8')
    } catch {
      continue
    }
    text.split('\n').forEach((content, index) => {
      for (const match of content.matchAll(/quay\.io\/keycloak\/keycloak[:@][^\s"'`]+/g)) {
        references.push({ file, line: index + 1, image: match[0] })
      }
    })
  }
  return references
}
