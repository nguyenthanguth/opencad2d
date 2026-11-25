"""Type-checks with exactly the errors marked by an expect comment on their lines: what a script gets wrong once the
entities of a drawing are typed (A7)."""

from opencad2d.cad import Entity, new_file
from GeometryHelper.Dxf import DxfCircle, DxfLine

drawing = new_file()

# an entity read with no class is a DxfEntity, which has no Radius until it is narrowed
drawing.entities()[0].net.Radius  # expect: reportAttributeAccessIssue

# a name gives the plain Entity as well
drawing.entities("CIRCLE")[0].net.Radius  # expect: reportAttributeAccessIssue

# a class that is no DxfEntity is no filter
drawing.entities(int)  # expect: reportArgumentType|reportCallIssue

# the entities of one class are of that class: a line has no Radius
drawing.entities(DxfLine)[0].net.Radius  # expect: reportAttributeAccessIssue

# an Entity of lines is no Entity of circles
circles: list[Entity[DxfCircle]] = drawing.entities(DxfLine)  # expect: reportAssignmentType

# add_circle gives a circle, not a line
wrong: Entity[DxfLine] = drawing.add_circle((0, 0), 5)  # expect: reportAssignmentType
