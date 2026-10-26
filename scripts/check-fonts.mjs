// Checks the fonts in the built SPA.
//
// Usage: node scripts/check-fonts.mjs [SPA_DIR]
//
// SPA_DIR defaults to src/Axis.Server/wwwroot. The font files are the .woff2 and .woff files in assets/. Prints
// them, and exits 1 when
// - a font file is not one of IBM Plex Sans (the variable file) or IBM Plex Mono (400) in the Latin, Latin Extended
//   or Vietnamese subset, so another subset or family ships,
// - the three subsets of one family have no .woff2 file in assets/, or
// - the SIL Open Font License 1.1 text of a family is missing from fonts/, as OFL-IBM-Plex-Sans.txt or
//   OFL-IBM-Plex-Mono.txt.
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const scriptRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

const families = [
  { name: 'IBM Plex Sans', prefix: 'ibm-plex-sans', weight: 'wght', license: 'OFL-IBM-Plex-Sans.txt' },
  { name: 'IBM Plex Mono', prefix: 'ibm-plex-mono', weight: '400', license: 'OFL-IBM-Plex-Mono.txt' },
]
const subsets = ['latin', 'latin-ext', 'vietnamese']
const licenseHeading = 'SIL OPEN FONT LICENSE Version 1.1'

const fontFiles = (spaDirectory) => {
  const assets = path.join(spaDirectory, 'assets')
  if (!existsSync(assets)) return []
  return readdirSync(assets)
    .filter((name) => /\.woff2?$/.test(name))
    .sort()
}

/**
 * Checks the font files and license texts in the SPA folder spaDirectory. Returns the font files, relative to
 * the assets folder, and the problems found.
 */
export function checkFonts(spaDirectory) {
  const files = fontFiles(spaDirectory)
  const problems = []

  const expected = (family, subset, extension) =>
    new RegExp(`^${family.prefix}-${subset}-${family.weight}-normal-.+\\.${extension}$`)
  for (const file of files) {
    const known = families.some((family) => subsets.some((subset) => expected(family, subset, 'woff2?').test(file)))
    if (!known) {
      problems.push(`The font file assets/${file} is not IBM Plex in the Latin, Latin Extended or Vietnamese subset.`)
    }
  }
  for (const family of families) {
    for (const subset of subsets) {
      if (!files.some((file) => expected(family, subset, 'woff2').test(file))) {
        problems.push(`There is no ${family.name} ${subset} font file: no .woff2 file in assets/ matches ${family.prefix}-${subset}-${family.weight}-normal-*.`)
      }
    }
    const license = path.join(spaDirectory, 'fonts', family.license)
    if (!existsSync(license)) {
      problems.push(`The ${family.name} license text fonts/${family.license} is missing.`)
    } else if (!readFileSync(license, 'utf8').includes(licenseHeading)) {
      problems.push(`fonts/${family.license} does not hold the text '${licenseHeading}'.`)
    }
  }
  return { files, problems }
}

function main(args) {
  const spaDirectory = path.resolve(args[0] ?? path.join(scriptRoot, 'src', 'Axis.Server', 'wwwroot'))
  const { files, problems } = checkFonts(spaDirectory)
  console.log(`Font files: ${files.length}.`)
  for (const file of files) console.log(`  assets/${file}`)
  for (const problem of problems) process.stderr.write(`${problem}\n`)
  return problems.length > 0 ? 1 : 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2))
}
