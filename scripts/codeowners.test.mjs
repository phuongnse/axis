import assert from 'node:assert/strict'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// The owner alone guards the security checks, so every file that CI's dependency and image scans run, and every
// accepted list, is listed in .github/CODEOWNERS. See
// docs/decisions/d137-authentication-by-default-and-the-owner-guards-the-security-checks-alone.md. The workflow is
// read line by line, since the scripts' tests have no YAML dependency.
const root = fileURLToPath(new URL('..', import.meta.url))

const scanJobs = ['image-scan', 'dependency-scan']

const indentOf = (line) => line.length - line.trimStart().length

const isContent = (line) => line.trim() !== '' && !line.trimStart().startsWith('#')

// The lines of the block that starts after `start` and ends before the next content line at `indent` or less.
const blockAfter = (lines, start, indent) => {
  const block = []
  for (const line of lines.slice(start + 1)) {
    if (isContent(line) && indentOf(line) <= indent) break
    block.push(line)
  }
  return block
}

const ownedPatterns = (codeowners) =>
  codeowners
    .split('\n')
    .filter(isContent)
    .map((line) => line.trim().split(/\s+/)[0].replace(/^\//, ''))

const isOwned = (patterns, path) =>
  patterns.some((pattern) => pattern === path || (pattern.endsWith('/') && path.startsWith(pattern)))

// The block of each job under `jobs:`, by its name.
const jobBlocks = (ci) => {
  const lines = ci.split('\n')
  const start = lines.findIndex((line) => line === 'jobs:')
  if (start === -1) return new Map()
  const blocks = new Map()
  const jobs = blockAfter(lines, start, 0)
  jobs.forEach((line, index) => {
    const match = /^ {2}([\w-]+):\s*$/.exec(line)
    if (match) blocks.set(match[1], blockAfter(jobs, index, 2))
  })
  return blocks
}

// The problems of each file a scan job runs, or an accepted list, that CODEOWNERS does not list. An entry script is
// followed one step down: the files it sources and the .mjs file it runs, but not that file's imports.
const scanOwnershipProblems = ({ codeowners, ci, read, acceptedLists }) => {
  const patterns = ownedPatterns(codeowners)
  const blocks = jobBlocks(ci)
  const problems = []
  const files = new Set(acceptedLists)
  for (const job of scanJobs) {
    const block = blocks.get(job)
    if (block === undefined) {
      problems.push(`ci.yml has no ${job} job`)
      continue
    }
    for (const line of block) {
      const entry = /^\s*run:\s*(scripts\/[\w./-]+\.sh)\s*$/.exec(line)?.[1]
      if (entry === undefined) continue
      files.add(entry)
      const text = read(entry) ?? ''
      for (const [, path] of text.matchAll(/^\s*source\s+(scripts\/[\w./-]+)/gm)) files.add(path)
      for (const [, path] of text.matchAll(/\bnode\s+(scripts\/[\w./-]+\.mjs)\b/g)) files.add(path)
    }
  }
  for (const path of files) {
    if (!isOwned(patterns, path)) problems.push(`${path} runs in a scan but CODEOWNERS does not list it`)
  }
  return problems.sort()
}

const readRoot = (path) => readFileSync(join(root, path), 'utf8')

const realFiles = () => ({
  codeowners: readRoot('.github/CODEOWNERS'),
  ci: readRoot('.github/workflows/ci.yml'),
  read: readRoot,
  acceptedLists: readdirSync(join(root, 'scripts'))
    .filter((name) => /-accepted\.json$/.test(name))
    .map((name) => `scripts/${name}`),
})

const fixtureCi = (jobs) => ['name: CI', 'on: push', 'jobs:', ...jobs, ''].join('\n')

const imageScanJob = [
  '  image-scan:',
  '    runs-on: ubuntu-24.04',
  '    steps:',
  '      - name: Image scan',
  '        run: scripts/scan-images.sh',
]

describe('CODEOWNERS lists every file the dependency and image scans run', () => {
  test('the scan scripts, what they source and run, and the accepted lists are all owned', () => {
    assert.deepEqual(scanOwnershipProblems(realFiles()), [])
  })

  test('a CODEOWNERS without the dependency scan script reports it', () => {
    const files = realFiles()
    const codeowners = files.codeowners
      .split('\n')
      .filter((line) => !line.startsWith('/scripts/scan.sh '))
      .join('\n')

    assert.deepEqual(scanOwnershipProblems({ ...files, codeowners }), [
      'scripts/scan.sh runs in a scan but CODEOWNERS does not list it',
    ])
  })

  test('a file that a scan script sources is reported when CODEOWNERS lacks it', () => {
    const scripts = {
      'scripts/scan-images.sh': 'source scripts/lib/tools.sh\ninstall_trivy\nexec node scripts/scan-images.mjs "$@"\n',
    }

    assert.deepEqual(
      scanOwnershipProblems({
        codeowners: '/scripts/scan-images.sh @owner\n/scripts/scan-images.mjs @owner\n',
        ci: fixtureCi(imageScanJob),
        read: (path) => scripts[path],
        acceptedLists: [],
      }),
      ['ci.yml has no dependency-scan job', 'scripts/lib/tools.sh runs in a scan but CODEOWNERS does not list it'],
    )
  })

  test('a workflow without the scan jobs names each missing job', () => {
    assert.deepEqual(
      scanOwnershipProblems({
        codeowners: '',
        ci: fixtureCi(['  build:', '    runs-on: ubuntu-24.04']),
        read: () => undefined,
        acceptedLists: [],
      }),
      ['ci.yml has no dependency-scan job', 'ci.yml has no image-scan job'],
    )
  })
})
