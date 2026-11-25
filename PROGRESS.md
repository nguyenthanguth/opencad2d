# Progress

The work list of OpenCAD2D: the 2D CAD app that works like AutoCAD 2021, with AutoLISP, Python, plugins and Remote,
split out of the GeometryHelper repository (`D:\GitHub\GeometryHelper`) in November 2026.

- `[x]` done; `[ ]` to do; `(running)` an agent is on it now.
- The maintainer adds, removes or reorders items; the lead takes the next open item from the top, ticks it when it is
  merged, and writes here what it leaves behind.
- If work stops (a usage limit, a crash), the next session reads this file first and carries on from the first open
  item. Every step says how it is checked.

## S. The split from GeometryHelper (done 2026-11-23, S12 left)

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

- [x] **S4. Lib and the references.** Done (opencad2d `808a833a`): `Lib\GeometryHelper.dll` + `.xml` from `6bb8136e`,
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

- [x] **S6-S8 as done (opencad2d `808a833a`, pushed; CI green on GitHub):**
  - S6: "GeometryHelper CAD" -> "OpenCAD2D" everywhere; `%AppData%\GeometryHelper\Drawing` and `\Dxf` -> one folder
    `%AppData%\OpenCAD2D` (GlobalSettings, CadCrashLog, CadPlotStyleFolder, Program.cs), copied once at the first
    start by the new `CadOldFolders.CopyOnce` (3 tests); `%LocalAppData%\OpenCAD2D\Remote`; temp folders `OpenCAD2D`;
    pipe `OpenCAD2D.<pid>`; ProgID `OpenCAD2D.Drawing` and `OldProgId` `GeometryHelper.Drawing` removed on
    register/unregister (1 new test). The exe was `OpenCAD2D.App.exe` at first, since an exe named
    OpenCAD2D would ship an `OpenCAD2D.dll` beside the engine's; the engine became `OpenCAD2D.Core` (see the leftovers).
  - S7: package `src/python/opencad2d` (was `geometryhelper`), `opencad2d.runtimeconfig.json`, `OPENCAD2D_RUNTIME`,
    pyproject (name, description, URLs, `OpenCAD2D-stubs`), hatch_build reads `OpenCAD2DVersion`; `opencad2d.__getattr__`
    hands out the names of `opencad2d.cad`. The stub generator writes two packages, `GeometryHelper-stubs` and
    `OpenCAD2D-stubs` (`StubGenerator.Roots`). PyStubs tests: 3 expectations updated, one of them a failure that was
    already in GeometryHelper (`CadTrueTypeFonts.WarmUp` returns a `Task`, written as Any). PyStubs 73/73, Python
    242/242 on net8.0 and net48.
  - S8: CLAUDE.md (part one copied, part two new), README.md, CHANGELOG.md, `.claude/settings.json`, `.claude/agents`
    (adapted), `tools/csharpier`, `.github/workflows/build.yml` (timing tests filtered out on the runner).
