# Prepare shared libraries

Updates the pre-existing `services/shared/` projects to match the new
project name and ensures all NuGet package versions are current.

## 3a — Detect the template namespace prefix

Find the first `<RootNamespace>` value in any `.csproj` under
`services/shared/`:

```bash
grep -rh '<RootNamespace>' services/shared/ | head -1
```

Extract the prefix (the segment before the first `.`). For example,
`ThePlot.ServiceDefaults` → prefix is `ThePlot`. This is the string
to replace with `«ProjectName»` everywhere.

## 3b — Rename namespaces in `.csproj` files

For each `.csproj` under `services/shared/`, replace:

- `<RootNamespace>«OldPrefix».X</RootNamespace>` →
  `<RootNamespace>«ProjectName».X</RootNamespace>`
- `<InternalsVisibleTo Include="«OldPrefix».X" />` →
  `<InternalsVisibleTo Include="«ProjectName».X" />`

Also rename `.csproj` files themselves if they are prefixed with the
old name. For example, if a project is named `ThePlot.ServiceDefaults.csproj`,
rename it to `«ProjectName».ServiceDefaults.csproj`. Update any
`<ProjectReference>` paths in sibling projects that pointed to the old
filename.

## 3c — Rename namespaces in `.cs` files

For each `.cs` file under `services/shared/`, replace the old prefix
with `«ProjectName»` in:

- `namespace «OldPrefix».X;` → `namespace «ProjectName».X;`
- `using «OldPrefix».X;` → `using «ProjectName».X;`

Use a simple text replacement — the prefix is unique enough that
false positives are unlikely.

## 3d — Update package versions

For each `<PackageReference>` in every `.csproj` under
`services/shared/`, query for the latest stable version:

```bash
dotnet package search «PackageName» --exact-match --take 1 --format json
```

Parse the version from the JSON output and update the `.csproj` in
place. Skip packages that are already at the latest version.

If `dotnet package search` is not available (older SDK), use:

```bash
dotnet package search «PackageName» --exact-match --take 1
```

and parse the tabular output instead.

## 3e — Build shared projects

Confirm everything compiles:

```bash
dotnet build services/shared/ServiceDefaults
dotnet build services/shared/Database.Abstractions
dotnet build services/shared/Database
dotnet build services/shared/Exceptions
```

Fix any compilation errors before proceeding. Common issues:

- A renamed namespace not caught in a `.cs` file — grep for the old
  prefix and fix any remaining occurrences.
- A package version bump introduced a breaking API change — check the
  package release notes and adapt.

## Notes

- `FrameworkReference` entries (e.g. `Microsoft.AspNetCore.App`) do not
  have versions — skip them.
- The `services/shared/` directory is part of the repo template and is
  checked into git. It is not scaffolded by a generator.
- After this step, the shared libraries are ready for services to
  reference via `<ProjectReference>` (handled by `add-dotnet-service`
  Step 4).
