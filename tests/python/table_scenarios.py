"""The scenarios of the styles, linetypes, dimensions, hatches and blocks of the Drawing API (PW5): one list, run on every
backend, so that a script written for one gives the same results on the others.

It follows ``drawing_scenarios``: a scenario is a function of a ``Drawing`` that returns what it checks, and the value it must
return. The headless runner (``test_k1_tables_headless.py``) and the remote runner (``remote/test_c18_tables_remote.py``) take
each scenario with a drawing of its own: ``BLANK`` is a new drawing with nothing in it, ``FILE`` is the drawing ``write_file``
writes, opened from its file. A scenario with ``needs_undo`` is also given ``undo``, which takes back the last step.

Every coordinate is an integer or a half, so a drawing that travels as DXF text returns it exactly; other floats are rounded
to six places.
"""

from __future__ import annotations

import math
from collections.abc import Callable
from typing import Any

from drawing_scenarios import BLANK, FILE, Scenario
from GeometryHelper.Dxf import (
    DxfBlockTableRecord,
    DxfCircle,
    DxfDatabase,
    DxfDBText,
    DxfDimStyleTableRecord,
    DxfLine,
    DxfLinetypeTableRecord,
    DxfMText,
    DxfRotatedDimension,
    DxfTextStyleTableRecord,
    DxfWriter,
)
from GeometryHelper.Geometry import GeoPoint3

__all__ = ["BLANK", "FILE", "SCENARIOS", "Scenario", "write_file"]


def write_file(path: str) -> None:
    """Write the drawing of ``FILE``: a line (0, 0) to (10, 0); the text style ``NOTE`` (romans.shx, 0.8 wide); the dimension
    style ``PLAN`` (text 3.5, scale 50, in ``NOTE``); the linetype ``DASHED`` (a dash 12, a gap 6); the block ``Nut`` (one line)."""
    database = DxfDatabase.Parse(DxfWriter().ToString())
    database.ModelSpace.AppendEntity(DxfLine(GeoPoint3(0, 0, 0), GeoPoint3(10, 0, 0)))
    style = DxfTextStyleTableRecord()
    style.Name = "NOTE"
    style.FileName = "romans.shx"
    style.XScale = 0.8
    database.TextStyleTable.Add(style)
    dimension = DxfDimStyleTableRecord()
    dimension.Name = "PLAN"
    dimension.Dimtxt = 3.5
    dimension.Dimscale = 50.0
    dimension.Dimtxsty = database.TextStyleTable["NOTE"]
    database.DimStyleTable.Add(dimension)
    dashed = DxfLinetypeTableRecord()
    dashed.Name = "DASHED"
    dashed.AsciiDescription = "Dashed"
    dashed.NumDashes = 2
    dashed.SetDashLengthAt(0, 12.0)
    dashed.SetDashLengthAt(1, -6.0)
    database.LinetypeTable.Add(dashed)
    nut = DxfBlockTableRecord()
    nut.Name = "Nut"
    nut.Origin = GeoPoint3(0, 0, 0)
    database.BlockTable.Add(nut)
    nut.AppendEntity(DxfLine(GeoPoint3(0, 0, 0), GeoPoint3(5, 0, 0)))
    database.Save(path)


def _r(value: Any) -> float:
    return round(float(value), 6)


def _names(records: Any) -> list[str]:
    return [str(record.Name) for record in records]


def _text(contents: str, style: str) -> Any:
    made = DxfDBText()
    made.TextString = contents
    made.Position = GeoPoint3(0, 5, 0)
    made.Height = 3.0
    made.TextStyleName = style
    return made


def _circle(x: float, y: float, radius: float) -> Any:
    made = DxfCircle()
    made.Center = GeoPoint3(x, y, 0)
    made.Radius = radius
    return made


def _words(drawing: Any) -> list[tuple[str, str]]:
    """The type and the text style of each text and multiline text of the drawing, in order."""
    found: list[tuple[str, str]] = []
    for entity in drawing.entities():
        if entity.dxf_type in ("TEXT", "MTEXT"):
            found.append((entity.dxf_type, str(entity.net.TextStyleName)))
    return found


# ---- The commit with a style or linetype the drawing has (the bug of PW5) ---------------------------------------------


