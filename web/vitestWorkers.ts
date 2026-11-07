// The rule and its reason: docs/development.md#unit-tests
const gibibyte = 2 ** 30

export function vitestWorkers(freeBytes: number, cores: number): number {
  return Math.min(6, Math.max(2, Math.min(Math.floor(freeBytes / gibibyte) - 1, cores)))
}
