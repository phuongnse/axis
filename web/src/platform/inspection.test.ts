import { describe, expect, it } from 'vitest'
import { inspectTarget, processTitle } from './inspection'

describe('inspectTarget', () => {
  it('reads the process and the instance of an instance detail path', () => {
    expect(inspectTarget('/api/apps/StepApp/processes/Submit/instances/0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01')).toEqual({
      process: 'Submit',
      instanceId: '0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01',
    })
  })

  it('decodes the segments', () => {
    expect(inspectTarget('/api/apps/StepApp/processes/Big%20Order/instances/abc')).toEqual({
      process: 'Big Order',
      instanceId: 'abc',
    })
  })

  it('returns null for a path that is not an instance detail path', () => {
    expect(inspectTarget('/api/apps/StepApp/processes/Submit/instances')).toBeNull()
    expect(inspectTarget('/api/apps/StepApp/entities/Request/records/abc')).toBeNull()
  })
})

describe('processTitle', () => {
  const t = (key: string) => (key === 'process.onboarding.title' ? 'Vendor onboarding' : key)

  it("names a process by its title in the reader's language, for an instance, its parent and a sub-process alike", () => {
    expect(processTitle(t, { process: 'VendorOnboarding', processTitleKey: 'process.onboarding.title' })).toBe(
      'Vendor onboarding',
    )
  })

  it('names a process by its name when its release gives no title', () => {
    expect(processTitle(t, { process: 'VendorOnboarding', processTitleKey: null })).toBe('VendorOnboarding')
    expect(processTitle(t, { process: 'VendorOnboarding' })).toBe('VendorOnboarding')
  })
})
