"""A13: Insert, Annotate, Parametric, View, Manage, Output, Settings and the contextual tabs, on the 32 grid."""

from lib import (P, R, O, E, G, p, head, tri, arc, plus, minus, cross, check, pencil, arrow_up, arrow_down,
                 arrow_right, star, cycle, stack, tag, block, doc, folder, disk, window, lens, bulb, snowflake, padlock,
                 eye, gear, dimh, letter_a)
import icons_home

H = icons_home.I
I = {}

# ---------------- Insert ----------------
I["GhIconAttach"] = doc(3, 3, 18, 24) + [p("y", "s", R(15, 15, 14, 14)), p("b", "s", "M7 11l8 8"), p("b", "s", head(15, 19, 45, 4))]
I["GhIconEditReference"] = [p("c", "s", R(3, 3, 16, 16)), p("y", "s", R(7, 7, 8, 8))] + pencil("BR")
I["GhIconRefSave"] = [p("c", "s", R(3, 3, 16, 16)), p("y", "s", R(7, 7, 8, 8))] + check("BR")
I["GhIconRefDiscard"] = [p("c", "s", R(3, 3, 16, 16)), p("y", "s", R(7, 7, 8, 8))] + cross("BR")
I["GhIconRefAdd"] = [p("c", "s", R(3, 3, 16, 16)), p("k", "f", P((7, 7), (17, 11), (12, 12), (11, 17), close=True))] + plus("BR")
I["GhIconRefRemove"] = [p("c", "s", R(3, 3, 16, 16)), p("k", "f", P((7, 7), (17, 11), (12, 12), (11, 17), close=True))] + minus("BR")
I["GhIconClip"] = [p("g", "s", R(3, 3, 26, 26)), p("y", "s", P((9, 9), (23, 9), (23, 19), (15, 23), (9, 23), close=True)), p("r", "S", "M23 25l6 6M29 25l-6 6")]
I["GhIconAdjust"] = [p("k", "s", O(16, 16, 12)), p("k", "f", "M16 4A12 12 0 0 1 16 28z"), p("y", "S", "M16 1v2")]
I["GhIconUnderlayLayers"] = H["GhIconLayers"][:3] + [p("c", "t", P((3, 10), (16, 4), (29, 10), (16, 16), close=True)), p("c", "s", P((3, 10), (16, 4), (29, 10), (16, 16), close=True))]
I["GhIconImageFrame"] = [p("c", "s", R(3, 3, 26, 26)), p("b", "t", R(9, 9, 14, 14)), p("k", "s", "M9 23l5-6 3 3 3-4 3 7")]
I["GhIconSnapUnderlay"] = [p("c", "s", R(3, 3, 26, 26)), p("y", "s", R(11, 11, 10, 10)), p("b", "s", "M16 5v4M16 23v4M5 16h4M23 16h4")]
I["GhIconImport"] = doc(13, 3, 16, 26, 6) + [p("b", "s", "M3 16h14"), p("b", "s", head(17, 16, 0, 5))]
I["GhIconCombineText"] = [p("g", "s", "M3 5h10M3 11h8M19 5h10M19 11h8"), p("b", "s", P((9, 15), (16, 20), (23, 15))), p("y", "s", "M3 25h26M3 29h18")]
I["GhIconField"] = [p("k", "s", R(3, 7, 26, 18)), p("c", "t", R(3, 7, 26, 18)), p("y", "s", "M11 11l-4 5 4 5M21 11l4 5-4 5"), p("k", "s", "M14 21l4-10")]
I["GhIconUpdateFields"] = [p("c", "t", R(3, 3, 18, 12)), p("k", "s", R(3, 3, 18, 12)), p("y", "s", "M7 9h10")] + cycle(24, 23, 6)
I["GhIconOleObject"] = window(5, 3, 22, 26) + [p("n", "f", R(9, 11, 6, 6)), p("b", "f", R(17, 11, 6, 6)), p("y", "f", R(9, 19, 6, 6)), p("r", "f", R(17, 19, 6, 6))]
I["GhIconHyperlink"] = [p("b", "s", "M14 18a5 5 0 0 0 7 0l5-5a5 5 0 0 0-7-7l-2 2"), p("k", "s", "M18 14a5 5 0 0 0-7 0l-5 5a5 5 0 0 0 7 7l2-2")]
I["GhIconDataLink"] = [p("n", "t", R(3, 3, 18, 14)), p("k", "s", R(3, 3, 18, 14)), p("k", "s", "M3 9h18M11 3v14"), p("n", "f", R(4, 4, 7, 5))] \
    + [p("b", "s", "M23 21h6M23 27h6M17 21a3 3 0 0 0 0 6h3")]
I["GhIconDownloadSource"] = [p("n", "t", R(3, 19, 18, 10)), p("k", "s", R(3, 19, 18, 10))] + arrow_down("TR")
I["GhIconUploadSource"] = [p("n", "t", R(3, 19, 18, 10)), p("k", "s", R(3, 19, 18, 10))] + arrow_up("TR")
I["GhIconExtractData"] = [p("k", "s", R(3, 3, 14, 26)), p("k", "s", "M3 11h14M3 19h14"), p("y", "f", R(6, 6, 8, 3)), p("b", "s", "M19 16h10"), p("b", "s", head(29, 16, 0, 5))]

