"""Writes a small dataset for training a vision model: three drawings built in code, each a picture, a mask and objects.

Run: python coco_dataset.py OUT_FOLDER   (OUT_FOLDER is made if it is not there; needs numpy)
Writes images/drawingN.png, masks/drawingN.png (the class of each pixel, 0 to 5) and annotations.json in COCO format.
Prints the size of each picture and how many objects it holds; the categories are words, dimensions, lines and so on.
"""

import json
import random
import struct
import sys
import zlib
from pathlib import Path
from typing import Any

import numpy as np
from numpy.typing import NDArray

from opencad2d.cad import LABEL_CLASSES, Drawing, new_file


def make(seed: int) -> Drawing:
    """A drawing of a room with a hatched slab, a dimension, a few labels and some bolts, laid out by the seed."""
    chance = random.Random(seed)
    drawing = new_file()
    width, depth = chance.randrange(4000, 6000, 100), chance.randrange(3000, 4000, 100)
    walls = [(0, 0), (width, 0), (width, depth), (0, depth)]
    drawing.add_polyline(walls, closed=True, color=7)
    slab = [(300, 300), (width / 2, 300), (width / 2, depth - 300), (300, depth - 300)]
    drawing.add_hatch(slab, pattern=chance.choice(["ANSI31", "ANSI37", "SOLID"]), scale=40.0, color=3)
    drawing.add_linear_dim((0, 0), (width, 0), (width / 2, -400))
    drawing.add_text(f"ROOM {seed}", (width * 0.6, depth * 0.7), 250.0, color=2)
    drawing.add_mtext("Slab on grade, see the notes of sheet S-1", (width * 0.6, depth * 0.4), 150.0, 1500.0)
    drawing.blocks.add("BOLT", [drawing_line((0, 0), (200, 0)), drawing_line((100, -100), (100, 100))])
    drawing.commit()
    for _ in range(chance.randrange(2, 6)):
        drawing.add_insert("BOLT", (chance.randrange(400, int(width) - 400), chance.randrange(400, int(depth) - 400)))
    drawing.commit()
    return drawing


def drawing_line(start: tuple[float, float], end: tuple[float, float]) -> Any:
    """A LINE of the library, for the block."""
    from GeometryHelper.Dxf import DxfLine
    from GeometryHelper.Geometry import GeoPoint3

    return DxfLine(GeoPoint3(start[0], start[1], 0), GeoPoint3(end[0], end[1], 0))


def write_grey_png(path: Path, pixels: NDArray[np.uint8]) -> None:
    """Writes an 8-bit grey PNG of a (height, width) array, with no library but zlib."""
    height, width = pixels.shape
    rows = b"".join(b"\x00" + pixels[y].tobytes() for y in range(height))

    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    header = struct.pack(">IIBBBBB", width, height, 8, 0, 0, 0, 0)
    path.write_bytes(
        b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b"")
    )


def add_to_dataset(dataset: dict[str, Any], coco: dict[str, Any], last_id: int) -> int:
    """Adds the pictures and objects of one ``to_coco`` to the dataset, the objects numbered on from ``last_id`` so that
    every id in the file is its own (the ids of one image are numbers within it), and gives the last id used."""
    for annotation in coco["annotations"]:
        last_id += 1
        annotation["id"] = last_id
    dataset["images"] += coco["images"]
    dataset["annotations"] += coco["annotations"]
    dataset["categories"] = coco["categories"]
    return last_id


def main(argv: list[str]) -> int:
    if len(argv) != 1:
        print("usage: python coco_dataset.py OUT_FOLDER", file=sys.stderr)
        return 2
    folder = Path(argv[0])
    (folder / "images").mkdir(parents=True, exist_ok=True)
    (folder / "masks").mkdir(parents=True, exist_ok=True)
    dataset: dict[str, Any] = {"images": [], "annotations": [], "categories": []}
    classes = list(LABEL_CLASSES)
    last_id = 0
    for number in range(1, 4):
        drawing = make(number)
        # The picture and its objects come from one call, so that they are of the same picture; the masks are of it too.
        image = str(folder / "images" / f"drawing{number}.png")
        coco = drawing.to_coco(image, classes=classes, width=1200, image_id=number)
        masks = drawing.render_masks(classes=classes, width=1200)
        write_grey_png(folder / "masks" / f"drawing{number}.png", masks.classes)
        # The ids of the objects are numbers within one image: renumbered, they are unique in the file.
        last_id = add_to_dataset(dataset, coco, last_id)
        height, width = masks.classes.shape
        print(f"drawing{number}.png {width} by {height} pixels, {len(coco['annotations'])} objects")
    (folder / "annotations.json").write_text(json.dumps(dataset, indent=1), encoding="utf-8")
    print(f"{len(dataset['images'])} pictures and {len(dataset['annotations'])} objects written to {folder.name}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