def _a_text_in_a_text_style_the_drawing_has(d: Any) -> object:
    d.add(_text("Plate", "NOTE"))
    handles = d.commit()
    made = d.entity(handles[0])
    return (len(handles), made.dxf_type, str(made.net.TextStyleName), str(made.net.TextString))


def _an_mtext_in_a_text_style_the_drawing_has(d: Any) -> object:
    made = DxfMText()
    made.Contents = "Plate PL10"
    made.Location = GeoPoint3(0, 5, 0)
    made.TextHeight = 3.0
    made.TextStyleName = "NOTE"
    d.add(made)
    handles = d.commit()
    read = d.entity(handles[0])
    return (len(handles), read.dxf_type, str(read.net.TextStyleName), str(read.net.Contents))


def _a_dimension_in_a_dimension_style_the_drawing_has(d: Any) -> object:
    made = DxfRotatedDimension(0.0, GeoPoint3(0, 0, 0), GeoPoint3(50, 0, 0), GeoPoint3(50, 20, 0), "", None)
    made.DimensionStyleName = "PLAN"
    d.add(made)
    handles = d.commit()
    read = d.entity(handles[0])
    parts = sum(1 for part in read.net.DimBlock if not part.IsErased)
    return (len(handles), read.dxf_type, str(read.net.DimensionStyleName), _r(read.net.Measurement), parts > 0)


def _a_line_with_a_linetype_the_drawing_has(d: Any) -> object:
    d.add_line((0, 5), (10, 5), linetype="DASHED")
    handles = d.commit()
    read = d.entity(handles[0])
    return (len(handles), read.linetype)


def _every_kind_in_one_commit_is_one_undo_step(d: Any, undo: Callable[[], None]) -> object:
    d.add(_text("Plate", "NOTE"))
    d.add_mtext("Plate PL10", (0, 10), 3.0, text_style="NOTE")
    d.add_line((0, 5), (10, 5), linetype="DASHED")
    d.add_linear_dim((0, 0), (50, 0), (25, 20), dim_style="PLAN")
    d.commit()
    after = len(d.entities())
    undo()
    return (after, len(d.entities()))


def _a_text_style_the_drawing_lacks_is_refused_and_nothing_is_applied(d: Any) -> object:
    d.add_line((0, 0), (10, 0))
    d.add(_text("Plate", "Nowhere"))
    try:
        d.commit()
        refused = False
    except Exception:
        refused = True
    return (refused, len(d.entities()))


def _a_dimension_style_or_a_linetype_nobody_has_is_refused(d: Any) -> object:
    refused = []
    d.add_linear_dim((0, 0), (100, 0), (50, 20), dim_style="Nowhere")
    try:
        d.commit()
        refused.append("taken")
    except ValueError as error:
        refused.append(str(error))
    d.add_line((0, 0), (10, 0), linetype="Nowhere")
    try:
        d.commit()
        refused.append("taken")
    except ValueError as error:
        refused.append(str(error))
    return (refused, len(d.entities()))


# ---- Text styles ---------------------------------------------------------------------------------------------------


def _a_text_style_added_with_a_text_and_an_mtext_in_it(d: Any) -> object:
    name = d.text_styles.add("NOTE", font="romans.shx", x_scale=0.75, height=2.5)
    d.add_text("Plate", (0, 5), 3.0, text_style="NOTE")
    d.add_mtext("Plate PL10", (0, 10), 3.0, text_style="NOTE")
    before = (_names(d.text_styles), "NOTE" in d.text_styles)
    handles = d.commit()
    style = d.text_styles["note"]
    return (
        name,
        before,
        len(handles),
        _names(d.text_styles),
        (str(style.Name), str(style.FileName), _r(style.XScale), _r(style.TextSize)),
        _words(d),
    )


def _the_current_text_style(d: Any) -> object:
    before = d.current_text_style
    d.text_styles.add("NOTE", font="romans.shx")
    d.commit()
    d.current_text_style = "NOTE"
    return (before, d.current_text_style)


def _a_text_style_that_exists_refuses_the_commit(d: Any) -> object:
    d.text_styles.add("NOTE")
    d.add_line((0, 0), (10, 0))
    try:
        d.commit()
        refused = None
    except RuntimeError as error:
        refused = str(error)
    return (refused, len(d.entities()), _names(d.text_styles))


