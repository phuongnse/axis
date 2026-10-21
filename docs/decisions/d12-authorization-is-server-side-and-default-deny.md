# D12. Authorization is server-side and default-deny

Status: **Agreed**

Policies grant access to resources, records, fields and actions.

- **No matching grant:** access is denied.
- **Policy evaluation fails:** access is denied.
- **Client-side conditions:** these only shape the UI.
- **Enforcement:** the server enforces every read, write, task action, export
  and inspection.
