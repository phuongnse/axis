import { useSearchParams } from 'react-router'

/**
 * Proposed, see the review on #3650: the tab a page shows, kept in the address under `name`, so a link or a reload
 * opens the same tab. `keys` are the tabs' keys in order; the first is the default and leaves the address without
 * `name`, and a value that is no key selects the first and leaves the address as it is. The `tabs` widget and a page's
 * own tabs, such as a process instance's, keep their tab through it.
 */
export function useAddressTab(name: string, keys: readonly string[]): [string, (key: string) => void] {
  const [searchParams, setSearchParams] = useSearchParams()
  const raw = searchParams.get(name)
  const selected = raw !== null && keys.includes(raw) ? raw : keys[0]

  function select(key: string) {
    const next = new URLSearchParams(searchParams)
    if (key === keys[0]) {
      next.delete(name)
    } else {
      next.set(name, key)
    }
    setSearchParams(next, { preventScrollReset: true })
  }

  return [selected, select]
}
