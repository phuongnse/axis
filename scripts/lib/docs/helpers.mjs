import { execFileSync } from 'node:child_process'
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'

export const cellSeparator = /(?<!\\)\|/
export const testClassPattern = /`([A-Z]\w*Tests)`/g
export const issueLinkPattern = /\]\(https:\/\/github\.com\/[\w.-]+\/[\w.-]+\/issues\/\d+\)/
const classPattern = /\bclass\s+(\w+)/g
const ignoredFolders = new Set(['bin', 'obj', 'node_modules'])

export const relativePath = (root, file) => path.relative(root, file).split(path.sep).join('/')

export function markdownFiles(directory) {
  const files = []
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name)
    if (entry.isDirectory()) files.push(...markdownFiles(fullPath))
    else if (entry.isFile() && entry.name.endsWith('.md')) files.push(fullPath)
  }
  return files
}

function sourceFiles(directory) {
  if (!existsSync(directory)) return []
  const files = []
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name)
    if (entry.isDirectory()) {
      if (!ignoredFolders.has(entry.name)) files.push(...sourceFiles(fullPath))
    } else if (entry.isFile() && entry.name.endsWith('.cs')) files.push(fullPath)
  }
  return files
}

const splitLines = (text) => text.replace(/\n$/, '').split('\n')
const countLines = (text) => splitLines(text).length
export const lines = (file) => splitLines(readFileSync(file, 'utf8'))
export const countWords = (text) => (text.match(/\S+/g) ?? []).filter((word) => /[\p{L}\p{N}]/u.test(word)).length

/** Returns a function that gives the names of the classes declared under tests/, controlplane/tests/ and samples/, read once. */
export function testClassLookup(root) {
  let classes
  return () => {
    if (!classes) {
      classes = new Set()
      const folders = ['tests', path.join('controlplane', 'tests'), 'samples']
      for (const file of folders.flatMap((folder) => sourceFiles(path.join(root, folder)))) {
        for (const match of readFileSync(file, 'utf8').matchAll(classPattern)) classes.add(match[1])
      }
    }
    return classes
  }
}

/** Runs git in root and returns its output, or undefined when git fails. */
function git(root, args) {
  try {
    return execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] })
  } catch {
    return undefined
  }
}

/** Returns a counter of a doc's lines at the base commit, or undefined when no base commit can be read. */
export function gitBaseLineCount(root, env = process.env) {
  const mergeBase = git(root, ['merge-base', 'HEAD', 'origin/main'])?.trim()
  const sha = [env.NEXKIT_BASE_SHA, mergeBase].find(
    (ref) => ref && git(root, ['rev-parse', '--verify', '--quiet', `${ref}^{commit}`]),
  )
  if (!sha) return undefined
  return (rel) => {
    const text = git(root, ['show', `${sha}:./${rel}`])
    return text === undefined ? 0 : countLines(text)
  }
}