# ---------------- Annotate ----------------
I["GhIconScaleText"] = letter_a(3, 9, 18, "k") + [p("y", "s", "M19 13 29 3"), p("y", "s", P((22, 3), (29, 3), (29, 10)))]
I["GhIconCheckSpelling"] = letter_a(3, 3, 16, "k") + check("BR")
I["GhIconTextAlign"] = [p("y", "w", "M4 6h14M4 16h14M4 26h14"), p("b", "s", "M25 3v26")]
I["GhIconJustify"] = [p("k", "s", "M3 5h20M3 11h20M3 17h14"), p("y", "f", R(22, 22, 8, 8)), p("b", "S", "M26 18v-3M18 26h-3")]
I["GhIconTolerance"] = [p("k", "s", R(1, 9, 30, 14)), p("k", "s", "M11 9v14M21 9v14"), p("y", "s", O(6, 16, 3)), p("b", "s", "M14 16h4M24 16h4")]
I["GhIconDimOblique"] = [p("g", "s", "M3 27h26"), p("k", "s", "M9 27 15 5M21 27l6-22"), p("y", "s", "M12 16h12"), p("b", "f", tri(12, 16, 180, 4, 2)), p("b", "f", tri(24, 16, 0, 4, 2))]
I["GhIconDimTextAngle"] = [p("g", "s", "M3 27h26"), p("y", "w", "M8 22 20 10"), p("b", "s", arc(3, 27, 12, -2, -40))]
_dj = lambda x: [p("g", "s", "M3 7v20M29 7v20"), p("k", "s", "M3 21h26"), p("b", "f", tri(3, 21, 180, 4, 2)), p("b", "f", tri(29, 21, 0, 4, 2)), p("y", "f", R(x, 10, 10, 6))]
I["GhIconDimJustifyLeft"] = _dj(6)
I["GhIconDimJustifyCenter"] = _dj(11)
I["GhIconDimJustifyRight"] = _dj(16)
I["GhIconDimOverride"] = dimh(3, 29, 21, 13, 27, False) + letter_a(10, 2, 10, "y")
I["GhIconDimBreak"] = [p("g", "s", "M3 11v14M29 11v14"), p("k", "s", "M3 19h9M20 19h9"), p("b", "f", tri(3, 19, 180, 4, 2)), p("b", "f", tri(29, 19, 0, 4, 2)),
                       p("r", "s", "M16 7v8M16 23v6")]
I["GhIconDimSpace"] = [p("k", "s", "M3 7h20M3 15h20M3 23h20"), p("b", "s", "M27 7v16"), p("b", "s", head(27, 7, -90, 3)), p("b", "s", head(27, 23, 90, 3))]
I["GhIconJogLine"] = [p("g", "s", "M3 9v14M29 9v14"), p("k", "s", "M3 17h8"), p("y", "s", "M11 17l3-6 4 12 3-6"), p("k", "s", "M21 17h8")]
I["GhIconInspect"] = [p("y", "t", "M9 10h14a6 6 0 0 1 0 12H9a6 6 0 0 1 0-12z"), p("y", "s", "M9 10h14a6 6 0 0 1 0 12H9a6 6 0 0 1 0-12z"), p("k", "s", "M13 16h6"),
                      p("g", "s", "M1 16h2M29 16h2")]
I["GhIconDimUpdate"] = [p("g", "s", "M3 3v10M19 3v10"), p("k", "s", "M3 8h16")] + cycle(24, 23, 6)
I["GhIconReassociate"] = [p("g", "s", "M3 3v10M19 3v10"), p("k", "s", "M3 8h16"), p("b", "s", "M19 23a3 3 0 0 0 0 6h3M25 23h3a3 3 0 0 1 0 6h-3"), p("y", "s", "M20 26h8")]
I["GhIconQuickDim"] = [p("g", "s", "M3 21v8M13 21v8M23 21v8"), p("k", "s", "M3 25h20"), p("y", "f", P((19, 1), (11, 13), (17, 13), (13, 21), (25, 7), (19, 7), close=True))]
I["GhIconDimContinue"] = [p("g", "s", "M3 13v14M15 13v14M29 13v14"), p("g", "s", "M3 19h12"), p("k", "s", "M15 19h14"), p("b", "f", tri(15, 19, 180, 4, 2)), p("b", "f", tri(29, 19, 0, 4, 2)),
                          p("y", "w", "M18 13h8")]