- [x] **S5 as done:** files and text renamed by `rename.sh` (regex `GeometryHelper\.Cad(?![A-Za-z0-9])`,
  so `GeometryHelper.CadConvert` is untouched); 324 files given `using GeometryHelper;` (the engine was inside the
  core's root namespace before) and partial names (`Geometry.GeoPoint2`, `Export.Numbers`) qualified; csharpier run
  over everything. Build `-warnaserror --no-incremental`: 0 warnings, 0 errors. First suite run: OpenCAD2D.UnitTest
  13 883 / 13 884 (DisplaySpeed16, a timing test that fails under load, to rerun alone); the others running.
- [x] **S5. Rename projects, assemblies and namespaces** as in the table above, with a script, together with
  `InternalsVisibleTo`, pack URIs, resource names and XML doc references.

  Check: `dotnet build OpenCAD2D.slnx -c Release -warnaserror --no-incremental` with 0 warnings; every suite green
  (OpenCAD2D, Wpf, Plugins, Python on net48 and net8.0, Remote); no "GeometryHelper.Cad" left (`grep -rn`).

- [x] **S6. What the user sees.**
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

- [x] **S7. Python.**
  1. The package `opencad2d`: the wheel, the stubs of the CAD libraries, the examples, `GH-DrawingInfo.py`, the
     Pythons folder.
  2. `tools/python/verify-python.sh` moves along.

  Check: pytest green on coreclr and netfx; pyright strict clean.

- [x] **S8. The repository's own setup.**
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

- [x] **S9. Carry the open work over.** The open and parked items of Part C (AutoLISP, Python) and Part A's leftovers
  move from GeometryHelper's `PROGRESS.md` to the section "Open work" below.

  Check: nothing open is left only in GeometryHelper.

- [x] **S10. Clean GeometryHelper.**
  1. Remove the CAD projects, tests, example, docs, `geometryhelper.cad` and the CAD stubs.
  2. Update the CAD jobs of the CI, the `.slnx`, `CLAUDE.md`, `README.md` and `PROGRESS.md`.
  3. Add a **BREAKING.** entry in `CHANGELOG.md` pointing to opencad2d.

  Check: the full verify of GeometryHelper green (build, core, IfcConvert, TeklaConvert 2020/2025/2026, PyStubs,
  pytest, docfx).

- [x] **S11. Switch over.**
  1. The run copy is published to `D:\OpenCAD2D-run`; the maintainer points the shortcut to `OpenCAD2D.exe`.
  2. The code-review graph is built for opencad2d.
  3. Both repositories are committed and pushed.

  Check: the app runs from `D:\OpenCAD2D-run`; both `main` branches are pushed and their CI is green.

- [ ] **S12. Installer** (after the split): Inno Setup, per-user install, .NET included, `OpenCAD2D-Setup-x.y.z.exe`.
  Planned 2026-11-23; started the same day at the maintainer's word, so I2-I8 are taken as written. Done so far: steps 1,
  2 (the icon E3) and 4; step 3 checked by a silent install on this machine, the Sandbox run still open.

  | # | Decision | State |
  |---|---|---|
  | I1 | Inno Setup 6 (free, one `.iss` script, per-user installs without administrator). WiX/MSI is heavier for the same result, MSIX needs a signing certificate to install at all, Velopack adds auto-update the app does not need yet. | decided by the maintainer (S12 line) |
  | I2 | .NET included as a self-contained publish: `dotnet publish src/OpenCAD2D.App -c Release -r win-x64 --self-contained`. Not single-file and not trimmed: plugins, Python (pythonnet) and the support folders find their files beside the exe, and WPF and plugin loading use reflection. About 70 MB more than the framework-dependent build. | proposed |
  | I3 | Per-user: `PrivilegesRequired=lowest`, installed to `%LocalAppData%\Programs\OpenCAD2D`, a Start menu shortcut, a desktop shortcut as an unticked task. | decided by the maintainer 2026-11-23: kept over Program Files (no administrator; users drop files into Lisps, Plugins, Pythons beside the exe) |
  | I4 | The `.dxf`/`.dwg` association is a ticked task that runs `OpenCAD2D.exe --register`; the uninstaller runs `--unregister`. | proposed |
  | I5 | The uninstaller removes only what the setup installed. `%AppData%\OpenCAD2D` (settings, plot styles, the plugin list) and files the user added to `Lisps`, `Pythons`, `Plugins`, `Fonts` stay, as AutoCAD leaves its user folders. | proposed |
  | I6 | Python is not bundled: the app finds the Python of the machine or of Global settings, as it does now. | proposed |
  | I7 | The setup is unsigned for now (SmartScreen warns on the first run); a code signing certificate is the maintainer's call. | proposed |
  | I8 | One fixed `AppId` GUID, the version from `Directory.Build.props`, `CloseApplications=yes` so an upgrade closes a running app first. | proposed |

  1. `tools/installer/OpenCAD2D.iss` and `tools/installer/make-installer.ps1` (publish into `artifacts/publish`, then
     `iscc`, writing `artifacts/installer/OpenCAD2D-Setup-<version>.exe`).
     -> verify: the script runs on a clean checkout; the setup is written; its size is recorded here.
     DONE: `OpenCAD2D-Setup-1.0.0.exe` 81 700 683 bytes (78 MB) from a 307 MB publish (507 files); about 85 s. Inno Setup
     6 per user (`winget install JRSoftware.InnoSetup --scope user`). AppId `{CFF9090B-3DE5-438A-AC7A-86ED890B73FB}`.
     Beyond I4: the uninstaller runs `--unregister` only when the association names this install, so a copy registered
     from another folder (`D:\OpenCAD2D-run`) keeps it. The satellite languages dropped after 1.0.0 (`SatelliteResourceLanguages` en: 507 files to 286, 307 MB to
     291 MB); the .pdb files stay.
  2. The new icon (the design canvas, then `make-icons.ps1`) is in the setup and its shortcuts.
     -> verify: the setup, the Start menu entry and a `.dxf` in Explorer show it.
  3. Install, run, upgrade and uninstall on a Windows user with no .NET 8 Desktop Runtime (a Windows Sandbox).
     -> verify: the app opens a DXF and a DWG, a LISP file and a `.py` load, a `.dxf` opens from Explorer; installing
        the same version again over a running app closes it and keeps the user's added Lisps; uninstalling leaves
        `%AppData%\OpenCAD2D` and no file under the install folder that the setup wrote.
     Checked on this machine (2026-11-23): `/VERYSILENT /TASKS=""` into a temporary folder, exit 0, 509 files; the
     installed exe draws `--screenshot` of a DXF, exit 0; the silent uninstall leaves only a file added to `Lisps`; the
     Start menu entry and the uninstall key gone; the maintainer's association unchanged. Not yet: a Sandbox with no
     .NET, a DWG, LISP and `.py` in the installed app, the association task, an upgrade over a running app.
  4. CI: a job that builds the setup and keeps it as a workflow artifact (not a release).
     -> verify: the CI run is green and the artifact downloads and installs.
     DONE: `.github/workflows/publish-installer.yml`, on a published release (attaches the setup) or by hand (`tag`).
  5. A release with the setup attached: the maintainer's word (tags and releases need it).

### Leftovers of the split

- S10 done in GeometryHelper `360b73a7` (+ `9f72b308`, which takes out the generated stubs the first commit carried by
  mistake: 77 000 lines of `.pyi` stay in GeometryHelper's history). Core 19 272 passed, IfcConvert 339, docfx 0
  warnings; TeklaConvert unchanged and not run. The leader's `docfx-check.sh` there lost its Python steps and now
  exports with `git diff HEAD` (staged deletions were kept before).
- S11 done: run copy `D:\OpenCAD2D-run` (`.claude/gk/leader/publish-run.sh` here, git-excluded through
  `.git/info/exclude` like GeometryHelper's); the 22 Lisp files the maintainer had in `D:\GeometryHelper-run\Lisps`
  copied over. Left to the maintainer: a shortcut to `D:\OpenCAD2D-run\OpenCAD2D.exe`, `OpenCAD2D.exe --register`
  once for the `.dxf`/`.dwg` association, deleting `D:\GeometryHelper-run` when no longer wanted.
- [x] The app icon (2026-11-23): concept E3 of the design canvas https://claude.ai/artifact/68myTJqRSBBmwvSsu2h1W5,
  chosen by the maintainer after rounds A-D, E-H, E1/E2 and E3: on a white rounded tile, a blue O holds a C, the C holds
  AD. The letters are Space Grotesk Bold outlines (OFL) written into `make-icons.ps1`, their ink centred in the room the
  C leaves (26.05 units each side, 128 up and down); below 24 px only the O and the C. `app.ico`, `drawing-file.ico`,
  and `docfx/images/logo.svg` as the docs site's logo and favicon.
- [x] A docs site for OpenCAD2D (2026-11-23): `docfx.json`, `docfx/index.md` and `toc.yml`, `docs/toc.yml`; the .NET
  reference of `OpenCAD2D.Core` and `OpenCAD2D.Remote`, the guides, and the Python API (412 pages, 806 types) that
  `make-api-pages.py --namespace OpenCAD2D` writes from `OpenCAD2D-stubs`; core types link to GeometryHelper's site
  through its xrefmap (85 pages linked). `docfx metadata` and `build --warningsAsErrors`: 0 warnings.
  `.github/workflows/docs.yml` builds it on every push and pull request and, with GitHub Pages on and the repository
  variable `DOCS_PAGES` true (both set by the maintainer 2026-11-23), publishes it at
  https://nguyenthanguth.github.io/opencad2d/ (first deployment run 45179012750).
- [x] Tests that run `OpenCAD2D.exe --screenshot` no longer touch `%AppData%\OpenCAD2D` (44c8734e): only a start that
  opens the window copies the old folders (`CadAppHost.ShowsWindow`).
- [x] The code-review graph: built 2026-11-23 (2 026 files, 31 105 nodes) with `.mcp.json` (git-excluded) here.
- [x] The leader's harnesses moved (2026-11-23) to `.claude/gk` here (git-excluded): `lisp` (run, routines, survey,
  the runner on `OpenCAD2D.Core.dll`), `acon`, `acon-stdin`, `acad-control.md`, `acad-gui-template.ps1`; the L1/L2/L5
  feeds and stored AutoCAD runs to `.claude/scratch` (ll and rev from L8). Scripts only: the 4.3 GB of old run output
  stays in GeometryHelper, and so does AutoCAD's Trusted Location `gk\acad-trusted`, which AutoCAD's profile names.
  Check: `routines.ps1 -Both -ReuseAcad` 18 of 19 transcripts and 19 of 19 dumps the same; ll differs by ZOOM W's
  `Specify opposite corner`, a line the console does not echo (L8's acceptance had ll DIFF too). Not moved: the
  per-round tools of L3-L5 (`scratch/lisp-l3/tools`, `l5/tools`, `lisp-l4/tools`).
- [x] The exe is `OpenCAD2D.exe` (maintainer 2026-11-23): the engine's assembly is `OpenCAD2D.Core` (folder
  `src/OpenCAD2D.Core`, namespace still `OpenCAD2D`, so no code changed), the app's `OpenCAD2D`. GeometryHelper
  `9938c207` names `OpenCAD2D.Core` in `InternalsVisibleTo`; `Lib` rebuilt from it with `tools\update-lib.ps1`.
  XAML `assembly=OpenCAD2D` became `assembly=OpenCAD2D.Core`; Python loads `OpenCAD2D.Core`.
- [x] PyPI: `opencad2d` 1.0.0 published 2026-11-23 (the maintainer's word) through the GitHub Release v1.0.0 and
  `.github/workflows/publish-python.yml` (trusted publishing, no environment; run 45189345008): the wheel
  `opencad2d-1.0.0-py3-none-win_amd64.whl` is on PyPI and attached to the release. Checked: `pip install opencad2d`
  in a fresh venv, `new_file()` gives a Drawing on .NET 8.
- [x] NuGet: `OpenCAD2D.Core` and `OpenCAD2D.Remote` 1.0.0 on nuget.org (2026-11-23), the API only (Wpf and Python
  stay unpacked, the maintainer's call). `publish-nuget.yml` by trusted publishing, no environment; the packages
  depend on GeometryHelper 13.12.1 from nuget.org (`-p:GeometryHelperFromPackage=true`) and are attached to the
  release v1.0.0. A published release now puts out the wheel, the packages and the setup together.
- Files: DXF R12 to R2018 and our own DWG reader and writer (R2000 to R2018), opened by AutoCAD 2021 with AUDIT 0;
  RECOVER, AUDIT, autosave, templates, xrefs, raster images, PDF underlays, OLE frames, WMF in and out.
- Drawing and editing: AutoCAD 2021's 2D draw and modify commands with their options and prompts, grips and grip modes,
  object snaps and tracking, UCS, selection, undo, clipboard, blocks and dynamic blocks, attributes, groups, arrays,
  constraints, MLINE, regions, hatches and gradients.
- Annotation: text and MTEXT with its editor, tables, fields, dimensions (associative, annotative) and multileaders.
- Layouts: viewports (double click in, snaps through them, resize and preview as AutoCAD), layers per viewport, PLOT,
  PUBLISH, page setups, plot styles, PDF and image export.
- The app: ribbon, command line with AutoComplete, Properties with the selection box, palettes, managers and dialogs,
  Global settings, shortcuts and acad.pgp aliases, plugins.

#### Asked for since

- [x] A1. Command line history as AutoCAD's (maintainer 2026-11-21; design first): drag the top edge to show more or
      fewer lines (kept in the settings), scroll back with the wheel, select lines and copy them (Ctrl+C, right-click
      Copy and Copy History); F2's text window stays as it is
- [x] A2. Plugins extend the window as in AutoCAD (maintainer 2026-11-21; design first): a plugin loaded at run
      time (PLUGINLOAD/APPLOAD, both runtimes, unloadable) adds its own ribbon tabs and panels (large/small buttons,
      icons, split buttons, tooltips, as AutoCAD's RibbonTab/RibbonPanel), rows of the PROPERTIES palette for its
      objects, buttons and items of the DRAWINGS list, and custom object snap modes (as AutoCAD's CustomObjectSnapMode
      with a glyph); described without WPF in GeometryHelper.Cad.Plugins, drawn by the window, gone when unloaded
- [x] A2 parked: colour swatches in the Color rows of Properties (the presenter's and a plugin's; the A2Properties board
      to match), collapsing plugin panels, slide-outs, dialog launchers, macros, a pick-point button, and plugin snaps
      through a viewport, inside a block and for typed points. Snaps done 2026-11-25 (A2P T2, plan .claude/scratch/a2p);
      the rest done 2026-11-25 (A2P T1): swatches as AutoCAD (measured), plugin panels Medium/Collapsed, slide-outs,
      dialog launchers, macro buttons (API 1.2), a pick button on point rows. Left: whether a macro's last ";" adds an
      Enter (AutoCAD GUI check); `_END` typed fixed 2026-11-25; the presenter's own point rows have the pick button
      since 2026-11-25; left: Hatch Origin, Dimension Text position, and whether AutoCAD sets Z from the pick (to probe)
- [x] A3. Asked for 2026-11-22/23, done: the first STYLE and the Font box no longer stall (font lists read ahead,
      long drop-downs virtualized); Remove Vertex from a grip's menu acts at once; UCS P after UCS W returns to World
      (the CD routine) and UCS changes are undo steps as in AutoCAD; the DRAWINGS right-click menu as AutoCAD's file
      tab menu, Save/Close of another row keeping the drawing shown; every support folder read with its subfolders;
      a .dwg dropped on the drawing area inserted as a block; built-in linetypes with AutoCAD's lengths
- [x] A4. Applications (2026-11-22/23): one Load/Unload Applications window for Lisp files and plugins (APPLOAD),
      LISPLOAD for .lsp files, NETLOAD for .dll, Manage > Load Manager/Load Lisp/Load Plugin/Load Python; Unload only
      unticks, unloaded files kept across restarts with their commands, Remove takes a file out; the load-error line;
      the Plugins ribbon tab and .ghplugin packages removed; the sample plugin in examples/
- [x] A4 parked: a UCS change inside the Block Editor and then BCLOSE discard not put back. Done 2026-11-25 as AutoCAD
      2021 does it (measured): BEDIT in the World UCS, UCS/UCSMAN refused inside, BCLOSE puts the UCS back unnamed. The
      UCS dialog half was moot: the presenter has no UCSMAN dialog (new item below)
- [x] A5. The UCS dialog (UCSMAN) as AutoCAD's, design first; its changes one undo step per dialog. Done 2026-11-25
      (board A5UcsDialog, AutoCAD probe .claude/scratch/a5): three tabs, rename/delete/Details, depth; OK one step named
      UCSMAN. Parked: orthographic depth and Reset not measured in AutoCAD (built from its documentation); UCSVP,
      UCSFOLLOW and UCSSELECTMODE kept on the drawing, not as variables; UCSNAME stays "" for an orthographic system
- [x] A6. More of the app as API (maintainer 2026-11-25): the window's view functions that exist inside (ZoomExtents,
      ZoomToObjects, CadView.ZoomTo/ZoomAt/ZoomToHeight, and the like: zoom window, center, previous, pan, regen, the
      current view read and set) made public for C# plugins (the command context), Remote (`CadRemoteDocument`) and
      Python (`Drawing.zoom_extents()` ...), one meaning in all three; a survey first of every such internal function
      worth exposing. -> verify: each call tested in-process, through Remote and from a Python tool; docs and stubs.
      C# and Remote done 2026-11-25 (API T1, CadEditor, plugin API 1.3); Python done the same day (T4: zoom_*, pan,
      view, named views, select_*, get/set_variable; pytest 779 / 764)
- [x] A7. Typed entities in Python, as C#'s `if (obj is Line line)`: `Entity.net` typed `DxfEntity` (not `Any`) so that
      `isinstance(e.net, DxfLine)` and `match e.net: case DxfCircle() as c:` narrow in Pylance (both already run under
      pythonnet, checked 2026-11-25); `Entity` generic, `entities(DxfCircle)` giving `list[Entity[DxfCircle]]`.
      -> verify: pyright strict on an example; pytest on both runtimes.
      Done 2026-11-25 (API T2): Entity[E], entities(DxfCircle), add_* typed; pytest 685 coreclr / 672+13 netfx, pyright
      strict clean, bad_typed_entities.py gives exactly its 6 errors
- [x] A8. A few edit calls with types for plugins and Python: erase, move, copy, rotate, scale, mirror, offset on
      entities, returning the new ones (no command text), one undo step with the rest of the run.
      -> verify: in-process, Remote and Python tests; the same drawing as the command gives.
      C# and Remote done 2026-11-25 (API T3: CadEdits shared by the commands, CadEditor.Edits, Remote transform/offset;
      MIRROR's too-short band fixed); Python done the same day (T5: Drawing.move ... offset, typed; pytest 1058 /
      1040). Parked: a public dry-run check on CadEditor (a refusal found only while planning an offset, inside the
      script's own group on a file, comes after the queue is applied); CadRemoteDocument.Edit to return the inserted
      handles (one request per insert today)
- [x] A9. Documents for C# plugins as AutoCAD's DocumentManager: the open drawings listed, one opened, closed, made
      active (Remote and Python have them). Plan in .claude/scratch/api/spec.md (API-D22).
      Done 2026-11-25: CadDocuments (List, Active, Open, Add, Close/CloseAndDiscard/CloseAndSave), plugin API 1.4.
      Parked: a [CadCommand] cannot open, add or show a drawing (showing ends its command); a session-context flag
      as AutoCAD's CommandFlags.Session would allow it
- [x] A11. Our own technical font (maintainer 2026-11-25): OpenCAD2D ISO, opencad2d.shx, type B as ISO 3098 /
      TCVN 7284, drawn clean-room on the type B grid (board FontIso), 242 code points with the 134 Vietnamese letters,
      replacing ghsimplex outright (BREAKING, no alias; a saved FONTALT of ghsimplex read as opencad2d). Checked in
      AutoCAD 2021 by accoreconsole: 241/242 text boxes equal to 4 decimals before the `&` fix, all after; every
      AutoCAD number of the tests re-measured with the new font. Two reader faults fixed on the way (code 10 radius
      signed; a code 12 half circle measured by its chord's rectangle) and the MTEXT stack gap 0.1 h for this font.
      Plan .claude/scratch/font/spec.md, numbers .claude/scratch/font/p2p3-report.md
- [x] A10. The last GeometryHelper/GH names out of what OpenCAD2D writes and shows (maintainer 2026-11-25, no compatibility
      kept, as for the font): XRECORD/XData GH_REFEDIT, GH_LAYTRANS_MAP, GH_OWNER (public CadOwnerTag.ApplicationName),
      the template's dimension styles GH and GH-Annotative, the formats GeometryHelperDataExtraction,
      GeometryHelper.ToolPalettes and "GeometryHelper clipboard 1", the samples GH-DrawingInfo.lsp/GHINFO and GHPYINFO;
      the old-name readers removed (GeometryHelper PDF/PNG/JPG, GeometryHelper.pat/.lin, GeometryHelper-mm.dwt,
      %AppData%\GeometryHelper copy, ProgId GeometryHelper.Drawing). Kept: the core library's own name (Lib, namespaces,
      GeometryHelper-stubs). BREAKING in the CHANGELOG. Agreed by the maintainer 2026-11-25 (group c renamed, group b
      readers removed; names decided by the lead). Done 2026-11-25: OPENCAD2D_* keys, styles OpenCAD2D, samples
      LSPINFO/PYINFO/CSINFO, old-name readers removed; test fixtures GH_ -> OC_
- [ ] A12. Sheet Set Manager as AutoCAD 2021's (maintainer 2026-11-25; design first): the SHEETSET palette (Sheet List,
      Sheet Views, Model Views tabs), NEWSHEETSET (the wizard: from an example sheet set or from existing drawings),
      OPENSHEETSET, sheets as layouts of drawings in the set (new sheet from a template, import layout as sheet, rename and
      renumber, subsets), sheet set and sheet properties (custom properties), fields that read them (SheetSet, CurrentSheet*
      fields in title blocks), sheet list table, publish the set to PDF (one file or one per sheet) and eTransmit-like
      zip of the set; the -SHEETSET? and the archive left out unless asked.
      1. Research -> verify: what AutoCAD 2021 does, from its help pages and console probes (accoreconsole can open and
         read a .dst through the SheetSet COM API? check), the .dst format (AutoCAD's is a proprietary encoded file:
         decide whether OpenCAD2D reads/writes .dst or keeps its own open format, e.g. JSON, with an import of .dst if it
         can be read), what open-source CAD tools do (none known; search GitHub), and a spec with numbered decisions.
      2. Design -> verify: boards on the Design canvas (the palette's three tabs, the wizard, the properties dialogs, the
         sheet list table) approved.
      3. Engine (Core): the sheet set model, its file, sheets <-> layouts, numbering, custom properties, fields -> verify:
         tests of open/save round trips, renumbering, a field in a title block updating with the sheet.
      4. Window (Wpf): the palette and dialogs from the boards -> verify: view-model tests, screenshots against the boards.
      5. Publish and transmit -> verify: a set of three drawings published to one PDF with the pages in set order.
- [ ] A13. Colour ribbon icons (maintainer 2026-11-25; design first): redraw the ribbon's icons of OpenCAD2D in colour,
      in the visual language of AutoCAD 2021's ribbon (the maintainer's screenshot: Home tab, dark theme: blue/teal
      accents, yellow highlights, red where AutoCAD uses red), so commands are recognised at a glance. Our own drawings:
      AutoCAD's icons are Autodesk's and are never copied, traced or extracted (look and colour roles only; see
      acad-reference-sources). One set at 32 px (large buttons) and 16 px (small), crisp at 100/125/150 % scaling, for
      the dark theme (and readable on the light one if the app has it).
      1. Inventory -> verify: the list of every ribbon command icon in OpenCAD2D (tabs Home, Insert, Annotate,
         Parametric, View, Manage, Output, Settings, the contextual tabs), each with the AutoCAD look it should evoke and
         the colour roles (a palette of 6-8 colours: line/stroke grey-white, accent blue, teal, yellow, red, green).
      2. Design -> verify: a board per tab on the Design canvas, the new icons beside today's, approved by the
         maintainer (Home first). Maintainer 2026-11-25: the lead chooses the colours and does every tab without asking;
         report afterwards. A12 and A13 start after the S12 Sandbox reboot.
      3. Build -> verify: the icons as vector geometry (as today's icons are drawn) in the theme resources, every button
         bound, a test that every ribbon command has an icon and none is missing, screenshots of each tab at 100 % and
         150 % against the boards.

#### Parked follow-ups

Small parity and speed follow-ups left by finished items. They stay parked under "features first" unless the
maintainer meets one as a real fault. Details for each are in the history at `ddf1ff27`.

- [ ] Checks only the AutoCAD GUI can give: LAYERP with U and the Layer manager; the guessed prompts of SPELL,
      ATTIPEDIT, MLEADERALIGN Spacing/Distribute; DIMBREAK Auto breaks; filter.nfl; Ctrl cycling of grip modes; the
      Properties kind list order and its reset to All
- [ ] Dialog polish (design3+design4): one footer button order, error brush, MTextMask header drag, SelectTemplate list,
      Plot styles/Publish layout details
- [ ] Ribbon at 125 % and 150 % display scaling; Home panels a few pixels wider than the design
- [ ] PLOT on real printers and Microsoft Print to PDF; -PLOT choosing a printer; smaller PDFs (glyph subsetting)
- [ ] Trans-spatial dimensions (DIMASSOC 2 through a viewport); typed snaps and OTRACK through a viewport; viewports
      with a 3D view direction entered
- [ ] MLINE edits at mitred corners and CJ pairs; annotative turned words on an extension line; MTEXT rare codes
      and column grips; table long texts and data links in old versions
- [ ] DWG and DXF version leftovers: R2013 AuxHeader numbers, spline normals off XY, R12 gradient meshes, version
      converter number parsing
- [ ] Xrefs, REFEDIT, managers and hatch dialogs: GUI-only checks and small wording differences

### Part C: scripting

Asked for by the maintainer on 2026-11-20; AutoLISP finished 2026-11-23 (L1-L9), Python re-scoped and finished the same
day (PW1-PW3). (The letter B went with model
drawings, which moved to TeklaHelper.DrawingManager.) Each stage starts with a look at open-source projects on GitHub
that do the same, and names the ideas it takes. The maintainer's own AutoLISP routines are the acceptance set for the AutoLISP
stages: they are read where they are and never copied into the repository, and tests build small routines of their own.

#### AutoLISP

The goal is one routine for both programs: a `.lsp` (and its `.dcl`) written once runs in AutoCAD 2021 and in
`GeometryHelper.Cad.App` with the same result. A stage is done when the maintainer's routines that need only what it adds
run in both alike. Each test runs the same routine in AutoCAD (accoreconsole, hidden) and in the presenter, and compares
the two drawings written as DXF (entities, layers, coordinates within the tolerance), the values returned and the
command-line output. What AutoCAD alone can do (COM to other programs, ARX and .NET loading) is refused with a message
naming it, never ignored; `(getvar "PRODUCT")` tells a routine which program runs it. Input a routine asks for (points,
picks, keywords) is fed to both programs from the test's script; DCL dialogs, which accoreconsole cannot show, are
compared by their values and by eye.

- [x] L1. The interpreter: AutoLISP's reader, evaluator and data types (lists, symbols, reals, integers, strings, entity
      names, selection sets), `defun` and `c:` commands, error handling with `*error*`, APPLOAD, `(load)`, `acad.lsp`,
      `acaddoc.lsp` and `S::STARTUP`; LISP expressions typed at the command line
- [x] L1b. APPLOAD dialog (design first): the Load/Unload Applications dialog with the Startup Suite, designed in the
      Design artifact before it is built
- [x] L1 parked, closed by L9 (2026-11-23): U naming `L` for LINE measured the same (`LINE`). Left with their reasons:
      acad.lsp/acaddoc.lsp order in AutoCAD (the maintainer has neither file, his Support folder holds only AutoCAD's own
      acad2021.lsp; ours follows AutoCAD's documented order); Unicode strings typed at the console (the app's command line is
      Unicode end to end, only accoreconsole's ANSI stdin cannot show it); the harness's Enter-repeat echo (a harness artefact)
- [x] L2. Drawing functions: `command` and `vl-cmdf` driving the presenter's commands with their prompts; `ssget` with
      its modes and filters, `ssname`, `sslength`, `ssadd`; `entsel`, `nentsel`, `entget`, `entmod`, `entmake`,
      `entdel`, `entnext`, `tblsearch`; `getpoint`, `getcorner`, `getdist`, `getangle`, `getreal`, `getint`,
      `getstring`, `getkword`, `initget`; `getvar`, `setvar`, `trans`, `polar`, `inters`, `osnap`; `princ` and `prompt`
- [x] L2 parked, closed by L9 (2026-11-23): an entmade MTEXT's group 50 (radians, the later of 11 and 50 wins, as
      AutoCAD) and the words U writes for a routine's commands are fixed. Left with their reasons: -HATCH's second Select
      objects adding to the first (no routine of the maintainer's does it); the MATERIAL object of a layer's 347 (no routine
      reads it); entget of MTEXT about 0.05 ms slower (10 000 MTEXTs cost half a second); a `c:` routine with only `entmod`
      is a step of its own where AutoCAD's console made none (u4, decided to leave: AutoCAD's merge is odd and rare; ours
      undoes only what the routine did); REDO after a typed `(ssget "_X")` or REGEN redoes here and says Nothing to redo in
      AutoCAD; `(getvar "VIEWSIZE")` is nil in the headless runner after ZOOM W (AutoCAD 60.0)
- [x] L3. vl- functions and files: `vl-string-*`, `vl-sort`, `vl-remove`, `vl-position`, `vl-file-*`, `open`, `close`,
      `read-line`, `write-line`, `findfile`, `getfiled`
- [x] L4. vla- and vlax- objects: the members the maintainer's routines use (about 40), on the presenter's own object
      model; COM to other programs (Excel) refused with a message
- [x] L4 parked, closed by L9 (2026-11-23): a fence's objects now in the order along the fence. Left with their reasons:
      ObjectID as AutoCAD's small numbers (ours are handle values, unique and stable, and fields resolve them; only the
      printed number differs); typed forms after a `c:` routine joining its undo step (typing forms by hand after a routine is
      a developer's act; U after the routine itself is right); a crossing wholly outside the view finding a long line (probe
      p4: view-dependent, unexplained, no routine hits it); ZOOM's 5 % margin against AutoCAD's about 1 % (the view only);
      ZOOM n alone keeping the centre (routines write `ZOOM 0.8X` or `E`)
