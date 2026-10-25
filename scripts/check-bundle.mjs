// Checks the size of the built SPA against its budget.
//
// Usage: node scripts/check-bundle.mjs [SPA_DIR] [--budget FILE]
//
// SPA_DIR defaults to src/Axis.Server/wwwroot and FILE to web/bundle-budget.json. The main bundle is
// the entry script of index.html plus every script it preloads. The chart chunk is the file in
// assets/ whose name starts with the budget's chartChunk prefix, the lazy module that draws charts. The page
// design chunk is the same for the designChunk prefix, the lazy module of the page designer's canvas and palette.
// The drag and drop chunk is the same for the dragChunk prefix, the lazy @dnd-kit library both designers share.
// Sizes are gzipped. Prints the four sizes, and exits 1 when
// - the main bundle is larger than mainBaselineBytes + allowedGrowthBytes,
// - there is no separate chart chunk, page design chunk or drag and drop chunk, or
// - the chart code, the page designer or the drag and drop library is among the main bundle files, so every
//   page would load it.
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { gzipSync } from 'node:zlib'

const scriptRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

const entryPattern = /<script\b[^>]*\btype="module"[^>]*\bsrc="([^"]+)"|<script\b[^>]*\bsrc="([^"]+)"[^>]*\btype="module"/g
const preloadPattern = /<link\b[^>]*\brel="modulepreload"[^>]*\bhref="([^"]+)"|<link\b[^>]*\bhref="([^"]+)"[^>]*\brel="modulepreload"/g

const kilobytes = (bytes) => `${(bytes / 1024).toFixed(1)} KB`

function gzippedSize(file) {
  return gzipSync(readFileSync(file)).length
}

/** The files of the main bundle, relative to the SPA folder: the entry script, then the preloaded scripts. */
function mainBundleFiles(spaDirectory) {
  const html = readFileSync(path.join(spaDirectory, 'index.html'), 'utf8')
  const references = [...html.matchAll(entryPattern), ...html.matchAll(preloadPattern)].map((match) => match[1] ?? match[2])
  return [...new Set(references.map((reference) => reference.replace(/^\/+/, '')))]
}

/** The files in assets/ whose name starts with `prefix`, relative to the SPA folder. */
function chunkFiles(spaDirectory, prefix) {
  const assets = path.join(spaDirectory, 'assets')
  if (!existsSync(assets)) return []
  return readdirSync(assets)
    .filter((name) => name.startsWith(prefix) && name.endsWith('.js'))
    .map((name) => `assets/${name}`)
}

/**
 * Measures the SPA in spaDirectory and checks it against the budget. Returns the gzipped size of the
 * main bundle, of the chart chunk, of the page design chunk and of the drag and drop chunk in bytes, and the problems found.
 */
export function checkBundle(spaDirectory, budget) {
  const mainFiles = mainBundleFiles(spaDirectory)
  const chartFiles = chunkFiles(spaDirectory, budget.chartChunk)
  const designFiles = chunkFiles(spaDirectory, budget.designChunk)
  const dragFiles = chunkFiles(spaDirectory, budget.dragChunk)
  const mainBytes = mainFiles.reduce((sum, file) => sum + gzippedSize(path.join(spaDirectory, file)), 0)
  const chartBytes = chartFiles.reduce((sum, file) => sum + gzippedSize(path.join(spaDirectory, file)), 0)
  const designBytes = designFiles.reduce((sum, file) => sum + gzippedSize(path.join(spaDirectory, file)), 0)
  const dragBytes = dragFiles.reduce((sum, file) => sum + gzippedSize(path.join(spaDirectory, file)), 0)
  const limit = budget.mainBaselineBytes + budget.allowedGrowthBytes

  const problems = []
  if (mainBytes > limit) {
    problems.push(
      `The main bundle is ${kilobytes(mainBytes)} gzipped, ${kilobytes(mainBytes - limit)} over its budget of ${kilobytes(limit)} ` +
        `(${kilobytes(budget.mainBaselineBytes)} before M12 plus ${kilobytes(budget.allowedGrowthBytes)}).`,
    )
  }
  if (chartFiles.length === 0) {
    problems.push(`There is no chart chunk: no file in assets/ starts with '${budget.chartChunk}'.`)
  }
  for (const file of mainFiles.filter((candidate) => chartFiles.includes(candidate))) {
    problems.push(`The chart chunk ${file} is part of the main bundle, so every page loads the chart code.`)
  }
  if (designFiles.length === 0) {
    problems.push(`There is no page design chunk: no file in assets/ starts with '${budget.designChunk}'.`)
  }
  for (const file of mainFiles.filter((candidate) => designFiles.includes(candidate))) {
    problems.push(`The page design chunk ${file} is part of the main bundle, so every page loads the page designer.`)
  }
  if (dragFiles.length === 0) {
    problems.push(`There is no drag and drop chunk: no file in assets/ starts with '${budget.dragChunk}'.`)
  }
  for (const file of mainFiles.filter((candidate) => dragFiles.includes(candidate))) {
    problems.push(`The drag and drop chunk ${file} is part of the main bundle, so every page loads the drag and drop library.`)
  }
  return { mainBytes, chartBytes, designBytes, dragBytes, problems }
}

function main(args) {
  const rest = [...args]
  const budgetIndex = rest.indexOf('--budget')
  const budgetFile =
    budgetIndex >= 0 ? path.resolve(rest.splice(budgetIndex, 2)[1]) : path.join(scriptRoot, 'web', 'bundle-budget.json')
  const spaDirectory = path.resolve(rest[0] ?? path.join(scriptRoot, 'src', 'Axis.Server', 'wwwroot'))
  const budget = JSON.parse(readFileSync(budgetFile, 'utf8'))

  const { mainBytes, chartBytes, designBytes, dragBytes, problems } = checkBundle(spaDirectory, budget)
  console.log(
    `Main bundle: ${kilobytes(mainBytes)} gzipped, budget ${kilobytes(budget.mainBaselineBytes + budget.allowedGrowthBytes)}.`,
  )
  console.log(`Chart chunk: ${kilobytes(chartBytes)} gzipped.`)
  console.log(`Page design chunk: ${kilobytes(designBytes)} gzipped.`)
  console.log(`Drag and drop chunk: ${kilobytes(dragBytes)} gzipped.`)
  for (const problem of problems) process.stderr.write(`${problem}\n`)
  return problems.length > 0 ? 1 : 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2))
}
