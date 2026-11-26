"""Type-checks clean: the render calls of a drawing (PW6.1): the pictures of a drawing of a file as files and as a numpy
array of ``uint8``, and the pixels of a .NET ``Byte`` array copied with ``arrays.to_numpy``.

The file is never run: pyright only reads it."""

import numpy as np
from numpy.typing import NDArray

from opencad2d import arrays
from opencad2d.cad import Box, new_file
from GeometryHelper.Geometry import GeoPoint3
from OpenCAD2D import CadRender, CadRenderImage, CadRenderOptions

drawing = new_file()
drawing.add_line((0, 0), (400, 300))

# the files: a path, then any of the part, the size, the ground and the layout
drawing.render_png("plan.png")
drawing.render_png("plan.png", box=Box(0.0, 0.0, 100.0, 50.0), width=800)
drawing.render_png("plan.png", box=(0.0, 0.0, 100.0, 50.0), height=600, ground="white", layout="Layout1", dpi=150.0)
drawing.render_svg("plan.svg", width=800)
drawing.render_pdf("plan.pdf")
drawing.render_pdf("plan.pdf", layout="Model")

# the pixels are a numpy array of unsigned bytes, rows of pixels of three
picture: NDArray[np.uint8] = drawing.render_image(width=400)
rows: int = picture.shape[0]

# the facade of the library, as C# calls it
options = CadRenderOptions()
options.Width = 400
options.Background = 0xFFFFFF
image: CadRenderImage = CadRender.Draw(drawing.net, options)
pixels: NDArray[np.uint8] = arrays.to_numpy(image.Rgb)
corner = GeoPoint3(0, 0, 0)
