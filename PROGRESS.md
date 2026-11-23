# Progress

The work list of OpenCAD2D: the 2D CAD app that works like AutoCAD 2021, with AutoLISP, Python, plugins and Remote,
split out of the GeometryHelper repository (`D:\GitHub\GeometryHelper`) in November 2026.

- `[x]` done; `[ ]` to do; `(running)` an agent is on it now.
- The maintainer adds, removes or reorders items; the lead takes the next open item from the top, ticks it when it is
  merged, and writes here what it leaves behind.
- If work stops (a usage limit, a crash), the next session reads this file first and carries on from the first open
  item. Every step says how it is checked.

## S. The split from GeometryHelper (running)

### Decisions

| # | Decision | State |
|---|---|---|
| D1 | Two repositories: `GeometryHelper` (the core library, DXF and DWG included, the Tekla, AutoCAD and IFC bridges, the Viewer) and `opencad2d` (the CAD app and its libraries). | decided by the maintainer 2026-11-23 |
| D2 | opencad2d uses the core as a binary: `Lib\GeometryHelper.dll` with its `.xml` and `.pdb`, and `Lib\VERSION.txt` naming the GeometryHelper commit it was built from. No project reference across the repositories, no NuGet for now. `tools\update-lib.ps1` builds the core and copies it in. `Lib` is committed only when a batch of core work is done. | decided by the maintainer 2026-11-23 |
| D3 | The history of the CAD files comes along (`git filter-repo`), merged into opencad2d's first commit with `--allow-unrelated-histories`, so nothing on GitHub is force-pushed. | proposed |
| D4 | Names: display, namespaces, assemblies and the exe `OpenCAD2D`; lower case `opencad2d` where the convention asks for it (repository, domain, Python package, PyPI and NuGet ids). | under discussion |
| D5 | Class names stay (`CadDocument`, `CadCommandRegistry`, ...); only the namespace in front changes. | proposed |
| D6 | The Python API moves to the package `opencad2d` (`from opencad2d import command`); the core package `geometryhelper` stays in GeometryHelper. | proposed |
| D7 | The user's settings move from `%AppData%\GeometryHelper\Dxf` to `%AppData%\OpenCAD2D`, copied over on the first start. The `.dxf`/`.dwg` association becomes `OpenCAD2D.Drawing` and the old one is removed. | proposed |
| D8 | opencad2d has its own version, starting at 1.0.0 (Part A finished 2026-11-17). | proposed |
| D9 | The run copy moves from `D:\GeometryHelper-run` to `D:\OpenCAD2D-run`. | proposed |

### Names, old to new

| GeometryHelper | opencad2d |
|---|---|
| `src/GeometryHelper.Cad` | `src/OpenCAD2D` |
| `src/GeometryHelper.Cad.Wpf` | `src/OpenCAD2D.Wpf` |
| `src/GeometryHelper.Cad.Python` | `src/OpenCAD2D.Python` |
| `src/GeometryHelper.Cad.Remote` | `src/OpenCAD2D.Remote` |
| `src/GeometryHelper.Cad.App` (`GeometryHelper.Cad.App.exe`) | `src/OpenCAD2D.App` (`OpenCAD2D.exe`) |
| `tests/GeometryHelper.Cad.UnitTest`, `.Cad.Wpf.UnitTest`, `.Cad.Plugins.UnitTest`, `.Cad.Python.UnitTest`, `.Cad.Remote.UnitTest` | `tests/OpenCAD2D.UnitTest`, `.Wpf.UnitTest`, `.Plugins.UnitTest`, `.Python.UnitTest`, `.Remote.UnitTest` |
| `tests/GeometryHelper.Cad.SamplePlugin`, `.SampleUiPlugin`, `.Plugins.Net8Runner`, `.Remote.Sample`, `.Remote.TestServer` | `tests/OpenCAD2D.SamplePlugin`, ... |
| `examples/GeometryHelper.Cad.Plugins.DrawingInfo` | `examples/OpenCAD2D.Plugins.DrawingInfo` |
| `src/python/geometryhelper/cad` (`geometryhelper.cad`) | `src/python/opencad2d` (`opencad2d`) |
| `src/GeometryHelper/docs/presenter` (and the CAD half of `python.md`, `python-examples.md`) | `docs/` |
| namespace `GeometryHelper.Cad`, `.Cad.Wpf`, `.Cad.Python`, `.Cad.Remote`, `.Cad.App` | `OpenCAD2D`, `OpenCAD2D.Wpf`, `OpenCAD2D.Python`, `OpenCAD2D.Remote`, `OpenCAD2D.App` |
| window title "GeometryHelper CAD" | "OpenCAD2D" |