- [x] L5. DCL dialogs: the DCL parser and its tiles drawn as WPF, with action_tile, get_tile and set_tile (designed in
      the Design artifact first). Accepted 2026-11-21: E1 and FDVAL run in the full AutoCAD and the presenter with the
      same dialog values and E1 transcript
- [x] L5 parked, closed by L9 (2026-11-23): list clicks (plain, Ctrl, Shift), the list double click and the slider drag
      measured in AutoCAD 2021 with real clicks and the same as ours; a click that changes nothing now runs a list's action
      again (reason 1). Left with their reasons: a dialog of ok_cancel alone (162 x 93; two buttons and nothing else is not a
      real dialog, looks only); AutoCAD's narrower dialog text (FDVAL 219 against 242: Segoe UI was L5's choice, values the
      same); getpoint in an action (AutoCAD's help forbids input while a dialog is active, so no working routine does it)
- [x] L6. Trusted folders and SECURELOAD, as AutoCAD 2021, shared with the Python stages. Done 2026-11-22:
      SECURELOAD, TRUSTEDPATHS (`\...` for subfolders) and LEGACYCODESEARCH as measured in AutoCAD 2021, the warning
      "Security - Unsigned Executable File" (Always Load by file content), a Security page in Global settings; the four
      harness feeds and the L2/L5 routines give the same transcripts as before
- [x] L6 parked, closed by L9 (2026-11-23): the app keeps SECURELOAD 1 (AutoCAD 2021's default) and the plugin manager keeps
      its trusted folders apart from TRUSTEDPATHS (decided on the recommendation: the app's own folder with its subfolders,
      Lisps and Plugins, is trusted, so daily use does not ask; folding the plugin list into TRUSTEDPATHS would change how
      plugins in subfolders load). The Security page was driven by hand with the app (UIA, our PID only): the warning for a
      writable folder is right; two faults found and fixed (Add folder now a folder picker, as a Save dialog could not choose
      a read-only folder; Apply and OK held off by a stale DTEXT alias in the saved settings). Left: Remove, Move up and
      down, Include subfolders and the "(not found)" mark were covered by tests only
- [x] L7. Finishing AutoLISP in the app (maintainer 2026-11-22, "focus on finishing LISP first"). Done 2026-11-22: a
      `Lisps` folder beside the app loaded into every drawing and on the search path, relative names (`Sub/x`, `./x.lsp`)
      looked up along it as AutoCAD 2021 does; APPLOAD's Look in, typed folders and wildcards, list keys, Size column and
      drops on Loaded Applications as measured in AutoCAD 2021; files dropped on the window (`(LOAD "...")`, `_SCRIPT`, the
      Drag and Drop box, a drawing on the canvas INSERTed); 90 more functions (`command-s` and the error modes, `getenv`/`setenv`, `autoload`, `regapp`,
      xdata, dictionaries, `vlax-ldata-*`, `entmakex`, `ssnamex`, `grdraw`/`grvecs`), 161 of AutoCAD's 489 non-vla
      built-ins missing before, 72 after. The 19 L2 routines and L3's five against the stored AutoCAD side at e7c354e1:
      13 of 19 and 4 of 5 identical (hh newly), the rest unchanged known differences
- [x] L7 parked, closed by L8 (2026-11-23): GRREAD, the `vlr-` functions (46, not 54), menucmd, help, vmon, the
      vl-exit functions and vlax-queueexpr, entmod of XRECORD and table records, GRCLEAR measured in the GUI, entdel
      keeping an extension dictionary and dcd's MTEXT 42/43 (a harness font setting) are done; APPLOAD's own folder
      browser was replaced by the Load/Unload Applications window, so its items are void. What remains moved to L8
      parked.
- [x] L8. Round L8 (spec .claude/scratch/l8/spec.md, 2026-11-22): GRREAD with AutoCAD's window codes (measured by
      SendInput), the 46 `vlr-` functions with command, lisp, sysvar, dwg and object events, menucmd, menugroup, vmon,
      help, setfunhelp, getcname, fnsplitl, acdimenableupdate, `*error*`, the vl-exit functions, entmod of table records,
      XRECORD and VERTEX, SELECT, 3DPOLY, UNDO No/No, REVCLOUD lengths kept, AutoCAD linetype lengths, the command
      parity fixes, an open line continued as AutoCAD's and a waiting prompt written again. Acceptance: L2 19/19
      transcripts (ll with its linetype-description and regen allowances), L3 5/5, L8 feeds rx, gk, fx the same.
- [x] L8 parked, closed by L9 (2026-11-23), all left with their reasons: ldata as VLO-VL (no routine of the maintainer's uses
      ldata; VLO-VL is AutoCAD's own class); entmod of DICTIONARY and BLOCK_RECORD (routines change dictionaries through
      dictadd and dictremove, which work); ssnamex fence points and the method of a pick-first set (rare; the pick points
      routines read are given); mouse and docmanager reactors and persistent reactors (double-click reactors are rare and
      need canvas hooks, docmanager is multi-document, persistence needs reactors saved in the drawing); MLEADER's Options
      and landing-first orders (interactive; routines give points only); 3DPOLY's typed Z (rare in 2D drawings); FILLETRAD of
      a header with none (10, AutoCAD 0: accepted); trace, tablet, vports, setview and the `_vl-` internals (debugging and
      internals, no routine calls them)
- [x] L9. The AutoLISP leftovers (maintainer 2026-11-23, "finish AutoLISP"): the L1, L2, L4, L5, L6 and L8 parked
      lines above, each fixed or measured in AutoCAD and closed, or left with the reason. Done 2026-11-23 (spec
      .claude/scratch/l9/spec.md, measurements m/console.md and m/dcl.md): an entmade MTEXT's 50 (radians, the later of 11
      and 50 wins), `ssget "F"` in the order along the fence, the words U and REDO write for a routine's commands, and a list
      click that changes nothing running the action again; list selections, the list double click and the slider drag
      measured the same; the rest closed with their reasons