I["GhIconDimBaseline"] = [p("b", "s", "M3 3v26"), p("g", "s", "M15 11v8M27 19v8"), p("g", "s", "M3 13h12"), p("k", "s", "M3 23h24"), p("b", "f", tri(27, 23, 0, 4, 2)), p("y", "w", "M12 18h8")]
I["GhIconCenterMark"] = [p("k", "s", O(16, 16, 9)), p("y", "s", "M16 1v6M16 12v8M16 25v6M1 16h6M12 16h8M25 16h6")]
I["GhIconCenterline"] = [p("k", "s", "M3 7h26M3 25h26"), p("y", "s", "M1 16h8M13 16h3M20 16h8")]
I["GhIconAnnoScaleAdd"] = letter_a(2, 9, 20, "k") + [p("y", "f", R(4, 2, 4, 4))] + plus("TR")
I["GhIconAnnoScaleDelete"] = letter_a(2, 9, 20, "k") + [p("y", "f", R(4, 2, 4, 4))] + minus("TR")
I["GhIconScaleList"] = window(3, 3, 26, 26) + [p("k", "s", "M9 13h4M9 19h4M9 25h4"), p("y", "s", "M17 13h8M17 19h8M17 25h6")]
I["GhIconAddDeleteScales"] = window(3, 9, 20, 20) + [p("k", "s", "M7 19h12M7 24h8")] + plus("TR")

# ---------------- Parametric ----------------
GC_BOX = [p("c", "t", R(1, 1, 30, 30))]
I["GhIconAutoConstrain"] = [p("k", "s", "M3 29 17 15"), p("k", "s", "M17 15h12")] + star(22, 7, 6, 2.5, "y") + [p("c", "f", G(17, 15))]
I["GhIconGcCoincident"] = [p("k", "s", "M3 25 13 17M19 15 29 7"), p("c", "f", G(16, 16))]
I["GhIconGcParallel"] = [p("k", "s", "M5 27 17 5M15 27 27 5"), p("c", "S", "M13 13l6 6")]
I["GhIconGcTangent"] = [p("k", "s", O(16, 20, 9)), p("c", "s", "M3 11h26")]
I["GhIconGcCollinear"] = [p("k", "s", "M3 25 13 17M19 13l10-8"), p("c", "S", "M13 17l6-4")]
I["GhIconGcPerpendicular"] = [p("k", "s", "M3 27h26M13 27V3"), p("c", "s", "M13 19h8v8")]
I["GhIconGcSmooth"] = [p("k", "s", "M3 25C11 25 11 7 19 7"), p("c", "s", "M19 7h10"), p("c", "f", G(19, 7, 4))]
I["GhIconGcConcentric"] = [p("k", "s", O(16, 16, 12)), p("c", "s", O(16, 16, 5)), p("y", "f", G(16, 16, 2))]
I["GhIconGcHorizontal"] = [p("k", "s", "M3 17h26"), p("c", "s", "M7 11v12M25 11v12")]
I["GhIconGcSymmetric"] = [p("c", "s", "M15 3v26"), p("k", "s", P((11, 7), (3, 16), (11, 25))), p("k", "s", P((19, 7), (27, 16), (19, 25)))]
I["GhIconGcFix"] = [p("k", "s", "M3 27 23 7"), p("c", "f", G(9, 21))] + padlock(19, 1, True)
I["GhIconGcVertical"] = [p("k", "s", "M15 3v26"), p("c", "s", "M9 7h12M9 25h12")]
I["GhIconGcEqual"] = [p("k", "s", "M3 9h12M17 23h12"), p("c", "s", "M11 15h10M11 19h10")]
I["GhIconBarToggle"] = [p("c", "t", R(3, 9, 26, 14)), p("c", "s", R(3, 9, 26, 14)), p("k", "f", "M8 14h4v4H8zM14 14h4v4h-4zM20 14h4v4h-4z")]
I["GhIconBarShowAll"] = eye(16, 16, 13, 6) + [p("c", "S", "M3 29h26")]
I["GhIconBarHideAll"] = eye(16, 16, 13, 6) + [p("r", "s", "M5 27 27 5")]
I["GhIconDcLinear"] = dimh(5, 27, 21, 9, 27) + [p("c", "f", R(10, 5, 4, 6)), p("c", "f", R(18, 5, 4, 6))]
I["GhIconDcModeDynamic"] = dimh(3, 21, 13, 5, 19, False) + padlock(18, 17, True, "c")
I["GhIconDcModeAnnotational"] = dimh(3, 21, 13, 5, 19, False) + letter_a(19, 18, 11, "y")
I["GhIconDcHorizontal"] = dimh(5, 27, 15, 7, 23) + [p("c", "w", "M10 28h12")]
I["GhIconDcVertical"] = [p("g", "s", "M7 5h16M7 27h16"), p("k", "s", "M15 10v12"), p("b", "f", tri(15, 5, -90)), p("b", "f", tri(15, 27, 90)), p("y", "w", "M20 12v8"), p("c", "w", "M28 10v12")]
I["GhIconDcAligned"] = [p("k", "s", "M7 23 23 7"), p("b", "f", tri(5, 25, 135)), p("b", "f", tri(25, 5, -45)), p("g", "s", "M3 19l8 8M19 3l8 8"), p("c", "w", "M18 26l8-8")]
I["GhIconDcRadius"] = [p("k", "s", O(16, 16, 12)), p("c", "s", "M16 16 23 9"), p("c", "f", tri(24.5, 7.5, -45, 6, 3)), p("b", "f", G(16, 16, 4))]
I["GhIconDcAngular"] = [p("g", "s", "M5 27h24M5 27 19 7"), p("c", "s", arc(5, 27, 15, -6, -48))]
I["GhIconDcDiameter"] = [p("k", "s", O(16, 16, 12)), p("c", "s", "M10 22 22 10"), p("c", "f", tri(7.5, 24.5, 135, 6, 3)), p("c", "f", tri(24.5, 7.5, -45, 6, 3))]
I["GhIconDcConvert"] = [p("b", "s", "M3 9h18"), p("b", "s", head(25, 9, 0, 5)), p("c", "s", "M29 23H11"), p("c", "s", head(7, 23, 180, 5))]
I["GhIconDcShow"] = eye(16, 16, 13, 6) + [p("c", "S", "M3 29h26")]
I["GhIconParameters"] = window(3, 3, 26, 26) + [p("k", "s", "M7 13h6M7 19h6M7 25h6"), p("c", "s", "M17 13h8M17 19h8M17 25h8")]
I["GhIconDelConstraint"] = [p("k", "s", "M3 21 13 11"), p("c", "f", G(13, 11))] + cross("BR")

