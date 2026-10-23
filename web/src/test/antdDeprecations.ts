const recorded: string[] = []

/** Keeps the text of each Ant Design deprecation warning among the logged console errors. */
export function recordAntdDeprecation(args: unknown[]): void {
  const text = args.map(String).join(' ')
  if (/\[antd: [^\]]+\].*deprecated/.test(text)) recorded.push(text)
}

/** Returns the recorded deprecation warnings and clears the list. */
export function takeAntdDeprecations(): string[] {
  return recorded.splice(0)
}
