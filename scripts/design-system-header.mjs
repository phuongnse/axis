// Writes the first line of the design-system bundle, which the Design System artifact reads.
//
// Usage: node scripts/design-system-header.mjs BUNDLE
//
// The line is `/* @ds-bundle: {"format":4,"namespace":"Axis","components":[{"name":"TextField"},...]} */`.
// `components` lists every card once, named as the artifact's card folders are, in the order of
// web/src/designSystem/cardOrder.ts. Node runs that file directly, as its only import is a type.
// The script replaces the line when the bundle already has one.
import { readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { cardGroups, cardName } from '../web/src/designSystem/cardOrder.ts'

const marker = '/* @ds-bundle: '

/** The first line of the bundle: the format, the namespace and every card's name, in order. */
export function bundleHeader() {
  const components = cardGroups.flatMap((group) => group.previews).map((preview) => ({ name: cardName(preview) }))
  return `${marker}${JSON.stringify({ format: 4, namespace: 'Axis', components })} */`
}

/** The bundle's source with the header as its first line, in place of an earlier header. */
export function withHeader(source) {
  const body = source.startsWith(marker) ? source.slice(source.indexOf('\n') + 1) : source
  return `${bundleHeader()}\n${body}`
}

function main(args) {
  if (args.length !== 1) {
    process.stderr.write('Usage: node scripts/design-system-header.mjs BUNDLE\n')
    return 2
  }
  const file = path.resolve(args[0])
  writeFileSync(file, withHeader(readFileSync(file, 'utf8')))
  return 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2))
}