# ---------------- View ----------------
I["GhIconUcsIcon"] = [p("r", "s", "M7 25h20"), p("r", "s", head(27, 25, 0, 4)), p("n", "s", "M7 25V5"), p("n", "s", head(7, 5, -90, 4)), p("k", "f", R(4, 22, 6, 6))]
I["GhIconViewManager"] = window(3, 3, 26, 26) + [p("b", "t", R(7, 11, 10, 8)), p("k", "s", R(7, 11, 10, 8)), p("k", "s", "M7 23h18"), p("g", "s", "M21 11h4M21 15h4")]
I["GhIconNewView"] = [p("b", "t", R(3, 11, 16, 12)), p("k", "s", R(3, 11, 16, 12)), p("k", "s", P((19, 15), (25, 11), (25, 23), (19, 19)))] + plus("TR")
_vp = lambda extra: [p("c", "s", R(3, 5, 26, 22))] + ([p("c", "s", extra)] if extra else [])
I["GhIconViewports"] = _vp("M15 5v22M3 17h12") + [p("y", "t", R(4, 6, 10, 10))]
I["GhIconVpSingle"] = _vp("")
I["GhIconVpTwoVertical"] = _vp("M15 5v22")
I["GhIconVpTwoHorizontal"] = _vp("M3 15h26")
I["GhIconVpThreeRight"] = _vp("M15 5v22M3 15h12")
I["GhIconVpThreeLeft"] = _vp("M15 5v22M15 15h14")
I["GhIconVpThreeAbove"] = _vp("M3 15h26M15 15v12")
I["GhIconVpThreeBelow"] = _vp("M3 15h26M15 5v10")
I["GhIconVpThreeVertical"] = _vp("M11 5v22M21 5v22")
I["GhIconVpThreeHorizontal"] = _vp("M3 11h26M3 19h26")
I["GhIconVpFourEqual"] = _vp("M15 5v22M3 15h26")
I["GhIconVpNamed"] = [p("c", "s", R(3, 3, 18, 14)), p("c", "s", "M11 3v14"), p("y", "t", "M15 21h10l4 4-4 4H15z"), p("y", "s", "M15 21h10l4 4-4 4H15z")]
I["GhIconVpJoin"] = _vp("M15 5v6M15 21v6") + [p("y", "s", P((8, 12), (12, 16), (8, 20))), p("y", "s", P((22, 12), (18, 16), (22, 20)))]
I["GhIconVpRestore"] = _vp("M15 5v22") + [p("b", "s", "M19 13h6a3 3 0 0 1 0 6h-2"), p("b", "s", P((22, 10), (19, 13), (22, 16)))]
I["GhIconToolPalette"] = window(3, 3, 26, 26) + [p("y", "f", R(7, 11, 6, 6)), p("b", "f", R(17, 11, 6, 6)), p("n", "f", R(7, 21, 6, 6)), p("k", "f", R(17, 21, 6, 6))]
I["GhIconPropertiesPalette"] = window(5, 3, 22, 26) + [p("g", "s", "M9 13h4M9 19h4M9 25h4"), p("k", "s", "M17 13h6M17 19h6M17 25h6")]
I["GhIconCommandLine"] = [p("g", "s", "M3 5h20M3 11h14"), p("c", "s", R(3, 17, 26, 12)), p("y", "s", P((7, 20), (10, 23), (7, 26))), p("k", "s", "M13 25h8")]
I["GhIconDesignCenter"] = window(3, 3, 26, 26) + [p("c", "s", "M11 3v26"), p("g", "s", "M5 13h4M5 19h4"), p("y", "t", R(15, 11, 10, 10)), p("y", "s", R(15, 11, 10, 10))]
I["GhIconExternalReferences"] = folder(3, 3, 22, 16, "y") + [p("c", "s", R(15, 19, 14, 10)), p("b", "s", "M7 23h6")]
I["GhIconPanelLeft"] = [p("c", "s", R(3, 5, 26, 22)), p("c", "f", R(4, 6, 8, 20)), p("k", "s", "M17 11h8M17 17h8M17 23h4")]
I["GhIconZoomWindow"] = lens(13, 13, 9) + [p("y", "s", R(9, 10, 8, 6))]
I["GhIconZoomPrevious"] = lens(13, 13, 9) + [p("y", "s", "M17 13H9"), p("y", "s", P((12, 10), (9, 13), (12, 16)))]
I["GhIconZoomIn"] = lens(13, 13, 9) + [p("n", "s", "M13 9v8M9 13h8")]
I["GhIconZoomOut"] = lens(13, 13, 9) + [p("r", "s", "M9 13h8")]
I["GhIconRefresh"] = cycle(16, 16, 11)
I["GhIconModel"] = [p("r", "s", "M7 25h18"), p("r", "s", head(27, 25, 0, 4)), p("n", "s", "M7 25V7"), p("n", "s", head(7, 5, -90, 4)), p("k", "s", "M7 25l12-12"),
                    p("b", "f", G(7, 25))]
