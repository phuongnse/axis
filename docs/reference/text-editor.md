# Text editor

A `text` resource opens as one table of keys, with a column for each locale, and the JSON view stays as
the second view of the same draft.

- **Columns.** There is a row for each key, sorted by key, and a column for each locale in the order of
  the folder's `text` resources. Only the open resource's column is editable. The other locales show
  beside it as read-only text, read from their files, and the author opens another locale's `text`
  resource to edit it.
- **Missing keys.** A key that the open locale lacks shows an empty cell marked missing, and typing in
  it adds the key to this locale. Another locale that lacks the key shows a missing mark in place of
  the text.
- **Search.** The search is the table's own, through its Search box. It keeps the rows whose key, or text
  in any locale, contains the search, ignoring letter case. It starts on Enter or on the search button,
  goes back to page 1, and lives in the address as `search`, so a reload or the Back button keeps it. See
  [tables](tables.md#tables).
- **Filter.** The table's Filter works on the key and on each locale's text. A missing text counts as empty,
  so "is empty" on a locale finds the keys it lacks. It narrows the rows already loaded and lives in the
  address as `filter`. See [tables](tables.md#tables).
- **Paging.** The table pages 20 rows at a time, as every table with a view does. See
  [tables](tables.md#tables).
- **Cells.** A cell edit goes into the draft, and Save sends one JSON Patch for the open resource only.
  Clearing a cell saves an empty string and keeps the key, because every locale holds the same keys
  (`AXC0027`).
- **Add key.** The row below the table takes a key and a text for each locale. The boxes share the row's width
  equally, and Add key sits under them. It writes the key to
  every locale at once through the [text write route](authoring-text-writes.md). The key cannot be
  empty, cannot contain `/`, and cannot already be in the table. The editor takes the open file's new
  version from the answer and adds the key to its loaded copy and its draft, so unsaved cell edits stay
  and the next Save does not meet a stale version. The diagnostics and the names load again. A key it refuses
  marks the **New key** box in error, with the reason under Add key, and focuses it. The mark goes as soon as the
  key is right, see [fields](actions-and-controls.md#fields).