# ---- Linetypes -----------------------------------------------------------------------------------------------------


def _a_linetype_loaded_with_a_layer_and_a_line_that_use_it(d: Any) -> object:
    before = list(d.linetypes)
    spelled = d.linetypes.load("hidden")
    d.layers.add("Hid", linetype="HIDDEN")
    d.add_line((0, 0), (10, 0), layer="Hid", linetype="HIDDEN")
    d.commit()
    line = d.entities()[0]
    return (before, spelled, list(d.linetypes), "hidden" in d.linetypes, d.layers["Hid"].linetype, line.linetype)


def _a_linetype_loaded_twice_or_present_is_left_as_it_is(d: Any) -> object:
    d.linetypes.load("DASHED")
    d.linetypes.load("dashed")
    d.commit()
    d.linetypes.load("DASHED")
    handles = d.commit()
    return (list(d.linetypes), handles)


def _a_linetype_the_set_lacks_is_refused_at_once(d: Any) -> object:
    try:
        d.linetypes.load("NOPE")
        return "taken"
    except ValueError as error:
        return str(error)


# ---- Dimension styles and dimensions -------------------------------------------------------------------------------


def _a_dimension_style_with_its_text_style_and_a_dimension_in_it(d: Any) -> object:
    d.text_styles.add("NOTE", font="romans.shx")
    d.dimension_styles.add("PLAN", text_style="NOTE", scale=50, text_height=3.5, arrow_size=2.0, Dimdec=1)
    d.add_linear_dim((0, 0), (100, 0), (50, 20), dim_style="PLAN")
    d.commit()
    style = d.dimension_styles["plan"]
    made = next(e for e in d.entities() if e.dxf_type == "DIMENSION")
    return (
        _names(d.dimension_styles),
        (str(style.Name), str(style.Dimtxsty.Name), _r(style.Dimscale), _r(style.Dimtxt), _r(style.Dimasz), int(style.Dimdec)),
        (str(made.net.DimensionStyleName), _r(made.net.Measurement), sum(1 for p in made.net.DimBlock if not p.IsErased) > 0),
    )


def _the_current_dimension_style_is_the_style_of_a_new_dimension(d: Any) -> object:
    before = d.current_dim_style
    d.dimension_styles.add("PLAN", scale=50)
    d.commit()
    d.current_dim_style = "PLAN"
    d.add_aligned_dim((0, 0), (30, 40), (35, 5))
    d.commit()
    return (before, d.current_dim_style, str(d.entities()[0].net.DimensionStyleName))


def _a_dimension_style_with_a_text_style_the_drawing_has(d: Any) -> object:
    d.dimension_styles.add("SMALL", text_style="NOTE", text_height=1.5)
    d.commit()
    style = d.dimension_styles["SMALL"]
    return (str(style.Dimtxsty.Name), _r(style.Dimtxt))


def _a_dimension_style_naming_a_text_style_nobody_has_is_refused(d: Any) -> object:
    d.dimension_styles.add("BAD", text_style="Nowhere")
    try:
        d.commit()
        return "taken"
    except ValueError as error:
        return str(error)


def _a_dimension_variable_that_is_none_is_refused_at_once(d: Any) -> object:
    try:
        d.dimension_styles.add("BAD", Nothing=1)
        return "taken"
    except ValueError as error:
        return str(error)


def _the_five_kinds_of_dimension(d: Any) -> object:
    d.add_linear_dim((0, 0), (100, 0), (50, 20))
    d.add_linear_dim((0, 0), (0, 80), (-30, 40))
    d.add_aligned_dim((0, 0), (30, 40), (35, 5), text="<> TYP")
    d.add_radial_dim((50, 50), 25, (80, 70))
    d.add_diameter_dim((50, 50), 25, (80, 70))
    d.add_angular_dim((0, 0), (100, 0), (0, 100), (50, 50))
    handles = d.commit()
    found = []
    for entity in d.entities():
        net = entity.net
        found.append(
            (
                str(net.GetType().Name),
                _r(net.Measurement),
                str(net.DimensionText),
                str(net.DimensionStyleName),
                sum(1 for part in net.DimBlock if not part.IsErased) > 0,
            )
        )
    return (len(handles), found)


