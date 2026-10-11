/** Sends the browser to another address. It is a module of its own, so tests can mock it. */
export function assignLocation(url: string): void {
  window.location.assign(url)
}
