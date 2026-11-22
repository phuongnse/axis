import { useCallback } from 'react'
import { useSearchParams } from 'react-router'

/** What {@link useAddressParameter} gives: the parameter's value and how to link to, write and clear it. */
export interface AddressParameter {
  /** The parameter's value in the address, or `null` without it. */
  value: string | null
  /** The search of the address with the parameter set to `value`, keeping the other parameters, for a link to it. */
  search: (value: string) => string
  /** Sets the parameter, or removes it for `null`. A write that changes nothing adds no history entry. */
  write: (value: string | null, options?: { replace?: boolean }) => void
}

/**
 * Proposed, #4902: one parameter of the address that opens a view in place, such as a tab, a field's drawer as `field`
 * or a list item as `item`, see docs/reference/studio.md#route-and-address. A link to it is a real link built by
 * `search`, and the page follows `value`, so a shared link, a reload and Back open the same view. A write adds a
 * history entry unless `replace` is set, and keeps the scroll where it is.
 */
export function useAddressParameter(name: string): AddressParameter {
  const [searchParams, setSearchParams] = useSearchParams()
  const value = searchParams.get(name)
  const search = useCallback(
    (next: string) => {
      const params = new URLSearchParams(searchParams)
      params.set(name, next)
      return `?${params.toString()}`
    },
    [name, searchParams],
  )
  const write = useCallback(
    (next: string | null, options?: { replace?: boolean }) => {
      const params = new URLSearchParams(searchParams)
      if (next === null) {
        params.delete(name)
      } else {
        params.set(name, next)
      }
      if (params.toString() !== searchParams.toString()) {
        setSearchParams(params, { replace: options?.replace ?? false, preventScrollReset: true })
      }
    },
    [name, searchParams, setSearchParams],
  )
  return { value, search, write }
}
