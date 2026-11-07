// The rule and its reason: docs/development.md#unit-tests
const gibibyte = 2 ** 30

export function vitestWorkers(freeBytes: number): number {
  return Math.min(6, Math.max(2, Math.floor(freeBytes / gibibyte) - 1))
}
