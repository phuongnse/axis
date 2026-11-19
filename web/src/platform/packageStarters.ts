import starters from './packageStarters.json'

/** The files of a new extension package in one language, by path, with the placeholders still in them. */
type PackageStarter = Record<string, string>

const byLanguage: Record<string, PackageStarter> = starters

/** The languages that have a package starter, sorted ordinally. They are the languages a new package may use. */
export const packageLanguages: string[] = Object.keys(byLanguage).toSorted()

/** The label of a package id: hyphens become spaces and the first letter is a capital, so `purchasing-tools` is `Purchasing tools`. */
export function packageLabel(id: string): string {
  const words = id.split('-').join(' ')
  return words.charAt(0).toUpperCase() + words.slice(1)
}

/** The PascalCase name of a package id, so `purchasing-tools` is `PurchasingTools`. A C# package uses it as its namespace. */
export function packageName(id: string): string {
  return id
    .split('-')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join('')
}

/**
 * The files of a new package `id` in `language`, by path. Each path and text has `$id`, `$label` and `$name` replaced.
 * It throws for a language without a starter, as `createStarter` does for a kind.
 */
export function packageStarter(language: string, id: string): Record<string, string> {
  if (!Object.hasOwn(byLanguage, language)) {
    throw new Error(`There is no package starter for the language '${language}'.`)
  }
  const fill = (text: string) =>
    text.split('$id').join(id).split('$label').join(packageLabel(id)).split('$name').join(packageName(id))
  return Object.fromEntries(Object.entries(byLanguage[language]).map(([path, text]) => [fill(path), fill(text)]))
}
