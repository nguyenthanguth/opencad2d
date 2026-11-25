"""Type-checks clean: the edit calls of a Drawing (A8): ``move``, ``copy``, ``rotate``, ``scale``, ``mirror`` and ``offset`` give
back the entities of the class they were given, ``Entity[DxfCircle]`` for a circle, and ``offset`` gives curves."""

import math

from opencad2d.cad import Drawing, Entity, new_file
from GeometryHelper.Dxf import DxfArc, DxfCircle, DxfCurve, DxfDBText, DxfEntity, DxfLine, DxfPolyline
from GeometryHelper.Geometry import GeoPoint2, GeoPoint3, GeoVector2

drawing: Drawing = new_file()
line: Entity[DxfLine] = drawing.add_line((0, 0), (100, 0))
circle: Entity[DxfCircle] = drawing.add_circle((50, 50), 25)
arc: Entity[DxfArc] = drawing.add_arc((0, 0), 10, 0.0, 1.5)
plate: Entity[DxfPolyline] = drawing.add_polyline([(0, 0), (100, 0), (100, 50)], closed=True)
text: Entity[DxfDBText] = drawing.add_text("NOTE", (0, 200), 5.0)
drawing.commit()

# an edit gives back the class it was given
moved: list[Entity[DxfLine]] = drawing.move([line], (10, 5))
copies: list[Entity[DxfCircle]] = drawing.copy([circle], GeoVector2(0, 50))
turned: list[Entity[DxfArc]] = drawing.rotate([arc], (0, 0), math.pi / 2)
scaled: list[Entity[DxfPolyline]] = drawing.scale([plate], GeoPoint2(0, 0), 2.0)
mirrored: list[Entity[DxfDBText]] = drawing.mirror([text], (0, 0), (0, 10))
swapped: list[Entity[DxfDBText]] = drawing.mirror([text], (0, 0), (0, 10), erase_source=True)
positional: list[Entity[DxfDBText]] = drawing.mirror([text], (0, 0), (0, 10), False)

# and the class is the class of the net: no narrowing is needed
radius: float = drawing.copy([circle], (0, 50))[0].net.Radius
length: float = drawing.move([line], [10, 5])[0].net.Length
(only,) = drawing.copy([circle], (0, 50))
centre_x: float = only.net.Center.X
area: float = drawing.scale((plate,), (0, 0), 0.5)[0].net.Area

# a list of two classes gives their union
both = drawing.copy([line, circle], (0, 50))
layer: str = both[0].layer
first_net: DxfLine | DxfCircle = both[0].net

# an Entity read with no class is an Entity, and an edit of it gives Entity
read: list[Entity] = drawing.entities()
same: list[Entity[DxfEntity]] = drawing.move(read, (1, 1))
picked = drawing.entities("CIRCLE")
more: list[Entity[DxfEntity]] = drawing.copy(picked, (1, 1))

# a point is a GeoPoint2, a GeoPoint3 or a tuple, a displacement a GeoVector2 or a tuple
drawing.rotate([line], GeoPoint3(0, 0, 0), 1.0)
drawing.rotate([line], (0, 0, 0), 1.0)
drawing.copy([line], GeoVector2(1, 2))

# offset takes any curve and gives curves
offsets: list[Entity[DxfCurve]] = drawing.offset(line, 5.0, (50, 10))
from_circle: list[Entity[DxfCurve]] = drawing.offset(circle, 5, GeoPoint2(50, 10))
from_plate: list[Entity[DxfCurve]] = drawing.offset(plate, 5, (0, 0))
from_curve: list[Entity[DxfCurve]] = drawing.offset(drawing.entities(DxfCurve)[0], 5.0, (50, 10))
closed: bool = drawing.offset(line, 5.0, (50, 10))[0].net.Closed
for made in drawing.offset(arc, 2.0, (0, 0)):
    if isinstance(made.net, DxfArc):
        start_angle: float = made.net.StartAngle
    elif isinstance(made.net, DxfLine):
        end_x: float = made.net.EndPoint.X

# what comes back is an Entity of the drawing: select, modify, erase and zoom take it
drawing.select(drawing.copy([circle], (0, 50)))
drawing.modify(drawing.move([line], (1, 1))[0])
drawing.erase(drawing.offset(line, 5.0, (50, 10))[0])
drawing.zoom_objects(drawing.copy([line], (0, 50)))
