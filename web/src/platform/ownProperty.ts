/** Sets a property on the object itself, even for a key that would otherwise reach its prototype. */
export function setOwn<T>(target: Record<string, T>, key: string, value: NoInfer<T>): void {
  Object.defineProperty(target, key, { value, writable: true, enumerable: true, configurable: true })
}
