# Source stepping can break silently in this repo

**Status: fixed on 2026-10-09**, for 10.1.0.11, by adding `EmbedAllSources` to
`src/Com.H.Data.Common.csproj` (see "The fix" below). The rest of this note explains why that
line is there, so nobody removes it. Nothing is wrong with the Source Link configuration — the
problem is that Source Link alone is not enough to guarantee stepping works, and the failure
leaves no trace in the build output.

Written 2026-08-31, after hitting this for real while packing `Com.H.Net.Ssh` 10.1.0.
`Com.H` (fixed 2026-10-04) carries the same fix and note, and `Com.H.Net.Ssh` (fixed 2026-08-31)
the same fix.

## The symptom

A downstream developer steps into a method of this library in the debugger and gets
decompiled code or "source not available", instead of the real source — but only for
**some** target frameworks. `net8.0` might fail while `netstandard2.0` works, or the
reverse. The package builds and packs with no warning and no error.

## What actually goes wrong

Source Link does not put your source in the package. It puts a *map* in the PDB:

```json
{"documents":{"/_/*":"https://raw.githubusercontent.com/H7O/Com.H.Data.Common/<commit>/*"}}
```

For that map to resolve, the compiler must first rewrite each source file path from its
local form to the `/_/` form the map covers. That rewrite is driven by `PathMap`, which is
built from the repository root as **git** reports it, and it is applied as a
**case-sensitive prefix match**.

So when the drive-letter casing MSBuild resolves for the project differs from the casing
git reports for the repo root, the prefix does not match, and the rewrite silently does
not happen. The PDB is then internally inconsistent: the map says `/_/*` while the
documents say `c:\code\H7O\...`. Nothing joins them up, so stepping fails.

Which target frameworks are hit varies between runs, because the inner builds of a
multi-targeted project run in parallel and do not all resolve the path the same way.

The trap is that a casual `dotnet build` from Explorer or Visual Studio usually gets the
casing right, so this looks fine right up until the one pack that ships broken.

## Reproducing it

Verified in this repo on 2026-08-31. Note the lowercase `c:` — that is the whole trick,
and it is how some tools (the Claude Code shell among them) spell the working directory:

```powershell
Remove-Item -Recurse -Force .\src\bin, .\src\obj -ErrorAction SilentlyContinue
dotnet build 'c:\code\H7O\Com.H.Data.Common\src\Com.H.Data.Common.csproj' `
    -c Release -p:ContinuousIntegrationBuild=true
```

Result: `net8.0`, `net9.0` and `net10.0` all ended up with 14 unmapped documents each
(`c:\code\H7O\Com.H.Data.Common\src\AdoNetExt.cs` and friends), so **3 of 4 targets would
not step**. `netstandard2.0` happened to survive. Building the same project with an
uppercase `C:` gave 4 of 4 correct, five runs in a row.

## The fix — one line

This sits in the main `<PropertyGroup>` of `src/Com.H.Data.Common.csproj`, next to
`<DebugType>embedded</DebugType>`:

```xml
<!-- Put the source itself in the PDB, not just a Source Link URL pointing at it, so that
     stepping does not depend on the compiler's source-path rewrite landing (a
     case-sensitive prefix match that parallel inner builds can silently miss) or on
     GitHub being reachable. -->
<EmbedAllSources>true</EmbedAllSources>
```

This sidesteps the whole mechanism: with the source inside the PDB there is no path to
rewrite and no URL to fetch. Stepping also then works offline and keeps working if the
repo is ever renamed, made private, or has its history rewritten.

Keep Source Link as well — it stays useful, and costs nothing.

**Cost:** package size. In `Com.H.Net.Ssh` (4 source files) it went 137 KB to 170 KB. In this
repo (17 documents per target), built from the same 10.1.0.11 source, the package is about
321 KB without it and 472 KB with it, before nuget.org adds its signature.

The alternatives are worse: always invoking with correctly-cased absolute paths, or always
passing `-m:1` to serialise the inner builds. Both work, and both fail the moment someone
forgets.

## Detecting it

Never assume it worked — the failure is invisible. Check the built assemblies directly.
Build this throwaway tool and point it at a `bin/Release` directory:

`slcheck.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

`Program.cs`

```csharp
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// usage: slcheck <binReleaseDir> <assemblyName>
var root = args[0];
var name = args[1];
var slGuid  = new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A"); // SourceLink
var embGuid = new Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE"); // EmbeddedSource
int broken = 0, checkedTfms = 0;

foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d))
{
    var dll = Path.Combine(dir, name + ".dll");
    if (!File.Exists(dll)) continue;
    var tfm = Path.GetFileName(dir);
    using var fs = File.OpenRead(dll);
    using var pe = new PEReader(fs);
    var entry = pe.ReadDebugDirectory()
                  .FirstOrDefault(e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
    if (entry.Type != DebugDirectoryEntryType.EmbeddedPortablePdb)
    { Console.WriteLine($"  {tfm,-16} NO embedded PDB"); continue; }
    using var prov = pe.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
    var md = prov.GetMetadataReader();

    bool hasMap = md.CustomDebugInformation
        .Any(h => md.GetGuid(md.GetCustomDebugInformation(h).Kind) == slGuid);
    int total = 0, embedded = 0, unmapped = 0;
    string firstBad = null;
    foreach (var dh in md.Documents)
    {
        var n = md.GetString(md.GetDocument(dh).Name);
        total++;
        bool isEmb = md.GetCustomDebugInformation(dh)
            .Any(h => md.GetGuid(md.GetCustomDebugInformation(h).Kind) == embGuid);
        if (isEmb) embedded++;
        // A document is only reachable if it was rewritten to /_/ (Source Link resolves it)
        // or carries its source inline (EmbedAllSources).
        if (!n.StartsWith("/_/") && !isEmb) { unmapped++; firstBad ??= n; }
    }
    checkedTfms++;
    if (unmapped != 0) broken++;
    var status = unmapped == 0 ? "OK" : $"BROKEN ({unmapped} unmapped, e.g. {firstBad})";
    Console.WriteLine($"  {tfm,-16} map={(hasMap ? "yes" : "NO ")}  docs={total,-3} embedded={embedded,-3} {status}");
}
Console.WriteLine(broken == 0
    ? $"  => all {checkedTfms} target(s) will step correctly"
    : $"  => {broken} of {checkedTfms} target(s) WILL NOT step");
```

Run it:

```powershell
dotnet run --project <path>\slcheck.csproj -c Release -- `
    'C:\code\H7O\Com.H.Data.Common\src\bin\Release' 'Com.H.Data.Common'
```

Healthy output, with the fix in place, looks like `docs=17 embedded=17 OK` on every row
(10.1.0.11 showed exactly that).
Without `EmbedAllSources`, healthy looks like `docs=17 embedded=3 OK` — the 3 being
generated files under `obj/` that `EmbedUntrackedSources` already embeds. Any `BROKEN`
row means that target framework ships unsteppable.

## Also worth remembering when packing

`ContinuousIntegrationBuild` makes the build deterministic and rewrites source paths to `/_/`.
The csproj turns it on for every Release build (as well as on CI), and a pack of any other
configuration stops with an error before it builds. So Visual Studio's right-click Pack and the
command line produce the same package, and no flag needs remembering:

```powershell
dotnet pack .\src\Com.H.Data.Common.csproj -c Release
```

Debug builds leave the flag off, so local debugging keeps opening the files on disk.

Commit and push **before** packing, so the commit the PDB points at actually exists on
GitHub. Verify afterwards that the stamped commit matches `git rev-parse HEAD`.
