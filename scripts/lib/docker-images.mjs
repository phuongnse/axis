// The saved copy of every container image that the integration, E2E and smoke checks pull, so that an outage or a rate
// limit of Docker Hub, Quay or Microsoft's registry does not fail them, see docs/development.md#setup and D147.
// scripts/docker-images.json lists the images. `save` (run by scripts/setup.sh on CI) keeps them all in one archive,
// images.tar, with the index images.json beside it in the cache folder, and pulls only the images the archive lacks.
// One archive stores each shared layer once. `load` (run by the suites' scripts) loads the archive when Docker lacks
// one of its images, and never pulls. AXIS_DOCKER_CACHE_DIR sets the folder, which is ~/.cache/axis-docker by default.
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs'
import { homedir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { runDocker } from './docker-build.mjs'

const root = fileURLToPath(new URL('../..', import.meta.url))
const archiveName = 'images.tar'
const indexName = 'images.json'

export const cacheDirectory = () => process.env.AXIS_DOCKER_CACHE_DIR ?? path.join(homedir(), '.cache/axis-docker')

// The images the list holds, sorted.
export function listedImages(base = root) {
  return JSON.parse(readFileSync(path.join(base, 'scripts/docker-images.json'), 'utf8')).toSorted()
}

// The listed image of one repository, such as postgres, with its tag.
export function listedImage(base, repository) {
  const image = listedImages(base).find((name) => name.slice(0, name.lastIndexOf(':')) === repository)
  if (!image) throw new Error(`scripts/docker-images.json lists no ${repository} image`)
  return image
}

const present = (image) => spawnSync('docker', ['image', 'inspect', image], { stdio: 'ignore' }).status === 0

// The images the index names, or undefined when the archive or the index is missing or the index is unreadable.
function saved(dir) {
  if (!existsSync(path.join(dir, archiveName)) || !existsSync(path.join(dir, indexName))) return undefined
  try {
    const images = JSON.parse(readFileSync(path.join(dir, indexName), 'utf8'))
    return Array.isArray(images) ? images.toSorted() : undefined
  } catch {
    return undefined
  }
}

const discard = (dir) => {
  rmSync(path.join(dir, archiveName), { force: true })
  rmSync(path.join(dir, indexName), { force: true })
}

// Loads the archive. A load that fails deletes the archive and the index, so the next save writes new ones.
async function loadArchive(dir) {
  try {
    await runDocker(['load', '-i', path.join(dir, archiveName)], { attempts: 1 })
    return true
  } catch (error) {
    console.error(`${error.message}, so ${archiveName} and ${indexName} in ${dir} are deleted`)
    discard(dir)
    return false
  }
}

// Makes the archive hold every listed image. Pulls only the images that neither Docker nor the archive has, and
// leaves out an image that still fails to pull after its tries: a job that needs no image must not fail when a
// registry is down, and the suite that needs it pulls it itself.
export async function saveImages({ dir = cacheDirectory(), base = root } = {}) {
  const listed = listedImages(base)
  const index = saved(dir)
  if (index && index.length === listed.length && index.every((image, i) => image === listed[i])) {
    console.log(`Every listed container image is in ${path.join(dir, archiveName)}.`)
    return
  }
  if (spawnSync('docker', ['info'], { stdio: 'ignore' }).status !== 0) {
    console.log('Docker is not running, so the container images are not saved.')
    return
  }

  let missing = listed.filter((image) => !present(image))
  if (missing.length > 0 && index && (await loadArchive(dir))) missing = missing.filter((image) => !present(image))
  for (const image of missing) {
    try {
      await runDocker(['pull', image])
    } catch (error) {
      console.error(`Warning: ${error.message}, so ${image} is not saved.`)
    }
  }

  const images = listed.filter(present)
  if (images.length === 0) return
  mkdirSync(dir, { recursive: true })
  const temporary = path.join(dir, `${archiveName}.tmp`)
  await runDocker(['save', '-o', temporary, ...images], { attempts: 1 })
  renameSync(temporary, path.join(dir, archiveName))
  writeFileSync(path.join(dir, indexName), `${JSON.stringify(images, null, 2)}\n`)
}

// Loads the saved images when Docker lacks one of them. Never pulls: an image that the archive lacks is pulled by
// the suite that needs it.
export async function loadImages({ dir = cacheDirectory() } = {}) {
  const images = saved(dir)
  if (!images) {
    console.log(`${dir} holds no saved container images.`)
    return
  }
  if (images.every(present)) return
  await loadArchive(dir)
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const [command] = process.argv.slice(2)
  if (!['save', 'load'].includes(command)) {
    console.error('usage: node scripts/lib/docker-images.mjs save|load')
    process.exit(2)
  }
  try {
    if (command === 'save') await saveImages()
    else await loadImages()
  } catch (error) {
    console.error(error.message)
    process.exit(1)
  }
}
