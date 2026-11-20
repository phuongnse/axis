import { useSearchParams } from 'react-router'

/**
 * The tab of a page or a widget, kept in the address as `?<parameter>=<key>`, see docs/reference/container-widgets.md#tabs.
 * The first key is the default and is left out of the address. A value that is not one of the keys shows the first tab
 * and leaves the address as it is. Each choice adds a history entry, so the back button returns to the previous tab.
 * Returns the selected key and a function that selects another.
 */
export function useAddressTab(parameter: string, keys: readonly string[]): [string, (key: string) => void] {
  const [searchParams, setSearchParams] = useSearchParams()
  const raw = searchParams.get(parameter)
  const selected = raw !== null && keys.includes(raw) ? raw : keys[0]

  function select(key: string) {
    const next = new URLSearchParams(searchParams)
    if (key === keys[0]) {
      next.delete(parameter)
    } else {
      next.set(parameter, key)
    }
    setSearchParams(next, { preventScrollReset: true })
  }

  return [selected, select]
}
