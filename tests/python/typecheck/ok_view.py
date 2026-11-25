"""Type-checks clean: the view, the named views, the selection by area and the system variables of a Drawing (A6)."""

from opencad2d.cad import Box, Drawing, Entity, new_file

drawing: Drawing = new_file()

drawing.zoom_extents()
drawing.zoom_window((0, 0), (10, 10))
drawing.zoom_center((50, 50), 20)
drawing.zoom_scaled(2)
drawing.pan((10, 0))
drawing.regen()
moved: bool = drawing.zoom_previous()
circle = drawing.add_circle((50, 50), 25)
drawing.zoom_objects([circle, "2F"])

view: Box = drawing.view
least: float = view.min_x
greatest: float = view.max_y
drawing.view = Box(0.0, 0.0, 10.0, 10.0)
shown: bool = drawing.has_window

names: list[str] = drawing.named_views
drawing.save_view("Corner")
drawing.restore_view("Corner")
drawing.delete_view("Corner")

everything: list[Entity] = drawing.select_all()
inside: list[Entity] = drawing.select_window((0, 0), (100, 50))
across: list[Entity] = drawing.select_crossing((50, 15), (60, 25))

value: int | float | str | tuple[float, ...] = drawing.get_variable("LUNITS")
drawing.set_variable("LUNITS", 4)
drawing.set_variable("LTSCALE", 2.5)
drawing.set_variable("CLAYER", "Steel")
drawing.set_variable("INSBASE", (1.5, 2.5, 0.0))
drawing.set_variable("SNAPBASE", [1.5, 2.5])
