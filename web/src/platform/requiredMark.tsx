import type { ReactNode } from 'react'
import { RequiredAsterisk } from './RequiredAsterisk'

/** Ant Design's `requiredMark`: the label, then a `*` in `dangerText` when the field is required. */
export function requiredMark(label: ReactNode, { required }: { required: boolean }): ReactNode {
  return required ? (
    <>
      {label}
      <RequiredAsterisk />
    </>
  ) : (
    label
  )
}