#### Python

Python works with GeometryHelper.Cad in three places: a script outside the app on the libraries alone, a console and
scripts inside the app, and a script outside that drives a running app.

- [x] P1. Choose how: CPython through pythonnet against IronPython, on net48 and .NET 10, with numpy usable; how a
      script finds the libraries; a short decision in the plan. Decided 2026-11-21: CPython 3.11+ through pythonnet 3.2
      (IronPython 3.4 has no numpy and no `match`/`:=`); the netstandard2.0 libraries load unchanged on net48, .NET 8 and
      .NET 10; a 10 000-line DXF opens in 30 ms from Python; scripts read coordinates in blocks (numpy), not one by one
- [x] P2. A Python package `geometryhelper` (pip) loading the core and `GeometryHelper.Cad` without a window: geometry,
      DXF and DWG read and write, a document edited and saved headless; type stubs (`.pyi`) so editors complete names.
      Done 2026-11-21: a local wheel `py3-none-win_amd64` (10.2 MB), 152 pytest tests on .NET 8 and .NET Framework 4.8,
      stubs generated by tools/GeometryHelper.PyStubs (806 types), guide docs/python.md
- [ ] P2 parked: the PyPI name (now `opencad2d`, free) and the upload, both the maintainer's word; a Pylance look in
      VS Code; Linux, macOS and ARM64; a PyInstaller
      hook; one `<summary>` per partial class so hovers stay short (CadDocument); in the stubs, 331 settable reference
      properties and 139 optional parameters that default to null still refuse None, getters are never `| None`
