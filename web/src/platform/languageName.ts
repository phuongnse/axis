/**
 * Proposed: a language's name in that language, such as "Tiếng Việt" for `vi`, with a capital first letter, as
 * language menus name them so a person finds their own. The tag itself when the browser knows no name for it.
 */
export function nativeLanguageName(tag: string): string {
  try {
    const name = new Intl.DisplayNames([tag], { type: 'language' }).of(tag)
    if (name && name !== tag) {
      return name.charAt(0).toLocaleUpperCase(tag) + name.slice(1)
    }
  } catch {
    // An invalid tag has no name.
  }
  return tag
}
