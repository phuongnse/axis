import { greeting } from "axis-test-dependency";

export function greet(input: { name: string }): { greeting: string } {
  return { greeting: greeting(input.name) };
}
