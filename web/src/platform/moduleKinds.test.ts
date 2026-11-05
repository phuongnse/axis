/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

import { moduleKinds } from './moduleKinds'

const compilerFolder = '../../../src/Axis.Configuration/'

/** A compiler source file, read from the repository as the dependency tests read theirs. */
function source(name: string): string {
  return readFileSync(join(fileURLToPath(new URL(compilerFolder, import.meta.url)), name), 'utf8')
}

/** The kinds in `ModuleChecker._allowedKinds`, read from the C# source. */
function allowedKinds(): string[] {
  const checker = source('Compilation/ModuleChecker.cs')
  const start = checker.indexOf('_allowedKinds = new(')
  const block = checker.slice(start, checker.indexOf('};', start))
  const names = [...block.matchAll(/ResourceKinds\.(\w+)/g)].map((match) => match[1])
  const values = new Map(
    [...source('Resources/ResourceKinds.cs').matchAll(/public const string (\w+) = "([^"]+)";/g)].map((match) => [
      match[1],
      match[2],
    ]),
  )
  return names.map((name) => {
    const value = values.get(name)
    if (value === undefined) {
      throw new Error(`ResourceKinds.${name} was not found.`)
    }
    return value
  })
}

describe('moduleKinds', () => {
  it('equals the kinds the compiler allows in a module', () => {
    const allowed = allowedKinds()

    expect(allowed).not.toHaveLength(0)
    expect([...moduleKinds].toSorted()).toEqual(allowed.toSorted())
  })
})
