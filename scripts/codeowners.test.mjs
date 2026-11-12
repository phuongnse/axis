import assert from 'node:assert/strict'
import { readdirSync, readFileSync } from 'node:fs'
import { join, posix } from 'node:path'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { blockAfter, isContent } from './lib/workflow-lines.mjs'

// The owner alone guards the security checks, so every file that CI's dependency and image scans run, through every
// file they source, run or import, and every accepted list, is listed in .github/CODEOWNERS. See
// docs/decisions/d137-authentication-by-default-and-the-owner-guards-the-security-checks-alone.md. The workflow is
// read line by line with scripts/lib/workflow-lines.mjs.
const root = fileURLToPath(new URL('..', import.meta.url))

const scanJobs = ['image-scan', 'dependency-scan']

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

// The files that `path` runs. A shell script's sourced files, its `.mjs` files run with node, and its scripts run
// with `bash` or `sh` or by their path in command position, outside comment lines. A `.mjs` file's own imports by a
// relative path, and, when it imports `child_process`, the quoted `scripts/` paths of the scripts it runs. Package
// imports such as `node:fs` are not the repository's code.
const filesRunBy = (path, text) => {
  if (path.endsWith('.sh')) {
    const content = text.split('\n').filter(isContent).join('\n')
    return [
      ...[...text.matchAll(/^\s*(?:source|\.)\s+(scripts\/[\w./-]+)/gm)].map((match) => match[1]),
      ...[...text.matchAll(/\bnode\s+(scripts\/[\w./-]+\.mjs)\b/g)].map((match) => match[1]),
      ...[
        ...content.matchAll(
          /(?:^|[;&|(]|\b(?:then|do|else|if|exec|bash|sh)\s)\s*(?:\.\/)?(scripts\/[\w./-]+\.(?:sh|mjs))\b/gm,
        ),
      ].map((match) => match[1]),
    ]
  }
  const imports = [...text.matchAll(/(?:\bfrom\s+|\bimport\s*\(?\s*)['"](\.{1,2}\/[^'"]+)['"]/g)].map((match) =>
    posix.join(posix.dirname(path), match[1]),
  )
  if (!/['"](?:node:)?child_process['"]/.test(text)) return imports
  const code = text
    .split('\n')
    .filter((line) => !line.trimStart().startsWith('//'))
    .join('\n')
  return [
    ...imports,
    ...[...code.matchAll(/['"`](?:\.\/)?(scripts\/[\w./-]+\.(?:sh|mjs))['"`]/g)].map((match) => match[1]),
  ]
}

// The problems of each file a scan job runs, or an accepted list, that CODEOWNERS does not list. Each entry script
// is followed to every file it sources, runs or imports, and on through theirs.
const scanOwnershipProblems = ({ codeowners, ci, read, acceptedLists }) => {
  const patterns = ownedPatterns(codeowners)
  const blocks = jobBlocks(ci)
  const problems = []
  const files = new Set(acceptedLists)
  const pending = []
  for (const job of scanJobs) {
    const block = blocks.get(job)
    if (block === undefined) {
      problems.push(`ci.yml has no ${job} job`)
      continue
    }
    // A step may run its script on a `run:` line, in a `run: |` body, through `bash` or with `node`.
    const entries = block
      .filter(isContent)
      .flatMap((line) => [...line.matchAll(/(?:^|[\s:])(scripts\/[\w./-]+\.(?:sh|mjs))\b/g)].map((match) => match[1]))
    if (entries.length === 0) problems.push(`ci.yml's ${job} job runs no script under scripts/`)
    pending.push(...entries)
  }
  while (pending.length > 0) {
    const path = pending.pop()
    if (files.has(path)) continue
    files.add(path)
    pending.push(...filesRunBy(path, read(path) ?? ''))
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
      'scripts/scan-images.sh': 'source scripts/lib/trivy.sh\ninstall_trivy\nexec node scripts/scan-images.mjs "$@"\n',
    }

    assert.deepEqual(
      scanOwnershipProblems({
        codeowners: '/scripts/scan-images.sh @owner\n/scripts/scan-images.mjs @owner\n',
        ci: fixtureCi(imageScanJob),
        read: (path) => scripts[path],
        acceptedLists: [],
      }),
      ['ci.yml has no dependency-scan job', 'scripts/lib/trivy.sh runs in a scan but CODEOWNERS does not list it'],
    )
  })

  test('a file reached through a sourced script or a relative import is reported when CODEOWNERS lacks it', () => {
    const scripts = {
      'scripts/scan-images.sh': 'source scripts/lib/trivy.sh\nexec node scripts/scan-images.mjs "$@"\n',
      'scripts/lib/trivy.sh': 'source scripts/lib/retry.sh\n',
      'scripts/scan-images.mjs':
        "import { spawnSync } from 'node:child_process'\nimport { findImages } from './lib/images.mjs'\n",
      'scripts/lib/images.mjs': "import { readdirSync } from 'node:fs'\n",
    }
    const owned = ['/scripts/scan-images.sh', '/scripts/lib/trivy.sh', '/scripts/scan-images.mjs']

    assert.deepEqual(
      scanOwnershipProblems({
        codeowners: owned.map((path) => `${path} @owner`).join('\n'),
        ci: fixtureCi(imageScanJob),
        read: (path) => scripts[path],
        acceptedLists: [],
      }),
      [
        'ci.yml has no dependency-scan job',
        'scripts/lib/images.mjs runs in a scan but CODEOWNERS does not list it',
        'scripts/lib/retry.sh runs in a scan but CODEOWNERS does not list it',
      ],
    )
  })

  test('a script run from a run block, through bash or with node, and a file sourced with a dot are followed', () => {
    const scripts = {
      'scripts/scan.sh': '. scripts/lib/retry.sh\n',
    }
    const dependencyScanJob = [
      '  dependency-scan:',
      '    runs-on: ubuntu-24.04',
      '    steps:',
      '      - name: Dependency scan',
      '        run: |',
      '          # scripts/old-scan.sh is not run',
      '          bash scripts/scan.sh',
    ]
    const imageScanByNode = [
      '  image-scan:',
      '    runs-on: ubuntu-24.04',
      '    steps:',
      '      - run: node scripts/scan-images.mjs',
    ]

    assert.deepEqual(
      scanOwnershipProblems({
        codeowners: '/scripts/scan.sh @owner\n',
        ci: fixtureCi([...dependencyScanJob, ...imageScanByNode]),
        read: (path) => scripts[path],
        acceptedLists: [],
      }),
      [
        'scripts/lib/retry.sh runs in a scan but CODEOWNERS does not list it',
        'scripts/scan-images.mjs runs in a scan but CODEOWNERS does not list it',
      ],
    )
  })

  test('a script that a scan script runs with bash or sh is reported when CODEOWNERS lacks it', () => {
    const scripts = {
      'scripts/scan-images.sh': 'source scripts/lib/trivy.sh\n',
      'scripts/lib/trivy.sh': 'bash scripts/x.sh\nsh scripts/y.sh\n',
    }

    assert.deepEqual(
      scanOwnershipProblems({
        codeowners: '/scripts/scan-images.sh @owner\n/scripts/lib/trivy.sh @owner\n',
        ci: fixtureCi(imageScanJob),
        read: (path) => scripts[path],
        acceptedLists: [],
      }),
      [
        'ci.yml has no dependency-scan job',
        'scripts/x.sh runs in a scan but CODEOWNERS does not list it',
        'scripts/y.sh runs in a scan but CODEOWNERS does not list it',
      ],
    )
  })

  test('a script that a scan script runs by its path is reported, and one named in a comment is not', () => {
    const scripts = {
      'scripts/scan-images.sh': 'source scripts/lib/trivy.sh\n',
      'scripts/lib/trivy.sh': [
        '# scripts/old.sh is not run',
        'if [ -n "$X" ]; then',
        '  scripts/x.sh --flag',
        'fi',
        './scripts/y.sh',
        'cat scripts/data.sh',
        '',
      ].join('\n'),
    }

    assert.deepEqual(
      scanOwnershipProblems({
        codeowners: '/scripts/scan-images.sh @owner\n/scripts/lib/trivy.sh @owner\n',
        ci: fixtureCi(imageScanJob),
        read: (path) => scripts[path],
        acceptedLists: [],
      }),
      [
        'ci.yml has no dependency-scan job',
        'scripts/x.sh runs in a scan but CODEOWNERS does not list it',
        'scripts/y.sh runs in a scan but CODEOWNERS does not list it',
      ],
    )
  })

  test('a script that a scan .mjs file runs through child_process is reported when CODEOWNERS lacks it', () => {
    const scripts = {
      'scripts/scan-images.sh': 'node scripts/scan-images.mjs\n',
      'scripts/scan-images.mjs': [
        "import { spawnSync } from 'node:child_process'",
        "// spawnSync('bash', ['scripts/old.sh'])",
        "spawnSync('bash', ['scripts/x.sh'])",
        '',
      ].join('\n'),
    }

    assert.deepEqual(
      scanOwnershipProblems({
        codeowners: '/scripts/scan-images.sh @owner\n/scripts/scan-images.mjs @owner\n',
        ci: fixtureCi(imageScanJob),
        read: (path) => scripts[path],
        acceptedLists: [],
      }),
      ['ci.yml has no dependency-scan job', 'scripts/x.sh runs in a scan but CODEOWNERS does not list it'],
    )
  })

  test('a scan job that runs no script under scripts/ is reported', () => {
    assert.deepEqual(
      scanOwnershipProblems({
        codeowners: '/scripts/scan-images.sh @owner\n',
        ci: fixtureCi([
          ...imageScanJob,
          '  dependency-scan:',
          '    runs-on: ubuntu-24.04',
          '    steps:',
          '      - run: trivy fs .',
        ]),
        read: () => '',
        acceptedLists: [],
      }),
      ["ci.yml's dependency-scan job runs no script under scripts/"],
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
