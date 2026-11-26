"""Draws a drawing to pictures with no window: a PNG of the whole, a PNG of its lower left quarter, an SVG and a PDF.

Run: python render_views.py DRAWING OUT_FOLDER   (DRAWING is a .dxf or .dwg file; OUT_FOLDER is made if it is not there)
Writes whole.png, detail.png, whole.svg and plan.pdf into OUT_FOLDER and prints the size in pixels of each picture.
The pictures are the canvas of the app's: the same renderer and fonts, with the grid and the UCS icon off.
"""

import sys
from pathlib import Path

from opencad2d.cad import Box, Drawing, open_file


def extents(drawing: Drawing) -> Box | None:
    """The least box that holds every object of model space, or None where there is none."""
    boxes = [entity.net.GeometricExtents for entity in drawing.entities(layout="Model")]
    boxes = [box for box in boxes if not box.IsEmpty]
    if not boxes:
        return None
    return Box(
        min(box.Min.X for box in boxes),
        min(box.Min.Y for box in boxes),
        max(box.Max.X for box in boxes),
        max(box.Max.Y for box in boxes),
    )


def main(argv: list[str]) -> int:
    if len(argv) != 2 or Path(argv[0]).suffix.lower() not in (".dxf", ".dwg"):
        print("usage: python render_views.py DRAWING OUT_FOLDER   (DRAWING is a .dxf or .dwg file)", file=sys.stderr)
        return 2
    drawing = open_file(argv[0])
    whole = extents(drawing)
    if whole is None:
        print(f"{Path(argv[0]).name} has nothing in model space to draw", file=sys.stderr)
        return 1
    folder = Path(argv[1])
    folder.mkdir(parents=True, exist_ok=True)
    # The picture of the whole drawing is 800 pixels wide with the margin ZOOM Extents leaves; its height follows.
    drawing.render_png(str(folder / "whole.png"), width=800)
    # A box is fitted with no margin, so the quarter fills the picture.
    quarter = Box(whole.min_x, whole.min_y, (whole.min_x + whole.max_x) / 2, (whole.min_y + whole.max_y) / 2)
    drawing.render_png(str(folder / "detail.png"), box=quarter, width=800)
    drawing.render_svg(str(folder / "whole.svg"), width=800)
    drawing.render_pdf(str(folder / "plan.pdf"))
    for name, picture in (
        ("whole.png", drawing.render_image(width=800)),
        ("detail.png", drawing.render_image(box=quarter, width=800)),
    ):
        height, width = picture.shape[:2]
        print(f"{name} {width} by {height} pixels")
    print(f"whole.svg and plan.pdf written to {folder.name}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
