import { describe, expect, it } from 'vitest'
import { vitestWorkers } from './vitestWorkers.ts'

const gibibyte = 2 ** 30

describe('vitestWorkers', () => {
  it('starts at least two workers when little memory is free', () => {
    expect(vitestWorkers(0)).toBe(2)
    expect(vitestWorkers(2.9 * gibibyte)).toBe(2)
    expect(vitestWorkers(3 * gibibyte)).toBe(2)
  })

  it('starts one worker for each whole GiB above 1 GiB', () => {
    expect(vitestWorkers(4 * gibibyte)).toBe(3)
    expect(vitestWorkers(5.5 * gibibyte)).toBe(4)
  })

  it('starts at most six workers', () => {
    expect(vitestWorkers(7 * gibibyte)).toBe(6)
    expect(vitestWorkers(64 * gibibyte)).toBe(6)
  })
})
