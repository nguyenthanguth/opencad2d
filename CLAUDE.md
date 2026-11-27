# CLAUDE.md

Guidance for Claude Code, and any other coding agent, working in this repository. Part one is a set of working habits
adapted from [andrej-karpathy-skills](https://github.com/multica-ai/andrej-karpathy-skills); part two is what is
particular to OpenCAD2D. Where the two differ, part two wins.

The habits lean towards care over speed. For a trivial change, use judgement.

## Part one: how to work

### 1. Think before coding

Don't assume, don't hide confusion, and say what the trade-offs are.

- State what a change rests on: the units, the tolerance, whether a face is flat, whether a body is closed. Where one is
  uncertain, check it in the code or ask.
- When a request can be read two ways, set out both rather than picking one quietly. "Make this body closed" can mean
  welding a crack or filling a hole, and the two change the volume very differently.
- If there is a simpler way, say so, and push back where it is warranted.
- When something is unclear, stop, name what is unclear, and ask.
- For a feature you are not familiar with, don't write it from nothing: search GitHub for open-source projects and
  compare their ideas before you start.

### 2. Simplicity first

The least code that solves the problem, and nothing speculative.

- No features, options, overloads or abstractions beyond what was asked for, and no configurability nobody requested.
- No handling for states that cannot happen. Near-degenerate geometry is not one of them: slivers, near-coincident
  faces and faces a hair off flat come out of real models every day, and coping with them is this library's job.
- If 200 lines could be 50, rewrite them. If a senior engineer would call it overcomplicated, simplify it.

### 3. Surgical changes

Touch only what you must, and clean up only your own mess.

- Don't "improve" the code, comments or formatting next to your change, and don't refactor what isn't broken.
- Match the style around you, even where you would write it differently.
- Mention dead code you notice; don't delete it unasked. Remove only what your own change left unused.
- Every changed line should trace back to the request.

### 4. Goal-driven execution

Say what success is, then loop until it is verified.

- "Fix the bug" means a test that fails without the fix, then the fix that makes it pass.
- "Make it faster" means timings before and after on the same inputs, and the results shown unchanged.
- "Make it robust" means a test or a fuzz run over the inputs that broke it.
- For work in several steps, write the plan with a check for each: `1. [step] -> verify: [check]`.

These habits are working when diffs carry fewer needless changes, fewer rewrites come from over-building, and
clarifying questions come before the code rather than after the mistake.

## Part two: this repository

### What it is

OpenCAD2D is a free 2D CAD app for Windows that works like AutoCAD 2021: DXF and DWG files, the 2D draw and modify
commands, annotation, layouts and plotting, AutoLISP, Python, C# plugins, and Remote (driving the app from another
program). It came out of the GeometryHelper repository in November 2026, with its history.

### Layout

| Path | What it holds |
|---|---|
| `src/OpenCAD2D.Core` | the engine (`OpenCAD2D.Core.dll`, namespace `OpenCAD2D`), netstandard2.0, no UI: documents, commands, display lists, LISP, plugins, Remote server |
| `src/OpenCAD2D.Wpf` | the WPF presenter: canvas, ribbon, palettes, dialogs, the app host |
| `src/OpenCAD2D.App` | the app, net8.0-windows (`OpenCAD2D.exe`, shown as OpenCAD2D) |
| `src/OpenCAD2D.Rendering` | the drawing with no window (netstandard2.0, SkiaSharp): canvas renderer, PDF and raster writers, `CadRender` |
| `src/OpenCAD2D.Mcp` | the MCP server `OpenCAD2D.Mcp.exe` (net8.0 console), installed beside the app |
| `src/OpenCAD2D.Remote` | the client library other programs drive a running app with |
| `src/python` | the Python package `opencad2d` (wheel), its examples |
| `tests/` | the xUnit suites (net48; Plugins also on net8.0-windows), `tests/python` (pytest), `tests/Shared` |
| `tools/OpenCAD2D.PyStubs`, `tools/python` | the Python stub generator and the wheel, stub and pytest scripts |
| `examples/` | the sample plugins as source folders (`DrawingInfo`, `DrawRandom`, `HelloWorld`), shipped in `Plugins`, and the sample Python tools in `examples/Pythons`, shipped in `Pythons` |
| `docs/` | the guides (`docs/presenter`, `docs/python.md`) |
| `Lib/` | the core library, `GeometryHelper.dll` and its XML documentation, with `VERSION.txt` |
| `PROGRESS.md` | the work list; read it first |

### The core library, GeometryHelper

Geometry, DXF and DWG live in GeometryHelper (https://github.com/nguyenthanguth/GeometryHelper, cloned beside this
repository at `D:\GitHub\GeometryHelper`). OpenCAD2D uses it as a binary, `Lib\GeometryHelper.dll`, referenced through
`$(GeometryHelperLib)` (`Directory.Build.props`).

- A change to the core is made, tested and committed in GeometryHelper, then brought here with
  `pwsh tools\update-lib.ps1`, which builds it in Release, copies the DLL and its XML and writes `Lib\VERSION.txt`.
  Commit `Lib` when a batch of core work is done, not for every trial copy.
