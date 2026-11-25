"""A13: the Home tab and the Quick Access Toolbar, drawn on the 32 grid (see lib.py for the roles and styles)."""

from lib import (P, R, O, E, G, p, head, tri, arc, cloud, along, plus, minus, cross, check, pencil, arrow_up,
                 arrow_down, arrow_right, star, cycle, stack, tag, block, doc, folder, disk, window, lens, bulb,
                 snowflake, sun, padlock, eye, gear, dimh, leader, letter_a, clipboard)

I = {}

# ---------------- Quick Access Toolbar ----------------
I["GhIconNew"] = doc(5, 3, 18, 26) + plus("BR")
I["GhIconOpen"] = folder(3, 6, 26, 20) + [p("b", "s", "M16 23V13"), p("b", "s", head(16, 13, -90, 5))]
I["GhIconSave"] = disk(5, 5, 22)
I["GhIconSaveAs"] = disk(3, 3, 20) + pencil("BR")
I["GhIconUndo"] = [p("b", "s", "M7 13h14a6 6 0 0 1 0 12h-6"), p("b", "s", P((12, 8), (7, 13), (12, 18)))]
I["GhIconRedo"] = [p("b", "s", "M25 13H11a6 6 0 0 0 0 12h6"), p("b", "s", P((20, 8), (25, 13), (20, 18)))]

# ---------------- Draw ----------------
I["GhIconLine"] = [p("k", "s", "M8 24 24 8"), p("b", "f", G(5, 27)), p("b", "f", G(27, 5))]
I["GhIconPolyline"] = [p("k", "s", "M5 25 11 9 19 21 27 7"), p("b", "f", G(5, 25)), p("b", "F", G(11, 9)),
                       p("b", "F", G(19, 21)), p("b", "f", G(27, 7))]
I["GhIconCircle"] = [p("k", "s", O(16, 16, 12)), p("y", "s", "M16 16 24.5 7.5"), p("b", "f", G(16, 16, 4))]
I["GhIconCircleCenterDiameter"] = [p("k", "s", O(16, 16, 12)), p("y", "s", "M7.5 24.5 24.5 7.5"), p("b", "f", G(16, 16, 4))]
I["GhIconCircle2P"] = [p("k", "s", O(16, 16, 11)), p("b", "f", G(5, 16)), p("b", "f", G(27, 16))]
I["GhIconCircle3P"] = [p("k", "s", O(16, 16, 11)), p("b", "f", G(16, 5)), p("b", "f", G(6.5, 21.5)), p("b", "f", G(25.5, 21.5))]
I["GhIconCircleTtr"] = [p("g", "s", "M3 27h26M3 27 15 5"), p("y", "s", O(16.5, 19, 8)), p("b", "S", "M16.5 19l5.7-5.7")]
I["GhIconCircleTtt"] = [p("g", "s", P((3, 27), (29, 27), (16, 4), close=True)), p("y", "s", O(16, 19.5, 7.5))]

ARC = "M7 25A10.8 10.8 0 1 1 25 25"
CEN = [p("y", "s", "M13 19h6M16 16v6")]
I["GhIconArc3P"] = [p("k", "s", ARC), p("b", "f", G(7, 25)), p("b", "f", G(16, 8)), p("b", "f", G(25, 25))]
I["GhIconArcSce"] = [p("k", "s", ARC), p("b", "f", G(7, 25)), p("b", "f", G(25, 25))] + CEN
I["GhIconArcSca"] = [p("k", "s", ARC), p("b", "f", G(7, 25)), p("g", "s", "M7 25 16 19 25 25"), p("y", "s", arc(16, 19, 5, 34, 146))]
I["GhIconArcScl"] = [p("k", "s", ARC), p("b", "f", G(7, 25)), p("y", "s", "M7 30h18M7 28v4M25 28v4")] + CEN
I["GhIconArcSea"] = [p("k", "s", ARC), p("b", "f", G(7, 25)), p("b", "f", G(25, 25)), p("y", "s", "M7 25 16 19 25 25")]
I["GhIconArcSed"] = [p("k", "s", ARC), p("b", "f", G(7, 25)), p("b", "f", G(25, 25)), p("y", "s", "M7 25 3 13"),
                     p("y", "s", head(3, 13, -108, 5))]
