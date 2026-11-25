"""A13 icon drawing kit: parts on a 32 by 32 grid, drawn at 32 px (large) and at half scale, 16 px (small).

A part is (role, style, d):
  role  k ink, g grey, b blue, c teal, y yellow, r red, n green, m dark (the panel colour, for knock-outs)
  style s  main stroke, 2 units: 2 px at 32, 1 px at 16 (centre lines on odd coordinates are crisp at both)
        w  wide stroke, 4 units, butt ends: 4 px at 32, 2 px at 16 (centre lines on even coordinates)
        f  fill;  t  tint (fill at 30 %);  h  half fill (60 %)
        d  detail stroke, 1 unit, 32 only;   S / F  stroke / fill drawn at 32 only;   x / X  at 16 only
"""

import math

PALETTE = {
    "k": "#E6EBF0",
    "g": "#8E99A8",
    "b": "#4DA3FF",
    "c": "#2BC4B4",
    "y": "#F5C242",
    "r": "#F2584B",
    "n": "#45C45A",
    "m": "#262B33",
}


def f(v):
    s = ("%.2f" % v).rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def R(x, y, w, h):
    return "M%s %sh%sv%sh%sz" % (f(x), f(y), f(w), f(h), f(-w))


def O(cx, cy, r, rev=False):
    sw = "0" if not rev else "1"
    return "M%s %sA%s %s 0 1 %s %s %sA%s %s 0 1 %s %s %sz" % (
        f(cx - r), f(cy), f(r), f(r), sw, f(cx + r), f(cy), f(r), f(r), sw, f(cx - r), f(cy))


def E(cx, cy, rx, ry):
    return "M%s %sA%s %s 0 1 0 %s %sA%s %s 0 1 0 %s %sz" % (
        f(cx - rx), f(cy), f(rx), f(ry), f(cx + rx), f(cy), f(rx), f(ry), f(cx - rx), f(cy))


def G(x, y, s=6):
    return R(x - s / 2, y - s / 2, s, s)


def P(*pts, close=False):
    d = "M" + " ".join("%s %s" % (f(a), f(b)) for a, b in pts)
    return d + ("z" if close else "")


def p(role, style, d):
    return (role, style, d)


def head(x, y, ang, size=5, spread=40):
    """An open arrowhead (two strokes) with its tip at x, y pointing along ang degrees (0 right, 90 down)."""
    a1 = math.radians(ang + 180 - spread)
    a2 = math.radians(ang + 180 + spread)
    return P((x + size * math.cos(a1), y + size * math.sin(a1)), (x, y), (x + size * math.cos(a2), y + size * math.sin(a2)))


def tri(x, y, ang, size=6, half=3):
    """A filled arrowhead triangle, tip at x, y, pointing along ang degrees."""
    a = math.radians(ang)
    bx, by = x - size * math.cos(a), y - size * math.sin(a)
    nx, ny = -math.sin(a) * half, math.cos(a) * half
    return P((x, y), (bx + nx, by + ny), (bx - nx, by - ny), close=True)


def arc(cx, cy, r, a0, a1):
    """An arc from angle a0 to a1 (degrees, y down, clockwise positive)."""
    x0, y0 = cx + r * math.cos(math.radians(a0)), cy + r * math.sin(math.radians(a0))
    x1, y1 = cx + r * math.cos(math.radians(a1)), cy + r * math.sin(math.radians(a1))
    large = 1 if abs(a1 - a0) > 180 else 0
    sweep = 1 if a1 > a0 else 0
    return "M%s %sA%s %s 0 %d %d %s %s" % (f(x0), f(y0), f(r), f(r), large, sweep, f(x1), f(y1))


def cloud(pts, bulge=0.75):
    """A revision cloud through the points (closed), each chord an arc bulging outward."""
    d = "M%s %s" % (f(pts[0][0]), f(pts[0][1]))
    n = len(pts)
    for i in range(n):
        a, b = pts[i], pts[(i + 1) % n]
        ch = math.hypot(b[0] - a[0], b[1] - a[1])
        r = ch * bulge
        d += "A%s %s 0 0 1 %s %s" % (f(r), f(r), f(b[0]), f(b[1]))
    return d + "z"


def along(pts, step):
    """Points every `step` along a closed polygon."""
    out = []
    n = len(pts)
    for i in range(n):
        a, b = pts[i], pts[(i + 1) % n]
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        k = max(1, round(L / step))
        for j in range(k):
            t = j / k
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
    return out


# ---- badges: a 14 by 14 box, top-right (TR: 17..31 x 1..15) or bottom-right (BR: 17..31 x 17..31) ----
BOX = {"TR": (17, 1), "BR": (17, 17), "BL": (1, 17), "TL": (1, 1)}


def plus(at="BR", role="n"):
    x, y = BOX[at]
    return [p(role, "w", "M%d %dv12M%d %dh12" % (x + 7, y + 1, x + 1, y + 7))]


