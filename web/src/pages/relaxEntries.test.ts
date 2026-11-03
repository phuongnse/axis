import { describe, expect, it } from 'vitest'
import {
  hasRelaxEntry,
  moduleIndex,
  relaxedManifest,
  relaxEntries,
  relaxPatch,
  relaxReasonValid,
  undoRelaxPatch,
  unrelaxedManifest,
} from './relaxEntries'

const point = 'Checklist/transitions/check'
const bare = { modules: [{ id: 'other' }, { id: 'checklists' }] }
const one = { modules: [{ id: 'checklists', relax: [{ point, reason: 'Small team' }] }] }
const two = {
  modules: [
    {
      id: 'checklists',
      relax: [
        { point: 'Checklist/transitions/close', reason: 'Old' },
        { point, reason: 'New' },
      ],
    },
  ],
}

describe('relaxEntries', () => {
  it('finds a module declared directly and returns -1 for any other', () => {
    expect(moduleIndex(bare, 'checklists')).toBe(1)
    expect(moduleIndex(bare, 'missing')).toBe(-1)
    expect(moduleIndex({}, 'checklists')).toBe(-1)
  })

  it('lists the entries of a module', () => {
    expect(relaxEntries(two, 'checklists')).toEqual([
      { point: 'Checklist/transitions/close', reason: 'Old' },
      { point, reason: 'New' },
    ])
    expect(relaxEntries(bare, 'checklists')).toEqual([])
    expect(relaxEntries(bare, 'missing')).toEqual([])
  })

  it('tells whether any module has an entry, and not for an empty list', () => {
    expect(hasRelaxEntry(one)).toBe(true)
    expect(hasRelaxEntry(bare)).toBe(false)
    expect(hasRelaxEntry({ modules: [{ id: 'checklists', relax: [] }] })).toBe(false)
    expect(hasRelaxEntry({})).toBe(false)
  })

  it('accepts a reason of 1 to 500 characters after trimming', () => {
    expect(relaxReasonValid('')).toBe(false)
    expect(relaxReasonValid('   ')).toBe(false)
    expect(relaxReasonValid('a')).toBe(true)
    expect(relaxReasonValid('a'.repeat(500))).toBe(true)
    expect(relaxReasonValid('a'.repeat(501))).toBe(false)
    expect(relaxReasonValid(` ${'a'.repeat(500)} `)).toBe(true)
  })

  it('adds the whole list when there is none, with the trimmed reason', () => {
    expect(relaxPatch(bare, 'checklists', point, ' Small team ')).toEqual([
      { op: 'add', path: '/modules/1/relax', value: [{ point, reason: 'Small team' }] },
    ])
  })

  it('adds one item at the end of an existing list, empty or not', () => {
    expect(relaxPatch(one, 'checklists', 'X/y/z', 'Why')).toEqual([
      { op: 'add', path: '/modules/0/relax/1', value: { point: 'X/y/z', reason: 'Why' } },
    ])
    expect(relaxPatch({ modules: [{ id: 'checklists', relax: [] }] }, 'checklists', point, 'Why')).toEqual([
      { op: 'add', path: '/modules/0/relax/0', value: { point, reason: 'Why' } },
    ])
  })

  it('removes one item, or the whole list when it is the only entry', () => {
    expect(undoRelaxPatch(two, 'checklists', point)).toEqual([{ op: 'remove', path: '/modules/0/relax/1' }])
    expect(undoRelaxPatch(one, 'checklists', point)).toEqual([{ op: 'remove', path: '/modules/0/relax' }])
    expect(undoRelaxPatch(one, 'checklists', 'X/y/z')).toEqual([])
  })

  it('gives the file as the patches leave it', () => {
    expect(relaxedManifest(bare, 'checklists', point, ' Small team ')).toEqual({
      modules: [{ id: 'other' }, { id: 'checklists', relax: [{ point, reason: 'Small team' }] }],
    })
    expect(unrelaxedManifest(one, 'checklists', point)).toEqual({ modules: [{ id: 'checklists' }] })
    expect(unrelaxedManifest(two, 'checklists', point)).toEqual({
      modules: [{ id: 'checklists', relax: [{ point: 'Checklist/transitions/close', reason: 'Old' }] }],
    })
  })
})