I["GhIconArcSer"] = [p("k", "s", ARC), p("b", "f", G(7, 25)), p("b", "f", G(25, 25)), p("y", "s", "M16 19V8")]
I["GhIconArcCse"] = [p("k", "s", ARC), p("b", "f", G(16, 19)), p("b", "f", G(7, 25)), p("b", "f", G(25, 25))]
I["GhIconArcCsa"] = [p("k", "s", ARC), p("b", "f", G(16, 19)), p("b", "f", G(7, 25)), p("g", "s", "M16 19 25 25"),
                     p("y", "s", arc(16, 19, 5, 34, 146))]
I["GhIconArcCsl"] = [p("k", "s", ARC), p("b", "f", G(16, 19)), p("b", "f", G(7, 25)), p("y", "s", "M7 30h18M7 28v4M25 28v4")]
I["GhIconArcContinue"] = [p("g", "s", "M3 25h8"), p("k", "s", "M11 25a12 12 0 0 0 12-12V9"), p("y", "s", P((19, 9), (23, 5), (27, 9))),
                          p("b", "f", G(11, 25))]
I["GhIconRectangle"] = [p("k", "s", R(5, 9, 22, 14)), p("b", "f", G(5, 23)), p("b", "f", G(27, 9))]
I["GhIconPolygon"] = [p("k", "s", P((16, 4), (26.4, 10), (26.4, 22), (16, 28), (5.6, 22), (5.6, 10), close=True)),
                      p("y", "s", "M16 16 26.4 10"), p("b", "f", G(16, 16, 4))]
I["GhIconEllipse"] = [p("k", "s", E(16, 16, 13, 8)), p("y", "s", "M16 16h13M16 16V8"), p("b", "f", G(16, 16, 4))]
I["GhIconEllipseAxisEnd"] = [p("k", "s", E(16, 16, 13, 8)), p("y", "s", "M3 16h26"), p("b", "f", G(3, 16)), p("b", "f", G(29, 16))]
I["GhIconEllipticalArc"] = [p("g", "d", "M29 16A13 8 0 0 1 3 16"), p("k", "s", "M3 16A13 8 0 0 1 29 16"),
                            p("b", "f", G(3, 16)), p("b", "f", G(29, 16))]
HATCH_BOX = R(5, 5, 22, 22)
I["GhIconHatch"] = [p("k", "s", HATCH_BOX), p("y", "s", "M5 13 13 5M7 27 27 7M23 27l4-4"), p("y", "S", "M5 21 21 5M15 27 27 15")]
I["GhIconGradient"] = [p("b", "f", R(6, 6, 8, 20)), p("b", "h", R(14, 6, 6, 20)), p("b", "t", R(20, 6, 6, 20)), p("k", "s", HATCH_BOX)]
I["GhIconBoundary"] = [p("g", "s", R(3, 3, 26, 26)), p("y", "s", P((9, 9), (23, 9), (23, 23), (9, 23), close=True)),
                       p("b", "s", "M16 13v6M13 16h6")]
I["GhIconSplineFit"] = [p("k", "s", "M3 25C7 7 13 7 16 16S25 25 29 7"), p("b", "f", G(3, 25)), p("b", "f", G(16, 16, 4)), p("b", "f", G(29, 7))]
I["GhIconSplineCv"] = [p("g", "s", "M5 27 9 5 23 27 27 5"), p("k", "s", "M5 27C8 9 13 8 16 16S24 23 27 5"),
                       p("b", "f", G(5, 27)), p("b", "F", G(9, 5)), p("b", "F", G(23, 27)), p("b", "f", G(27, 5))]
I["GhIconConstructionLine"] = [p("k", "s", "M3 29 29 3"), p("y", "s", P((23, 3), (29, 3), (29, 9))), p("y", "s", P((3, 23), (3, 29), (9, 29))),
                               p("b", "f", G(16, 16, 4))]
I["GhIconRay"] = [p("b", "f", G(5, 27)), p("k", "s", "M8 24 29 3"), p("y", "s", P((22, 3), (29, 3), (29, 10)))]
I["GhIconMultiplePoints"] = [p("b", "s", "M4 9h6M7 6v6M20 7h6M23 4v6M8 23h6M11 20v6M22 25h6M25 22v6")]
I["GhIconDivide"] = [p("k", "s", "M3 17h24"), p("k", "s", "M3 13v8M27 13v8"), p("b", "f", G(11, 17)), p("b", "f", G(19, 17))]
I["GhIconMeasure"] = [p("k", "s", "M3 21h26"), p("b", "s", "M9 17v8M15 17v8M21 17v8M27 17v8"), p("y", "s", "M3 11h6M3 9v4M9 9v4")]
I["GhIconRegion"] = [p("b", "t", P((5, 11), (13, 5), (27, 11), (25, 27), (9, 27), close=True)),
                     p("k", "s", P((5, 11), (13, 5), (27, 11), (25, 27), (9, 27), close=True))]