def minus(at="BR", role="r"):
    x, y = BOX[at]
    return [p(role, "w", "M%d %dh12" % (x + 1, y + 7))]


def cross(at="BR", role="r"):
    x, y = BOX[at]
    return [p(role, "w", "M%d %dl10 10M%d %dl-10 10" % (x + 2, y + 2, x + 12, y + 2))]


def check(at="BR", role="n"):
    x, y = BOX[at]
    return [p(role, "w", "M%d %dl4 4 8-8" % (x + 1, y + 7))]


def pencil(at="BR"):
    x, y = BOX[at]
    # body from the tip at the box's bottom-left to its top-right
    return [
        p("y", "f", P((x, y + 14), (x, y + 9), (x + 9, y), (x + 14, y + 5), (x + 5, y + 14), close=True)),
        p("k", "f", P((x, y + 14), (x, y + 10), (x + 4, y + 14), close=True)),
        p("m", "d", P((x + 7, y + 2), (x + 12, y + 7))),
    ]


def arrow_up(at="BR", role="b"):
    x, y = BOX[at]
    return [p(role, "s", "M%d %dV%d" % (x + 9, y + 13, y + 2)),
            p(role, "s", P((x + 4, y + 7), (x + 9, y + 2), (x + 14, y + 7)))]


def arrow_down(at="BR", role="b"):
    x, y = BOX[at]
    return [p(role, "s", "M%d %dV%d" % (x + 9, y + 1, y + 12)),
            p(role, "s", P((x + 4, y + 7), (x + 9, y + 12), (x + 14, y + 7)))]


def arrow_right(at="TR", role="b"):
    x, y = BOX[at]
    return [p(role, "s", "M%d %dh11" % (x + 1, y + 7)), p(role, "s", P((x + 8, y + 3), (x + 12, y + 7), (x + 8, y + 11)))]


def star(cx, cy, r1, r2, role="y", n=5, style="f"):
    pts = []
    for i in range(2 * n):
        a = math.radians(-90 + i * 180 / n)
        r = r1 if i % 2 == 0 else r2
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return [p(role, style, P(*pts, close=True))]


def cycle(cx, cy, r, role="b"):
    """Two curved arrows round a centre: sync, refresh, regenerate."""
    return [
        p(role, "s", arc(cx, cy, r, 200, 330)), p(role, "s", head(*_pt(cx, cy, r, 330), 60, 4)),
        p(role, "s", arc(cx, cy, r, 20, 150)), p(role, "s", head(*_pt(cx, cy, r, 150), 240, 4)),
    ]


def _pt(cx, cy, r, a):
    return cx + r * math.cos(math.radians(a)), cy + r * math.sin(math.radians(a))


# ---- recurring objects ----
def stack(top="b", low="g", x=1, y=0):
    """The layer stack, bottom-left (two layers), leaving the top-right for a badge."""
    t = P((x + 2, y + 21), (x + 12, y + 16), (x + 22, y + 21), (x + 12, y + 26), close=True)
    return [p(top, "t", t), p("k" if top in ("b", "c") else top, "s", t),
            p(low, "s", P((x + 2, y + 26), (x + 12, y + 31), (x + 22, y + 26)))]


def tag(x, y, w=16, role="k", tint="c"):
    """An attribute tag: a label with a pointed end."""
    d = P((x, y), (x + w - 4, y), (x + w, y + 4), (x + w - 4, y + 8), (x, y + 8), close=True)
    out = [p(tint, "t", d)] if tint else []
    return out + [p(role, "s", d)]


def block(x, y, s=14, role="k", tint="b"):
    """A block: a frame holding a circle and a triangle."""
    out = [p(tint, "t", R(x, y, s, s))] if tint else []
    q = s / 14.0
    return out + [p(role, "s", R(x, y, s, s)), p(role, "S", O(x + 4.5 * q, y + 4.5 * q, 2.5 * q)),
                  p(role, "S", P((x + 7 * q, y + 11 * q), (x + 10 * q, y + 6 * q), (x + 12 * q, y + 11 * q), close=True))]


def doc(x=7, y=3, w=18, h=26, fold=7, role="k", tint="c"):
    d = P((x, y), (x + w - fold, y), (x + w, y + fold), (x + w, y + h), (x, y + h), close=True)
    out = [p(tint, "t", d)] if tint else []
    return out + [p(role, "s", d), p(role, "s", P((x + w - fold, y), (x + w - fold, y + fold), (x + w, y + fold)))]


def folder(x=3, y=7, w=26, h=20, role="y"):
    d = P((x, y), (x + 9, y), (x + 12, y + 3), (x + w, y + 3), (x + w, y + h), (x, y + h), close=True)
    return [p(role, "t", d), p(role, "s", d), p(role, "s", P((x, y + 7), (x + w, y + 7)))]


