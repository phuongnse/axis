// Builds an extension package in a package host image, as an operator does, for the smoke tests scripts/images.test.mjs
// and scripts/onprem.test.mjs. See docs/reference/extension-packages.md#building-a-package.
// The images run as a non-root user, so the folders they write are 0777, and the files they write belong to that user,
// which the host user cannot delete: removeAsImageUser deletes them with the image's own shell.
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { chmodSync, copyFileSync, mkdirSync, readdirSync } from 'node:fs'
import { join } from 'node:path'

const docker = (args, options = {}) => spawnSync('docker', args, { encoding: 'utf8', ...options })

// Copies a folder so that the non-root user of a package host image can read it: mkdtemp makes folders with mode 0700.
export const copyReadable = (from, to, skip = () => false) => {
  mkdirSync(to, { recursive: true })
  chmodSync(to, 0o755)
  for (const entry of readdirSync(from, { withFileTypes: true })) {
    if (skip(entry.name)) continue
    if (entry.isDirectory()) copyReadable(join(from, entry.name), join(to, entry.name), skip)
    else {
      copyFileSync(join(from, entry.name), join(to, entry.name))
      chmodSync(join(to, entry.name), 0o644)
    }
  }
}

// Makes a folder that the non-root user of an image can write.
export const writable = (dir) => {
  mkdirSync(dir)
  chmodSync(dir, 0o777)
  return dir
}

// Downloads Trivy's vulnerability database into `cache` once, in up to three attempts, because the download is the one
// step that needs the network and fails at times. The builds then use it with TRIVY_SKIP_DB_UPDATE, as
// scripts/scan-images.mjs retries its Trivy runs.
export const downloadTrivyDatabase = async ({ image, cache }) => {
  let result
  for (let attempt = 1; attempt <= 3; attempt++) {
    result = docker(['run', '--rm', '--entrypoint', 'trivy', '-e', 'TRIVY_CACHE_DIR=/trivy-cache', '-v', `${cache}:/trivy-cache`, image, 'fs', '--download-db-only'])
    if (result.status === 0) break
    if (attempt < 3) await new Promise((resolve) => setTimeout(resolve, attempt * 15_000))
  }
  assert.equal(result.status, 0, `trivy could not download its database\n${result.stdout}${result.stderr}`)
}

// Builds `source`, mounted read-only, with the `build` command of the package host image and writes the package to
// `extensions`. Returns what the command printed.
export const buildPackage = ({ image, source, extensions, cache }) => {
  const result = docker([
    'run',
    '--rm',
    '-e',
    'TRIVY_CACHE_DIR=/trivy-cache',
    '-e',
    'TRIVY_SKIP_DB_UPDATE=true',
    '-v',
    `${cache}:/trivy-cache`,
    '-v',
    `${source}:/source:ro`,
    '-v',
    `${extensions}:/extensions`,
    image,
    'build',
    '/source',
  ])
  assert.equal(result.status, 0, `docker run ${image} build /source\n${result.stdout}${result.stderr}`)
  return result.stdout.trim()
}

// Deletes what the builds in the subfolders of `dir` wrote, with the shell of the image.
export const removeAsImageUser = (image, dir) => {
  docker(['run', '--rm', '--entrypoint', 'sh', '-v', `${dir}:/work`, image, '-c', 'rm -rf /work/*/*'], { stdio: 'ignore' })
}
