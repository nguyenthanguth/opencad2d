"""A snap, a setting, a command and a ribbon button for circles: CTOP, textHeight, MARKTOPS.

A Python tool: PYLOAD its requirements.txt, then snap to the top of a circle with CTOP, or select circles and press Mark
tops (or type MARKTOPS). A mark is a text of the height set in Load/Unload Applications, Settings.
"""

from opencad2d.cad import SnapRequest, active_drawing, command, get_selection, setting, snap, write
from GeometryHelper.Dxf import DxfCircle
from GeometryHelper.Geometry import GeoPoint2


@snap("CTOP")
def circle_top(request: SnapRequest) -> list[GeoPoint2]:
    circle = request.entity
    if not isinstance(circle, DxfCircle):
        return []
    return [GeoPoint2(circle.Center.X, circle.Center.Y + circle.Radius)]  # r=25 at (100, 50) gives (100, 75)


@command("MARKTOPS", uses_selection=True)
def mark_tops() -> None:
    height = setting("textHeight")
    circles = [entity for entity in get_selection() if isinstance(entity.net, DxfCircle)]
    if not circles or not isinstance(height, float):
        write("No circle selected.")
        return
    drawing = active_drawing()
    for entity in circles:
        circle = entity.net
        assert isinstance(circle, DxfCircle)
        drawing.add_text(f"R{circle.Radius:g}", (circle.Center.X, circle.Center.Y + circle.Radius), height)
    write(f"Marks written: {len(circles)}, {height:g} high.")