def disk(x=5, y=5, s=22, role="b"):
    d = P((x, y), (x + s - 5, y), (x + s, y + 5), (x + s, y + s), (x, y + s), close=True)
    return [p(role, "t", d), p(role, "s", d), p("k", "f", R(x + 5, y + 2, s - 12, 6)),
            p("k", "s", P((x + 5, y + s), (x + 5, y + s - 8), (x + s - 5, y + s - 8), (x + s - 5, y + s)))]


def window(x, y, w, h, role="c", bar=True):
    out = [p(role, "s", R(x, y, w, h))]
    if bar:
        out.append(p(role, "f", R(x, y, w, 4)))
    return out


def lens(cx=13, cy=13, r=8, role="k"):
    return [p("b", "t", O(cx, cy, r)), p(role, "s", O(cx, cy, r)),
            p(role, "w", P((cx + r * 0.72 + 1, cy + r * 0.72 + 1), (cx + r * 0.72 + 8, cy + r * 0.72 + 8)))]


def bulb(x, y, role="y", fill=True):
    """A light bulb in a 12 by 14 box at x, y."""
    d = "M%s %sA5.5 5.5 0 1 1 %s %sV%sH%sz" % (f(x + 3.5), f(y + 9.5), f(x + 8.5), f(y + 9.5), f(y + 11), f(x + 3.5))
    out = [p(role, "f" if fill else "t", d), p(role, "s", d)]
    return out + [p(role if fill else "g", "s", "M%s %sh4" % (f(x + 4), f(y + 13)))]


def snowflake(cx, cy, r, role="c"):
    d = ""
    for a in (90, 30, -30):
        x0, y0 = _pt(cx, cy, r, a)
        x1, y1 = _pt(cx, cy, r, a + 180)
        d += P((x0, y0), (x1, y1))
    return [p(role, "s", d)]


def sun(cx, cy, role="y"):
    d = ""
    for i in range(8):
        a = i * 45
        x0, y0 = _pt(cx, cy, 4.5, a)
        x1, y1 = _pt(cx, cy, 6.5, a)
        d += P((x0, y0), (x1, y1))
    return [p(role, "f", O(cx, cy, 3)), p(role, "s", d)]


def padlock(x, y, closed=True, role="y"):
    """A padlock in a 12 by 14 box at x, y."""
    body = R(x + 1, y + 6, 10, 7)
    shackle = "M%d %dV%dA3 3 0 0 1 %d %d" % (x + 3, y + 6, y + 4, x + 9, y + 4)
    shackle += ("V%d" % (y + 6)) if closed else ""
    return [p(role, "f", body), p("k", "s", shackle)]


def eye(cx, cy, w=10, h=6, role="k"):
    d = "M%s %sQ%s %s %s %sQ%s %s %s %sz" % (f(cx - w), f(cy), f(cx), f(cy - h * 1.6), f(cx + w), f(cy), f(cx), f(cy + h * 1.6), f(cx - w), f(cy))
    return [p(role, "s", d), p("b", "f", O(cx, cy, 2.5))]


def gear(cx, cy, r=9, role="k"):
    pts = []
    for i in range(16):
        a = math.radians(i * 22.5 - 11.25)
        rr = r if (i // 2) % 2 == 0 else r - 3
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    return [p("b", "t", P(*pts, close=True)), p(role, "s", P(*pts, close=True)), p(role, "s", O(cx, cy, r * 0.38))]


def dimh(x0, x1, y, ext_top, ext_bottom, text=True, role_line="k"):
    """A horizontal linear dimension: grey extension lines, ink line, blue arrowheads, yellow text bar."""
    out = [p("g", "s", "M%d %dV%dM%d %dV%d" % (x0, ext_top, ext_bottom, x1, ext_top, ext_bottom)),
           p(role_line, "s", "M%d %dH%d" % (x0 + 5, y, x1 - 5)),
           p("b", "f", tri(x0, y, 180)), p("b", "f", tri(x1, y, 0))]
    if text:
        mid = (x0 + x1) // 2
        mid -= mid % 2
        out.append(p("y", "w", "M%d %dh%d" % (mid - 5, y - 6, 10)))
    return out


def leader(x0=5, y0=27, x1=15, y1=15, land=27, text=True):
    out = [p("k", "s", P((x0 + 3, y0 - 3), (x1, y1), (land, y1))), p("b", "f", tri(x0, y0, math.degrees(math.atan2(y0 - y1, x0 - x1)), 7, 3.5))]
    if text:
        out.append(p("y", "w", "M%d %dh%d" % (x1 + 2, y1 - 6, land - x1 - 2)))
    return out


def letter_a(x, y, h=22, role="k", style="s"):
    w = h * 0.8
    return [p(role, style, P((x, y + h), (x + w / 2, y), (x + w, y + h)) + "M%s %sH%s" % (f(x + w * 0.2), f(y + h * 0.62), f(x + w * 0.8)))]


def clipboard(x=5, y=5, w=18, h=24):
    board = R(x, y + 2, w, h - 2)
    return [p("g", "t", board), p("k", "s", board), p("g", "f", R(x + 5, y - 1, w - 10, 6))]
