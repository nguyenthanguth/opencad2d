"""SVG for the A13 icons: the new colour icons (32 grid) and today's monochrome ones (24 grid)."""

from lib import PALETTE

STROKE = {"s": 2, "S": 2, "x": 2, "w": 4, "d": 1}
FILL_OPACITY = {"f": 1, "F": 1, "X": 1, "t": 0.3, "h": 0.6}
ONLY32 = set("dSF")
ONLY16 = set("xX")


def shown(style, size):
    if size == 16 and style in ONLY32:
        return False
    if size == 32 and style in ONLY16:
        return False
    return True


def path_el(role, style, d, size):
    col = PALETTE[role]
    if style in STROKE:
        cap = "butt" if style == "w" else "square"
        return '<path d="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="%s" stroke-linejoin="miter" stroke-miterlimit="4"/>' % (
            d, col, STROKE[style], cap)
    op = FILL_OPACITY[style]
    return '<path d="%s" fill="%s"%s/>' % (d, col, "" if op == 1 else ' fill-opacity="%s"' % op)


def svg(parts, size, standalone=False):
    body = "".join(path_el(r, s, d, size) for r, s, d in parts if d and shown(s, size))
    ns = ' xmlns="http://www.w3.org/2000/svg"' if standalone else ""
    return '<svg%s width="%d" height="%d" viewBox="0 0 32 32" shape-rendering="geometricPrecision" aria-hidden="true">%s</svg>' % (
        ns, size, size, body)


def today_svg(data, size):
    sw = 1.5 if size >= 24 else 1.8
    return ('<svg width="%d" height="%d" viewBox="0 0 24 24" fill="none" stroke="#C9D1DB" stroke-width="%s" '
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="%s"/></svg>') % (size, size, sw, data)