I["GhIconLayout"] = [p("k", "s", R(3, 5, 26, 22)), p("c", "t", R(7, 9, 14, 14)), p("c", "s", R(7, 9, 14, 14)), p("k", "s", "M23 21h4M23 17h4")]
I["GhIconGrid"] = [p("k", "f", "".join(R(x - 1, y - 1, 2, 2) for x in (6, 16, 26) for y in (6, 16, 26)))]
I["GhIconSnap"] = [p("k", "f", "".join(R(x - 1, y - 1, 2, 2) for x in (6, 26) for y in (6, 26))), p("y", "s", "M16 9v14M9 16h14"), p("b", "f", G(16, 16, 4))]
I["GhIconOrtho"] = [p("k", "s", "M7 3v22h22"), p("y", "s", P((4, 6), (7, 3), (10, 6))), p("y", "s", P((26, 22), (29, 25), (26, 28)))]
I["GhIconPolar"] = [p("k", "s", "M3 27h26M3 27 21 9M3 27 13 5"), p("y", "s", arc(3, 27, 12, -2, -44))]
I["GhIconOsnap"] = [p("g", "s", "M16 3v8M16 21v8M3 16h8M21 16h8"), p("y", "s", R(11, 11, 10, 10))]

# ---------------- Manage ----------------
I["GhIconUserInterface"] = window(3, 3, 26, 26) + [p("y", "f", R(7, 11, 6, 6)), p("k", "s", "M17 13h8M17 17h6M7 23h18")]
I["GhIconKeyboard"] = [p("k", "s", R(3, 9, 26, 16)), p("k", "f", "M7 13h2v2H7zM11 13h2v2h-2zM15 13h2v2h-2zM19 13h2v2h-2zM23 13h2v2h-2z"), p("y", "s", "M11 21h10")]
I["GhIconPluginLoad"] = [p("b", "s", "M16 3v14"), p("b", "s", head(16, 17, 90, 6)), p("k", "s", R(5, 21, 22, 8)), p("n", "f", R(22, 24, 2, 2))]
I["GhIconAppManager"] = window(3, 3, 26, 26) + [p("k", "s", "M7 13h12M7 19h12M7 25h8"), p("n", "f", R(22, 12, 3, 3)), p("n", "f", R(22, 18, 3, 3)), p("g", "f", R(22, 24, 3, 3))]
I["GhIconLoadLisp"] = [p("b", "s", "M9 3C3 9 3 23 9 29M23 3c6 6 6 20 0 26"), p("y", "s", "M13 11l6 10M19 11l-6 10")]
I["GhIconLoadPlugin"] = [p("k", "s", "M11 3v8M21 3v8"), p("b", "t", "M7 11h18v6a9 9 0 0 1-18 0z"), p("k", "s", "M7 11h18v6a9 9 0 0 1-18 0z"), p("k", "s", "M16 26v4")]
I["GhIconLoadPython"] = [p("b", "w", "M5 8V28"), p("b", "w", "M5 10h6a4 4 0 0 1 0 8H5"),
                         p("y", "w", "M19 10l5 9"), p("y", "w", "M29 10 22 24l-3 4")]
I["GhIconScript"] = doc(5, 3, 22, 26, 7) + [p("y", "s", P((9, 13), (12, 16), (9, 19))), p("k", "s", "M15 19h6M9 24h12")]
I["GhIconStandards"] = [p("c", "t", R(5, 3, 22, 26)), p("k", "s", R(5, 3, 22, 26)), p("n", "w", "M8 12l4 4 8-8"), p("k", "s", "M9 21h14M9 25h10")]
I["GhIconLayerTranslator"] = [p("k", "s", P((1, 7), (8, 3), (15, 7), (8, 11), close=True)), p("g", "s", P((1, 11), (8, 15), (15, 11))),
                              p("y", "s", P((17, 21), (24, 17), (31, 21), (24, 25), close=True)), p("g", "s", P((17, 25), (24, 29), (31, 25))),
                              p("b", "s", "M19 7h4a4 4 0 0 1 4 4v3"), p("b", "s", head(27, 14, 90, 3)), p("b", "S", "M13 25H9a4 4 0 0 1-4-4v-3")]