I["GhIconWipeout"] = [p("g", "s", "M3 11h6M3 21h6M23 11h6M23 21h6"), p("m", "f", R(9, 5, 14, 22)), p("k", "s", R(9, 5, 14, 22))]
I["GhIconDonut"] = [p("b", "h", O(16, 16, 12) + O(16, 16, 6, rev=True)), p("k", "s", O(16, 16, 12)), p("k", "s", O(16, 16, 6))]
_rect = [(5, 9), (27, 9), (27, 23), (5, 23)]
I["GhIconRevCloudRect"] = [p("r", "s", cloud(along(_rect, 5.5), 0.62))]
_poly = [(16, 4), (28, 12), (24, 27), (8, 27), (4, 12)]
I["GhIconRevCloudPoly"] = [p("r", "s", cloud(along(_poly, 6), 0.62))]
_free = [(6, 8), (13, 4), (21, 6), (28, 11), (26, 21), (19, 27), (10, 26), (4, 18)]
I["GhIconRevCloudFreehand"] = [p("r", "s", cloud(_free, 0.62)), p("y", "S", "M24 26l5 5")]

# ---------------- Modify ----------------
I["GhIconMove"] = [p("g", "s", R(3, 17, 10, 10)), p("y", "s", R(19, 5, 10, 10)), p("b", "s", "M11 21 22 10"), p("b", "s", head(22, 10, -45, 6))]
I["GhIconCopy"] = [p("k", "s", R(3, 3, 16, 16)), p("y", "t", R(13, 13, 16, 16)), p("y", "s", R(13, 13, 16, 16))]
I["GhIconRotate"] = [p("g", "s", "M9 23H29"), p("y", "s", "M9 23 23 9"), p("b", "s", arc(9, 23, 16, -2, -40)),
                     p("b", "S", head(*[9 + 16 * 0.766, 23 - 16 * 0.643], -130, 5)), p("b", "f", G(9, 23))]
I["GhIconScale"] = [p("y", "s", R(5, 5, 22, 22)), p("g", "s", R(5, 17, 10, 10)), p("b", "s", "M15 17 22 10"), p("b", "s", head(22, 10, -45, 5)),
                    p("b", "f", G(5, 27))]
I["GhIconMirror"] = [p("b", "s", "M15 3v4M15 11v4M15 19v4M15 27v2"), p("g", "s", P((11, 7), (3, 25), (11, 25), close=True)),
                     p("y", "s", P((19, 7), (27, 25), (19, 25), close=True))]
I["GhIconStretch"] = [p("g", "s", R(3, 9, 14, 12)), p("y", "s", "M17 9h10v12H17"), p("b", "s", "M5 27h20"), p("b", "s", head(25, 27, 0, 4))]
I["GhIconTrim"] = [p("b", "s", "M13 3v26"), p("k", "s", "M3 17h10"), p("r", "s", "M17 17h12"), p("r", "S", "M19 7l6 6M25 7l-6 6")]
I["GhIconExtend"] = [p("b", "s", "M27 3v26"), p("k", "s", "M3 17h10"), p("y", "s", "M13 17h14"), p("y", "S", head(23, 17, 0, 5))]
I["GhIconFillet"] = [p("g", "d", "M5.5 17V5.5H17"), p("k", "s", "M5 29V17M17 5h12"), p("y", "s", "M5 17A12 12 0 0 1 17 5")]
I["GhIconChamfer"] = [p("g", "d", "M5.5 15V5.5H15"), p("k", "s", "M5 29V15M15 5h14"), p("y", "s", "M5 15 15 5")]
I["GhIconBlend"] = [p("k", "s", "M3 23h8M21 9h8"), p("y", "s", "M11 23C19 23 13 9 21 9")]
_sq = lambda x, y, r: p(r, "f", R(x, y, 6, 6))
I["GhIconArrayRect"] = [_sq(4, 22, "k")] + [_sq(x, y, "y") for x in (4, 14, 24) for y in (2, 12, 22) if (x, y) != (4, 22)]
I["GhIconArrayPath"] = [p("b", "s", "M3 29C11 29 9 15 17 15S23 3 29 3"), _sq(2, 24, "k"), _sq(12, 12, "y"), _sq(24, 2, "y"), p("y", "F", R(19, 6, 4, 4))]
I["GhIconArrayPolar"] = [p("b", "f", G(16, 16, 4))] + [_sq(x - 3, y - 3, "k" if i == 0 else "y") for i, (x, y) in enumerate(
    [(16, 5), (25.5, 10.5), (25.5, 21.5), (16, 27), (6.5, 21.5), (6.5, 10.5)])]
