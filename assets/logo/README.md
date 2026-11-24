# OpenCAD2D logo

The mark of OpenCAD2D (concept E3, chosen 2026-11-23): on a white rounded tile, a blue O holds a C opening to the right,
and the C holds the letters AD, so it reads O, C, AD. The drawing file icon (option F1, 2026-11-24) is the same tile with
its top right corner folded over like a page.

The source of truth is `src/OpenCAD2D.App/Icons/make-icons.ps1`, which draws `app.ico`, `drawing-file.ico` and the
NuGet `icon.png` from one drawing 256 units square. The files here are copies for use outside the app (the website, the
README, slides). After a shape changes in the script, run it, then copy the frames here again.

| File | What it is |
|---|---|
| `opencad2d.svg` | The mark, 256 units, with the letters AD (24 pixels and up) |
| `opencad2d-small.svg` | The mark below 24 pixels: the letters left out, the O and the C stay |
| `drawing-file.svg` | The icon of a .dwg or .dxf file associated with the app |
| `opencad2d-16.png` ... `opencad2d-256.png` | The frames of `app.ico`: 16, 24, 32, 48 and 256 pixels |
| `opencad2d-128.png` | The icon of the NuGet packages |
| `drawing-file-16.png` ... `drawing-file-256.png` | The frames of `drawing-file.ico` |

Colours: blue `#1F6FD6`, tile `#FFFFFF` edged `#D5DBE3`; the page of the file icon `#FFFFFF` edged `#C3CCD7`, its fold
`#D5DBE3`. The letters are outlines of Space Grotesk Bold (SIL Open Font License), so no font is needed to show them.

The PNG frames were taken from the .ico files as built, pixel for pixel. The app's title bar shows the small mark at
22 pixels as its application menu button.