def _a_dimension_through_two_equal_points_is_refused_at_once(d: Any) -> object:
    try:
        d.add_linear_dim((5, 5), (5, 5), (5, 20))
        return "taken"
    except ValueError:
        return "refused"


# ---- Hatches ---------------------------------------------------------------------------------------------------------


def _a_hatch_of_points_a_polyline_and_a_circle_with_an_island(d: Any) -> object:
    d.add_hatch([(0, 0), (100, 0), (100, 50), (0, 50)], scale=2.0, color=1)
    box = d.add_polyline([(200, 0), (300, 0), (300, 100), (200, 100)], closed=True)
    island = d.add_circle((250, 50), 10)
    d.add_hatch([box, island], pattern="ansi32", angle=math.pi / 4)
    d.add_hatch(d.add_circle((400, 50), 20), pattern="SOLID")
    d.commit()
    found = []
    for entity in d.entities():
        if entity.dxf_type == "HATCH":
            net = entity.net
            found.append(
                (
                    str(net.PatternName),
                    _r(net.Area),
                    bool(net.IsSolidFill),
                    _r(net.PatternScale),
                    _r(net.PatternAngle),
                    int(net.NumberOfLoops),
                    entity.color,
                )
            )
    return found


def _a_hatch_with_a_pattern_the_set_lacks_is_refused_at_once(d: Any) -> object:
    try:
        d.add_hatch([(0, 0), (10, 0), (10, 10)], pattern="NOPE")
        return "taken"
    except ValueError as error:
        return str(error)


# ---- Blocks -----------------------------------------------------------------------------------------------------------


def _a_block_made_and_inserted_in_one_commit(d: Any) -> object:
    d.blocks.add("BOLT", [DxfLine(GeoPoint3(0, 0, 0), GeoPoint3(10, 0, 0)), _circle(5, 0, 2)], base_point=(0, 0))
    d.add_insert("BOLT", (100, 50))
    d.add_insert("bolt", (200, 50), scale=2.0, rotation=math.pi / 2)
    handles = d.commit()
    inserts = d.entities("INSERT")
    bolt = d.blocks["bolt"]
    return (
        len(handles),
        _names(d.blocks),
        sum(1 for _ in bolt),
        [(str(i.net.Name), _r(i.net.Position.X), _r(i.net.ScaleFactors.X), _r(i.net.Rotation)) for i in inserts],
    )


def _a_block_of_the_drawing_inserted(d: Any) -> object:
    d.add_insert("Nut", (50, 50))
    handles = d.commit()
    made = d.entity(handles[0])
    return (len(handles), made.dxf_type, str(made.net.Name))


def _a_block_that_exists_or_is_made_of_nothing_is_refused(d: Any) -> object:
    d.blocks.add("Nut", [DxfLine(GeoPoint3(0, 0, 0), GeoPoint3(1, 0, 0))])
    try:
        d.commit()
        taken = True
    except RuntimeError as error:
        taken = str(error)
    try:
        d.blocks.add("EMPTY", [])
        empty = "taken"
    except ValueError as error:
        empty = str(error)
    return (taken, empty, _names(d.blocks))


def _a_block_with_a_text_in_a_style_added_in_the_same_commit(d: Any) -> object:
    d.text_styles.add("NOTE", font="romans.shx")
    d.blocks.add("LABEL", [_text("tag", "NOTE")])
    d.add_insert("LABEL", (0, 0))
    d.commit()
    label = d.blocks["LABEL"]
    return (_names(d.blocks), [(entity.DxfName, str(entity.TextStyleName)) for entity in label])


def _a_commit_of_records_is_one_undo_step(d: Any, undo: Callable[[], None]) -> object:
    d.text_styles.add("NOTE", font="romans.shx")
    d.linetypes.load("DASHED")
    d.dimension_styles.add("PLAN", text_style="NOTE")
    d.blocks.add("BOLT", [DxfLine(GeoPoint3(0, 0, 0), GeoPoint3(10, 0, 0))])
    d.add_text("Plate", (0, 0), 3.0, text_style="NOTE")
    d.add_insert("BOLT", (10, 10))
    d.commit()
    after = (_names(d.text_styles), _names(d.dimension_styles), list(d.linetypes), _names(d.blocks), len(d.entities()))
    undo()
    return (after, (_names(d.text_styles), _names(d.dimension_styles), list(d.linetypes), _names(d.blocks), len(d.entities())))


