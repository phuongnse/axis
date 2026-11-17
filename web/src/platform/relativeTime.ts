import { createContext, useContext, useEffect, useState } from 'react'

/** The units a time ago is counted in, each with the seconds it holds, largest first. */
const units: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 60 * 60],
  ['month', 30 * 24 * 60 * 60],
  ['week', 7 * 24 * 60 * 60],
  ['day', 24 * 60 * 60],
  ['hour', 60 * 60],
  ['minute', 60],
]

/**
 * Proposed: how long ago `at` was, in the locale's words, in the largest whole unit it reaches, such as "5 minutes ago",
 * "yesterday" or "last month". Under a minute, and for a time ahead of `now`, it is "now". See the review on #3263.
 */
export function formatTimeAgo(at: string | Date, locale: string, now: Date = new Date()): string {
  const format = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' })
  const seconds = Math.max(0, (now.getTime() - new Date(at).getTime()) / 1000)
  for (const [unit, size] of units) {
    if (seconds >= size) {
      return format.format(-Math.floor(seconds / size), unit)
    }
  }
  return format.format(0, 'second')
}

/** How long an event stays told as a time ago before its date and time tell it: a week, as Slack and Jira do. */
const agoFor = 7 * 24 * 60 * 60 * 1000

/**
 * Proposed, see the review on #3650: whether an event is recent enough to be told as how long ago it was. After a
 * week, its date and time in the system's own format tell it, as every other date and time does.
 */
export function isRecent(at: string | Date, now: Date = new Date()): boolean {
  return now.getTime() - new Date(at).getTime() < agoFor
}

/**
 * Proposed, see the review on #3650: the time now, drawn again once a minute, so a time ago that a page shows moves on
 * from "now" to "1 minute ago" while the page stays open, with no request.
 */
export function useMinuteClock(): Date {
  const fixed = useContext(FixedClock)
  const [now, setNow] = useState(() => fixed ?? new Date())
  useEffect(() => {
    if (fixed !== null) {
      return
    }
    const timer = window.setInterval(() => setNow(new Date()), 60_000)
    return () => window.clearInterval(timer)
  }, [fixed])
  return fixed ?? now
}

/**
 * Proposed, see the review on #3650: a time that stands for now, which the design system's previews give, so the
 * durations, times ago and due dates of their fixtures read as they would on the day the fixtures were made. The
 * running application gives none and reads the browser's clock.
 */
export const FixedClock = createContext<Date | null>(null)

/**
 * Proposed, see the review on #3650: how far ahead `at` is, in the locale's words, such as "in 27 days", for a due time
 * beside its date and time; `null` once it has passed.
 */
export function formatTimeUntil(at: string | Date, locale: string, now: Date = new Date()): string | null {
  const seconds = (new Date(at).getTime() - now.getTime()) / 1000
  if (seconds <= 0) {
    return null
  }
  const format = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' })
  for (const [unit, size] of units) {
    if (seconds >= size) {
      return format.format(Math.floor(seconds / size), unit)
    }
  }
  return format.format(1, 'minute')
}