I["GhIconErase"] = [p("g", "s", "M17 29h12"), p("r", "f", P((17, 9), (25, 17), (17, 25), (9, 17), close=True)),
                    p("k", "f", P((9, 17), (17, 25), (13, 29), (5, 21), close=True)), p("k", "s", P((17, 9), (25, 17), (13, 29), (5, 21), close=True))]
I["GhIconExplode"] = [p("k", "s", "M3 9V3h6M23 3h6v6M29 23v6h-6M9 29H3v-6")] + star(16, 16, 9, 3.6, "y")
I["GhIconOffset"] = [p("k", "s", "M5 29V17A12 12 0 0 1 17 5h12"), p("y", "s", "M11 29V17a6 6 0 0 1 6-6h12")]
I["GhIconSetByLayer"] = [p("k", "s", R(3, 3, 10, 10))] + [p("b", "s", "M15 8h6a4 4 0 0 1 4 4v1"), p("b", "s", head(25, 15, 90, 4))] + stack("b", "g", 6, 0)[0:2] \
    + [p("g", "s", P((8, 26), (18, 31), (28, 26)))]
I["GhIconChangeSpace"] = [p("c", "s", R(3, 3, 14, 12)), p("k", "s", R(15, 17, 14, 12)), p("y", "f", R(6, 6, 5, 5)), p("b", "s", "M9 15v8h4"), p("b", "s", head(13, 23, 0, 4))]
I["GhIconLengthen"] = [p("k", "s", "M3 13v8M3 17h14"), p("y", "s", "M17 17h10"), p("y", "s", head(27, 17, 0, 5))]
I["GhIconEditPolyline"] = [p("k", "s", "M3 19 8 7 13 15 19 5")] + pencil("BR")
I["GhIconEditSpline"] = [p("k", "s", "M3 17C6 3 10 3 12 11S17 13 19 3")] + pencil("BR")
I["GhIconEditHatch"] = [p("k", "s", R(3, 3, 14, 14)), p("y", "s", "M3 11l8-8M9 17l8-8")] + pencil("BR")
I["GhIconEditArray"] = [_sq(2, 2, "k"), _sq(12, 2, "y"), _sq(2, 12, "y"), _sq(12, 12, "y")] + pencil("BR")
I["GhIconAlign"] = [p("g", "s", R(3, 19, 10, 10)), p("y", "s", P((21, 3), (29, 11), (21, 19), (13, 11), close=True)),
                    p("b", "s", "M13 19l8-8"), p("b", "f", G(13, 19, 4))]
I["GhIconBreak"] = [p("k", "s", "M3 17h8M21 17h8"), p("r", "S", "M14 17h4"), p("b", "f", G(11, 17)), p("b", "f", G(21, 17))]
I["GhIconBreakAtPoint"] = [p("k", "s", "M3 17h10M19 17h10"), p("b", "f", G(16, 17)), p("b", "S", "M16 9v4M16 21v4")]
I["GhIconJoin"] = [p("k", "s", "M3 17h8M21 17h8"), p("y", "w", "M12 17h8"), p("b", "s", P((7, 9), (11, 13), (15, 9))), p("b", "S", P((17, 25), (21, 21), (25, 25)))]
I["GhIconReverse"] = [p("g", "s", "M5 11h22"), p("g", "s", head(27, 11, 0, 5)), p("y", "s", "M27 21H5"), p("y", "s", head(5, 21, 180, 5))]
I["GhIconOverkill"] = [p("k", "s", "M3 11h22"), p("r", "s", "M7 19h22"), p("r", "S", "M13 25l6 6M19 25l-6 6")]

