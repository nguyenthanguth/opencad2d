r"""DrawingInfo -- a sample Python tool: GHPYINFO writes what the drawing holds.

Try it: type GHPYINFO at the command line. It only reads; the drawing is not changed. Loading this folder starts no Python:
the app starts the tool's process at the first GHPYINFO. It is the Python twin of GH-DrawingInfo.lsp (GHINFO) and of the
DrawingInfo plugin (GHDWGINFO).

The output for a drawing of two circles and a line, a layer Steel and a block BOLT:

    Drawing: Plan.dwg in C:\Projects
    Current layer: Steel
    Model space: 3 objects
      CIRCLE   2
      LINE     1
    Layers: 2, blocks: 1
    Extents: (-10.00,-10.00) to (120.00,80.00)

Delete this folder if you do not want the command: every folder of Pythons with a requirements.txt loads when the app starts.
"""

import os
from collections import Counter

from opencad2d.cad import active_drawing, command, write


@command("GHPYINFO")
def drawing_info() -> None:
    drawing = active_drawing()
    folder = os.path.dirname(drawing.path) if drawing.path else "not saved yet"
    write(f"Drawing: {drawing.name} in {folder}")
    write(f"Current layer: {drawing.current_layer}")

    # The objects of model space, counted by type and listed by name, so the lines come in the same order every time.
    counts = Counter(entity.dxf_type for entity in drawing.entities(layout="Model"))
    write(f"Model space: {sum(counts.values())} objects")
    for name in sorted(counts):
        write(f"  {name:<9}{counts[name]}")

    write(f"Layers: {len(drawing.layers)}, blocks: {len(drawing.blocks)}")

    # The extents are an empty box (the least corner above the greatest) until something is drawn.
    extents = drawing.net.Extents
    if extents.IsEmpty:
        write("Extents: none")
    else:
        write(f"Extents: ({extents.Min.X:.2f},{extents.Min.Y:.2f}) to ({extents.Max.X:.2f},{extents.Max.Y:.2f})")
