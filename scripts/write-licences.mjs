// Writes the list of open source packages Axis ships.
//
// Usage: node scripts/write-licences.mjs [PUBLIC_FILE [PRIVATE_FILE]]
//
// PUBLIC_FILE defaults to src/Axis.Server/wwwroot/licences.json and PRIVATE_FILE to src/Axis.Server/licences.json.
// Both hold { "npm": [...], "nuget": [...] }. Each entry has a name and a licence. The private file adds the version,
// so only GET /api/licences gives versions, and only to a signed-in person. The npm list is the runtime packages of
// web/package-lock.json, without the dev packages. The NuGet list is the packages of the server, the worker and
// Axis.Admin, read from obj/project.assets.json, each with the licence of its nuspec. The environment variable
// AXIS_LICENCES_ROOT replaces the repository root, for the tests. Prints the counts, and exits 1 when a source file is
// missing or a list in the output is empty.
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const scriptRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

const nugetProjects = ['Axis.Server', 'Axis.Worker', 'Axis.Admin']
const unknown = 'UNKNOWN'

/** The runtime packages of the parsed package-lock.json lock, as { name, version, licence }. */
export function npmPackages(lock) {
  const modules = 'node_modules/'
  return Object.entries(lock.packages ?? {})
    .filter(([key, entry]) => key !== '' && !entry.link && !entry.dev)
    .map(([key, entry]) => ({
      name: entry.name ?? key.slice(key.lastIndexOf(modules) + modules.length),
      version: entry.version,
      licence: entry.license ?? unknown,
    }))
}

const decode = (text) =>
  text.trim().replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, '&')

/** The licence of the nuspec text: the license expression, else the license file name, else the licenseUrl, else UNKNOWN. */
export function nuspecLicence(nuspec) {
  const element = (name, type) => {
    const attribute = type ? `\\s+type="${type}"` : ''
    const match = new RegExp(`<${name}${attribute}[^>]*>([^<]*)</${name}>`, 'i').exec(nuspec)
    return match && decode(match[1]) !== '' ? decode(match[1]) : null
  }
  return element('license', 'expression') ?? element('license', 'file') ?? element('licenseUrl') ?? unknown
}

/**
 * The packages of the parsed project.assets.json file assets, as { name, version, licence }. readNuspec(file) gives the
 * text of a nuspec file, or null when it does not exist.
 */
export function nugetPackages(assets, readNuspec) {
  const packageFolder = Object.keys(assets.packageFolders ?? {})[0] ?? ''
  return Object.entries(assets.libraries ?? {})
    .filter(([, library]) => library.type === 'package')
    .map(([key, library]) => {
      const slash = key.lastIndexOf('/')
      const id = key.slice(0, slash)
      const nuspec = readNuspec(path.join(packageFolder, library.path, `${id.toLowerCase()}.nuspec`))
      return { name: id, version: key.slice(slash + 1), licence: nuspec === null ? unknown : nuspecLicence(nuspec) }
    })
}

const byNameAndVersion = (packages) => {
  const unique = new Map()
  for (const entry of packages) unique.set(`${entry.name}/${entry.version}`, entry)
  return [...unique.values()].sort(
    (a, b) => a.name.localeCompare(b.name) || a.version.localeCompare(b.version),
  )
}

const withoutVersion = ({ name, licence }) => ({ name, licence })

const writeJson = (file, value) => {
  mkdirSync(path.dirname(file), { recursive: true })
  writeFileSync(file, `${JSON.stringify(value, null, 2)}\n`)
}

/**
 * Reads the lock file and the project assets of root and writes the public file outPublic and the private file
 * outPrivate. Returns the npm and NuGet package counts and the problems found.
 */
export function writeLicences({ root, outPublic, outPrivate }) {
  const problems = []
  const sources = [
    path.join(root, 'web', 'package-lock.json'),
    ...nugetProjects.map((project) => path.join(root, 'src', project, 'obj', 'project.assets.json')),
  ]
  for (const source of sources) {
    if (!existsSync(source)) problems.push(`${path.relative(root, source)} is missing. Run npm ci and dotnet restore first.`)
  }
  if (problems.length > 0) return { npm: 0, nuget: 0, problems }

  const readNuspec = (file) => (existsSync(file) ? readFileSync(file, 'utf8') : null)
  const npm = byNameAndVersion(npmPackages(JSON.parse(readFileSync(sources[0], 'utf8'))))
  const nuget = byNameAndVersion(
    sources.slice(1).flatMap((file) => nugetPackages(JSON.parse(readFileSync(file, 'utf8')), readNuspec)),
  )
  if (npm.length === 0) problems.push('The lock file has no runtime npm packages.')
  if (nuget.length === 0) problems.push('The project assets files have no NuGet packages.')

  writeJson(outPrivate, { npm, nuget })
  writeJson(outPublic, { npm: npm.map(withoutVersion), nuget: nuget.map(withoutVersion) })
  return { npm: npm.length, nuget: nuget.length, problems }
}

function main(args) {
  const outPublic = path.resolve(args[0] ?? path.join(scriptRoot, 'src', 'Axis.Server', 'wwwroot', 'licences.json'))
  const outPrivate = path.resolve(args[1] ?? path.join(scriptRoot, 'src', 'Axis.Server', 'licences.json'))
  const { npm, nuget, problems } = writeLicences({ root: process.env.AXIS_LICENCES_ROOT ?? scriptRoot, outPublic, outPrivate })
  console.log(`Licences: ${npm} npm packages, ${nuget} NuGet packages.`)
  for (const problem of problems) process.stderr.write(`${problem}\n`)
  return problems.length > 0 ? 1 : 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2))
}