# draw order
UP = arrow_up("BR")
DOWN = arrow_down("BR")
I["GhIconBringToFront"] = [p("g", "s", R(11, 3, 14, 14)), p("y", "f", R(4, 10, 14, 14))] + UP
I["GhIconSendToBack"] = [p("y", "f", R(12, 2, 14, 14)), p("g", "f", R(4, 10, 14, 14))] + DOWN
I["GhIconBringAbove"] = [p("g", "f", R(2, 16, 16, 8)), p("y", "f", R(6, 6, 10, 14))] + UP
I["GhIconSendUnder"] = [p("y", "f", R(6, 8, 10, 14)), p("g", "f", R(2, 12, 16, 8))] + DOWN
I["GhIconTextToFront"] = [p("g", "s", "M3 9l8-8M3 17l8-8")] + [p("y", "w", "M4 4h12M10 4v18")] + UP
I["GhIconDimsToFront"] = [p("g", "s", "M3 3v14M17 3v14")] + [p("y", "s", "M3 10h14"), p("y", "f", tri(3, 10, 180, 4, 2)), p("y", "f", tri(17, 10, 0, 4, 2))] + UP
I["GhIconLeadersToFront"] = [p("y", "s", "M4 18 10 8h8"), p("y", "f", tri(3, 20, 120, 5, 2.5))] + UP
I["GhIconAnnotationsToFront"] = [p("y", "w", "M2 4h10M7 4v10"), p("y", "s", "M3 21h14M3 18v6M17 18v6")] + UP
I["GhIconHatchesToBack"] = [p("y", "s", R(3, 5, 14, 14)), p("y", "s", "M3 13l8-8M9 19l8-8"), p("k", "s", "M1 25 21 9")] + DOWN

# ---------------- Annotation ----------------
I["GhIconText"] = letter_a(6, 5, 22, "k")
I["GhIconMText"] = letter_a(4, 4, 14, "k") + [p("g", "s", "M19 7h10M19 13h10M3 23h26M3 29h18"), p("b", "s", "M1 1h4M1 1v4M31 31h-4M31 31v-4")]
I["GhIconDimSmart"] = dimh(3, 21, 11, 5, 19) + [p("k", "f", P((17, 17), (29, 23), (24, 24), (22, 29), close=True))]
I["GhIconDimension"] = dimh(5, 27, 21, 9, 27)
I["GhIconAligned"] = [p("g", "s", "M5 21l6 6M21 5l6 6"), p("k", "s", "M10 16 16 10"), p("b", "f", tri(7, 19, 135, 6, 3)), p("b", "f", tri(19, 7, -45, 6, 3)),
                      p("y", "w", "M18 20l4-4")]
I["GhIconAngular"] = [p("g", "s", "M5 27h24M5 27 19 7"), p("k", "s", arc(5, 27, 15, -6, -48)), p("b", "f", tri(20, 25.5, 90, 5, 2.5)),
                      p("b", "f", tri(15, 15.9, 230, 5, 2.5)), p("y", "w", "M22 12l4-4")]
I["GhIconDimArc"] = [p("k", "s", arc(16, 28, 9, 200, 340)), p("b", "s", arc(16, 28, 15, 205, 335)), p("g", "s", "M4 22l-2-2M28 22l2-2"),
                     p("y", "s", "M12 8a4 4 0 0 1 8 0")]
I["GhIconRadius"] = [p("k", "s", O(16, 16, 12)), p("b", "s", "M16 16 23 9"), p("b", "f", tri(24.5, 7.5, -45, 6, 3)), p("b", "f", G(16, 16, 4)),
                     p("y", "w", "M8 24h8")]
I["GhIconDiameter"] = [p("k", "s", O(16, 16, 12)), p("b", "s", "M10 22 22 10"), p("b", "f", tri(7.5, 24.5, 135, 6, 3)), p("b", "f", tri(24.5, 7.5, -45, 6, 3))]
I["GhIconDimOrdinate"] = [p("g", "s", "M5 29V3M5 29h24"), p("k", "s", "M13 21V13l4-4h10"), p("b", "f", G(13, 21, 4)), p("y", "w", "M18 4h10")]
I["GhIconDimJogged"] = [p("k", "s", "M3 29A26 26 0 0 1 29 3"), p("b", "s", "M27 27 21 21 25 17 15 11"), p("b", "f", tri(11, 8, 210, 6, 3))]
I["GhIconLeader"] = leader(5, 27, 15, 15, 29)
I["GhIconAddLeader"] = leader(3, 29, 11, 19, 17, False) + [p("y", "w", "M12 13h6")] + plus("TR")
I["GhIconRemoveLeader"] = leader(3, 29, 11, 19, 17, False) + [p("y", "w", "M12 13h6")] + minus("TR")
I["GhIconLeaderAlign"] = [p("b", "s", "M9 3v26"), p("y", "w", "M12 7h14M12 17h10M12 27h14")] + [p("k", "S", "M3 9l6-2M3 19l6-2")]
I["GhIconLeaderCollect"] = [p("k", "s", "M5 29l6-8"), p("b", "f", tri(4, 30.5, 125, 5, 2.5)), p("y", "s", O(8, 15, 4)), p("y", "s", O(17, 15, 4)), p("y", "s", O(26, 15, 4)),
                            p("k", "S", "M3 9h26")]
