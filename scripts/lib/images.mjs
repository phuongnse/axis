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