- [x] P3. Python inside `GeometryHelper.Cad.App`: a PYTHON console palette and the commands PYLOAD and PYRUN, with the
      active document, the selection, the command line, prompts (get point, get selection) and undo of a script as one
      step. Done 2026-11-21: GeometryHelper.Cad.Python embeds CPython 3.11-3.15 on the window thread, Esc stops Python
      code in about 20 ms, one undo step per run, PYLOAD with `@cad_command`, a script connecting to its own app refused
- [ ] P3 parked: the command and function names (PYTHON, PYRUN, PYLOAD, get_point...) await the maintainer's word; a
      Wpf test of CadAppRemoteHost.IsOwnerThread; EntityCount cached inside an open group and a failed ApplyEdit taking back a run's whole group
      silently (the maintainer to decide); output flushed only when the window is next idle during a long run (settled by
      TF7: the tools channel redraws the command line after each request)
- [x] P4. Driving a running app from outside: a Python client over `GeometryHelper.Cad.Remote`'s named pipe (open,
      draw, select, run commands, read the drawing). Done 2026-11-21: the .NET CadApplication through pythonnet in a
      `with` block, no protocol change; a headless test server and 63 pytest tests on both runtimes; checked on the
      real app (100 lines committed in 207 ms, one U takes the commit back)
- [ ] P4 parked: a CadApplication used after Dispose reconnects silently (documented); the app's announced title empty
      just after it starts
- [x] P5. Examples and the guide: a script per common task, and the API reference for Python from the XML docs.
      Done 2026-11-22: 13 examples in src/python/examples (library, in the app, remote), each run by a test on both
      runtimes; a Python API section of the docs site, one page per type (806), generated from the stubs with links to
      the .NET pages
