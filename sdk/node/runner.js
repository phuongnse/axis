"use strict";

// The Node.js runner: runs the operations of one installed Node.js package version, one `run` at a time, as the C#
// runner does, see docs/reference/extension-packages.md#the-runner-protocol. It is started as
// `node runner.js runner <root> <id> <version>`. The protocol goes over the standard input and output. Package code
// that writes to the console writes to standard error, so it cannot corrupt the protocol stream. The exit code is 0
// when the input ends or a `stop` arrives, 1 when the version does not load, and 2 when a line is not a `run` or a
// `stop`. Each `run` gets exactly one answer. The context hands out only the bindings of the run, so an operation
// that asks for another binding gets a `failure` with that binding, even when it catches the error. Any other error
// is a `failure` with the name of the error, never its message. An error named `RetryableError` is `retryable`.

const fs = require("node:fs");
const path = require("node:path");
const readline = require("node:readline");
const { pathToFileURL } = require("node:url");
const { createContext } = require("./context.js");
const { decode, encode } = require("./values.js");

const STOPPED = 0;
const NOT_LOADED = 1;
const BAD_INPUT = 2;

// The protocol keeps the real standard output. Console output of package code goes to standard error.
const writeProtocol = process.stdout.write.bind(process.stdout);
process.stdout.write = process.stderr.write.bind(process.stderr);

function send(message) {
  return new Promise((resolve, reject) => writeProtocol(`${JSON.stringify(message)}\n`, (error) => (error ? reject(error) : resolve())));
}

/** The name of an error, which is never its message, because a message can hold input values or secrets. */
function nameOf(error) {
  const name = error?.name;
  return typeof name === "string" && name.length > 0 ? name : "Error";
}

function find(items, name) {
  return items.find((item) => item.name.toLowerCase() === String(name).toLowerCase());
}

/** The one answer to `run`. */
async function answer(manifest, adapter, run) {
  let context = null;
  let output = null;
  let error = null;
  let declared = null;
  try {
    declared = find(manifest.operations, run.operation);
    if (declared === undefined) {
      throw new TypeError("The operation is not declared by the package.");
    }

    const operation = adapter[declared.type] ?? adapter.default?.[declared.type];
    if (typeof operation !== "function") {
      throw new TypeError("The adapter does not export a function for the operation.");
    }

    const input = decode(declared.input ?? [], run.input);
    context = createContext(run);
    output = await operation(input, context);
  } catch (caught) {
    error = { caught };
  }

  // The operation may have swallowed the error, so a refused binding fails the attempt whatever it returned.
  if (context?.violation) {
    return { type: "failure", binding: context.violation };
  }

  if (error !== null) {
    const name = nameOf(error.caught);
    return name === "RetryableError" ? { type: "retryable", error: name } : { type: "failure", error: name };
  }

  try {
    if (output === null || typeof output !== "object") {
      throw new TypeError("The operation returned no output.");
    }

    return { type: "result", output: encode(declared.output ?? [], output) };
  } catch (caught) {
    return { type: "failure", error: nameOf(caught) };
  }
}

async function main(root, id, version) {
  let manifest;
  let adapter;
  try {
    const folder = path.join(root, id, version);
    manifest = JSON.parse(fs.readFileSync(path.join(folder, "axis-package.json"), "utf8"));
    if (manifest.language !== "node" || typeof manifest.adapter !== "string") {
      console.error(`Package '${id}' ${version} is not a node package.`);
      return NOT_LOADED;
    }

    const file = path.join(folder, manifest.adapter);
    if (!fs.existsSync(file)) {
      console.error(`Package '${id}' ${version}: the adapter '${manifest.adapter}' is missing.`);
      return NOT_LOADED;
    }

    adapter = await import(pathToFileURL(file).href);
  } catch (error) {
    console.error(`Package '${id}' ${version} does not load: ${nameOf(error)}.`);
    return NOT_LOADED;
  }

  await send({ type: "ready" });

  const lines = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
  for await (const line of lines) {
    let message;
    try {
      message = JSON.parse(line);
    } catch {
      return BAD_INPUT;
    }

    if (message?.type === "run") {
      await send(await answer(manifest, adapter, message));
    } else if (message?.type === "stop") {
      return STOPPED;
    } else {
      return BAD_INPUT;
    }
  }

  return STOPPED;
}

const [mode, root, id, version] = process.argv.slice(2);
if (mode !== "runner" || !root || !id || !version) {
  console.error("Usage: runner.js runner <root> <id> <version>");
  process.exit(BAD_INPUT);
}

main(root, id, version).then(
  (code) => process.exit(code),
  (error) => {
    console.error(`The runner failed: ${nameOf(error)}.`);
    process.exit(NOT_LOADED);
  },
);
