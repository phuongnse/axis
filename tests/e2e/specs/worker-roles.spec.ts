import { readFileSync } from "node:fs";
import { expect, test } from "@playwright/test";

test("the worker process holds the runtime role and no schema credentials", async () => {
  const pidFile = process.env.AXIS_E2E_WORKER_PID_FILE;
  if (!pidFile) {
    throw new Error(
      "AXIS_E2E_WORKER_PID_FILE is not set. Run the E2E tests with scripts/e2e.sh.",
    );
  }

  // Another spec can restart the worker at any time, so read the pid again until its environment is readable.
  let variables: string[] = [];
  await expect(async () => {
    const pid = readFileSync(pidFile, "utf8").trim();
    variables = readFileSync(`/proc/${pid}/environ`, "utf8")
      .split("\0")
      .filter(Boolean);
    expect(variables.length).toBeGreaterThan(0);
  }).toPass({ timeout: 30_000 });

  const names = variables.map((variable) => variable.split("=")[0]);
  expect(names.filter((name) => /schema/i.test(name))).toEqual([]);
  expect(
    variables.filter((variable) => variable.includes("axis_schema")),
  ).toEqual([]);
  expect(
    variables.find((variable) =>
      variable.startsWith("Tenants__default__ConnectionString="),
    ),
  ).toContain("Username=axis_runtime;");
});
