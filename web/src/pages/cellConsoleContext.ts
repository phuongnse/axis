import { useOutletContext } from 'react-router'

/** What the cell console's layout route gives its pages through the outlet. */
export interface CellConsoleContext {
  /** Reports that a console call was refused. The console then signs in again on `unauthorized` and shows the forbidden page on `forbidden`. */
  onRefused: (refusal: 'unauthorized' | 'forbidden') => void
}

/** The context of the cell console's layout route. A page loads its data through the helpers of `platform/cell.ts` and reports a refusal here. */
export function useCellConsole(): CellConsoleContext {
  return useOutletContext<CellConsoleContext>()
}
