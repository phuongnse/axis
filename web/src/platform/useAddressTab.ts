import { useAddressParameter } from './useAddressParameter'

/**
 * The tab of a page or a widget, kept in the address as `?<parameter>=<key>`, see docs/reference/container-widgets.md#tabs.
 * The first key is the default and is left out of the address. A value that is not one of the keys shows the first tab
 * and leaves the address as it is. Each choice adds a history entry, so the back button returns to the previous tab.
 * Returns the selected key and a function that selects another. It keeps the key through `useAddressParameter`.
 */
export function useAddressTab(parameter: string, keys: readonly string[]): [string, (key: string) => void] {
  const { value, write } = useAddressParameter(parameter)
  const selected = value !== null && keys.includes(value) ? value : keys[0]
  return [selected, (key: string) => write(key === keys[0] ? null : key)]
}
