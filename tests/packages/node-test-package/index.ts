import { greeting } from "axis-test-dependency";

export function greet(name: string): { greeting: string } {
  return { greeting: greeting(name) };
}
