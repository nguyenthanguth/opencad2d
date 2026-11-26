# Progress

The work list of OpenCAD2D: the 2D CAD app that works like AutoCAD 2021, with AutoLISP, Python, plugins and Remote,
split out of the GeometryHelper repository (`D:\GitHub\GeometryHelper`) in November 2026.

- `[x]` done; `[ ]` to do; `(running)` an agent is on it now.
- The maintainer adds, removes or reorders items; the lead takes the next open item from the top, ticks it when it is
  merged, and writes here what it leaves behind.
- If work stops (a usage limit, a crash), the next session reads this file first and carries on from the first open
  item. Every step says how it is checked.
- Finished work is summarised under "Done" and then dropped. The full list, with every step, decision and number, is in
  the history: `git show cf1018c6:PROGRESS.md` is the last long version (trimmed 2026-11-26), GeometryHelper
  `ddf1ff27` the one before the split.

## Done

- **S. The split from GeometryHelper** (2026-11-23): the CAD app, its libraries, tests and docs moved here with their
  history; the core used as `Lib\GeometryHelper.dll` (`tools\update-lib.ps1`); names `OpenCAD2D`, `opencad2d`; settings
  in `%AppData%\OpenCAD2D`; run copy `D:\OpenCAD2D-run`; the app icon (concept E3). S12, the Inno Setup installer with
  .NET and Python included, done 2026-11-26.
- **Asked for since the split** (2026-11-23 to 2026-11-26): ribbon and palettes polish, multi-functional grips, the
  dimension, multileader and table style previews, Applications window, Edit Reference panel, A12/A13 and the rest.
- **Parked follow-ups** (2026-11-26, parked P1-P9 and core C1-C3): dialog polish; ribbon at 125/150 %; -PLOT printers
  and PDF glyph subsetting (554 KB -> 10 KB); 3D viewport views, typed snaps and OTRACK through a viewport,
  trans-spatial DIMASSOC, DIMREGEN; MLINE mitred cuts (57 -> 1 of 700), MTEXT spacing codes, masks and manual columns;
  one number reader in the core, tables in R2000-R2007, the R2013 AuxHeader.
- **Part C, scripting**: AutoLISP L1-L9 (2026-11-23); Python PW1-PW4 (2026-11-26, the last 7787d302); C# source
  plugins CS1-CS5 (2026-11-24); tools as folders TF1-TF8 (2026-11-25).
- **Release**: OpenCAD2D 2.0.0 (2026-11-26) with `OpenCAD2D-Setup-2.0.0.exe`, the `opencad2d` wheel on PyPI and
  `OpenCAD2D.Core`/`OpenCAD2D.Remote` on NuGet.

### Parked with numbers (no rule found; taken up only if the maintainer meets one as a real fault)

