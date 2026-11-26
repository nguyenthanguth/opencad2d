"""A plotted sheet made from nothing: a steel plate in model space, an A3 layout in millimetres with a filled title
block and two viewports (1:10, and a detail at 1:5), plotted to a PDF.
Run: python sheet_from_code.py OUT_FOLDER   (OUT_FOLDER is made if it is not there)
The plate is 1200 by 800 with two holes of 60; its dimensions are on the layer Dims, in a style of scale 10. The
title block is a block with three attributes, put on the layout with ``add_insert``. Writes plate.dxf and plate.pdf."""

import sys
from pathlib import Path

from opencad2d.cad import Drawing, new_file
from GeometryHelper.Dxf import DxfAttributeDefinition, DxfLine
from GeometryHelper.Geometry import GeoPoint3

SHEET = "Plate A3"
TITLE_AT = (250.0, 10.0)  # the lower left corner of the title block on the sheet, in millimetres
HOLES = [(300.0, 400.0), (900.0, 400.0)]  # the centres of the two holes of 60, in millimetres
VALUES = {"DRAWING": "PLATE PL12", "NUMBER": "A-101", "SCALE": "1:10, DETAIL 1:5"}
FIELDS = [("DRAWING", "UNTITLED", (4.0, 21.0)), ("NUMBER", "A-000", (4.0, 5.0)), ("SCALE", "1:1", (84.0, 5.0))]


def box(width: float, height: float, at: tuple[float, float] = (0.0, 0.0)) -> list[DxfLine]:
    """The four sides of a box, ``width`` by ``height`` millimetres, with its lower left corner at ``at``."""
    x, y = at
    corners = [(x, y), (x + width, y), (x + width, y + height), (x, y + height)]
    return [DxfLine(GeoPoint3(*corners[i], 0.0), GeoPoint3(*corners[(i + 1) % 4], 0.0)) for i in range(4)]


def draw_plate(drawing: Drawing) -> None:
    """The plate with its holes and centre marks, and two dimensions whose words are 2.5 high on a sheet at 1:10."""
    drawing.layers.add("Plate", color=7)
    drawing.layers.add("Centre", color=1)
    drawing.layers.add("Notes", color=3)
    drawing.layers.add("Dims", color=2)
    drawing.dimension_styles.add("PLATE-10", scale=10.0, text_height=2.5, arrow_size=2.5)
    drawing.commit()
    drawing.current_layer = "Plate"
    drawing.add_polyline([(0, 0), (1200, 0), (1200, 800), (0, 800)], closed=True)
    for x, y in HOLES:
        drawing.add_circle((x, y), 30.0)
        drawing.add_line((x - 50, y), (x + 50, y), layer="Centre")
        drawing.add_line((x, y - 50), (x, y + 50), layer="Centre")
    drawing.add_linear_dim((0, 0), (1200, 0), (600, -80), dim_style="PLATE-10", layer="Dims")
    drawing.add_linear_dim((0, 0), (0, 800), (-80, 400), dim_style="PLATE-10", layer="Dims")
    drawing.add_text("2 HOLES D60", (950.0, 470.0), 25.0, layer="Notes")
    drawing.commit()


def title_block(drawing: Drawing) -> None:
    """Defines the block TITLE, a box 160 by 35 with the attributes DRAWING, NUMBER and SCALE, and puts it on the sheet
    with their words in the same commit."""
    parts: list[DxfLine | DxfAttributeDefinition] = [*box(160.0, 35.0)]
    parts.append(DxfLine(GeoPoint3(0.0, 17.5, 0.0), GeoPoint3(160.0, 17.5, 0.0)))
    style = drawing.text_styles["Standard"]
    for tag, default, (x, y) in FIELDS:
        definition = DxfAttributeDefinition(GeoPoint3(x, y, 0.0), default, tag, tag.title(), style)
        definition.Height = 5.0
        parts.append(definition)
    drawing.blocks.add("TITLE", parts)
    drawing.add_insert("TITLE", TITLE_AT, layout=SHEET, attributes=VALUES)
    drawing.commit()


def lay_out(drawing: Drawing) -> None:
    """The layout on an A3 sheet in millimetres with a border, the title block and two viewports: the plate at 1:10 with
    the layer Notes frozen in it, and a detail of the second hole at 1:5 with the layer Dims frozen."""
    sheet = drawing.layouts.add(SHEET, paper="ISO_A3")
    for side in box(400.0, 277.0, (10.0, 10.0)):
        drawing.add(side, layout=sheet)
    title_block(drawing)
    sheet.add_viewport((20, 55, 250, 277), "1:10", center=(600, 400), frozen_layers=["Notes"])
    sheet.add_viewport((260, 55, 400, 277), 0.2, center=(900, 400), frozen_layers=["Dims"])  # 0.2 mm to a unit is 1:5


def main(argv: list[str]) -> int:
    if len(argv) != 1:
        print("usage: python sheet_from_code.py OUT_FOLDER", file=sys.stderr)
        return 2
    folder = Path(argv[0])
    folder.mkdir(parents=True, exist_ok=True)
    drawing = new_file()
    draw_plate(drawing)
    lay_out(drawing)
    sheet = drawing.layouts[SHEET]
    drawing.save_as(str(folder / "plate.dxf"))
    plot = sheet.plot_pdf(folder / "plate.pdf")
    print(f"{sheet.name}: {sheet.paper}, {sheet.viewports} viewports, title block {VALUES['NUMBER']}")
    print(f"plate.pdf: sheet {plot.sheet[0]:g} by {plot.sheet[1]:g}, scale {plot.scale}, written to {folder.name}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
