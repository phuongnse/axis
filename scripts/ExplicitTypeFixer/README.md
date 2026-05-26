# ExplicitTypeFixer

One-off Roslyn tool used when introducing comprehensive `.editorconfig` rules to replace `var` with explicit types solution-wide.

```bash
dotnet run --project scripts/ExplicitTypeFixer -- /path/to/Axis.sln
dotnet format Axis.sln
```

Re-run only when new `var` locals slip in before CI catches them via `dotnet format --verify-no-changes`.
