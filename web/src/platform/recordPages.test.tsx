import { renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it } from 'vitest'
import { isRecordPage, RecordPagesContext, useRecordOpener } from './recordPages'
import { setHostSite } from './siteLinks'

const id = '0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b'
const recordPages = {
  Request: '/Sample/request',
  Category: '/sample/records/category',
  'Hr.Employee': '/sample/records/hr/employee',
}

function opener(pages: Record<string, string> | null = recordPages) {
  const wrapper = ({ children }: { children: ReactNode }) => (
    <RecordPagesContext value={pages ? { sitePath: 'sample', recordPages: pages } : null}>
      {children}
    </RecordPagesContext>
  )
  return renderHook(() => useRecordOpener(), { wrapper }).result.current
}

describe('useRecordOpener', () => {
  afterEach(() => setHostSite(null))

  it('appends the id to the page an entity names, ignoring the letter case of the site', () => {
    expect(opener()('Request', id)).toBe(`/sample/request/${id}`)
  })

  it('gives the default record page address of an entity and of a module entity', () => {
    expect(opener()('Category', id)).toBe(`/sample/records/category/${id}`)
    expect(opener()('Hr.Employee', id)).toBe(`/sample/records/hr/employee/${id}`)
  })

  it('gives null for an entity with no entry, a name of the object prototype and outside a site', () => {
    expect(opener()('Invoice', id)).toBeNull()
    expect(opener()('toString', id)).toBeNull()
    expect(opener(null)('Request', id)).toBeNull()
  })

  it('drops the site prefix on the site own host', () => {
    setHostSite('sample')

    expect(opener()('Category', id)).toBe(`/records/category/${id}`)
  })
})

describe('isRecordPage', () => {
  it('is true when the site names the page for the entity, ignoring letter case', () => {
    expect(isRecordPage('sample', recordPages, 'Request', 'request')).toBe(true)
    expect(isRecordPage('Sample', recordPages, 'Request', 'REQUEST')).toBe(true)
  })

  it('is false for another page, an entity with no entry, a name of the object prototype and no pages', () => {
    expect(isRecordPage('sample', recordPages, 'Request', 'requests')).toBe(false)
    expect(isRecordPage('sample', recordPages, 'Invoice', 'request')).toBe(false)
    expect(isRecordPage('sample', recordPages, 'toString', 'request')).toBe(false)
    expect(isRecordPage('sample', null, 'Request', 'request')).toBe(false)
    expect(isRecordPage('sample', undefined, 'Request', 'request')).toBe(false)
  })
})