- The core gives OpenCAD2D its internals (`InternalsVisibleTo("OpenCAD2D.Core")` in GeometryHelper's AssemblyInfo): the
  MTEXT layout measures with the core's fonts.
- Never point a project here at GeometryHelper's source.

### Build and verify

After every change, build from scratch and run the suites of what changed:

```bash
dotnet build OpenCAD2D.slnx -c Release -warnaserror --no-incremental
dotnet test tests/OpenCAD2D.UnitTest/OpenCAD2D.UnitTest.csproj -c Release --no-build
dotnet test tests/OpenCAD2D.Wpf.UnitTest/OpenCAD2D.Wpf.UnitTest.csproj -c Release --no-build
dotnet test tests/OpenCAD2D.Plugins.UnitTest/OpenCAD2D.Plugins.UnitTest.csproj -c Release --no-build
dotnet test tests/OpenCAD2D.Remote.UnitTest/OpenCAD2D.Remote.UnitTest.csproj -c Release --no-build
dotnet test tests/OpenCAD2D.PyStubs.UnitTest/OpenCAD2D.PyStubs.UnitTest.csproj -c Release --no-build
dotnet test tests/OpenCAD2D.Mcp.UnitTest/OpenCAD2D.Mcp.UnitTest.csproj -c Release --no-build
bash tools/python/verify-python.sh
dotnet csharpier check .
```

- `--no-incremental` is not optional: analyzers run only when the compiler runs.
- Timing tests sit in the `ViewerSpeedCollection` (`[Collection(ViewerSpeedCollection.Name)]`), which runs alone after the
  rest; a new timing test goes there too. If one still fails, rerun it alone before calling it a failure.
- Report what happened. A suite skipped or a test failing is said plainly, with the output.
- Tests never show a real window or dialog, and never write the user's settings (`%AppData%\OpenCAD2D`).

### Tests

- A fix comes with a test shown to fail without it.
- Pass a `Tolerance` explicitly; never set `Tolerance.Global`.
- The xUnit analyzers run under `-warnaserror`: `Assert.Contains(items, p)`, `Assert.Equal(a, b)`, `Assert.Empty(list)`.
- Name a test the way its file does; newer files name each one as a sentence.
- Drawings from the maintainer's real projects never go into the repository. Build the case in code.

### Code and docs

- Units are millimetres. Coordinates are compared through a `Tolerance`, never with `==`.
- LF line endings and UTF-8 without a BOM everywhere. C# and XML are formatted by CSharpier (width 120).
- Every public member carries XML documentation (CS1591 fails the build).
- Prose in XML docs, guides and the changelog is plain English with measured examples.
- A change to public behaviour updates its guide in `docs/` and `CHANGELOG.md` (`## Unreleased`, marked **NEW.**,
  **FIXED.**, **CHANGED.** or **BREAKING.**).
- New code gives every sort a full tie-break and lets no result follow the order of a `Dictionary` or `HashSet`.
- The app works like AutoCAD 2021: commands, prompts, options, dialogs. When unsure, check AutoCAD.
- A new UI is designed in the Design artifact first, then built from the design.

### Git and releases

- Commit only once the verify is green. Commit messages: a summary line, then what changed and why with the tests and
  numbers behind it, ending with the `Co-Authored-By:` trailer.
- Tags, releases and packages (PyPI, NuGet) need the maintainer's word.

### Working with the maintainer

- Reply in Vietnamese. Code, comments, docs and commit messages stay in English.
- When several agents share a task, only one builds in the repository at a time, since they share `bin` and `obj`.

## MCP Tools: code-review-graph

**This project has a knowledge graph. Start with the code-review-graph
MCP tools to narrow scope, then read the source.** The graph is cheaper than scanning files and
gives you structural context (callers, dependents, test coverage) that file search cannot.

### When to use graph tools FIRST

- **Exploring code**: `semantic_search_nodes_tool` or `query_graph_tool` instead of Grep
- **Understanding impact**: `get_impact_radius_tool` instead of manually tracing imports
- **Code review**: `detect_changes_tool` + `get_review_context_tool` instead of reading entire files
- **Finding relationships**: `query_graph_tool` with callers_of/callees_of/imports_of/tests_for
- **Architecture questions**: `get_architecture_overview_tool` + `list_communities_tool`

### Verify in the source

- Narrow scope with the graph, then read the source. Do not change code from graph output alone.
- For any non-trivial change, read the implementation and the relevant tests before concluding.
- Verify the exact source when touching behavior, database logic, migrations, retries, fallbacks,
  recovery, or compatibility code.
- When the graph and the source disagree, the source wins. The graph may be stale or may not
  model that relationship.
- An empty graph result can mean "not indexed" or "not statically visible", not "does not exist".

### Key Tools

| Tool | Use when |
| ------ | ---------- |
| `detect_changes_tool` | Reviewing code changes — gives risk-scored analysis |
| `get_review_context_tool` | Need source snippets for review — token-efficient |
| `get_impact_radius_tool` | Understanding blast radius of a change |
| `get_affected_flows_tool` | Finding which execution paths are impacted |
| `query_graph_tool` | Tracing callers, callees, imports, tests, dependencies |
| `semantic_search_nodes_tool` | Finding functions/classes by name or keyword |
| `get_architecture_overview_tool` | Understanding high-level codebase structure |
| `refactor_tool` | Planning renames, finding dead code |

### Workflow

1. The graph auto-updates on file changes (via hooks).
2. Use `detect_changes_tool` for code review.
3. Use `get_affected_flows_tool` to understand impact.
4. Use `query_graph_tool` pattern="tests_for" to check coverage.
<!-- /code-review-graph MCP tools -->


### The graph in this repository

- It indexes this repository only. GeometryHelper's types (`GeometryHelper.Dxf`, `.Geometry`) come from
  `Lib\GeometryHelper.dll` and are not in it: read them in `D:\GitHub\GeometryHelper` or its own graph.
- Callers are a floor, not a count: confirm with `Grep` before saying a public member is unused.
- Worktrees under `.claude/worktrees/` are not indexed; an agent in one reads the graph of `main`.
- Search is by name; no embeddings are built.
