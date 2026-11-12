// The production images of one smoke run. scripts/smoke.sh runs `node scripts/lib/smoke-images.mjs build <version>`
// once, before the images and onprem suites, and `remove <version>` when it exits. The suites call smokeImages.
// The PostgreSQL tools layer of axis-admin comes from the admin-tools stage, which the build saves with its base
// image in admin-tools-<hash>.tar in the cache folder and loads in a later run, see docs/development.md#smoke-tests.
// The base image is pulled only when Docker lacks it: scripts/lib/docker-images.mjs loads the saved images first, see
// docs/development.md#setup. AXIS_DOCKER_CACHE_DIR sets the folder, which is ~/.cache/axis-docker by default.
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { existsSync, mkdirSync, readdirSync, rmSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { buildImage, runDocker } from './docker-build.mjs'
import { cacheDirectory, present } from './docker-images.mjs'
import { productionTargets, stageText } from './images.mjs'

const root = fileURLToPath(new URL('../..', import.meta.url))
const toolsStage = 'admin-tools'

// Builds the production images, with axis-admin on the admin-tools layer that an earlier run saved when its stage
// text is unchanged. A saved file that does not load is deleted and the layer is built again.
export async function buildSmokeImages(version, { dir = cacheDirectory() } = {}) {
  const text = stageText(root, toolsStage)
  const base = text.split('\n')[0].match(/^FROM\s+(\S+)/i)[1]
  const hash = createHash('sha256').update(text).digest('hex').slice(0, 12)
  const file = path.join(dir, `${toolsStage}-${hash}.tar`)
  const tag = `axis-${toolsStage}:${hash}`

  let loaded = false
  if (existsSync(file)) {
    try {
      await runDocker(['load', '-i', file], { attempts: 1 })
      loaded = true
    } catch (error) {
      console.error(`${error.message}, so ${file} is deleted and the layer is built again`)
      rmSync(file, { force: true })
    }
  }
  if (!loaded) {
    if (!present(base)) await runDocker(['pull', base])
    await buildImage({ target: toolsStage, tag, buildArgs: ['BUILDKIT_INLINE_CACHE=1'] })
    mkdirSync(dir, { recursive: true })
    for (const old of readdirSync(dir)) {
      if (/^admin-tools-.+\.tar$/.test(old) && old !== path.basename(file)) rmSync(path.join(dir, old), { force: true })
    }
    await runDocker(['save', '-o', file, tag, base])
  }

  for (const target of productionTargets(root)) {
    await buildImage({ target, version, tag: `${target}:${version}`, cacheFrom: target === 'axis-admin' ? tag : undefined })
  }
}

export function removeSmokeImages(version) {
  for (const target of productionTargets(root)) {
    spawnSync('docker', ['rmi', '-f', `${target}:${version}`], { cwd: root, stdio: 'ignore' })
  }
}

// For a suite: the version of the images that scripts/smoke.sh built. Fails when one is missing.
export function smokeImages(targets) {
  const version = process.env.AXIS_SMOKE_IMAGE_VERSION
  assert.ok(version, 'AXIS_SMOKE_IMAGE_VERSION is not set: run from scripts/smoke.sh')
  for (const target of targets) {
    const result = spawnSync('docker', ['image', 'inspect', `${target}:${version}`], { cwd: root, stdio: 'ignore' })
    assert.equal(
      result.status,
      0,
      `${target}:${version} is missing: the production image build in scripts/smoke.sh failed, see its log`,
    )
  }
  return version
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const [command, version] = process.argv.slice(2)
  if (!version || !['build', 'remove'].includes(command)) {
    console.error('usage: node scripts/lib/smoke-images.mjs build|remove <version>')
    process.exit(2)
  }
  try {
    if (command === 'build') await buildSmokeImages(version)
    else removeSmokeImages(version)
  } catch (error) {
    console.error(error.message)
    process.exit(1)
  }
}
