"use strict";

// The Node.js codec between the values of an operation's input and output and the JSON values of the runner
// protocol, with the rules of RunnerValues in the C# runner, see
// docs/reference/extension-packages.md#the-runner-protocol. Values keep their protocol form: text, integer,
// decimal, date and date-time are strings, boolean is a boolean and a missing value is null. The runner refuses
// here whatever the worker would refuse when it decodes the output.

const INTEGER = /^[+-]?\d+$/;
const DECIMAL = /^[+-]?(\d+(\.\d*)?|\.\d+)$/;
const DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
const OFFSET = /(Z|z|[+-]\d{2}:\d{2})$/;
const INT64_MIN = -(2n ** 63n);
const INT64_MAX = 2n ** 63n - 1n;

/** The error of a value that does not fit its field. It names the field, never the value, which can be a secret. */
function misfit(field) {
  return new TypeError(`The value of the field '${field.name}' is not a valid '${field.type}' value.`);
}

function isDate(text) {
  const match = DATE.exec(text);
  if (match === null) {
    return false;
  }

  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(Date.UTC(year, month - 1, day));
  date.setUTCFullYear(year);
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
}

function fits(field, value) {
  if (field.type === "boolean") {
    return typeof value === "boolean";
  }

  if (typeof value !== "string") {
    return false;
  }

  switch (field.type) {
    case "text":
      return true;
    case "integer": {
      if (!INTEGER.test(value)) {
        return false;
      }

      const integer = BigInt(value);
      return integer >= INT64_MIN && integer <= INT64_MAX;
    }
    case "decimal":
      return DECIMAL.test(value);
    case "date":
      return isDate(value);
    case "date-time":
      return OFFSET.test(value) && !Number.isNaN(Date.parse(value));
    default:
      return false;
  }
}

function check(field, value) {
  if (!fits(field, value)) {
    throw misfit(field);
  }

  return value;
}

/** Reads every declared field of `input`, with null for a field that `input` lacks. */
function decode(fields, input) {
  const result = {};
  for (const field of fields) {
    const value = Object.hasOwn(input, field.name) ? input[field.name] : null;
    result[field.name] = value === null || value === undefined ? null : check(field, value);
  }

  return result;
}

/** Writes the declared fields that `output` holds, and no other. A value that does not fit its field throws a TypeError. */
function encode(fields, output) {
  const result = {};
  for (const field of fields) {
    if (Object.hasOwn(output, field.name)) {
      const value = output[field.name];
      result[field.name] = value === null || value === undefined ? null : check(field, value);
    }
  }

  return result;
}

module.exports = { decode, encode };