I["GhIconTable"] = [p("c", "f", R(3, 5, 26, 6)), p("k", "s", R(3, 5, 26, 22)), p("k", "s", "M3 11h26M3 19h26M11 11v16")]
STYLE_BADGE = [p("c", "f", R(18, 18, 12, 12)), p("m", "s", "M21 23h6M21 27h4")]
I["GhIconTextStyle"] = letter_a(3, 3, 14, "k") + STYLE_BADGE
I["GhIconDimStyle"] = [p("g", "s", "M3 3v12M17 3v12"), p("k", "s", "M3 9h14"), p("b", "f", tri(3, 9, 180, 4, 2)), p("b", "f", tri(17, 9, 0, 4, 2))] + STYLE_BADGE
I["GhIconMLeaderStyle"] = [p("k", "s", "M4 16 10 8h7"), p("b", "f", tri(3, 18, 125, 5, 2.5))] + STYLE_BADGE
I["GhIconTableStyle"] = [p("c", "f", R(3, 3, 14, 4)), p("k", "s", R(3, 3, 14, 12)), p("k", "s", "M3 9h14M9 9v6")] + STYLE_BADGE

# ---------------- Layers ----------------
I["GhIconLayers"] = [p("g", "s", P((3, 23), (16, 29), (29, 23))), p("c", "t", P((3, 17), (16, 11), (29, 17), (16, 23), close=True)),
                     p("c", "s", P((3, 17), (16, 11), (29, 17), (16, 23), close=True)),
                     p("b", "t", P((3, 10), (16, 4), (29, 10), (16, 16), close=True)), p("k", "s", P((3, 10), (16, 4), (29, 10), (16, 16), close=True))]
I["GhIconLayerState"] = stack() + [p("c", "f", R(18, 1, 12, 14)), p("m", "s", "M21 6h6M21 10h6")]
I["GhIconLayerPrevious"] = stack() + [p("b", "s", "M21 5h5a3 3 0 0 1 0 6h-2"), p("b", "s", P((24, 2), (21, 5), (24, 8)))]
I["GhIconLayerChangeToCurrent"] = stack() + [p("y", "f", R(4, 2, 8, 8)), p("b", "s", "M12 6h10v8"), p("b", "s", head(22, 14, 90, 4))]
I["GhIconLayerCopyToNew"] = stack() + [p("y", "f", R(4, 2, 8, 8))] + plus("TR")
I["GhIconLayerWalk"] = stack() + arrow_right("TR")
I["GhIconLayerVpFreeze"] = stack() + [p("c", "s", R(17, 1, 14, 14))] + snowflake(24, 8, 4.5, "c")
I["GhIconLayerMerge"] = stack() + [p("b", "s", P((18, 2), (24, 8), (30, 2))), p("b", "s", "M24 8v7")]
I["GhIconLayerDelete"] = stack() + cross("TR")
I["GhIconLayerOff"] = stack("g", "g") + bulb(19, 1, "g", False)
I["GhIconLayerOn"] = stack() + bulb(19, 1, "y", True)
I["GhIconLayerIsolate"] = stack("y", "g") + [p("b", "s", "M18 2h4M26 2h4v4M30 10v4h-4M22 14h-4v-4M18 6V2")]
I["GhIconLayerUnisolate"] = stack() + cycle(24, 8, 5)
I["GhIconLayerFreeze"] = stack() + snowflake(24, 8, 6.5, "c")
I["GhIconLayerThaw"] = stack() + sun(24, 8)
I["GhIconLayerLock"] = stack() + padlock(18, 1, True)
I["GhIconLayerUnlock"] = stack() + padlock(18, 1, False)
I["GhIconCurrent"] = stack() + check("TR")
I["GhIconMatch"] = stack() + [p("k", "s", "M30 2l-6 6"), p("y", "f", P((21, 7), (25, 11), (20, 16), (16, 16), (16, 12), close=True))]

