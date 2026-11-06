/** The longest subdomain, as the rule of a tenant id has it. */
const maxLength = 40

/**
 * Suggests a subdomain from a business name: accents and `đ` dropped, lower-case letters and digits kept, every other run
 * of characters one hyphen, and no hyphen at either end.
 */
export function suggestSubdomain(name: string): string {
  return name
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .replace(/[đĐ]/g, 'd')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, maxLength)
    .replace(/-+$/, '')
}
