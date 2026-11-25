r"""DrawRandom -- a sample Python tool that draws: PYRANDOM fills an area with objects of random kinds, layers and colours.

Try it: type PYRANDOM at the command line. It asks how many objects (1 to 10000, Enter is 50) and the two corners of the
area (Enter at the first takes the view shown), makes the layers RANDOM-1 to RANDOM-5 if they are missing, each of a random
colour, and draws the objects inside the area: a LINE, CIRCLE, ARC, LWPOLYLINE (3 to 6 vertices, closed or open), TEXT or
rectangle each, on a random one of the five layers, ByLayer half the time and else of a random colour from 1 to 255. The
layers and the objects are one undo step. It is the Python twin of the DrawRandom plugin (CSRANDOM).

    Command: PYRANDOM
    Number of objects <50>:
    Specify first corner <view>:
    Specify opposite corner:
    Drew 50 objects on 5 layers.

Set the environment variable OPENCAD2D_RANDOM_SEED to a whole number to draw the same objects every time.

Delete this folder if you do not want the command: every folder of Pythons with a requirements.txt loads when the app starts.
"""

import math
import os
import random

from opencad2d.cad import Box, Drawing, active_drawing, command, get_integer, get_point, write

LAYERS = 5
GAP = 1e-6  # two corners nearer than this in x or in y make no area
WORDS = ["Beam", "Plate", "Bolt", "Stud", "Rib", "Cap", "Pin", "Nut"]


def new_random() -> random.Random:
    """The one place the random numbers come from: the same every time where OPENCAD2D_RANDOM_SEED is a whole number."""
    seed = os.environ.get("OPENCAD2D_RANDOM_SEED", "")
    try:
        return random.Random(int(seed))
    except ValueError:
        return random.Random()


def default_area(drawing: Drawing) -> Box:
    """The area Enter takes: the view shown, or the extents of the drawing where the app has no window."""
    if drawing.has_window:
        return drawing.view

    # The extents are an empty box until something is drawn: then a square of 1000 stands in.
    extents = drawing.net.Extents
    if extents.IsEmpty or extents.Max.X - extents.Min.X <= GAP or extents.Max.Y - extents.Min.Y <= GAP:
        return Box(0.0, 0.0, 1000.0, 1000.0)
    return Box(extents.Min.X, extents.Min.Y, extents.Max.X, extents.Max.Y)


def make(drawing: Drawing, rng: random.Random, x: float, y: float, width: float, height: float, **look: object) -> None:
    """Queue one object of a random kind that fills the box from (x, y), width wide and height high, at most."""
    centre = (x + width / 2.0, y + height / 2.0)
    radius = min(width, height) / 2.0
    kind = rng.randrange(6)

    if kind == 0:  # LINE: one of the two diagonals of the box.
        if rng.randrange(2) == 0:
            drawing.add_line((x, y), (x + width, y + height), **look)
        else:
            drawing.add_line((x, y + height), (x + width, y), **look)
    elif kind == 1:
        drawing.add_circle(centre, radius, **look)
    elif kind == 2:  # ARC: from a random angle, counter-clockwise by 30 to 300 degrees (the angles are radians).
        start = rng.uniform(0.0, 2.0 * math.pi)
        sweep = rng.uniform(math.pi / 6.0, math.pi)
        drawing.add_arc(centre, radius, start, (start + sweep) % (2.0 * math.pi), **look)
    elif kind == 3:  # LWPOLYLINE of 3 to 6 vertices in the box, closed or open.
        corners = [(x + rng.uniform(0.0, width), y + rng.uniform(0.0, height)) for _ in range(rng.randint(3, 6))]
        drawing.add_polyline(corners, closed=rng.randrange(2) == 0, **look)
    elif kind == 4:  # TEXT: a letter is at most 0.8 of its height wide and goes 0.3 of it below the baseline.
        word = rng.choice(WORDS)
        text_height = min(height / 1.5, width / (0.8 * len(word)))
        drawing.add_text(word, (x, y + 0.3 * text_height), text_height, **look)
    else:  # The rectangle: a closed polyline of four vertices.
        drawing.add_polyline([(x, y), (x + width, y), (x + width, y + height), (x, y + height)], closed=True, **look)


def draw_random(
    drawing: Drawing, count: int, first: tuple[float, float], second: tuple[float, float], rng: random.Random
) -> None:
    """Make the layers if missing, queue ``count`` objects inside the area of the two corners, and commit: one undo step.

    Example:
        draw_random(new_file(), 50, (0, 0), (1000, 500), random.Random(1))   # 50 objects inside 0..1000 by 0..500
    """
    min_x, min_y = min(first[0], second[0]), min(first[1], second[1])
    width, height = abs(second[0] - first[0]), abs(second[1] - first[1])

    for number in range(1, LAYERS + 1):
        if f"RANDOM-{number}" not in drawing.layers:
            drawing.layers.add(f"RANDOM-{number}", color=rng.randint(1, 255))

    for _ in range(count):
        # Each object lives in a small box of 3 to 15 % of the shorter side, which lies inside the area.
        side = min(width, height) * rng.uniform(0.03, 0.15)
        box_width = side * rng.uniform(0.5, 1.0)
        box_height = side * rng.uniform(0.5, 1.0)
        x = min_x + (width - box_width) * rng.random()
        y = min_y + (height - box_height) * rng.random()
        look: dict[str, object] = {"layer": f"RANDOM-{rng.randint(1, LAYERS)}"}
        if rng.randrange(2) == 0:  # ByLayer is what an object has until it is given a colour.
            look["color"] = rng.randint(1, 255)
        make(drawing, rng, x, y, box_width, box_height, **look)

    drawing.commit()


@command("PYRANDOM", description="Draw objects of random kinds on random layers in random colours")
def random_objects() -> None:
    count = get_integer("Number of objects", default=50)
    while not isinstance(count, int) or not 1 <= count <= 10000:
        write("Requires an integer between 1 and 10000.")
        count = get_integer("Number of objects", default=50)

    drawing = active_drawing()
    first = get_point("Specify first corner", default="view")
    if isinstance(first, str):  # Enter: the view shown.
        area = default_area(drawing)
        corners = ((area.min_x, area.min_y), (area.max_x, area.max_y))
    else:
        while True:
            second = get_point("Specify opposite corner", base=first)
            if not isinstance(second, str) and abs(second.X - first.X) > GAP and abs(second.Y - first.Y) > GAP:
                break
            write("The two corners must differ in x and in y.")
        corners = ((first.X, first.Y), (second.X, second.Y))

    draw_random(drawing, count, corners[0], corners[1], new_random())
    write(f"Drew {count} objects on {LAYERS} layers.")
