// The probe package for the Node.js runner, as a plain ES module. Its operations mirror the C# test package, see
// docs/reference/extension-packages.md#sample-and-test-packages.

/** An error that the runner answers as `retryable`, because of its name. */
class RetryableError extends Error {
  constructor(message) {
    super(message);
    this.name = "RetryableError";
  }
}

const attempts = new Map();

/** Adds the integers `a` and `b`, which are strings in their protocol form. */
export function sum(input) {
  return { total: String(BigInt(input.a) + BigInt(input.b)) };
}

/** Throws a RangeError whose message holds the input `a`. */
export function fail(input) {
  throw new RangeError(`The input ${input.a} is not valid.`);
}

/** Throws a RetryableError for the first `failures` attempts of each operation id, then returns the attempts. */
export function flaky(input, context) {
  const count = (attempts.get(context.operationId) ?? 0) + 1;
  attempts.set(context.operationId, count);
  if (BigInt(count) <= BigInt(input.failures)) {
    throw new RetryableError(`Attempt ${count} of ${input.failures} failures is not through yet.`);
  }

  return { attempts: String(count) };
}

/**
 * Uses the capabilities of its context in the way `mode` says and returns the `status` it got. `declared` posts to
 * `probe` through the declared `TestUrl` with the declared `TestToken` and the operation id as the idempotency key.
 * `otherHost` sends a request to a host that is not declared. `undeclaredSecret` reads a secret that is not declared.
 * `swallow` does the same and catches the error.
 */
export async function probe(input, context) {
  switch (input.mode) {
    case "declared": {
      const response = await context.fetch("TestUrl", "probe", {
        method: "POST",
        headers: {
          Authorization: `Bearer ${context.getSecret("TestToken")}`,
          "Idempotency-Key": context.operationId,
        },
        body: "{}",
      });
      return { status: String(response.status) };
    }
    case "otherHost": {
      const response = await context.fetch("TestUrl", "https://other.example.test/probe");
      return { status: String(response.status) };
    }
    case "undeclaredSecret":
      context.getSecret("OtherToken");
      return { status: "0" };
    case "swallow":
      try {
        context.getSecret("OtherToken");
      } catch {
        // The platform still fails the call.
      }

      return { status: "0" };
    default:
      throw new TypeError("Unknown mode.");
  }
}

/** Returns an integer as a JavaScript number, which does not fit its field. */
export function badOutput() {
  return { total: 3 };
}

/** Writes to the console, which the runner sends to standard error, and returns a boolean. */
export function log() {
  console.log("axis-console-marker-4242424242");
  return { done: true };
}