# ---------------- Block ----------------
I["GhIconInsertBlock"] = block(13, 13, 16, "y", "y") + [p("b", "s", "M3 3l9 9"), p("b", "s", P((5, 12), (12, 12), (12, 5)))]
I["GhIconCreateBlock"] = [p("b", "s", "M3 7V3h4M13 3h4M3 13v4M17 13v4h-4M7 17H3"), p("k", "s", O(8, 8, 3)), p("k", "s", "M10 15l3-5 3 5z")] + plus("BR")
I["GhIconWriteBlock"] = disk(3, 3, 20) + block(17, 17, 14, "y", "y")
I["GhIconBlockEditor"] = block(3, 3, 14) + pencil("BR")
I["GhIconEditAttribute"] = tag(3, 4, 22) + [p("g", "s", "M3 19h12M3 25h8")] + pencil("BR")
I["GhIconAttEditMultiple"] = tag(9, 3, 20, "g", None) + tag(3, 11, 20) + [p("g", "s", "M3 25h16")]
I["GhIconDefineAttributes"] = tag(3, 4, 22) + [p("g", "s", "M3 19h12")] + plus("BR")
I["GhIconManageAttributes"] = tag(3, 3, 18) + tag(3, 13, 18, "g", None) + tag(3, 23, 18, "g", None) + [p("b", "s", "M27 5v22"), p("b", "s", head(27, 5, -90, 4)), p("b", "s", head(27, 27, 90, 4))]
I["GhIconAttSync"] = tag(3, 3, 20) + cycle(24, 24, 6)
I["GhIconAttDisplay"] = tag(3, 3, 20) + eye(16, 23, 12, 5)
I["GhIconBasePoint"] = [p("g", "s", R(9, 3, 20, 20)), p("b", "s", "M9 17v12M3 23h12"), p("b", "f", G(9, 23))]

# ---------------- Properties ----------------
I["GhIconMatchProperties"] = [p("k", "w", "M28 4 20 12"), p("g", "f", P((17, 11), (21, 15), (18, 18), (14, 14), close=True)),
                              p("y", "f", P((14, 14), (18, 18), (11, 27), (5, 27), (5, 21), close=True)), p("y", "s", "M17 29h12")]
I["GhIconObjectColor"] = [p("r", "f", "M16 16V4A12 12 0 0 1 26.4 22z"), p("n", "f", "M16 16 26.4 22A12 12 0 0 1 5.6 22z"),
                          p("b", "f", "M16 16 5.6 22A12 12 0 0 1 16 4z"), p("k", "s", O(16, 16, 12))]
I["GhIconLineweight"] = [p("k", "f", R(3, 5, 26, 2)), p("k", "f", R(3, 12, 26, 4)), p("k", "f", R(3, 21, 26, 6))]
I["GhIconLinetype"] = [p("k", "s", "M3 7h26"), p("k", "s", "M3 17h6M13 17h6M23 17h6"), p("y", "s", "M3 27h10M17 27h2M23 27h6")]
I["GhIconPlotStyle"] = doc(5, 3, 22, 26, 6) + [p("k", "s", "M9 13h12"), p("k", "f", R(9, 17, 14, 2)), p("k", "f", R(9, 22, 14, 4))]
I["GhIconTransparency"] = [p("k", "s", R(3, 3, 18, 18)), p("b", "h", R(12, 12, 18, 18)), p("b", "s", R(11, 11, 18, 18))]
I["GhIconList"] = [p("b", "f", R(3, 5, 4, 4)), p("b", "f", R(3, 15, 4, 4)), p("b", "f", R(3, 25, 4, 4)), p("k", "s", "M11 7h18M11 17h18M11 27h14")]

# ---------------- Groups ----------------
GROUP_IN = [p("k", "s", R(7, 7, 9, 9)), p("k", "s", O(21, 21, 5))]
I["GhIconGroup"] = GROUP_IN + [p("b", "s", "M3 9V3h6M23 3h6v6M29 23v6h-6M9 29H3v-6")]
I["GhIconUngroup"] = [p("k", "s", R(3, 3, 9, 9)), p("k", "s", O(23, 23, 5)), p("r", "s", "M17 3l12 12M29 3 17 15")]
I["GhIconGroupEdit"] = [p("k", "s", R(3, 3, 8, 8)), p("k", "s", O(17, 7, 4)), p("b", "s", "M1 15h14")] + pencil("BR")
I["GhIconPickStyle"] = [p("y", "s", "M3 9V3h6M17 3h6v6M23 17v6h-6M9 23H3v-6"), p("k", "s", R(7, 7, 12, 12)),
                        p("k", "f", P((17, 17), (29, 22), (24, 24), (22, 29), close=True))]
I["GhIconGroupManager"] = window(3, 3, 26, 26) + [p("k", "s", R(7, 11, 6, 6)), p("k", "s", "M17 14h8M7 23h18")]

