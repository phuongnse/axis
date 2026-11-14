# Text writes in the authoring API

`PUT .../texts/{key}` of the [authoring API](authoring.md) writes one text key in every locale at once, so a key is never in one locale
only. The body is `{ texts: { <locale>: <text> } }`.

- **Every locale.** The body must hold exactly the locales of the folder's `text` resources, matched
  ignoring letter case. With duplicate locales (`AXC0026`), the first file in path order counts. A
  missing locale, an extra locale, a value that is not a string, or another top-level key is `400`. A
  value may be any string, as the text schema allows. The body is `400` too when a text resource was
  created or deleted between this check and the lock.
- **The key.** `{key}` is matched ordinally and cannot contain `/`, as in the
  [text key references](authoring.md#references) route. A new key is added at the end of `texts`, and an existing
  key keeps its position.
- **The lock.** The write takes the folder [write lock](authoring.md#versions). Under it, the server reads each
  text file and writes every temporary file, then renames each one over its target. It changes only
  that key and leaves the other keys as they are. A text resource with no `texts` object gets one. A
  `texts` that is not an object is `409`, and nothing is written.
- **No `If-Match`.** A text write takes no version, and the last write to a key wins, because one
  version per file does not fit a write that spans files. Only that key can be lost.
- **Not atomic across files.** Each rename is atomic, but all of them together are not. A crash between
  two renames can leave the key in some locales only, and the next compile reports it as `AXC0027`.
- **Stale versions.** A text resource that Studio has open has a new version after the write, so its
  next save would be `409`. The answer therefore lists each text resource's new `version`, as
  `{ resources: [{ id, locale, version }] }`.
- **Answer.** `200` with the new versions and the folder's [diagnostics](authoring.md#diagnostics), or `503` when
  the lock cannot be taken, and then nothing is written.
