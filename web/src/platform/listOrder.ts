/** Compares two names with the page language's collation, so Đ follows D in Vietnamese. */
export function nameCompare(locale: string): (left: string, right: string) => number {
  return new Intl.Collator(locale).compare
}

/** A new array of `items` sorted by `name`, grouped first by `rank` (lower first) when given. */
export function sortByName<T>(
  items: readonly T[],
  name: (item: T) => string,
  locale: string,
  rank?: (item: T) => number,
): T[] {
  const compare = nameCompare(locale)
  return items.toSorted((left, right) =>
    rank ? rank(left) - rank(right) || compare(name(left), name(right)) : compare(name(left), name(right)),
  )
}
