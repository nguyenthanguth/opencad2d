"""Type-checks clean: the labelled renders of a drawing (PW7.4): the matrix, the class and instance masks, and the COCO dict; and the
facade of the library as C# calls it (``CadRender.Transform``, ``DrawLabels`` and the arrays of ``CadRenderLabels``).

The file is never run: pyright only reads it."""

from typing import Any

import numpy as np
from numpy.typing import NDArray

from opencad2d import arrays
from opencad2d.cad import LABEL_CLASSES, Box, RenderMasks, new_file
from OpenCAD2D import CadLabelClass, CadRender, CadRenderLabels, CadRenderOptions

drawing = new_file()
drawing.add_line((0, 0), (400, 300))

# the matrix: a numpy array of 3 by 3 doubles, the arguments of render_png less the ground
matrix: NDArray[np.float64] = drawing.render_transform()
point = matrix @ np.array([400.0, 300.0, 1.0])
drawing.render_transform(box=Box(0.0, 0.0, 100.0, 50.0), width=800)
drawing.render_transform(box=(0.0, 0.0, 100.0, 50.0), height=600, layout="Layout1", dpi=150.0)

# the masks: classes of unsigned bytes, instances of 32-bit integers or None, the handles, the matrix and the names
masks: RenderMasks = drawing.render_masks()
classes: NDArray[np.uint8] = masks.classes
instances: NDArray[np.int32] | None = drawing.render_masks(classes=["words", "blocks"], instances=True).instances
handles: list[str] = masks.handles
names: list[str] = masks.names
every: tuple[str, ...] = LABEL_CLASSES

# the COCO dict, ready for json.dump
coco: dict[str, Any] = drawing.to_coco("plan.png", classes=list(LABEL_CLASSES), segmentation=True, width=800, image_id=3)

# the facade of the library, as C# calls it
options = CadRenderOptions()
options.Width = 400
labels: CadRenderLabels = CadRender.DrawLabels(drawing.net, options, [CadLabelClass.Words, CadLabelClass.Lines])
bytes_of_classes: NDArray[np.uint8] = arrays.to_numpy(labels.Classes)
numbers: NDArray[np.float64] = arrays.to_numpy(CadRender.Transform(drawing.net, options))
words: int = CadLabelClass.Words
