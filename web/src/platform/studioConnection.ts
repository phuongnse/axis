import { createContext, useContext } from 'react'

/** Studio's server connection. `generation` counts the reconnects, so loads that depend on it run again. */
export const StudioConnectionContext = createContext({
  generation: 0,
  reconnecting: false,
  lost: () => {},
})

export function useStudioConnection() {
  return useContext(StudioConnectionContext)
}
