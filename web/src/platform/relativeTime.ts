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
