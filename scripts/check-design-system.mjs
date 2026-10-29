// Checks the design-system bundle that scripts/design-system.sh builds.
//
// Usage: node scripts/check-design-system.mjs [FOLDER]
//
// FOLDER defaults to artifacts/design-system. The Design System artifact loads the bundle as a classic
// script and inlines it in a page, so the folder must hold exactly one file, axis-design-system.js, and
// that file must
// - be at most 6 MB, the cap of the Design System type,
// - start with the line `/* @ds-bundle: {...} */` that scripts/design-system-header.mjs writes: JSON with the card
//   `width`, a positive number, and the `groups` of cards, each with a name and the previews it lists, once in all,
// - parse as a classic script, so it has no `import` declaration and no `import.meta`,
// - have no dynamic `import(`, which would load code after the script, and
// - have no literal `</script` or `<!--`, which would end or hide an inlined script.
// Prints the size and exits 1 with the problems on stderr when a check fails.
import { readdirSync, readFileSync, statSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import vm from 'node:vm'

const scriptRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
export const bundleFile = 'axis-design-system.js'
export const maxBytes = 6 * 1024 * 1024
export const headerPattern = /^\/\* @ds-bundle: (\{.*\}) \*\/$/

const megabytes = (bytes) => `${(bytes / 1024 / 1024).toFixed(2)} MB`

const isName = (value) => typeof value === 'string' && value !== ''

/** The problems in the bundle's first line, which hands the Design System artifact the card width and order. */
function checkHeader(source) {
  const match = headerPattern.exec(source.split('\n', 1)[0])
  if (!match) {
    return [`${bundleFile} must start with a line like /* @ds-bundle: {"width":992,"groups":[...]} */.`]
  }
  let header
  try {
    header = JSON.parse(match[1])
  } catch (error) {
    return [`The @ds-bundle line of ${bundleFile} is not JSON: ${error.message}`]
  }
  const problems = []
  if (typeof header.width !== 'number' || !(header.width > 0)) {
    problems.push(`The @ds-bundle line of ${bundleFile} needs a positive number as its width.`)
  }
  const { groups } = header
  const listed = new Set()
  const repeated = new Set()
  const wellFormed =
    Array.isArray(groups) &&
    groups.length > 0 &&
    groups.every(
      (group) =>
        group !== null &&
        isName(group.name) &&
        Array.isArray(group.previews) &&
        group.previews.length > 0 &&
        group.previews.every(isName),
    )
  if (!wellFormed) {
    problems.push(
      `The @ds-bundle line of ${bundleFile} needs groups: a non-empty array of {name, previews} with named previews.`,
    )
  } else {
    for (const preview of groups.flatMap((group) => group.previews)) {
      if (listed.has(preview)) repeated.add(preview)
      listed.add(preview)
    }
    if (repeated.size > 0) {
      problems.push(`The @ds-bundle line of ${bundleFile} lists a preview twice: ${[...repeated].join(', ')}.`)
    }
  }
  return problems
}

/** Checks the folder and returns the size of the bundle in bytes, or 0 when there is none, and the problems found. */
export function checkDesignSystemBundle(folder) {
  const problems = []
  const files = readdirSync(folder, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile())
    .map((entry) => path.relative(folder, path.join(entry.parentPath, entry.name)))
  if (files.length !== 1 || files[0] !== bundleFile) {
    problems.push(`The folder must hold only ${bundleFile}, but it holds: ${files.join(', ') || 'no files'}.`)
  }
  if (!files.includes(bundleFile)) {
    return { bytes: 0, problems }
  }

  const file = path.join(folder, bundleFile)
  const bytes = statSync(file).size
  const source = readFileSync(file, 'utf8')
  if (bytes > maxBytes) {
    problems.push(`${bundleFile} is ${megabytes(bytes)}, over the ${megabytes(maxBytes)} cap.`)
  }
  problems.push(...checkHeader(source))
  try {
    new vm.Script(source, { filename: bundleFile })
  } catch (error) {
    problems.push(`${bundleFile} is not a classic script: ${error.message}`)
  }
  if (/\bimport\s*\(/.test(source)) {
    problems.push(`${bundleFile} has a dynamic import().`)
  }
  if (/<\/script/i.test(source)) {
    problems.push(`${bundleFile} has a literal </script.`)
  }
  if (/<!--/.test(source)) {
    problems.push(`${bundleFile} has a literal <!--.`)
  }
  return { bytes, problems }
}

function main(args) {
  const folder = path.resolve(args[0] ?? path.join(scriptRoot, 'artifacts', 'design-system'))
  const { bytes, problems } = checkDesignSystemBundle(folder)
  console.log(`Design-system bundle: ${megabytes(bytes)}, cap ${megabytes(maxBytes)}.`)
  for (const problem of problems) process.stderr.write(`${problem}\n`)
  return problems.length > 0 ? 1 : 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2))
}