I["GhIconStandardsCheck"] = doc(3, 3, 18, 24, 6) + check("BR")
I["GhIconStandardsConfigure"] = doc(3, 3, 18, 24, 6) + gear(24, 24, 7)
I["GhIconPurge"] = [p("y", "w", "M23 2l-5 12"), p("k", "f", P((7, 15), (25, 15), (27, 29), (5, 29), close=True)), p("m", "s", "M11 19v8M16 19v8M21 19v8")]
I["GhIconFindNonPurgeable"] = lens(13, 13, 9) + [p("r", "w", "M12 8v5M12 16v2")]
I["GhIconAudit"] = [p("c", "t", "M16 3 5 7v8c0 7 5 12 11 14 6-2 11-7 11-14V7z"), p("k", "s", "M16 3 5 7v8c0 7 5 12 11 14 6-2 11-7 11-14V7z"), p("n", "w", "M10 16l4 4 8-8")]

# ---------------- Output ----------------
I["GhIconPlot"] = [p("k", "s", R(9, 3, 14, 8)), p("k", "s", "M9 23H5a2 2 0 0 1-2-2v-8a2 2 0 0 1 2-2h22a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2h-4"),
                   p("c", "t", R(9, 19, 14, 10)), p("k", "s", R(9, 19, 14, 10)), p("n", "f", R(23, 14, 3, 2))]
I["GhIconBatchPlot"] = [p("g", "s", "M11 3h18v18"), p("g", "s", "M7 7h18v18")] + doc(3, 11, 18, 18, 5)
I["GhIconPlotPreview"] = doc(3, 3, 18, 24, 6) + lens(20, 20, 6)
I["GhIconPageSetup"] = [p("k", "s", R(5, 3, 22, 26)), p("c", "t", R(9, 7, 14, 18)), p("c", "s", R(9, 7, 14, 18)), p("y", "f", R(11, 9, 4, 4))]
I["GhIconPlotStamp"] = [p("k", "s", "M3 29h26"), p("y", "f", R(5, 19, 22, 6)), p("k", "s", "M13 19v-6a6 6 0 1 1 6 0v6")]
I["GhIconExportPdf"] = doc(3, 3, 22, 26, 7) + [p("r", "f", R(1, 15, 22, 10)), p("m", "s", "M5 23v-6h2a2 2 0 0 1 0 4H5M11 23v-6h2a3 3 0 0 1 0 6zM19 23v-6h3M19 20h2")]
I["GhIconExportDwf"] = doc(3, 3, 22, 26, 7) + [p("b", "f", R(1, 15, 22, 10)), p("m", "s", "M4 17l2 6 2-4 2 4 2-6M15 17v6h2a3 3 0 0 0 0-6z")]
I["GhIconExport"] = doc(3, 3, 18, 26, 6) + [p("b", "s", "M15 21h14"), p("b", "s", head(29, 21, 0, 5))]

# ---------------- Settings ----------------
I["GhIconGear"] = gear(16, 16, 13)
I["GhIconPalette"] = [p("k", "s", "M16 3a13 13 0 1 0 0 26c2 0 2-2 1-4s0-4 3-4h3a6 6 0 0 0 6-6C29 8 23 3 16 3z"),
                      p("r", "f", G(9, 15, 4)), p("y", "f", G(13, 9, 4)), p("n", "f", G(20, 9, 4)), p("b", "f", G(24, 14, 4))]
I["GhIconRuler"] = H["GhIconMeasureGeom"]
I["GhIconTextFonts"] = [p("k", "s", "M5 9V5h18v4M14 5v22M10 27h8"), p("y", "s", "M21 27l4-10 4 10M22 23h6")]
I["GhIconShield"] = I["GhIconAudit"]

