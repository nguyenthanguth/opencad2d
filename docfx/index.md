# OpenCAD2D

A free 2D CAD app for Windows that works like AutoCAD 2021: DXF and DWG files, the 2D draw and modify commands,
annotation, layouts and plotting, with AutoLISP, Python, C# plugins and Remote, which drives the app from another
program. Geometry, DXF and DWG come from [GeometryHelper](https://nguyenthanguth.github.io/GeometryHelper/), whose
reference the pages here link to.

## Where to start

| | |
|---|---|
| [The app](../docs/presenter/index.md) | the projects, a drawing opened, drawn, picked in and edited through its command line |
| [Opening, drawing and picking](../docs/presenter/display.md) | the document, display lists, picking and what is drawn |
| [Editing as AutoCAD does](../docs/presenter/editing.md) | the commands, their prompts and options, grips, snaps, undo |
| [Plotting to PDF](../docs/presenter/plot.md) | PLOT, PUBLISH, page setups and plot styles |
| [Command plugins](../docs/presenter/plugins.md) | C# plugins: commands, ribbon tabs, Properties rows, snaps |
| [AutoLISP](../docs/presenter/lisp.md) | one `.lsp` for AutoCAD 2021 and OpenCAD2D, DCL dialogs included |
| [Python commands and tools](../docs/presenter/python.md) | `.py` files loaded as LISP files are, with the same Drawing API in the app and outside |
| [Settings, autosave and the window](../docs/presenter/app.md) | global settings, autosave, the window and its options |
| [Driving the app from another program](../docs/presenter/remote.md) | Remote: a running app opened, drawn in and read from .NET or Python |
| [Python](../docs/python.md) | the `opencad2d` package, its stubs and [examples](../docs/python-examples.md) |
| [API Reference](xref:OpenCAD2D.CadDocument) | every public type of `OpenCAD2D.Core` and `OpenCAD2D.Remote`, from the XML documentation |
| [Python API](python-api/index.md) | every type of `OpenCAD2D` as a Python script sees it |