### Traps found while planning

- The core gives its internals to the CAD engine: `[assembly: InternalsVisibleTo("GeometryHelper.Cad")]` in
  `src/GeometryHelper/Properties/AssemblyInfo.cs` (MTEXT layout, `DxfTrueTypeFont`). It must name `OpenCAD2D` before the
  `Lib` copy is built, or the engine no longer compiles against it.
- The CAD projects give internals to their tests by name (`InternalsVisibleTo("GeometryHelper.Cad.UnitTest")` and the
  like, in AssemblyInfo files and csproj items): all renamed together.
- XAML pack URIs carry the assembly name: `pack://application:,,,/GeometryHelper.Cad.Wpf;component/Themes/Dark.xaml`.
  A missed one fails only at run time, so the app is opened as part of the check.
- Embedded resource names carry the old namespace: `GeometryHelper.Cad.App.app.ico` in `Program.cs`, and others.
- The Python package loads the DLLs by file name (`clr.AddReference(... name + ".dll")`).
- opencad2d's `.gitignore` (GitHub's C template) ignores `*.dll`: `Lib/` needs an exception, and the template is
  replaced by the .NET one of GeometryHelper.
- `tests/GeometryHelper.PyStubs.UnitTest` and `tools/GeometryHelper.PyStubs` in GeometryHelper make the stubs of the CAD
  libraries too; the CAD half moves.
- The CI of GeometryHelper (`.github/workflows/build.yml`) builds and tests the CAD projects; those jobs move.
- Strings with "GeometryHelper": the temp folder `GeometryHelper\PluginCopies`, the plot style folder, the crash log,
  the named pipe of Remote, the ProgId `GeometryHelper.Drawing`, `CadAppHost("GeometryHelper CAD", ...)`.
- `git filter-repo` is not installed yet (`pip install git-filter-repo`).

### Steps

- [x] **S1. Get ready.** Done: the split starts from GeometryHelper `77eadeb5`; git-filter-repo a40bce548d2c
  installed; TeklaHelper.DrawingManager references only GeometryHelper and TeklaConvert (its `.claude/scratch` copies
  aside), so the split does not touch it. All decisions D1-D9 decided 2026-11-23 (the maintainer took the
  recommendations).
  1. Record the GeometryHelper commit the split starts from: no CAD work merges into GeometryHelper after it.
  2. `pip install git-filter-repo`.
  3. Check that the private repository TeklaHelper.DrawingManager uses only the core, not `GeometryHelper.Cad*`.

  Check: the commit is written here; `git filter-repo --version` answers; the DrawingManager answer is written here.

- [x] **S2. Rename in the core.** Done in GeometryHelper `6bb8136e`: `InternalsVisibleTo("OpenCAD2D")` ADDED beside
  the old line, which goes in S10 (so GeometryHelper keeps building meanwhile). The core's PDB is embedded, so `Lib`
  holds the DLL and the XML only. In GeometryHelper: `InternalsVisibleTo("GeometryHelper.Cad")` becomes
  `InternalsVisibleTo("OpenCAD2D")`. Then build the core in Release.

  Check: the core builds with 0 warnings.

- [x] **S3. Bring the history over.** Done: filtered from a clone at `6bb8136e` (the old folder names `Dxf.Viewer`,
  `Dxf.Viewer.Wpf`, `Dxf.App` and their tests mapped onto the new folders too), 1 098 commits, merged into opencad2d's
  first commit (not yet committed further or pushed). File counts equal GeometryHelper's (639, 552, 545, 281, ...);
  `git log --follow src/OpenCAD2D/CadDocument.cs` reaches `f17ea2e3`. Python came over whole (`src/python`,
  `tests/python`, `tools/python`, PyStubs); the files inside the folders are renamed in S5.
  1. In a fresh clone of GeometryHelper (never the working repository), run `git filter-repo` keeping only the CAD
     paths, and rename them to the new layout with `--path-rename`.
  2. Merge the result into opencad2d with `--allow-unrelated-histories`.

  Check: `git log --follow` of a CAD file reaches its first commit; the tree holds only CAD files.

