import { renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useReleaseTexts } from './useReleaseTexts'

const releaseId = '0192f4a0-1b2c-7d3e-8f40-5b6c7d8e9f02'

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('useReleaseTexts', () => {
  it('clears a failure when the same request is made again and succeeds', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    let failing = true
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        if (failing && url.includes('/texts/en?')) {
          return new Response('', { status: 500 })
        }
        const locale = url.includes('/texts/vi?') ? 'vi' : 'en'
        return new Response(JSON.stringify({ locale, texts: { key: locale } }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }),
    )

    const { result, rerender } = renderHook(({ locale }) => useReleaseTexts('e2e', [releaseId], locale, locale), {
      initialProps: { locale: 'en' },
    })
    await waitFor(() => expect(result.current.failed).toBe(true))

    rerender({ locale: 'vi' })
    await waitFor(() => expect(result.current.catalogs.get(releaseId)?.texts).toEqual({ key: 'vi' }))
    expect(result.current.failed).toBe(false)

    failing = false
    rerender({ locale: 'en' })
    await waitFor(() => expect(result.current.catalogs.get(releaseId)?.texts).toEqual({ key: 'en' }))
    expect(result.current.failed).toBe(false)
  })
})
