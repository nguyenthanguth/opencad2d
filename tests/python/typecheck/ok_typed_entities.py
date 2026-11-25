"""Type-checks clean: the entities of a drawing carry the class of their ``net`` (A7): ``entities(DxfCircle)`` gives
``Entity[DxfCircle]``, ``add_circle`` gives one too, and an ``Entity`` read by name or handle is narrowed with
``isinstance`` or ``match`` as C# narrows with ``is``.

The file is never run: pyright only reads it."""

from opencad2d.cad import Entity, new_file
from GeometryHelper.Dxf import DxfArc, DxfCircle, DxfCurve, DxfDBText, DxfEntity, DxfLine, DxfMText, DxfPolyline

drawing = new_file()

# a class asks for that class and its subclasses, and the copies come back typed
circles: list[Entity[DxfCircle]] = drawing.entities(DxfCircle)
radius: float = drawing.entities(DxfCircle)[0].net.Radius
curves: list[Entity[DxfCurve]] = drawing.entities(DxfCurve, layer="Steel", layout="Model")
closed: bool = drawing.entities(DxfCurve)[0].net.Closed

# a name asks for a DXF type and gives the plain Entity, which is an Entity[DxfEntity]
named: list[Entity] = drawing.entities("CIRCLE")
everything: list[Entity[DxfEntity]] = drawing.entities()

# the add calls give the exact type
line: Entity[DxfLine] = drawing.add_line((0, 0), (100, 0), layer="Steel")
circle: Entity[DxfCircle] = drawing.add_circle((50, 50), 25)
arc: Entity[DxfArc] = drawing.add_arc((0, 0), 10, 0.0, 1.5)
polyline: Entity[DxfPolyline] = drawing.add_polyline([(0, 0), (100, 0), (100, 50)], closed=True)
text: Entity[DxfDBText] = drawing.add_text("Plate", (0, 100), 5.0)
mtext: Entity[DxfMText] = drawing.add_mtext("Plate PL10", (10, 20), 5.0)
added: Entity[DxfCircle] = drawing.add(DxfCircle())
area: float = polyline.net.Area
height: float = text.net.Height

# an Entity[DxfCircle] passes where an Entity is taken (E is covariant)
as_entity: Entity = circle
as_entities: list[Entity] = [line, circle, arc]
drawing.select([circle, line])
drawing.modify(circle)
drawing.erase(circle)

# the C#-like forms: isinstance narrows, and so does match
for entity in drawing.entities():
    if isinstance(entity.net, DxfLine):
        start_x: float = entity.net.StartPoint.X
    match entity.net:
        case DxfCircle() as found:
            found_radius: float = found.Radius
        case DxfArc() as found_arc:
            found_angle: float = found_arc.StartAngle
        case _:
            pass

picked = drawing.entity("2F")
if isinstance(picked.net, DxfCircle):
    picked_radius: float = picked.net.Radius
