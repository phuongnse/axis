import { useEffect, useRef, useState } from 'react'

const doneFor = 2000

/**
 * Copies a text to the clipboard, see docs/reference/frontend.md#shown-values. `copied` is true for two seconds after a
 * copy that worked. `copy` resolves to whether the copy worked, so a caller says so in its own words.
 */
export function useCopy(): { copied: boolean; copy: (text: string) => Promise<boolean> } {
  const [copied, setCopied] = useState(false)
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  useEffect(() => () => clearTimeout(timer.current), [])

  async function copy(text: string): Promise<boolean> {
    try {
      await navigator.clipboard.writeText(text)
    } catch (error) {
      console.warn('Copying failed', error)
      return false
    }
    setCopied(true)
    clearTimeout(timer.current)
    timer.current = setTimeout(() => setCopied(false), doneFor)
    return true
  }

  return { copied, copy }
}