- MLEDIT: CJ pairs 148 of 2000 differ from AutoCAD; oblique tees 185 of 1000; tw33 6 of 432.
- MTEXT: flow direction 3; the column frame outline; wrap at exactly the column width.
- DWG/DXF: spline normals off XY (best rule 88-92 %); R12 gradient meshes (AutoCAD's adaptive mesher).
- Tables: the "going up" flow (ContentUp). Core writer: BackgroundFill or Height set after the columns.

## Left to the maintainer (checks that need a real desktop or the AutoCAD GUI)

- [ ] The AutoCAD GUI checks: LAYERP with U and the Layer manager; the guessed prompts of SPELL, ATTIPEDIT,
      MLEADERALIGN Spacing/Distribute; DIMBREAK Auto breaks; filter.nfl; Ctrl cycling of grip modes; the Properties kind
      list order and its reset to All. Xrefs, REFEDIT, managers and hatch dialogs: GUI-only checks and small wording
      differences.
- [ ] The installer on a real desktop: the icon in the Start menu, on the setup and on a `.dxf` in Explorer;
      SmartScreen's warning on the unsigned setup; an upgrade over a running app as a standard user.
- [ ] A look at the ribbon on a real 125 % and 150 % desktop; PLOT to a real paper printer and the Windows print path.
- [ ] The run copy: a shortcut to `D:\OpenCAD2D-run\OpenCAD2D.exe`, `OpenCAD2D.exe --register` once for the
      `.dxf`/`.dwg` association, deleting `D:\GeometryHelper-run` when no longer wanted.

## Python

- [x] PW5. The maintainer's Python review (.reports/python-opencad2d.md, 2026-11-26), on v2.0.0. DONE 2026-11-26,
      merged a7b08dd1 (branch d763efbd, e60467df):
      1. FIXED: drawing.add() of a DxfDBText/DxfMText/DxfDimension using a custom text or dimension style failed at
         commit() ("The text style X is not in the file's text style table"): the batch database lacked the styles.
         -> verify: a test per kind (text, mtext, dimension with a custom style; a linetype) red then green, one undo
         step kept, the same on every backend (local, in the app, remote).
      2. Drawing API: text_styles.add, current_text_style; dimension_styles.add, current_dim_style; linetypes.load
         from the shipped Linetypes; add_text/add_mtext(text_style=); add_linear_dim, add_aligned_dim, add_radial_dim,
         add_diameter_dim, add_angular_dim; add_hatch from the shipped Hatches; blocks.add and add_insert. Every table
         (layers, text_styles, dimension_styles, linetypes, blocks) is used the same way: an index or a name.
         -> verify: tests on every backend, docstrings with an example each (test_g1), stubs.
      3. Docs: using opencad2d.cad with GeometryHelper.Dxf for styles, dimensions, hatches, blocks and linetypes, and
         keeping direct .NET edits inside one undo step -> verify: the examples in the guide run as tests.

- [x] PW6. Python for AI drawing layout (.reports/python-ai.md, the maintainer 2026-11-26; after PW5): opencad2d as the
      core of an AI that lays out drawings (generate -> render -> evaluate -> refine; datasets; reward functions).
      Built in C# in the core (and a netstandard rendering assembly); Python is the thin wrapper it is today, giving
      bulk results as NumPy arrays through opencad2d.arrays (decided 2026-11-26).
      1. Headless render from Python: drawing.render_png(path, box=, dpi=), render_svg, render_pdf, no window. Spec and
         rulings in `.claude/scratch/pw6/render-spec.md`, in four parts:
         P1 (done, 2ce58ef5) the `OpenCAD2D.Rendering` netstandard2.0 assembly, the nine Skia/PDF files moved, types forwarded
         from `OpenCAD2D.Wpf`; P2 (done, 5ee2b298) the `CadRender` facade (PNG, SVG, PDF) and `--screenshot --canvas`;
         P3 and P4 (done, c32811ac) Python and the guide.
         -> verify: the same pixels as the app's --screenshot for 10 drawings; SVG and PDF read back; time per page.
      2. DONE 2026-11-26 (d7dc6b09). Spatial queries: entity.bounds as drawn (SHX/TTF words, dimension and block pictures, MTEXT columns), and
         overlaps (text or dimension over lines, over each other) with a spatial index, fast enough for a reward
         function. -> verify: bounds equal the display list's within 1E-6; an overlap test of 100 000 objects timed.
      3. DONE 2026-11-26 (3cdbe6bd). Preparing real DWG data: walk nested blocks with their transforms (flattened entities in world
         coordinates), normalise units (INSUNITS -> mm), export the drawing as JSON and as a graph (nodes = entities,
         edges = touching/containing/dimensioning) for GNN, Transformer or LLM input. -> verify: JSON schema tests,
         round trip of a block tree, the graph of a sample drawing.
      4. DONE 2026-11-26 (380d4a73). Docs and examples for batch and AI pipelines (opencad2d.cad +
         GeometryHelper.Dxf + opencad2d.arrays over a
         folder of DWG), each example run by a test.

- [ ] PW7. More for AI drawing layout (proposed by the lead 2026-11-26, the maintainer: "kế hoạch em đưa ra tốt đó";
      after PW6; heavy work in C#, Python wraps it):
      1. DONE 2026-11-26 (8e049b3e). Measure before placing: measure_text, measure_dimension, measure_leader (the box a thing would take, not
         added), and free_space(box) (the empty regions of a frame). -> verify: equal to the box once added.
      2. DONE 2026-11-26 (a71a7c43). A standards checker as reward terms: words on paper at the plotted scale, minimum gaps between words and
         lines, dimension chains in line, layers/linetypes/styles per a company standard file; a score per rule and
         the violations with their places. -> verify: a rule test each; a drawing scored before/after a fix.
      3. DONE 2026-11-26 (97b765a3). Fast snapshot and reset of a drawing in memory (thousands of RL episodes without reading the file again).
         -> verify: reset time for a 10 000-entity drawing; state equal after reset.
      4. DONE 2026-11-26 (2a0b634f). Labelled renders: the world-to-pixel matrix, class masks (words, dimensions, lines, hatches), COCO boxes.
         -> verify: a box drawn back from the matrix covers the entity's pixels.
      5. DONE 2026-11-26 (fabc8d7c, 6961615e): 33 tools, file and attach modes, an attached render of the app's
         drawing as it is (the Remote database request). An MCP server so an LLM agent drives OpenCAD2D (open, read, place, render, check), in C# (decided
         2026-11-26): a project of its own, `src/OpenCAD2D.Mcp`, a net8.0 console exe `OpenCAD2D.Mcp.exe` installed
         beside the app (the SDK's packages stay out of Core, its NuGet and the wheel; no WPF loaded, no window);
         headless on a file, or `--attach` to a running app through Remote; the official .NET MCP SDK; stdio only,
         `--read-only`, no free LISP/Python unless a flag allows it; every installed copy has it without Python.
         -> verify: `tests/OpenCAD2D.Mcp.UnitTest`, a scripted MCP client session in both modes.
      6. Compare two drawings (added, removed, moved). 7. Sheets from code (layout, title block, viewport at scale,
         PDF). 8. Parallel workers with deterministic results (seeded). 9. A synthetic drawing generator for data.
      Most valuable: 1, 2 and 5.
      10. MCP phase 2 (the maintainer 2026-11-26: "MCP còn cần thêm tool nào nữa"): the tools an AutoCAD-like MCP
         server is expected to have, from five public CAD MCP servers compared (Autodesk's AutoCAD MCP, autocad-mcp,
         best-cad-mcp, CAD-MCP on ezdxf, FreeCAD MCP): read and query (get_entity, count/aggregate, layouts, sysvars,
         texts, dimensions, blocks, measure, select), create (rectangle, polygon, ellipse, spline, mleader, table,
         define_block, styles), modify (copy, mirror, offset, arrays, explode, join, trim, extend, fillet, chamfer,
         stretch, edit_text, draw order, match properties, redo), layers and layouts (viewports at a scale), output
         (plot_pdf, save_as, purge, audit, zoom), run_plan (many steps validated, dry run, one undo step) and debug
         (app_screenshot, command_history, last_errors, dump_drawing, audit, compare_drawings, timings). Spec in
         `.claude/scratch/pw7/mcp-phase2-spec.md`; after the AI tools (feat/mcp-ai-tools) land.
         Goal (the maintainer): enough for an LLM to replace a drafter, from nothing to a plotted PDF, and to debug the
         user's app (attach --read-only). -> verify: one scripted MCP session, MCP tools only, in both modes: new
         drawing, layers and styles, a plate detail (polylines, holes, hatch, a block with attributes twice, a mirror,
         an array), dimensions and a multileader placed with measure/free_space/fits (overlaps 0, check_standard 1),
         an A3 layout in mm with a filled title block and viewports at 1:10 and 1:5, plot_pdf, save as DWG; the PDF
         reads back with its words; the tool calls counted with and without run_plan.

## The website and Share (planned 2026-11-23)

The opencad2d.com website is designed, not built: the Design artifact https://claude.ai/artifact/HzsdochEhENoSD3nzTRxMJ,
page "Website (C, Atelier)" (Home, Share, a tool's page, Upload a tool, Download, Share admin, and the app's Download
from Share window). Decided by the maintainer: the tool library is called Share; uploading needs sign-in with Google and
a reviewer's approval before a tool is visible; Share shows tools large, small or as a list; an admin page reviews
uploads and edits users' roles and the rules of each role. The uploader gives no version, command or licence: the
commands are read from the file, Share numbers each approved version (1.0, 1.1, ...), every tool is MIT, Share takes
source only (.lsp, .py, .cs or a .zip of them; no .dll or .fas, which a reviewer cannot read), and Share records each
file's SHA-256 (shown on the tool's page and in its versions; an upload equal to the live one is turned back; the app
compares it with the copy it has).

- [ ] W1. Choose the stack: hosting, the database of tools and users, file storage, Google sign-in, the malware scan.
- [ ] W2. Build the site from the design.
- [ ] W3. The app: a Download from Share window (design in the artifact, page "Website", board "App: Download from
      Share"). Paste a tool's link from its page, see its picture and details, download it into a Plugins or Lisps
      folder and load it; it waits on W2 for the links to resolve. The app asks the server only when the user presses
      Find or Download: no update checks, no background calls.

## Release

- [ ] Review by the maintainer of everything above