# ---------------- Utilities ----------------
RULER = P((3, 23), (23, 3), (29, 9), (9, 29), close=True)
I["GhIconMeasureGeom"] = [p("y", "t", RULER), p("y", "s", RULER), p("k", "s", "M9 17l3 3M13 13l2 2M17 9l3 3M21 5l2 2")]
I["GhIconMeasureQuick"] = [p("g", "s", R(3, 3, 18, 14)), p("y", "s", "M3 23h18M3 21v4M21 21v4"), p("k", "f", P((19, 15), (31, 21), (26, 22), (24, 27), close=True))]
I["GhIconDistance"] = [p("b", "f", G(5, 27)), p("b", "f", G(27, 5)), p("k", "s", "M8 24 24 8"), p("y", "s", "M3 15 15 3M1 13l4 4M13 1l4 4")]
I["GhIconArea"] = [p("y", "t", P((5, 9), (21, 5), (27, 23), (9, 27), close=True)), p("k", "s", P((5, 9), (21, 5), (27, 23), (9, 27), close=True)),
                   p("b", "f", G(5, 9)), p("b", "f", G(21, 5)), p("b", "f", G(27, 23)), p("b", "f", G(9, 27))]
I["GhIconId"] = [p("b", "f", "M16 30s-9-9-9-16a9 9 0 0 1 18 0c0 7-9 16-9 16z"), p("m", "f", O(16, 13, 3.5))]
I["GhIconPointStyle"] = [p("k", "s", O(8, 8, 4)), p("k", "s", "M24 3v10M19 8h10M4 20l8 8M12 20l-8 8"), p("y", "s", R(19, 19, 10, 10)), p("y", "f", G(24, 24, 4))]
I["GhIconQuickSelect"] = [p("b", "t", P((3, 3), (19, 3), (13, 12), (13, 21), (9, 23), (9, 12), close=True)),
                          p("b", "s", P((3, 3), (19, 3), (13, 12), (13, 21), (9, 23), (9, 12), close=True)),
                          p("y", "f", R(20, 14, 8, 8)), p("g", "s", R(19, 25, 10, 4))]
I["GhIconSelectAll"] = [p("b", "s", "M3 7V3h4M13 3h6M25 3h4v4M29 13v6M29 25v4h-4M19 29h-6M7 29H3v-4M3 19v-6"), p("y", "s", R(9, 9, 6, 6)), p("y", "s", O(20, 20, 3.5))]
I["GhIconQuickCalc"] = [p("k", "s", R(7, 3, 18, 26)), p("c", "f", R(10, 6, 12, 6)), p("k", "f", "M10 16h3v3h-3zM15 16h3v3h-3zM20 16h2v3h-2zM10 22h3v3h-3zM15 22h3v3h-3z"),
                        p("y", "f", R(20, 22, 2, 3))]

# ---------------- Clipboard ----------------
I["GhIconPaste"] = clipboard(3, 5, 20, 24) + [p("c", "t", R(13, 13, 16, 16)), p("k", "s", R(13, 13, 16, 16)), p("k", "S", "M17 19h8M17 23h6")]
I["GhIconPasteBlock"] = clipboard(3, 5, 20, 24) + block(15, 15, 14, "y", "y")
I["GhIconPasteOriginal"] = clipboard(3, 5, 20, 24) + [p("y", "s", "M17 29V15M17 29h12"), p("b", "f", G(17, 29))]
I["GhIconPasteSpecial"] = clipboard(3, 5, 20, 24) + star(23, 23, 8, 3.5, "y")
I["GhIconCut"] = [p("k", "s", "M11 19 25 3M21 19 7 3"), p("r", "s", O(8, 24, 4)), p("r", "s", O(24, 24, 4))]
I["GhIconCopyBase"] = [p("k", "s", R(3, 3, 16, 16)), p("y", "t", R(13, 13, 16, 16)), p("y", "s", R(13, 13, 16, 16)), p("b", "f", G(13, 29))]

# ---------------- View (Home) ----------------
I["GhIconExtents"] = [p("b", "s", "M3 11V3h8M21 3h8v8M29 21v8h-8M11 29H3v-8"), p("k", "s", R(9, 9, 14, 14)), p("y", "f", R(13, 13, 6, 6))]
I["GhIconPan"] = [p("k", "s", "M11 17V7a2 2 0 0 1 4 0v8M15 15V5a2 2 0 0 1 4 0v10M19 15V7a2 2 0 0 1 4 0v12a10 10 0 0 1-10 10h-1a8 8 0 0 1-6-3l-4-6a2 2 0 0 1 3-3l3 3v-7a2 2 0 0 1 3 0"),
                  p("b", "S", "M3 5h4M5 3v4")]
I["GhIconSearch"] = lens(13, 13, 9)