# ---------------- Text Editor (contextual) ----------------
I["IconStyle"] = letter_a(5, 5, 22, "k") + [p("c", "f", R(22, 22, 8, 8))]
I["IconHeight"] = [p("k", "s", "M3 7V5h14v2M10 5v22"), p("b", "s", "M25 5v22"), p("b", "s", head(25, 5, -90, 4)), p("b", "s", head(25, 27, 90, 4))]
I["IconField"] = I["GhIconField"]
I["IconMatch"] = H["GhIconMatchProperties"]
I["IconJustify"] = [p("k", "s", R(3, 3, 26, 26)), p("y", "f", R(7, 7, 6, 6)), p("g", "S", "M17 9h8M7 19h18M7 24h14")]
I["IconBullets"] = H["GhIconList"]
I["IconSpacing"] = [p("k", "s", "M13 7h16M13 16h16M13 25h16"), p("b", "s", "M6 5v22"), p("b", "s", head(6, 5, -90, 4)), p("b", "s", head(6, 27, 90, 4))]
_al = lambda a, b: [p("k", "s", "M3 7h26M%d 13h%dM3 19h26M%d 25h%d" % (a, b, a, b))]
I["AlignLeft"] = _al(3, 16)
I["AlignCentre"] = _al(9, 14)
I["AlignRight"] = _al(13, 16)
I["AlignJustify"] = _al(3, 26)
I["AlignDistribute"] = [p("k", "s", "M3 7h26M3 13h26M3 19h26"), p("y", "s", "M3 25h4M15 25h2M25 25h4")]
I["IconSymbol"] = [p("y", "s", O(16, 16, 5)), p("k", "s", "M21 11v7a3 3 0 0 0 6 0v-2A11 11 0 1 0 22 26")]
I["IconColumns"] = [p("k", "s", "M3 5h10M3 11h10M3 17h10M3 23h10M19 5h10M19 11h10M19 17h8"), p("c", "s", "M16 3v26")]
I["IconSpell"] = I["GhIconCheckSpelling"]
I["IconDictionary"] = [p("b", "t", R(5, 3, 20, 26)), p("k", "s", R(5, 3, 20, 26)), p("k", "s", "M9 3v26"), p("y", "f", R(13, 9, 8, 4))]
I["IconFind"] = lens(13, 13, 9) + [p("y", "s", "M9 13h8")]
I["IconImport"] = I["GhIconImport"]
I["IconCaps"] = letter_a(2, 7, 16, "k") + letter_a(16, 3, 20, "y")
I["IconMore"] = [p("k", "f", R(5, 15, 4, 4) + R(14, 15, 4, 4) + R(23, 15, 4, 4))]
I["IconRuler"] = [p("y", "t", R(3, 9, 26, 14)), p("y", "s", R(3, 9, 26, 14)), p("k", "s", "M9 9v5M15 9v7M21 9v5M27 9v7")]
I["IconCloseEditor"] = [p("r", "w", "M8 8l16 16M24 8 8 24")]
I["GhIconUndo"] = H["GhIconUndo"]
I["GhIconRedo"] = H["GhIconRedo"]

# ---------------- Array (contextual) ----------------
I["IconBasePoint"] = [p("g", "s", R(9, 3, 20, 20)), p("b", "s", "M9 17v12M3 23h12"), p("b", "f", G(9, 23))]
I["IconEditSource"] = [p("y", "f", R(3, 3, 12, 12)), p("g", "s", R(3, 19, 10, 10))] + pencil("BR")
I["IconReplace"] = [p("g", "s", R(3, 3, 10, 10)), p("y", "f", R(19, 19, 10, 10)), p("b", "s", "M17 7h6a4 4 0 0 1 4 4v3"), p("b", "s", head(27, 15, 90, 4)),
                    p("b", "S", "M15 25H9a4 4 0 0 1-4-4v-3")]
I["IconReset"] = [p("b", "s", "M7 16a10 10 0 1 0 3-7"), p("b", "s", P((5, 5), (8, 10), (13, 8))), p("y", "f", G(17, 16, 4))]

# ---------------- Block Editor (contextual) ----------------
I["IconEditBlock"] = H["GhIconBlockEditor"]
I["IconSaveBlock"] = disk(3, 3, 22) + block(19, 19, 12, "y", "y")
I["IconSaveAs"] = H["GhIconSaveAs"]
I["IconTest"] = [p("n", "f", P((9, 5), (27, 16), (9, 27), close=True))]
I["IconAutoConstrain"] = I["GhIconAutoConstrain"]
I["IconLinear"] = I["GhIconDcLinear"]
I["IconAttribute"] = H["GhIconDefineAttributes"]
I["IconProperties"] = I["GhIconPropertiesPalette"]
I["IconPoint"] = [p("k", "s", "M3 25h26"), p("b", "f", G(9, 25)), p("y", "t", P((17, 5), (25, 17), (9, 17), close=True)), p("y", "s", P((17, 5), (25, 17), (9, 17), close=True))]
I["IconMove"] = [p("y", "f", P((19, 3), (7, 18), (15, 18), (13, 29), (25, 13), (17, 13), close=True))]
I["IconVisibility"] = eye(16, 16, 13, 6) + [p("y", "S", "M5 29h22")]
I["IconClose"] = I["IconCloseEditor"]

# ---------------- Hatch (contextual) ----------------
I["IconPickPoints"] = [p("k", "s", R(3, 3, 26, 26)), p("y", "t", R(4, 4, 24, 24)), p("b", "s", "M16 9v14M9 16h14"), p("b", "f", G(16, 16, 4))]
I["IconSelectObjects"] = [p("y", "s", R(11, 11, 18, 18)), p("k", "f", P((3, 3), (21, 11), (13, 13), (11, 21), close=True))]
I["IconRemoveBoundary"] = [p("k", "s", R(3, 3, 18, 18))] + minus("BR")
I["IconRecreate"] = [p("y", "s", R(3, 3, 16, 16))] + cycle(23, 23, 6)
I["IconDisplayBoundary"] = [p("y", "s", R(3, 3, 26, 26))] + eye(16, 16, 9, 4)
I["IconSetOrigin"] = [p("k", "s", "M7 29V7M7 29h22"), p("y", "s", "M7 29l10-10"), p("b", "f", G(7, 29)), p("y", "f", R(17, 5, 10, 10))]
I["IconCloseHatch"] = I["IconCloseEditor"]

