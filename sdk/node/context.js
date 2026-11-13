"use strict";

// The operation context that the Node.js runner gives an operation, with the rules of PackageOperationContext in
// the C# runner, see docs/reference/extension-packages.md#contracts. It holds only the bindings of the run. Asking
// for another binding, or sending a request to another origin, throws, and the first such binding is kept in
// `violation` whatever the operation does with the error. A message names the binding, never a value or the
// refused host. Binding names are matched ignoring letter case.

function lookup(bindings, name) {
  const key = Object.keys(bindings).find((candidate) => candidate.toLowerCase() === String(name).toLowerCase());
  return key === undefined ? undefined : bindings[key];
}

/** The context of one run of the operation. `run` is the `run` message. */
function createContext(run) {
  let violation = null;

  function refuse(binding, message) {
    violation ??= binding;
    return new Error(message);
  }

  const origins = Object.values(run.bindings.urls).map((url) => new URL(url).origin);

  return {
    /** The binding of the first violation, or null when there is none. */
    get violation() {
      return violation;
    },

    operationId: run.operationId,

    getSecret(secretBinding) {
      const secret = lookup(run.bindings.secrets, secretBinding);
      if (secret === undefined) {
        throw refuse(secretBinding, `The operation asked for the secret binding '${secretBinding}', which it does not declare.`);
      }

      return secret;
    },

    /**
     * Sends a request to `path`, which is resolved against the base of the url binding. Only the origin of a declared
     * url binding is allowed, and a redirect is never followed.
     */
    async fetch(urlBinding, path, init) {
      const base = lookup(run.bindings.urls, urlBinding);
      if (base === undefined) {
        throw refuse(urlBinding, `The operation asked for the url binding '${urlBinding}', which it does not declare.`);
      }

      // A trailing slash makes a relative path extend the base path instead of replacing its last segment.
      const url = new URL(path, base.endsWith("/") ? base : `${base}/`);
      if (!origins.includes(url.origin)) {
        throw refuse(urlBinding, `The operation sent a request through the url binding '${urlBinding}' to a host that none of its url bindings declares.`);
      }

      return globalThis.fetch(url, { ...init, redirect: "manual" });
    },
  };
}

module.exports = { createContext };
