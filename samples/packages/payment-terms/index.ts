// A sample extension package that wraps dayjs, see docs/reference/extension-packages.md#sample-and-test-packages.
import dayjs from "dayjs";
import utc from "dayjs/plugin/utc";

dayjs.extend(utc);

export function dueDate(input: { from: string }): { dueDate: string } {
  return { dueDate: dayjs.utc(input.from).add(30, "day").endOf("month").format("YYYY-MM-DD") };
}
