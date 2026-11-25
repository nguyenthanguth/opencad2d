"""Writes svg/<key>-32.svg and svg/<key>-16.svg for every icon drawn in icons_home.py and icons_tabs.py.

python -I make_svgs.py

The 32 file is the large drawing, the 16 file the small one: the same 32 grid at width 16, without the parts only the large
one shows. build.py turns the files into Themes/RibbonIcons.xaml.
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from render import svg  # noqa: E402
import icons_home  # noqa: E402
import icons_tabs  # noqa: E402

ICONS = {}
ICONS.update(icons_home.I)
ICONS.update(icons_tabs.I)
OUT = os.path.join(HERE, "svg")
os.makedirs(OUT, exist_ok=True)

for key, parts in sorted(ICONS.items()):
    for size in (32, 16):
        with open(os.path.join(OUT, "%s-%d.svg" % (key, size)), "w", encoding="utf-8", newline="\n") as out:
            out.write(svg(parts, size, True) + "\n")

print(len(ICONS), "icons written to", OUT)
