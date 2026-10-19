# Com.H.Data.Common

## Releasing

1. Bump `<Version>` in `src/Com.H.Data.Common.csproj` (four parts: `10.1.0.N`) and replace
   `<PackageReleaseNotes>` with this version's changes only.
2. Keep `src/nuget.md` in step with `README.md`. It is the README with two differences: a line
   linking to the GitHub page sits just before `## Supported runtimes`, and the `## Installation`
   section is left out (NuGet is where the reader already is). Regenerate it after every README
   change.
3. Update the direct package references to the latest stable versions, then audit with
   `dotnet list package --vulnerable --include-transitive`.
4. Run both test projects (below). If you touched one of the classes listed in
   `tools/shared-classes.json`, run `pwsh tools/check-shared-drift.ps1` (see `tools/README.md`).
5. Leave the changes uncommitted until the owner has reviewed the diff. Commit messages carry no
   AI attribution.
6. Commit and push, then pack (below). The owner uploads the package to nuget.org.

## Source stepping: fixed, keep it that way

`src/Com.H.Data.Common.csproj` sets `<EmbedAllSources>true</EmbedAllSources>` (applied
2026-10-09, first shipped in 10.1.0.11), so the source of every file is inside the package's
PDB. Do not remove it.

Without it, Source Link alone does not guarantee downstream developers can step into this
library's source. If the drive-letter casing used to invoke the build differs from what git
reports for the repo root (`c:\` vs `C:\`), the compiler silently skips the source-path rewrite
that Source Link depends on, and stepping then fails for some target frameworks. There is no
warning or error — the package looks fine.

**[SOURCELINK-STEPPING.md](SOURCELINK-STEPPING.md)** has the full explanation, a verified
reproduction, and a small program (slcheck) that checks a built assembly and tells you whether
each target framework will actually step.

Sibling repos `Com.H` and `Com.H.Net.Ssh` carry the same fix.

## Packing

Every Release build sets `ContinuousIntegrationBuild`, and a pack of any other configuration
stops with an error before it builds, so Visual Studio's right-click Pack and this command
produce the same package:

```powershell
dotnet pack .\src\Com.H.Data.Common.csproj -c Release
```

Commit and push before packing, so the commit the PDB points at exists on GitHub. Then check
the package: run slcheck on `src\bin\Release` (every row should read `embedded` equal to `docs`,
and `OK`), and check that the `commit` in the `.nuspec`'s `<repository>` element equals
`git rev-parse HEAD`.

## Tests

```powershell
dotnet test Tests            # .NET 10
dotnet test Tests.Net481     # .NET Framework 4.8.1; compiles Tests\AdoNetExtTests.cs and FillExtensionTests.cs
```

SQLite tests run everywhere. The SQL Server tests (`Tests/SqlServerTests.cs`) need LocalDB
(`(localdb)\MSSQLLocalDB`), and the ODBC tests need the Microsoft Access driver
(`Tests/sample.accdb`) and ODBC Driver 17 for SQL Server. A test whose database or driver is
missing passes without running anything, so compare the test count with the last run's, not
only the colour. At 10.1.0.13: Tests 299, Tests.Net481 282, with LocalDB and both drivers
present.

DBToRestAPI (github.com/H7O/DBToRestAPI) is this package's main consumer. Before releasing a
change to how queries are built or read, pack it with a `-localN` version into a local feed and
run that project's tests against it. Delete the `-localN` packages from
`~/.nuget/packages/com.h.data.common` afterwards.

## Changing AdoNetExt.cs

- A query the previous release ran correctly must still run the same way. Placeholder handling
  has two paths, named parameters (`@name`, `:name`) and positional ones (ODBC and OleDb `?`),
  and they differ on purpose in places. The remarks on `ResolveMarkers` say where.
- After the first result set, later result sets are skipped with `NextResultAsync` alone. Reading
  their rows took quadratic time on SqlClient for large values.
- Providers differ when a reader is closed before its batch is read to the end. SqlClient,
  System.Data.SqlClient, Microsoft.Data.Sqlite and OleDb discard the batch's later errors.
  Npgsql and System.Data.Odbc throw them from `Close`, and an ODBC reader can stay open after
  such a `Close`. The comments in `CreateAsyncEnumerableFromReader` explain how each case is
  handled.
- When you fix a behaviour, add a test that fails without the fix. Break the code on purpose and
  check the test catches it.

## Line endings

Most working copies are CRLF (`.gitattributes` has `* text=auto`; git stores LF), but some are
LF: `src/JoinExtensions.cs`, `src/Polyfills.cs`, `Tests.Net481/*` and `tools/*`. Most files under
`src/` also start with a BOM (`AdoNetExt.cs` and the csproj among them; `DbQueryResult.cs`,
`JoinExtensions.cs` and `Polyfills.cs` don't). Keep each file's line endings and BOM as they are,
and check with `git ls-files --eol` and `head -c3`.

Scripts that rewrite a file must write back the same bytes they read: in Python, use
`read_bytes`/`write_bytes` (or `newline=''`). On Windows `write_text` writes CRLF, so it turns an
LF file into CRLF. Git Bash's `sed -i` turns CRLF into LF, and its `grep` hides CRs.
