# OpenCAD2D

A free 2D CAD app for Windows that works like AutoCAD 2021.

- **Files:** DXF R12 to R2018 and DWG R2000 to R2018, read and written; drawings it saves open in AutoCAD 2021 with
  AUDIT reporting nothing.
- **Drawing and editing:** the 2D draw and modify commands of AutoCAD with their prompts and options; grips, object snaps
  and tracking, UCS, blocks and attributes, hatches, groups, arrays.
- **Annotation:** text and MTEXT, tables, fields, dimensions and multileaders.
- **Layouts:** viewports, page setups, plot styles, PLOT and PUBLISH, PDF and image export.
- **The app:** ribbon, command line, Properties, palettes and dialogs; AutoCAD's shortcuts and `acad.pgp` aliases.
- **Scripting:**
  - AutoLISP (`.lsp` files loaded with APPLOAD or from the `Lisps` folder);
  - Python (`@command` functions in `.py` files, the `opencad2d` package);
  - C# plugins (the `Plugins` folder);
  - Remote: other programs drive a running app through a named pipe.

## Build

Windows, the .NET 8 SDK, and .NET Framework 4.8 for the tests.

```bash
dotnet build OpenCAD2D.slnx -c Release
dotnet run --project src/OpenCAD2D.App -c Release
```

Geometry, DXF and DWG come from the GeometryHelper library
([nguyenthanguth/GeometryHelper](https://github.com/nguyenthanguth/GeometryHelper)), used as `Lib\GeometryHelper.dll`;
`tools\update-lib.ps1` builds a new copy from a clone of it.

## Guides

- [The app](docs/presenter/index.md)
- [AutoLISP](docs/presenter/lisp.md)
- [Python](docs/presenter/python.md)
- [Plugins](docs/presenter/plugins.md)
- [Remote](docs/presenter/remote.md)

## Licence

MIT. See [LICENSE](LICENSE); the notices of the code and fonts it carries are in
[src/OpenCAD2D/THIRD-PARTY-NOTICES.md](src/OpenCAD2D/THIRD-PARTY-NOTICES.md).
