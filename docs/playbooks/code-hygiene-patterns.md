# Code Hygiene Patterns

> **Navigation**: [← docs/README.md](../README.md) · [← patterns index](./patterns-index.md) · [← AGENTS.md](../../AGENTS.md)

Pre-commit hygiene checks and regex rules for maintainable repository policy scripts.

---

## Code hygiene checklist

Run this checklist before every commit. Items are ordered from most to least likely to be missed.

### 1. No inline fully-qualified type names

AGENTS.md rule: **always use `using` directives — never write the namespace inline**.

**Wrong:**
```csharp
string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
    System.Text.Encoding.UTF8.GetBytes(token)));

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { … });

opts.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
```

**Right:**
```csharp
using System.Security.Cryptography;
using System.Text;
// …
string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
```

**Detection command** — run this before committing; output must be empty:
```bash
grep -rn --include="*.cs" \
  -E "(System\.(Collections\.Generic|Linq|Threading|IO|Text|Net|Security)\.|Microsoft\.(AspNetCore|Extensions|EntityFrameworkCore)\.[A-Z])" \
  src/ tests/ \
  | grep -v "obj/" \
  | grep -v "\.cs:.*using " \
  | grep -v "^\s*//"
```

Common namespaces that agents forget to add as `using` directives:

| Inline form seen in code | `using` to add |
|---|---|
| `System.Text.Encoding.UTF8` | `using System.Text;` |
| `System.Text.RegularExpressions.Regex` | `using System.Text.RegularExpressions;` |
| `System.Text.Json.JsonNamingPolicy` | `using System.Text.Json;` |
| `System.Text.Json.Serialization.JsonStringEnumConverter` | `using System.Text.Json.Serialization;` |
| `System.Security.Cryptography.SHA256` / `RandomNumberGenerator` | `using System.Security.Cryptography;` |
| `System.Net.HttpStatusCode` | `using System.Net;` |
| `Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions` | `using Microsoft.AspNetCore.Diagnostics.HealthChecks;` |

### 2. No restructuring to avoid a `using` directive

When replacing `var` with an explicit type, if that type requires a new `using` directive — add the directive. Never restructure or inline code just to avoid importing a type. That is a workaround, not a fix.

**Wrong** — chains `.Id` onto the query to avoid declaring an explicit type:
```csharp
Guid itemId = ctx.Items.First(i => i.Name == "target" && i.WorkspaceId == WorkspaceId).Id;
// … then uses itemId directly — the full object is discarded
```

**Right** — declares the explicit type and adds the `using` directive:
```csharp
using My.Module.Domain.Aggregates;
// …
MyEntity item = ctx.Items.First(i => i.Name == "target" && i.WorkspaceId == WorkspaceId);
// … then uses item.Id, item.Name, etc. as needed
```

The restructured version hides what type is being worked with, discards future flexibility (e.g. if a second property is later needed), and violates the intent of "no `var`" — which is to make types explicit, not to obscure them by different means.

### 3. Verify `!` is actually needed before adding it

Before using the null-forgiving operator `!` to suppress a nullable annotation, check whether the assignment compiles without it. If the existing codebase already uses the same API without `!`, adding it is a workaround — not a fix.

**Wrong** — `!` added without verifying it is necessary:
```csharp
MyType result = (await SomeApiAsync())!;
```

**Right** — if the return type is non-nullable (e.g. a value type or a `Task<T>` where T is a struct), `!` is unnecessary:
```csharp
MyType result = await SomeApiAsync();
```

The rule: grep the codebase for existing call sites of the same API before reaching for `!`. If they compile without it, yours should too. Never add `!` just because the compiler warns — resolve the underlying nullability issue instead.

### 4. No scaffold placeholder files

Visual Studio scaffolds `Class1.cs` when creating a new project. These files must be deleted immediately — never committed. A `Class1.cs` anywhere in `src/` or `tests/` is always wrong.

**Detection command:**
```bash
find src/ tests/ -name "Class1.cs" -not -path "*/obj/*"
```

### 5. User input flowing into external identifiers

Any string derived from user input that becomes an external identifier — filename, ZIP entry name, URL slug, S3 key, Redis key — needs two checks:

1. **Character safety**: use an allowlist, never a denylist or single-char replace. For filenames and ZIP entries the safe set is `[a-z0-9\-_]` after lowercasing and replacing spaces with `-`.
2. **Uniqueness**: when the identifier is used in a set (ZIP archive, directory), handle the collision case — two workflows with the same name produce the same slug and will clash.

```csharp
// ❌ handles spaces only — "/" ":" "?" survive and corrupt portable identifiers
string slug = name.ToLowerInvariant().Replace(' ', '-');

// ✅ allowlist — only safe chars survive; handle collisions at the call site
private static string ToSafeSlug(string name)
{
    string slug = name.ToLowerInvariant().Replace(' ', '-');
    slug = new string(slug.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_').ToArray());
    return slug.Trim('-', '_');
}
```

Collision handling in a bulk operation:
```csharp
Dictionary<string, int> seen = new(StringComparer.Ordinal);
foreach (WorkflowExportDto dto in workflows)
{
    string baseSlug = ToSafeSlug(dto.Name);
    seen.TryGetValue(baseSlug, out int count);
    string entrySlug = count == 0 ? baseSlug : $"{baseSlug}_{count + 1}";
    seen[baseSlug] = count + 1;
    // use entrySlug as the ZIP entry name
}
```

### 4. No direct commits to `main`

Every change — including one-line fixes — goes through a branch + PR. Steps:

```bash
git checkout -b chore/my-fix   # branch off current HEAD
# make changes
git add <files>
git commit -m "chore: ..."
git push -u origin chore/my-fix
gh pr create …
```

---

## Drift script regex constraints

`python scripts/axis.py check doc-drift` owns the forbidden-pattern ratchets. The implementation uses Python `re`, not shell/awk, so new checks should use Python regex syntax and live in [`scripts/axis.py`](../../scripts/axis.py) or a focused Python helper wired from that command.

### Keep rules readable

Prefer narrow, readable Python regexes over clever one-liners. If a rule can be
implemented by parsing structured data (JSON, Markdown sections, paths), use
Python data structures instead of regex-only matching.

Use raw strings for regex patterns:

```python
re.compile(r"DateTime[.]Now")
re.compile(r"GetAwaiter[(][)][.]GetResult[(][)]")
```

Bracket punctuation (`[.]`, `[(]`) remains acceptable because it is visually
unambiguous and works across regex dialects, but Python also supports escaped
punctuation such as `\.`. Pick one style per pattern and keep the comment clear.

### Validation before commit

Always test new patterns with the literal target string before pushing:

```bash
python - <<'PY'
import re
assert re.search(r"DateTime[.]Now", "DateTime.Now;")
PY
```

Run `python scripts/axis.py check doc-drift` before opening the PR.

---
