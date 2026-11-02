import { Select } from 'antd'
import { useCallback, useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import type { FieldMetadata } from './site'
import { findUser, searchUsers, type UserOption } from './tasks'

interface UserIdInputProps {
  /** A text field marked `userId`, with the address of its user search route in `usersPath`. */
  field: FieldMetadata
  /** The stored user id, or `null` when none is picked. */
  value: string | null
  onChange: (value: string | null) => void
  id?: string
  ariaLabel?: string
  /** Marks the picker required: the live state of the form, or `field.required` when left out. */
  ariaRequired?: boolean
  ariaDescribedBy?: string
  style?: CSSProperties
}

/**
 * The picker of a text field marked `userId`. It lists people by display name when it opens and
 * searches again as the user types. Picking a person stores that person's user id, and clearing
 * stores `null`, unless the field is required. A stored id shows the person's name, read through the
 * route's `id` parameter, or the id itself when no profile has it. A failed search leaves the list
 * empty and a failed name lookup shows the id, so the form keeps working.
 */
export function UserIdInput({
  field,
  value,
  onChange,
  id,
  ariaLabel,
  ariaRequired,
  ariaDescribedBy,
  style,
}: UserIdInputProps) {
  const path = field.usersPath!
  const required = ariaRequired ?? field.required
  const [users, setUsers] = useState<UserOption[]>([])
  // The names of the people seen so far, so a later search does not drop the name of the stored one.
  const [names, setNames] = useState<Record<string, string>>({})
  const searching = useRef<AbortController>(null)
  const named = value !== null && names[value] !== undefined

  const search = useCallback(
    (text: string) => {
      searching.current?.abort()
      const controller = new AbortController()
      searching.current = controller
      searchUsers(path, text, controller.signal)
        .then((found) => {
          setUsers(found)
          setNames((known) => ({ ...known, ...Object.fromEntries(found.map((user) => [user.id, user.displayName])) }))
        })
        .catch((failure: unknown) => {
          if (!controller.signal.aborted) {
            console.warn(`Searching the users of '${path}' failed`, failure)
            setUsers([])
          }
        })
    },
    [path],
  )

  useEffect(() => {
    if (value === null || named) {
      return
    }
    const controller = new AbortController()
    findUser(path, value, controller.signal)
      .then((user) => setNames((known) => ({ ...known, [value]: user?.displayName ?? value })))
      .catch((failure: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Reading the user '${value}' of '${path}' failed`, failure)
        }
      })
    return () => controller.abort()
  }, [path, value, named])

  useEffect(() => () => searching.current?.abort(), [])

  const pick = (picked: { value: string; label: ReactNode } | undefined) => {
    if (picked === undefined) {
      onChange(null)
      return
    }
    setNames((known) => ({ ...known, [picked.value]: String(picked.label) }))
    onChange(picked.value)
  }

  return (
    <Select
      id={id}
      aria-label={ariaLabel}
      aria-required={required || undefined}
      aria-describedby={ariaDescribedBy}
      style={style}
      showSearch={{ filterOption: false, onSearch: search }}
      labelInValue
      virtual={false}
      allowClear={!required}
      value={value === null ? undefined : { value, label: names[value] ?? value }}
      options={users.map((user) => ({ value: user.id, label: user.displayName }))}
      onOpenChange={(open) => open && search('')}
      onChange={pick}
    />
  )
}
