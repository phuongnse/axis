import { readFileSync } from 'node:fs'
import path from 'node:path'

// Finds what the image scan scans from the repository itself: every `axis-*` stage of the
// Dockerfile, which are the production images, and the identity provider image that
// KeycloakFixture.Image pins. A new production stage is scanned with no change to the scan.
export function findImages(root) {
  const dockerfile = path.join(root, 'Dockerfile')
  const targets = [...readFileSync(dockerfile, 'utf8').matchAll(/^FROM\s+\S+\s+AS\s+(axis-[\w-]+)\s*$/gim)].map(
    (match) => match[1],
  )
  if (targets.length === 0) throw new Error(`${dockerfile} has no axis-* stage`)
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