_IN_NEW = (["Standard"], ["ISO-25", "Standard"], ["ByBlock", "ByLayer", "Continuous"], [], 0)

SCENARIOS: list[Scenario] = [
    Scenario("a_text_in_a_text_style_the_drawing_has", FILE, _a_text_in_a_text_style_the_drawing_has, (1, "TEXT", "NOTE", "Plate")),
    Scenario(
        "an_mtext_in_a_text_style_the_drawing_has",
        FILE,
        _an_mtext_in_a_text_style_the_drawing_has,
        (1, "MTEXT", "NOTE", "Plate PL10"),
    ),
    Scenario(
        "a_dimension_in_a_dimension_style_the_drawing_has",
        FILE,
        _a_dimension_in_a_dimension_style_the_drawing_has,
        (1, "DIMENSION", "PLAN", 50.0, True),
    ),
    Scenario("a_line_with_a_linetype_the_drawing_has", FILE, _a_line_with_a_linetype_the_drawing_has, (1, "DASHED")),
    Scenario(
        "every_kind_in_one_commit_is_one_undo_step",
        FILE,
        _every_kind_in_one_commit_is_one_undo_step,
        (5, 1),
        needs_undo=True,
    ),
    Scenario(
        "a_text_style_the_drawing_lacks_is_refused_and_nothing_is_applied",
        FILE,
        _a_text_style_the_drawing_lacks_is_refused_and_nothing_is_applied,
        (True, 1),
    ),
    Scenario(
        "a_text_style_added_with_a_text_and_an_mtext_in_it",
        BLANK,
        _a_text_style_added_with_a_text_and_an_mtext_in_it,
        (
            "NOTE",
            (["Standard"], False),
            2,
            ["NOTE", "Standard"],
            ("NOTE", "romans.shx", 0.75, 2.5),
            [("TEXT", "NOTE"), ("MTEXT", "NOTE")],
        ),
    ),
    Scenario(
        "a_dimension_style_or_a_linetype_nobody_has_is_refused",
        BLANK,
        _a_dimension_style_or_a_linetype_nobody_has_is_refused,
        (
            [
                'The dimension style "Nowhere" is not in the drawing: add it with dimension_styles.add first.',
                'The linetype "Nowhere" is not in the drawing: add it with linetypes.load first.',
            ],
            0,
        ),
    ),
    Scenario("the_current_text_style", BLANK, _the_current_text_style, ("Standard", "NOTE")),
    Scenario(
        "a_text_style_that_exists_refuses_the_commit",
        FILE,
        _a_text_style_that_exists_refuses_the_commit,
        ('The text style "NOTE" already exists.', 1, ["NOTE", "Standard"]),
    ),
    Scenario(
        "a_linetype_loaded_with_a_layer_and_a_line_that_use_it",
        BLANK,
        _a_linetype_loaded_with_a_layer_and_a_line_that_use_it,
        (
            ["ByBlock", "ByLayer", "Continuous"],
            "HIDDEN",
            ["ByBlock", "ByLayer", "Continuous", "HIDDEN"],
            True,
            "HIDDEN",
            "HIDDEN",
        ),
    ),
    Scenario(
        "a_linetype_loaded_twice_or_present_is_left_as_it_is",
        BLANK,
        _a_linetype_loaded_twice_or_present_is_left_as_it_is,
        (["ByBlock", "ByLayer", "Continuous", "DASHED"], []),
    ),
    Scenario(
        "a_linetype_the_set_lacks_is_refused_at_once",
        BLANK,
        _a_linetype_the_set_lacks_is_refused_at_once,
        "The linetype 'NOPE' is not in the built-in set OpenCAD2D.lin.",
    ),
    Scenario(
        "a_dimension_style_with_its_text_style_and_a_dimension_in_it",
        BLANK,
        _a_dimension_style_with_its_text_style_and_a_dimension_in_it,
        (
            ["ISO-25", "PLAN", "Standard"],
            ("PLAN", "NOTE", 50.0, 3.5, 2.0, 1),
            ("PLAN", 100.0, True),
        ),
    ),
    Scenario(
        "the_current_dimension_style_is_the_style_of_a_new_dimension",
        BLANK,
        _the_current_dimension_style_is_the_style_of_a_new_dimension,
        ("ISO-25", "PLAN", "PLAN"),
    ),
    Scenario(
        "a_dimension_style_with_a_text_style_the_drawing_has",
        FILE,
        _a_dimension_style_with_a_text_style_the_drawing_has,
        ("NOTE", 1.5),
    ),
    Scenario(
        "a_dimension_style_naming_a_text_style_nobody_has_is_refused",
        BLANK,
        _a_dimension_style_naming_a_text_style_nobody_has_is_refused,
        'The text style "Nowhere" is not in the drawing: add it with text_styles.add first.',
    ),
    Scenario(
        "a_dimension_variable_that_is_none_is_refused_at_once",
        BLANK,
        _a_dimension_variable_that_is_none_is_refused_at_once,
        "'Nothing' is no dimension variable: give a name such as Dimdec, Dimtad or Dimexe.",
    ),
    Scenario(
        "the_five_kinds_of_dimension",
        BLANK,
        _the_five_kinds_of_dimension,
        (
            6,
            [
                ("DxfRotatedDimension", 100.0, "", "ISO-25", True),
                ("DxfRotatedDimension", 80.0, "", "ISO-25", True),
                ("DxfAlignedDimension", 50.0, "<> TYP", "ISO-25", True),
                ("DxfRadialDimension", 25.0, "", "ISO-25", True),
                ("DxfDiametricDimension", 50.0, "", "ISO-25", True),
                ("DxfPoint3AngularDimension", 1.570796, "", "ISO-25", True),
            ],
        ),
    ),
    Scenario(
        "a_dimension_through_two_equal_points_is_refused_at_once",
        BLANK,
        _a_dimension_through_two_equal_points_is_refused_at_once,
        "refused",
    ),
    Scenario(
        "a_hatch_of_points_a_polyline_and_a_circle_with_an_island",
        BLANK,
        _a_hatch_of_points_a_polyline_and_a_circle_with_an_island,
        [
            ("ANSI31", 5000.0, False, 2.0, 0.0, 1, 1),
            ("ANSI32", 10000.0 - 314.159265, False, 1.0, 0.785398, 2, 256),
            ("SOLID", 1256.637061, True, 1.0, 0.0, 1, 256),
        ],
    ),
    Scenario(
        "a_hatch_with_a_pattern_the_set_lacks_is_refused_at_once",
        BLANK,
        _a_hatch_with_a_pattern_the_set_lacks_is_refused_at_once,
        "The pattern 'NOPE' is not in the built-in set OpenCAD2D.pat.",
    ),
    Scenario(
        "a_block_made_and_inserted_in_one_commit",
        BLANK,
        _a_block_made_and_inserted_in_one_commit,
        (2, ["BOLT"], 2, [("BOLT", 100.0, 1.0, 0.0), ("BOLT", 200.0, 2.0, 1.570796)]),
    ),
    Scenario("a_block_of_the_drawing_inserted", FILE, _a_block_of_the_drawing_inserted, (1, "INSERT", "Nut")),
    Scenario(
        "a_block_that_exists_or_is_made_of_nothing_is_refused",
        FILE,
        _a_block_that_exists_or_is_made_of_nothing_is_refused,
        ('The block "Nut" already exists.', "The block EMPTY needs at least one object.", ["Nut"]),
    ),
    Scenario(
        "a_block_with_a_text_in_a_style_added_in_the_same_commit",
        BLANK,
        _a_block_with_a_text_in_a_style_added_in_the_same_commit,
        (["LABEL"], [("TEXT", "NOTE")]),
    ),
    Scenario(
        "a_commit_of_records_is_one_undo_step",
        BLANK,
        _a_commit_of_records_is_one_undo_step,
        (
            (["NOTE", "Standard"], ["ISO-25", "PLAN", "Standard"], ["ByBlock", "ByLayer", "Continuous", "DASHED"], ["BOLT"], 2),
            _IN_NEW,
        ),
        needs_undo=True,
    ),
]
