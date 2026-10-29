// Writes the first line of the design-system bundle, which the Design System artifact reads.
//
// Usage: node scripts/design-system-header.mjs BUNDLE
//
// The line is `/* @ds-bundle: {"width":992,"groups":[{"name":"Field inputs","previews":["field-text",...]},...]} */`.
// `width` is the width of a card, breakpointTokens.narrowWidth, and `groups` is the order of the cards, from
// web/src/designSystem/cardOrder.ts. Node runs both TypeScript files directly, as their only import is a type.
// The script replaces the line when the bundle already has one.
import { readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { breakpointTokens } from '../web/src/platform/designTokens.ts'
import { cardGroups } from '../web/src/designSystem/cardOrder.ts'

const marker = '/* @ds-bundle: '

/** The first line of the bundle: the card width and the groups of cards in order. */
export function bundleHeader() {
  return `${marker}${JSON.stringify({ width: breakpointTokens.narrowWidth, groups: cardGroups })} */`
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
