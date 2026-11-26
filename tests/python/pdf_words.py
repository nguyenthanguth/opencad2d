"""Reads the words a PDF written by the plotter of OpenCAD2D shows, as a reader's search does: the glyph codes of its ``Tj`` strings
through the ``ToUnicode`` maps of its fonts. The page is read as one run of text, in the order it was written; a space is a
glyph of its own where the plotter wrote one. Only what the plotter writes is read (flate streams, hex strings): it is no general
PDF reader.
"""

import re
import zlib
from pathlib import Path


def words_of(path: Path) -> str:
    """The text of every ``Tj`` string of the PDF at ``path``, joined; a glyph with no map is ``?``."""
    text = path.read_bytes().decode("latin-1")
    codes: dict[str, str] = {}
    pages: list[str] = []
    for stream in re.finditer(r"stream\r?\n(.*?)endstream", text, flags=re.DOTALL):
        try:
            inflated = zlib.decompressobj().decompress(stream.group(1).encode("latin-1")).decode("latin-1")
        except zlib.error:
            continue
        if "begincmap" in inflated:
            for section in re.finditer(r"beginbfchar(.*?)endbfchar", inflated, flags=re.DOTALL):
                for pair in re.finditer(r"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>", section.group(1)):
                    codes[pair.group(1).upper()] = chr(int(pair.group(2), 16))
            for section in re.finditer(r"beginbfrange(.*?)endbfrange", inflated, flags=re.DOTALL):
                for span in re.finditer(r"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>", section.group(1)):
                    low, high, first = (int(span.group(i), 16) for i in (1, 2, 3))
                    for code in range(low, high + 1):
                        codes[f"{code:04X}"] = chr(first + code - low)
        elif " Tj" in inflated:
            pages.append(inflated)
    words: list[str] = []
    for page in pages:
        for shown in re.finditer(r"<([0-9A-Fa-f]+)>\s*Tj", page):
            digits = shown.group(1).upper()
            words.extend(codes.get(digits[i : i + 4], "?") for i in range(0, len(digits) - 3, 4))
    return "".join(words)
