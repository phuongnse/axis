import { useEffect, useState } from 'react'

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

/** How long an event stays told as a time ago before its date tells it: a week, as Slack and Jira do. */
const agoFor = 7 * 24 * 60 * 60 * 1000

/**
 * Proposed, see the review on #3650: when an event of a record in time happened, as the reader best tells events
 * apart: how long ago, from `formatTimeAgo`, within a week, and its date after that, "4 Mar" in the same year and
 * "4 Mar 2025" in another, in the locale's words, so a list of old events still reads in order.
 */
export function formatEventTime(at: string | Date, locale: string, now: Date = new Date()): string {
  const when = new Date(at)
  if (now.getTime() - when.getTime() < agoFor) {
    return formatTimeAgo(when, locale, now)
  }
  const sameYear = when.getFullYear() === now.getFullYear()
  return new Intl.DateTimeFormat(locale, {
    day: 'numeric',
    month: 'short',
    ...(sameYear ? {} : { year: 'numeric' }),
  }).format(when)
}

/**
 * Proposed, see the review on #3650: the time now, drawn again once a minute, so a time ago that a page shows moves on
 * from "now" to "1 minute ago" while the page stays open, with no request.
 */
export function useMinuteClock(): Date {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 60_000)
    return () => window.clearInterval(timer)
  }, [])
  return now
}