- [x] **S4. Lib and the references.** Done (not committed yet): `Lib\GeometryHelper.dll` + `.xml` from `6bb8136e`,
  `Lib\VERSION.txt`; every `ProjectReference` to the core became `<Reference Include="GeometryHelper"
  HintPath="$(GeometryHelperLib)GeometryHelper.dll" />` (`GeometryHelperLib` in `Directory.Build.props`); the three
  plugins that take the engine with `Private="false"` take the core the same way; the test files the engine's tests
  borrowed from the core's tests (`SpeedGuard.cs`, `DxfDimensionDrawings.cs`, `TrueTypeFile.cs`) copied to
  `tests/Shared`; `tools\update-lib.ps1` written; the core's `.gitignore` used (it does not ignore `Lib`). Build files
  copied from GeometryHelper: `.gitattributes`, `.editorconfig`, `.csharpierrc.json`, `.csharpierignore`,
  `dotnet-tools.json`, `Directory.Build.targets`, `.vscode/settings.json`; `Directory.Build.props` new (version 1.0.0);
  `OpenCAD2D.slnx` new. Working scripts of the split: `D:\GitHub\GeometryHelper\.claude\scratch\split\` (rename.sh,
  refs.pl, addusing.pl, fixloop.sh, folders.pl).
  1. Copy `GeometryHelper.dll`, `.xml` and `.pdb` into `Lib\`, and write `VERSION.txt`.
  2. Replace every `ProjectReference` to `GeometryHelper.csproj` with a `Reference` whose `HintPath` is under `Lib`.
  3. Write `tools\update-lib.ps1`.
  4. Add `!Lib/*.dll` to `.gitignore`.

  Check: the projects find the core only in `Lib` (no path to `D:\GitHub\GeometryHelper` in any csproj).

- [ ] (running) **S6-S8 progress (2026-11-23, not committed):**
  - S6: "GeometryHelper CAD" -> "OpenCAD2D" everywhere; `%AppData%\GeometryHelper\Drawing` and `\Dxf` -> one folder
    `%AppData%\OpenCAD2D` (GlobalSettings, CadCrashLog, CadPlotStyleFolder, Program.cs), copied once at the first
    start by the new `CadOldFolders.CopyOnce` (3 tests); `%LocalAppData%\OpenCAD2D\Remote`; temp folders `OpenCAD2D`;
    pipe `OpenCAD2D.<pid>`; ProgID `OpenCAD2D.Drawing` and `OldProgId` `GeometryHelper.Drawing` removed on
    register/unregister (1 new test). The exe stays `OpenCAD2D.App.exe` (AssemblyTitle OpenCAD2D): an exe named
    OpenCAD2D would ship an `OpenCAD2D.dll` beside the engine's `OpenCAD2D.dll`.
  - S7: package `src/python/opencad2d` (was `geometryhelper`), `opencad2d.runtimeconfig.json`, `OPENCAD2D_RUNTIME`,
    pyproject (name, description, URLs, `OpenCAD2D-stubs`), hatch_build reads `OpenCAD2DVersion`; `opencad2d.__getattr__`
    hands out the names of `opencad2d.cad`. The stub generator writes two packages, `GeometryHelper-stubs` and
    `OpenCAD2D-stubs` (`StubGenerator.Roots`). PyStubs tests: 3 expectations updated, one of them a failure that was
    already in GeometryHelper (`CadTrueTypeFonts.WarmUp` returns a `Task`, written as Any). PyStubs 73/73, Python
    242/242 on net8.0 and net48.
  - S8: CLAUDE.md (part one copied, part two new), README.md, CHANGELOG.md, `.claude/settings.json`, `.claude/agents`
    (adapted), `tools/csharpier`, `.github/workflows/build.yml` (timing tests filtered out on the runner).
- [ ] (done) **S5 progress:** files and text renamed by `rename.sh` (regex `GeometryHelper\.Cad(?![A-Za-z0-9])`,
  so `GeometryHelper.CadConvert` is untouched); 324 files given `using GeometryHelper;` (the engine was inside the
  core's root namespace before) and partial names (`Geometry.GeoPoint2`, `Export.Numbers`) qualified; csharpier run
  over everything. Build `-warnaserror --no-incremental`: 0 warnings, 0 errors. First suite run: OpenCAD2D.UnitTest
  13 883 / 13 884 (DisplaySpeed16, a timing test that fails under load, to rerun alone); the others running.
- [ ] **S5. Rename projects, assemblies and namespaces** as in the table above, with a script, together with
  `InternalsVisibleTo`, pack URIs, resource names and XML doc references.

  Check: `dotnet build OpenCAD2D.slnx -c Release -warnaserror --no-incremental` with 0 warnings; every suite green
  (OpenCAD2D, Wpf, Plugins, Python on net48 and net8.0, Remote); no "GeometryHelper.Cad" left (`grep -rn`).

- [ ] **S6. What the user sees.**
  1. The title becomes OpenCAD2D, and the exe `OpenCAD2D.exe`.
  2. Settings go to `%AppData%\OpenCAD2D`, copied from `%AppData%\GeometryHelper\Dxf` on the first start.
  3. The ProgId becomes `OpenCAD2D.Drawing`, and the old one is removed when the association is written.
  4. The pipe of Remote, the temp folder, the plot style folder and the crash log are renamed.

  Check:
  - tests for the settings copy and for the association;
  - the app opened (our own instance; the maintainer's `settings.json` backed up and restored) showing the old
    settings;
  - a `.dxf` opened from Explorer;
  - Python from outside connecting through Remote.

- [ ] **S7. Python.**
  1. The package `opencad2d`: the wheel, the stubs of the CAD libraries, the examples, `GH-DrawingInfo.py`, the
     Pythons folder.
  2. `tools/python/verify-python.sh` moves along.

  Check: pytest green on coreclr and netfx; pyright strict clean.

- [ ] **S8. The repository's own setup.**
  1. The build and format files: `Directory.Build.props` (version 1.0.0), `OpenCAD2D.slnx`, `.gitattributes` (LF),
     `.editorconfig`, `.csharpierrc.json`, `dotnet-tools.json`.
  2. The project's own files: `README.md`, `CHANGELOG.md` (starting at the split, pointing at GeometryHelper's history),
     `THIRD-PARTY-NOTICES.md` (SkiaSharp, pythonnet, fonts).
  3. `CLAUDE.md` adapted from GeometryHelper's.
  4. Agent setup: `.claude/settings.json` (format hook), `.claude/agents`, and the lead's scripts (verify,
     `publish-run.sh` to `D:\OpenCAD2D-run`, the LISP and AutoCAD harnesses).
  5. The docs: a docfx site of its own.
  6. CI: `.github/workflows/build.yml` building and testing everything, the core taken from `Lib`.

  Check: `dotnet csharpier check .` exits 0; docfx 0 warnings; the CI run on GitHub is green.

- [ ] **S9. Carry the open work over.** The open and parked items of Part C (AutoLISP, Python) and Part A's leftovers
  move from GeometryHelper's `PROGRESS.md` to the section "Open work" below.

  Check: nothing open is left only in GeometryHelper.

- [ ] **S10. Clean GeometryHelper.**
  1. Remove the CAD projects, tests, example, docs, `geometryhelper.cad` and the CAD stubs.
  2. Update the CAD jobs of the CI, the `.slnx`, `CLAUDE.md`, `README.md` and `PROGRESS.md`.
  3. Add a **BREAKING.** entry in `CHANGELOG.md` pointing to opencad2d.

  Check: the full verify of GeometryHelper green (build, core, IfcConvert, TeklaConvert 2020/2025/2026, PyStubs,
  pytest, docfx).

- [ ] **S11. Switch over.**
  1. The run copy is published to `D:\OpenCAD2D-run`; the maintainer points the shortcut to `OpenCAD2D.exe`.
  2. The code-review graph is built for opencad2d.
  3. Both repositories are committed and pushed.

  Check: the app runs from `D:\OpenCAD2D-run`; both `main` branches are pushed and their CI is green.

- [ ] **S12. Installer** (after the split): Inno Setup, per-user install, .NET included, `OpenCAD2D-Setup-x.y.z.exe`.
  Planned separately.

### Leftovers of the split

(Written here as the steps finish.)

## Open work

(Filled in by S9.)
