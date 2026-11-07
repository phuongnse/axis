import { describe, expect, it } from 'vitest'
import { vitestWorkers } from './vitestWorkers.ts'

const gibibyte = 2 ** 30
const manyCores = 64

describe('vitestWorkers', () => {
  it('starts at least two workers when little memory is free', () => {
    expect(vitestWorkers(0, manyCores)).toBe(2)
    expect(vitestWorkers(2.9 * gibibyte, manyCores)).toBe(2)
    expect(vitestWorkers(3 * gibibyte, manyCores)).toBe(2)
  })

  it('starts one worker for each whole GiB above 1 GiB', () => {
    expect(vitestWorkers(4 * gibibyte, manyCores)).toBe(3)
    expect(vitestWorkers(5.5 * gibibyte, manyCores)).toBe(4)
  })

  it('starts at most six workers', () => {
    expect(vitestWorkers(7 * gibibyte, manyCores)).toBe(6)
    expect(vitestWorkers(64 * gibibyte, manyCores)).toBe(6)
  })

  it('starts no more workers than there are cores', () => {
    expect(vitestWorkers(64 * gibibyte, 4)).toBe(4)
    expect(vitestWorkers(64 * gibibyte, 1)).toBe(2)
  })
})
