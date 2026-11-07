import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { afterEach, beforeEach } from 'node:test'
import { relativePath } from './helpers.mjs'

export function useTempRoot() {
  let root
  beforeEach(() => {
    root = mkdtempSync(path.join(tmpdir(), 'check-docs-'))
  })
  afterEach(() => rmSync(root, { recursive: true, force: true }))

  const write = (file, text) => {
    mkdirSync(path.dirname(path.join(root, file)), { recursive: true })
    writeFileSync(path.join(root, file), text)
  }

  const messages = (rule, options) => {
    const found = []
    rule(root, (file, line, message) => found.push(`${relativePath(root, file)}:${line}: ${message}`), options)
    return found
  }

  return { root: () => root, write, messages }
}
