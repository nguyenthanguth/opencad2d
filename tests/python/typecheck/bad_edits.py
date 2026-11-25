"""Type-checks with exactly the errors marked by an expect comment on their lines: what a script gets wrong when it edits the
entities of a drawing (A8)."""

from opencad2d.cad import Entity, new_file
from GeometryHelper.Dxf import DxfCircle, DxfDBText, DxfLine

drawing = new_file()
line: Entity[DxfLine] = drawing.add_line((0, 0), (100, 0))
circle: Entity[DxfCircle] = drawing.add_circle((50, 50), 25)
text: Entity[DxfDBText] = drawing.add_text("NOTE", (0, 200), 5.0)

# the copy of a line is a line: it has no Radius
drawing.copy([line], (0, 50))[0].net.Radius  # expect: reportAttributeAccessIssue

# the copy of a line is no Entity of circles
wrong: list[Entity[DxfCircle]] = drawing.copy([line], (0, 50))  # expect: reportAssignmentType

# an edit takes entities, not their handles
drawing.move(["2F"], (10, 5))  # expect: reportArgumentType

# and a list of them, not one
drawing.move(line, (10, 5))  # expect: reportArgumentType

# the arguments of each edit
drawing.move([line])  # expect: reportCallIssue
drawing.rotate([line], (0, 0))  # expect: reportCallIssue
drawing.scale([line], (0, 0), "2")  # expect: reportArgumentType
drawing.mirror([line], (0, 0), (0, 10), erase_source="yes")  # expect: reportArgumentType
drawing.mirror([line], (0, 0), (0, 10), False, 5)  # expect: reportCallIssue
drawing.offset(line, "5", (50, 10))  # expect: reportArgumentType

# an offset gives curves, which have no Radius, and takes curves, which a text is not
drawing.offset(line, 5.0, (50, 10))[0].net.Radius  # expect: reportAttributeAccessIssue
drawing.offset(text, 5.0, (50, 10))  # expect: reportArgumentType
