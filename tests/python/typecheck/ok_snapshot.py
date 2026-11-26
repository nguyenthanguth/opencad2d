"""Type-checks clean: a snapshot and reset of a drawing in memory (PW7.3): ``drawing.snapshot()``, ``drawing.reset()`` and the
``Snapshot`` it gives, and the calls of the library as C# gives them (``CadDocument.TakeSnapshot`` and ``Reset``).

The file is never run: pyright only reads it."""

from opencad2d.cad import Drawing, Snapshot, new_file
from OpenCAD2D import CadSnapshot

drawing: Drawing = new_file()
drawing.add_line((0, 0), (400, 300))
drawing.commit()

start: Snapshot = drawing.snapshot()
drawing.add_circle((50, 50), 25)
drawing.commit()
drawing.reset(start)
same: Drawing = start.drawing
start.release()

# the library's own: the document keeps the point, the snapshot says whose it is
kept: CadSnapshot = drawing.net.TakeSnapshot()
drawing.net.Reset(kept)
owner = kept.Document