# ---------------- Image and PDF Underlay (contextual) ----------------
I["IconClipCreate"] = [p("c", "s", R(3, 3, 26, 26)), p("y", "t", R(9, 9, 14, 14)), p("y", "s", R(9, 9, 14, 14))] + []
I["IconClipRemove"] = [p("c", "s", R(3, 3, 26, 26)), p("g", "s", R(9, 9, 14, 14)), p("r", "s", "M5 5l22 22")]
I["IconCloseImage"] = I["IconCloseEditor"]
I["IconPdfClipCreate"] = I["IconClipCreate"]
I["IconPdfClipRemove"] = I["IconClipRemove"]
I["IconPdfLayers"] = I["GhIconUnderlayLayers"]
I["IconClosePdf"] = I["IconCloseEditor"]

# ---------------- Table Cell (contextual) ----------------
_row = lambda y: [p("k", "s", R(3, y, 26, 10)), p("k", "s", "M11 %dv10M21 %dv10" % (y, y))]
_col = lambda x: [p("k", "s", R(x, 3, 10, 26)), p("k", "s", "M%d 11h10M%d 21h10" % (x, x))]
I["IconInsertAbove"] = _row(19) + [p("n", "w", "M16 2v12M10 8h12")]
I["IconInsertBelow"] = _row(3) + [p("n", "w", "M16 18v12M10 24h12")]
I["IconDeleteRows"] = _row(11) + [p("r", "t", R(4, 12, 24, 8))] + [p("r", "s", "M3 16h26")]
I["IconInsertLeft"] = _col(19) + [p("n", "w", "M2 16h12M8 10v12")]
I["IconInsertRight"] = _col(3) + [p("n", "w", "M18 16h12M24 10v12")]
I["IconDeleteColumns"] = _col(11) + [p("r", "t", R(12, 4, 8, 24)), p("r", "s", "M16 3v26")]
I["IconMerge"] = [p("k", "s", R(3, 7, 26, 18)), p("y", "t", R(4, 8, 24, 16)), p("b", "s", P((9, 12), (13, 16), (9, 20))), p("b", "s", P((23, 12), (19, 16), (23, 20)))]
I["IconUnmerge"] = [p("k", "s", R(3, 7, 26, 18)), p("k", "s", "M3 16h26M16 7v18"), p("y", "t", R(4, 8, 11, 7))]
I["IconAlign"] = [p("k", "s", R(3, 3, 26, 26)), p("y", "s", "M9 11h14M11 16h10M9 21h14")]
I["IconBorders"] = [p("g", "s", "M16 3v26M3 16h26"), p("y", "s", R(3, 3, 26, 26))]
I["IconLock"] = [p("k", "s", R(3, 3, 18, 18)), p("k", "s", "M3 12h18M12 3v18")] + padlock(18, 16, True)
I["IconFormat"] = [p("k", "s", "M3 7h8M3 16h12M3 25h10"), p("y", "s", "M19 5l10 22"), p("y", "f", G(20, 25, 4))]
I["IconBlock"] = [p("k", "s", R(3, 3, 26, 26))] + block(9, 9, 14, "y", "y")
I["IconFormula"] = [p("y", "s", "M9 5h14M9 5l8 11-8 11h14")]
I["IconContents"] = [p("k", "s", "M3 7h20M3 16h20M3 25h14"), p("b", "s", "M27 5v22"), p("b", "s", head(27, 5, -90, 3)), p("b", "s", head(27, 27, 90, 3))]
I["IconLink"] = I["GhIconHyperlink"]
I["IconDownload"] = I["GhIconDownloadSource"]
I["IconUpload"] = I["GhIconUploadSource"]

# ---------------- Proposed: the Block Editor's small buttons that have no icon today (new keys) ----------------
I["IconBlockTable"] = [p("c", "f", R(3, 5, 26, 6)), p("k", "s", R(3, 5, 26, 22)), p("k", "s", "M3 11h26M3 19h26M13 11v16")] + [p("y", "f", R(17, 21, 10, 4))]
I["IconDeleteConstraints"] = I["GhIconDelConstraint"]
I["IconAuthoringPalettes"] = window(3, 3, 26, 26) + [p("y", "f", R(7, 11, 6, 6)), p("b", "f", G(20, 14, 6)), p("k", "s", "M7 25 13 19M17 25h8")]
I["IconParameterManager"] = I["GhIconParameters"]
I["IconConstruction"] = [p("g", "s", "M3 29 29 3"), p("g", "s", "M3 9h4M11 9h4M19 9h4"), p("y", "s", R(9, 15, 12, 10))]
I["IconMakeVisible"] = [p("y", "s", R(3, 3, 14, 14))] + eye(21, 23, 9, 4)
I["IconMakeInvisible"] = [p("g", "s", R(3, 3, 14, 14))] + eye(21, 23, 9, 4) + [p("r", "s", "M13 31 29 15")]