- [ ] P5 parked: the place of the examples and the docs build needing Python (the maintainer to confirm); 548 member
      groups with no .NET page to link (enum `__int__` and
      `__init__`, flags operators, delegates' `__call__`)
- [x] P6. Scripts in the trusted folders of L6 only, and a script's errors shown without closing the app. Done
      2026-11-22: PYRUN, PYLOAD and the palette go through the same gate; every kind of script error leaves the app
      running with one line on the command line (a .NET error too), the streams put back before each run
- [x] P6 parked: a list of Python startup scripts; recursion through .NET callbacks past about 650 levels overflows the
      stack. Settled by TF7 (2026-11-25): the Startup Suite holds tool folders; the recursion went with the embedding

Re-scoped 2026-11-23 by the maintainer: Python is to work as a C# plugin plus Remote, "like LISP", not as a console
inside the app; made to be used a lot, by people and by AI (plan and spec in .claude/scratch/pyl, D1600–D1642).

- [x] PW1. Loading as LISP: a Pythons folder beside the exe loaded at start, .py files in the Load/Unload Applications
      window (Kind Python, their commands), a Python Startup Suite, drag-drop of .py, PYLOAD as the .py picker; from
      outside, closing a drawing and reading text styles, dimension styles and blocks. Done 2026-11-23: an empty
      Pythons folder starts no engine (1 ms), one .py costs about 0.45 s at start; Close(discard) refuses unsaved or busy
      drawings; style and block reads as copies sorted by name
- [ ] PW1 parked: closed 2026-11-25: the dependent blocks of a loaded xref (`grid|TAG`) are carried by Remote
      `Blocks`, Python `drawing.blocks` and `-BLOCK ?` as AutoCAD 2021 lists them (held while loaded, never saved: AutoCAD's
      DXF reader refuses one; core 2cbd11c5 SetDependent). Left: an unloaded Python row shows no commands (Lisp rows keep theirs); no Global settings row for the Pythons
      folder or the Python Startup Suite; a .py asking a prompt at module level while APPLOAD runs; Remove's tooltip
      naming only Lisps and Plugins. TF7 (2026-11-25) settled the unloaded row's commands (from its plugin.json)
- [x] PW2. One API: `@command("NAME")`, `cmd(...)` and the get_* prompts; a Drawing API (entities, add, modify, erase,
      layers, tables, save, close) the same in the app, from outside (`connect()`, any open drawing) and headless
      (`open_file()`); parity with the C# plugin API (ribbon tabs, panels and buttons, side panel, settings, events).
      Done 2026-11-23: `@command(alias=, uses_selection=)`, `cmd` with PAUSE, `@on_unload`, get_entity/get_selection
      giving Entity objects; `add_ribbon_tab`, panels, buttons, split buttons, DRAWINGS button and menu, canvas menu
      and Properties from a .py, one host per file; Drawing, Entity, Layer, App, connect, open_file, new_file with
      commit() as one undo step; 25 shared scenarios held equal headless, outside and in the app
- [ ] PW2 parked: no Python side panel (it takes WPF content); style and block reads copy the whole drawing. The side
      panel is settled as never by TF-D16 (a tool shows no WPF content); the copies stay
- [x] PW3. Finish: the PYTHON console and PYTHONCLOSE removed (PYRUN kept); a docstring with an example for every public
      name of `geometryhelper.cad` (typed inline, so no .pyi), checked by a test; the guide, the changelog and the
      examples on the new API; acceptance: the C1 layer command written in Python run in the app and from outside, and
      a LISP routine and its Python port giving the same drawing. Done 2026-11-23: a dimension added or changed from
      Python is drawn by `CadDimensionPicture.Redraw`; the palette's 4 source files, 3 test files and 89 test methods are gone
      (the host's console entry stays, since the engine-error tests use it); DIMLAY in LISP and in Python give equal
      drawings, picture included (red without the redraw)
- [x] PW3 parked: closed 2026-11-25: `Entity.dxf_type` is the core's `DxfEntity.DxfName` (GeometryHelper ecd511ae);
      `save()` and `save_as()` inside a run or an `undo_group` are one undo step (`CadDocument.SavingInGroup`). Custom snaps and
      plugin settings closed by PW3 C1, P and C2 (2026-11-25: Settings... and the Plugin Settings dialog, tool snaps
      answered in the window). Kept as decided: `CadPythonHost.Enter`, `ResetConsole` and `CadPythonEntry` (gone with TF7); the remote examples take the raw `CadApplication` and wrap it in `App(...)`
      because `test_e0` pins that signature and `with CadApplication()`; `batch_folder.py` and `convert.py` stay on
      `DxfDatabase` (they need the version and the writer's warnings); no ribbon-button example file (`test_e0` lists
      the examples); the empty row 1 of the canvas grid stays

#### C# source plugins

Asked for by the maintainer on 2026-11-23: a command plugin given as its C# source, `.cs` files, loaded as a built
`.dll` is today, so that writing one needs no Visual Studio, no project and no build, as a `.lsp` or a `.py` needs none.
The app compiles the source when it loads it. AutoCAD 2021 has no such thing (NETLOAD takes assemblies only), so the
commands and prompts follow NETLOAD's and APPLOAD's, and the rest follows the `.py` files of PW1.

How a plugin loads today: `CadPluginManager` (PLUGINLOAD, alias NETLOAD; the Plugins folder at start; the Load/Unload
Applications window) copies the `.dll` files aside (shadow copy) and `CadPluginLoader` loads them, on .NET 8 each plugin
into a collectible `AssemblyLoadContext` of its own (`OpenCAD2D.Wpf.Plugins.CadPluginAssemblyLoader`), so PLUGINRELOAD
takes a new build while the app runs. A source plugin is compiled into an assembly in memory and then goes the same way:
the same `CadPlugin` API, the same load context, the same trusted folders.

| # | Decision | State |
|---|---|---|
| CS-D1 | The compiler is Roslyn (`Microsoft.CodeAnalysis.CSharp`, MIT), in the app only (`OpenCAD2D.Wpf`, net8.0-windows), not in `OpenCAD2D.Core`, so the NuGet API stays small. The source is compiled in memory to an assembly and its PDB (lines of errors and of exceptions point into the `.cs`). Before writing it: how CS-Script (MIT), dotnet-script (MIT) and Roslyn's own scripting API compile and cache, and what their ideas cost. | decided by the maintainer 2026-11-23 (the recommendation) |
| CS-D2 | One plugin is either one `.cs` file, or a folder whose `.cs` files are compiled together (`Plugins\Stairs\*.cs`), as a folder of `.dll` files is one plugin today. | decided by the maintainer 2026-11-23 (the recommendation) |
| CS-D3 | It references what a `.dll` plugin may use: the .NET 8 runtime the app runs on, `OpenCAD2D.Core`, `OpenCAD2D.Wpf`, `GeometryHelper`, WPF. Other `.dll` files beside the source are referenced with a `//#r "Name.dll"` line at the top, as dotnet-script writes it; no NuGet packages. C# `latest`, implicit usings off, nullable off, as the app's own code. | decided by the maintainer 2026-11-23 (the recommendation) |
| CS-D4 | A compiled build is cached by the SHA-256 of its sources and of the app's version in `%LocalAppData%\OpenCAD2D\PluginCache`, so a start with unchanged sources does not compile again. | decided by the maintainer 2026-11-23 (the recommendation) |
| CS-D5 | The same loading as a `.dll`: the Plugins folder at start; PLUGINLOAD/NETLOAD and APPLOAD take `.cs` files (file type "C# source (*.cs)"); the Load/Unload Applications window lists them with the Kind "C# source"; drag and drop of a `.cs` on the window; PLUGINRELOAD compiles again. The trusted folders and SECURELOAD of L6 decide whether a `.cs` loads, as for a `.dll`. | decided by the maintainer 2026-11-23 (the recommendation) |
| CS-D6 | Compiler errors: the plugin is not loaded, and the command line shows a line `Plugin <name> was not loaded: <n> errors`, then each error as `Stairs.cs(12,9): error CS0103: The name 'x' does not exist`, the first 20 at most; warnings are not shown. | decided by the maintainer 2026-11-23 (the recommendation) |
| CS-D7 | Not now: a file watcher that reloads a `.cs` when it is saved (PLUGINRELOAD does it by hand), and `.vb` sources. | decided by the maintainer 2026-11-23 (the recommendation) |

- [x] CS1. Research and the spec (CS-D1 to CS-D7 are decided, so the spec writes them down as they
      are unless the research shows one fails, which goes back to the maintainer): CS-Script, dotnet-script and Roslyn
      scripting read and compared (compile time of a 200-line plugin cold and warm, memory, how each references the
      host's assemblies and caches); the spec with numbered decisions in `.claude/scratch/cs/spec.md`.
      -> verify: the comparison table and the spec written; any decision the research changes asked of the maintainer.
      Done 2026-11-24: no decision changed. Measured on a 220-line plugin (.NET 8.0.28, Roslyn 4.14.0): cold 0.85-0.94 s,
      warm 15-34 ms; the runtime's assemblies memory-mapped (`AssemblyMetadata.CreateFromFile`) leave 51 MB of private
      bytes against 198 MB read into memory; Roslyn scripting fails on namespaces (CS7021) and never unloads; ref packs
      are on SDK machines only. Spec S1-S16 written
- [x] CS2. The compiler: a `CadPluginCompiler` in `OpenCAD2D.Wpf` that turns one or more `.cs` files into an assembly
      and its PDB in memory, with the references of CS-D3, `//#r` lines, and the cache of CS-D4.
      -> verify: tests that compile a one-file and a two-file plugin, a plugin with a `//#r` reference, and one with an
         error (its file, line, column and code as CS-D6 writes them); a second compile of unchanged sources is a cache
         hit (no compile, measured); the cache key changes when one source byte or the app's version changes.
      Done 2026-11-24: `CadPluginCompiler` + `CadPluginRoslyn` (net8.0-windows only), `CadPluginBuild` and
      `CadPluginLoader.CanCompile`/`Compile` in Core; 11 tests on net8 (the plugin tests now target net48 and
      net8.0-windows), shown red with the cache switched off; a cache hit takes under 1 ms and never loads
      Microsoft.CodeAnalysis (checked in a recording load context)
- [x] CS3. Loading: `CadPluginManager` takes `.cs` files and folders of them in every way it takes `.dll` files today
      (CS-D5), through the same load context; unload and PLUGINRELOAD compile again; SECURELOAD and the trusted folders.
      -> verify: tests that load the sample plugin `OpenCAD2D.Plugins.DrawingInfo` from its `.cs` sources and from its
         built `.dll` and get the same commands, ribbon tab and output; unload frees the context (a weak reference to
         the assembly is dead after a collection); a `.cs` in an untrusted folder asks as a `.dll` does; SECURELOAD 2
         refuses.
      Done 2026-11-24: 7 loading tests on net8 (red without the manager's change) and the refusal on net48; DrawingInfo
      from source and from .dll give the same command and the same six lines (it has no ribbon tab to compare). Left for
      the maintainer: SECURELOAD governs no .dll plugin today (L6/L9), so a .cs does not obey it either
- [x] CS4. The app: the Load/Unload Applications window and the file pickers (design first in the Design artifact for
      any new control or column), the error lines of CS-D6, drag and drop.
      -> verify: Wpf tests of the window's rows and of a dropped `.cs`; the screenshots of the window match the design.
      Done 2026-11-24: Kind `C# source` (#D2A8FF), a chip `C# source N` (`Plugins N` counts .dll plugins only), Load…,
      NETLOAD's picker and a drop take `.cs`, the red line `Stairs.cs did not compile: 2 errors. <first error>` with every
      error in its tooltip (spec S17). 5 new Wpf tests (a fake loader, as net48 compiles nothing), red without the change
      (7 failing with the 2 strings updated), green after; Wpf 2474, Plugins 96 + 18. Rendered at 900x700 offscreen, both
      states match the artboards but one: an unticked source row shows the commands it last had, where the csError
      artboard shows the Commands cell empty (kept as for a .dll row)
- [x] CS5. Docs and the sample: a section in `docs/presenter/plugins.md` (a plugin as source, its references, `//#r`,
      the cache, the errors), the sample plugin also shipped as source in `examples/`, the CHANGELOG, and the setup
      carrying Roslyn (its size before and after recorded).
      -> verify: the guide's example compiles and runs in a test; the setup built and the sample `.cs` loaded from the
         installed app's Plugins folder.
      Part A done 2026-11-24: the guide's example read from the guide and run in a test, the sample-as-source
      paragraph, the screenshot mode's builds kept in its own temporary folder (4f4a83cc).
      Done 2026-11-24: Part B built the setup at 3f059fb7 and at 4f4a83cc and installed a probe setup (same files, its
      own identity) into a scratch folder: with the installed exe, R1 the DrawingInfo sample as source (PNG identical
      to the .dll's), R2 the guide's Stairs.cs (`Steps: 18`), R3 the broken one (`Stairs.cs(12,13): error CS0103`,
      exit 2, as meant) and R4 a source using Linq, Text.Json, Xml.Linq and WPF; no CS0433 or CS1703. Setup 79,946,201
      -> 82,037,149 bytes (+2.09 MB, +2.6 %), publish 286 -> 288 files (+8.17 MB). Part B found that a screenshot run
      with --plugin rewrote the user's settings.json with defaults (in 1.0.0 too); fixed, a screenshot keeps its
      settings in `%TEMP%\OpenCAD2D\ScreenshotSettings\<pid>`, with a test shown red first.
      C1, decided by recommendation: the setup ships only the sample's .dll; its .cs is in `examples/` and the guide
      and Plugins\README.txt say how to load it in place of the .dll.

#### Tools as folders (planned 2026-11-24)

Asked for by the maintainer on 2026-11-24: a tool that needs libraries must be able to say so, and two Python tools
must be able to use two versions of one library, so Python leaves the app and every Python tool runs in a process and
venv of its own (TF-D14). Every tool of the
`Plugins` and `Pythons` folders becomes a folder of its own: a C# plugin a folder with a `.csproj` (its NuGet packages
in `<PackageReference>`), a Python tool a folder with a `requirements.txt`. The one-file forms (a `.cs` or a `.py`
directly in `Plugins` or `Pythons`) are dropped. A built `.dll` plugin, alone or as a folder (`dotnet publish`), stays as
it is. The same folder is what the Share website will carry.

```
Plugins\Stairs\                Pythons\StairsPy\
    Stairs.csproj  (required)      requirements.txt  (required, may be empty)
    StairsCommands.cs              __init__.py       (runs when the tool loads)
    Geometry\Steps.cs              steps.py
    plugin.json    (optional)      plugin.json       (optional)
```

| # | Decision | State |
|---|---|---|
| TF-D1 | A C# plugin is a folder with exactly one `.csproj`. The app does not build it with the .NET SDK (not on users' machines): it reads the `.csproj` and compiles with Roslyn as CS1-CS5 do. | agreed by the maintainer 2026-11-24 |
| TF-D2 | Read from the `.csproj`: `AssemblyName`, `Version`, `LangVersion`, `Nullable`, `ImplicitUsings`, `<PackageReference>`, `<Reference HintPath>`, `<Compile Remove>`. The sources are the SDK's default: every `*.cs` in the folder and below, but `bin\` and `obj\`; what Visual Studio builds, the app compiles. | agreed 2026-11-24 |
| TF-D3 | `//#r` lines are dropped: `<Reference>` and `<PackageReference>` of the `.csproj` take their place. | agreed 2026-11-24 |
| TF-D4 | A `<PackageReference>` to `OpenCAD2D.Core`, `OpenCAD2D.Wpf` or `GeometryHelper` is the app's own and is not downloaded. Other packages are restored from nuget.org into `%LocalAppData%\OpenCAD2D\NuGet` (net8.0, else netstandard2.0; dependencies and native assets included) and loaded into the plugin's own load context, so two plugins may use two versions of one library. This reverses CS-D3's "no NuGet packages". | agreed 2026-11-24 |
| TF-D5 | A Python tool is a folder with `requirements.txt` and `__init__.py`; a folder without `requirements.txt` stays a package the tools import, as today. | agreed 2026-11-24 |
| TF-D6 | Each Python tool has a venv of its own, `%LocalAppData%\OpenCAD2D\python\envs\<tool>`, made from the Python set in Global settings (else the one found) and holding the tool's `requirements.txt` and the `opencad2d` wheel; never the user's own Python environment. | agreed 2026-11-24 (changed the same day from one shared venv) |
| TF-D7 | No conflict check between tools is needed: each tool has its own venv, so numpy 1.26 for one and 2.1 for another both install and run. `pip` reports a tool whose own requirements cannot be met, before anything is changed. | agreed 2026-11-24 (changed the same day) |
| TF-D8 | Nothing is downloaded or installed without asking: the packages and versions are listed and the user says yes, asked again when the list changes. Offline: a clear error and the command to run by hand. | agreed 2026-11-24 |
| TF-D9 | A lower bound is installed as that version: `numpy>=1.26` installs `numpy==1.26` (fast, and the same on every computer); if that version cannot be installed for the Python in use (no wheel for it), the line is installed as written and pip picks. No warning either way. NuGet: a `Version="13.0.3"` is already the lowest applicable (S13). Other forms (`~=`, `<`, `*`) go to pip as written. | decided by the maintainer 2026-11-24 (changed the same day from "a range is taken with a warning") |
| TF-D10 | PLUGINLOAD/NETLOAD/APPLOAD/PYLOAD take a tool's folder, or its `.csproj` / `requirements.txt`; a lone `.cs` is refused with a line saying how to make it a folder. PYRUN (run a script once, as SCRIPT) still takes a lone `.py`. | agreed 2026-11-24 |
| TF-D11 | The build cache's key (CS-D4) also takes the `.csproj` and the resolved package list; an unchanged tool neither compiles nor installs again. | agreed 2026-11-24 |
| TF-D12 | The samples take the new shape: `Plugins\Stairs.cs` becomes `Plugins\Stairs\` with `Stairs.csproj`, `Pythons\GH-DrawingInfo.py` becomes `Pythons\GH-DrawingInfo\`; `examples\` holds them so that Visual Studio opens and builds them. | agreed 2026-11-24 |
| TF-D13 | **BREAKING** in the CHANGELOG: a one-file tool in `Plugins` or `Pythons` no longer loads; the log says why and the guide says how to convert it. | agreed 2026-11-24 |
| TF-D14 | Python no longer runs inside the app: every Python tool runs in a `python.exe` process of its own, from its venv, and drives the app through the Remote channel (`connect()` of the `opencad2d` wheel). A process starts at the tool's first command (0.2 to 0.3 s, the runtime and the connection, measured in `docs/presenter/remote.md`) and stays until the tool is unloaded or the app closes. One process cannot hold two versions of numpy, and numpy refuses sub-interpreters, so this is how two tools with two versions both run. | decided by the maintainer 2026-11-24 |
| TF-D15 | The in-app Python is retired: the pythonnet embedding of `OpenCAD2D.Python` and the in-app `opencad2d.cad` (a script inside the app) go; the `opencad2d` wheel stays (a tool uses it in its process, also to read and write DXF there). PYRUN runs a lone `.py` in a process of the default venv; the PYTHON console palette talks to a Python process of its own. | recommended 2026-11-24 |
| TF-D16 | What a Python tool adds to the window is declared in its `plugin.json` (commands, aliases, ribbon buttons, icons); the app shows them and starts the tool when one is used. A Python tool shows no WPF content of its own in the app (PW2's side panel stays out). | recommended 2026-11-24 |
| TF-D17 | A command run of an out-of-process tool is one undo step and Esc stops it, as in the app today: the Remote channel gains a command scope (begin, end, cancel) that the run holds. A tool process that crashes or hangs never takes the app down; the app can stop it, and the run's changes are taken back. | recommended 2026-11-24 |
| TF-D18 | The cost is accepted: 0.2 to 0.3 s at a tool's first command, and a round trip per call (about 17 ms for SendCommand, 0.15 s for 1 000 inserts queued and committed); a tool that makes many calls batches them (the array API). | accepted by the maintainer 2026-11-24 |
| TF-D19 | The samples (maintainer 2026-11-24): `Plugins\DrawingInfo\` ships as source (`DrawingInfo.csproj` + `DrawingInfoCommands.cs`), the built `.dll` dropped; `Plugins\HelloWorld\` new (`HELLOWORLD` writes `Hello, World!`); `Plugins\Stairs\` stays. `Pythons\GH-DrawingInfo.py` becomes the folder `Pythons\DrawingInfo\`; `Pythons\HelloWorld\` new (`PYHELLOWORLD`). The commands of the existing samples keep their names (GHDWGINFO, GHPYINFO). `examples\` holds the same folders. | decided 2026-11-24 (names by recommendation) |

- [x] TF1. Research and the spec: how Rhino 8 (`# r:`, `#r "nuget:"`), pyRevit, Blender extensions and dotnet-script
      get a script's libraries; the NuGet client libraries (Apache-2.0) for a restore without the SDK; `pip --dry-run
      --report` for conflicts; the way of TF-D14 measured; the spec with numbered decisions in `.claude/scratch/tf/spec.md`.
      -> verify: the comparison and the spec written; TF-D14 settled with numbers.
      Done 2026-11-24: spec S1-S44 in `.claude/scratch/tf/spec.md`; a tool process starts and connects in 0.295-0.314 s
      (0.36-0.37 s with numpy), 66 MB kept; two tools with numpy 2.3.5 and 2.4.4 ran side by side; NuGet.Protocol 7.9.0
      resolves as `dotnet restore` does (3.68 MB); Q1-Q8 taken as recommended
- [x] TF2. C# plugins as folders with a `.csproj` (TF-D1 to D3, D10 to D13 for C#): the `.csproj` read, sources below
      the folder, `<Reference HintPath>`, the lone `.cs` and `//#r` dropped, the Stairs sample moved.
      -> verify: a sample that Visual Studio builds runs in the app with the same commands; a lone `.cs` is refused with
         its line; the cache key changes with the `.csproj`.
      Done 2026-11-24: CadPluginProject reads the .csproj (S1-S11); the lone .cs and `//#r` refused; examples/Stairs
      builds with `dotnet build` and runs in the app (`Steps: 18`); Plugins 59 (net8) + 100 (net48), Wpf 2512, pytest 697 x2
- [x] TF3. NuGet (TF-D4, D8, D9): restore, dependencies, framework choice, native assets, the cache, asking first.
      -> verify: tests against a local package source in a temporary folder (no network); two plugins with two versions
         of one package both run; the host's own packages are never downloaded.
      Done 2026-11-24: NuGet.Protocol 7.9.0, restore as `dotnet restore` (NU1107/NU1605...), natives by full path (two of one
      name load side by side), the list kept per .csproj; the question is a hook the window sets in TF6 (until then a plugin
      with packages is refused with its line); Plugins 84 (net8) + 100; setup +3.6 MB in 9 files. Not yet checked against
      the live nuget.org (tests never use the network): checked by hand in TF6
- [x] TF4. Python tools as folders, out of process (TF-D5, D10, D12 to D17): the folder found, the tool's process
      started at its first command and kept, its commands from `plugin.json`, one undo step and Esc through the Remote
      command scope, a crashed or hung tool stopped without the app; the lone `.py` in `Pythons` dropped, the
      GH-DrawingInfo sample moved.
      -> verify: the sample runs from its folder with the same command and output; Esc and undo of a run; a tool that
         raises, hangs or exits leaves the app working; the first command's time measured.
      Part TF4a done 2026-11-24: the run scope in Core and the .NET client (a waiting prompt, `ask`/`write`/`end`/`fail`,
      a run's commit in its own undo step, Esc takes it back; the tools pipe `OpenCAD2D.<pid>.tools` with a token);
      Remote 140, Core 13896, Wpf 2511, pytest 697 x2. Decided: `cmd()` inside a run nests the command as AutoLISP's
      `(command ...)` does, with no "Resuming" line (TF4b)
      Part TF4b done 2026-11-24: a tool's python.exe started suspended into a job (killed with the app), control pipes,
      stdout/stderr, Esc then 5 s stop, a crash or hang leaves the app working; `cmd()` nests in the run (one undo step);
      first command 0.37-0.39 s, next 16 ms; Remote 157, Wpf 2527, pytest 710. The window starts no tool yet (TF7). Closed 2026-11-25: the
      window starts tools since TF7 (plugin.json commands, the lone .py refused, the sample moved by TF9)
- [x] TF5. A venv per tool and pip (TF-D6 to D9): the venv made, `requirements.txt` and the wheel installed after a
      yes, errors that say what to do.
      -> verify: tests with a local package index (no network); two tools with two versions of one package both run.
      Done 2026-11-24: PythonEnvironments (a venv per tool --without-pip + a .pth to the app's package and a shared base made
      once per Python from the shipped wheels, 1.3 MB), the dry-run list asked before installing, a lower bound pinned with
      a fallback, the old venv kept when an install fails; venv 75-98 ms, base 1.7 s once, one package 2.1-2.2 s; Wpf 2575.
      Wired into the app in TF7; live PyPI checked by hand in TF6
- [x] TF6. The app (design first in the Design artifact): the question before installing, "Load folder…" in
      Load/Unload Applications, a row's state (needs installing, conflict, installing).
      -> verify: Wpf tests of the question and the rows; the screenshots match the design.
      TF6a done 2026-11-24: the package question window (PackageQuestionWindow, the NuGet texts; the pip variant is the same
      window), wired to ConfirmDownload in the app; Load folder…, the *.csproj file type, the "needs packages" and "install
      failed" badges for C# plugins; Wpf 2549. Left for TF6b: the pip question, the Python rows, "installing…" (an install
      off the UI thread). Parked: the command-line form of the question (FILEDIA 0) needs a two-phase ConfirmDownload in Core
      TF6b done 2026-11-25: the pip question in the same window (Package, Version, Asked as; FILEDIA 0 keeps the command
      line form), Python rows with "needs packages", "installing…" and "install failed" (pip off the window's thread), a
      tool folder dropped on the table loads; screenshot mode runs a Python tool folder (venvs in the run's folder, pip
      answered No). Wpf 2623, Plugins 100 + 92. Parked: a No leaves the row ticked (its next command asks again); no live
      pip progress. The NuGet FILEDIA 0 form done 2026-11-25: PLUGINLOAD/PLUGINRELOAD list the packages and ask
      "Download these packages? [Yes/No] <No>:" (a two-phase load in Core); Wpf 2633, Plugins 100 + 99
- [x] TF7. The in-app Python retired (TF-D15): the pythonnet embedding and the in-app `opencad2d.cad` removed, PYRUN and
      the PYTHON console on a Python process, the tests and stubs that pinned the in-app Python moved or removed, the
      parked P/PW items it settles closed.
      -> verify: the app starts with no Python loaded; PYRUN and the console work; the Python suites green on what stays.
      Done 2026-11-25: src/OpenCAD2D.Python and its tests gone (no Python.Runtime in the app); a tool folder's plugin.json
      gives its commands and ribbon with no process; the first command readies its venv off the window's thread; PYRUN in
      a process of the default venv (0.65-0.94 s); a lone .py refused with its line; the pip question on the command line
      until TF6b; a failed requirements change runs on the old venv; a remote `extents` read. Core 13896, Wpf 2607,
      Plugins 100 + 90, Remote 159, pytest 656/643. The console clause was dropped (S39: removed in PW3)
- [x] TF9. The samples (TF-D19): the C# ones after TF3 (DrawingInfo as source, HelloWorld; the tests and the Net8Runner
      that loaded the built DrawingInfo .dll moved to the folder), the Python ones after TF4b and TF7 (DrawingInfo and
      HelloWorld folders).
      -> verify: each sample loads from the app's folders with its command and output; the first start compiles the
         three C# samples once (time measured), the next takes them from the cache.
      C# half done 2026-11-24: Plugins\DrawingInfo\ as source (the .dll dropped), Plugins\HelloWorld\ new; the first start
      compiles the three in 1.09 to 1.34 s, a start from the cache 61 to 82 ms. Python half done 2026-11-25:
      Pythons\DrawingInfo\ (GHPYINFO, the same lines) and Pythons\HelloWorld\ (PYHELLOWORLD), in examples\Pythons\
- [x] TF8. Docs, the CHANGELOG (BREAKING), the setup (its size with the NuGet client and without the embedding, before
      and after).
      -> verify: the guides' examples run in tests; the setup built and both samples load from the installed app.
      Done 2026-11-25: the guides and the CHANGELOG made one consistent set (BREAKING, NEW, CHANGED, FIXED); the setup
      83,726,648 bytes (79.8 MB; +1.61 MB on CS5: NuGet client +3.06 MB, wheels +1.27 MB, the embedding -0.57 MB), with
      THIRD-PARTY-NOTICES.md now installed; a probe install compiled and ran the three C# samples, the maintainer's install
      unchanged. Left: screenshot mode cannot run a Python tool (the installed Python files were checked by import)

#### The website and Share (planned 2026-11-23)

The opencad2d.com website is designed, not built: the Design artifact https://claude.ai/artifact/HzsdochEhENoSD3nzTRxMJ,
page "Website (C, Atelier)" (Home, Share, a tool's page, Upload a tool, Download, Share admin, and the app's Download
from Share window). Decided by the maintainer: the tool library is called Share; uploading needs sign-in with Google and
a reviewer's approval before a tool is visible; Share shows tools large, small or as a list; an admin page reviews
uploads and edits users' roles and the rules of each role. The uploader gives no version, command or licence: the
commands are read from the file, Share numbers each approved version (1.0, 1.1, ...), every tool is MIT, Share takes source only (.lsp, .py, .cs or a .zip of them; no .dll or .fas, which a reviewer cannot read), and Share
records each file's SHA-256 (shown on the tool's page and in its versions; an upload equal to the live one is turned
back; the app compares it with the copy it has).

- [ ] W1. Choose the stack: hosting, the database of tools and users, file storage, Google sign-in, the malware scan.
- [ ] W2. Build the site from the design.
- [ ] W3. The app: a Download from Share window (design in the artifact, page "Website", board "App: Download from
      Share"). Paste a tool's link from its page, see its picture and details, download it into a Plugins or Lisps
      folder and load it; it waits on W2 for the links to resolve. The app asks the server only when the user presses
      Find or Download: no update checks, no background calls.

### Release

- [ ] Review by the maintainer of everything above
- [x] Package `OpenCAD2D.Core` on NuGet: 1.0.0 published 2026-11-23 with `OpenCAD2D.Remote` (see the NuGet line above)
