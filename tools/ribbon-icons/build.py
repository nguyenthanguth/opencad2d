"""Writes src/OpenCAD2D.Wpf/Themes/RibbonIcons.xaml from the drawings in svg/.

python -I build.py [output file]

Every icon is one RibbonIcon keyed "<key>.Color": its Large drawing is svg/<key>-32.svg, its Small drawing svg/<key>-16.svg
with a ScaleTransform of 0.5, both on the 32 grid. A stroke becomes a pen of the theme brush of its colour (thickness,
caps and joins as rule C2 of the design: 2 units square, 4 units butt, 1 unit square, mitre joins), a fill the brush itself
or, at 30 or 60 %, its tint. The file is formatted as CSharpier formats it, so that "dotnet csharpier check ." passes on it.
"""

import os
import re
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "..", "src", "OpenCAD2D.Wpf", "Themes", "RibbonIcons.xaml")
WIDTH = 120

# the colour of the SVG -> the theme name; GhIcon<name> is the brush, GhIcon<name>Color its colour (Themes/Dark.xaml)
ROLES = {
    "#E6EBF0": "Ink",
    "#8E99A8": "Grey",
    "#4DA3FF": "Blue",
    "#2BC4B4": "Teal",
    "#F5C242": "Yellow",
    "#F2584B": "Red",
    "#45C45A": "Green",
}
KNOCK_OUT = "#262B33"  # the panel colour, GhPanel
TINTS = {"0.3": "30", "0.6": "60"}
MIME = "{http://www.w3.org/2000/svg}"


def read(path):
    """The parts of an SVG file as (d, fill, fill-opacity, stroke, stroke-width, linecap) tuples."""
    parts = []
    for el in ET.parse(path).getroot():
        parts.append(
            (
                el.get("d"),
                el.get("fill"),
                el.get("fill-opacity", "1"),
                el.get("stroke"),
                el.get("stroke-width"),
                el.get("stroke-linecap"),
            )
        )
    return parts


def brush(colour):
    if colour == KNOCK_OUT:
        return "GhPanel"
    return "GhIcon" + ROLES[colour]


def pen_key(colour, width, cap):
    name = "Panel" if colour == KNOCK_OUT else ROLES[colour]
    return "RibbonPen%s%s%s" % (name, width, "Butt" if cap == "butt" else "")


def tint_key(colour, opacity):
    name = "Panel" if colour == KNOCK_OUT else ROLES[colour]
    return "RibbonTint%s%s" % (name, TINTS[opacity])


def element(indent, tag, attrs, close):
    """One element as CSharpier writes it: on one line where it fits in 120 columns, else one attribute to a line."""
    pad = " " * indent
    line = pad + "<" + tag + "".join(' %s="%s"' % a for a in attrs) + (" />" if close else ">")
    if len(line) <= WIDTH or not attrs:
        return [line]
    out = [pad + "<" + tag]
    out += [pad + "    " + '%s="%s"' % a for a in attrs]
    out.append(pad + ("/>" if close else ">"))
    return out


def drawing(parts, indent, scale):
    """The group of a drawing, clipped to the 32 grid as the SVG files are by their viewBox; the small one inside its scale."""
    pad = " " * indent
    out = []
    if scale:
        out.append(pad + "<DrawingGroup>")
        out.append(pad + "    <DrawingGroup.Transform>")
        out += element(indent + 8, "ScaleTransform", [("ScaleX", "0.5"), ("ScaleY", "0.5")], True)
        out.append(pad + "    </DrawingGroup.Transform>")
        indent += 4
        pad = " " * indent
    out.append(pad + '<DrawingGroup ClipGeometry="{StaticResource RibbonIconGrid}">')
    for d, fill, opacity, stroke, width, cap in parts:
        geometry = "F1 " + d
        if stroke:
            attrs = [("Pen", "{StaticResource %s}" % pen_key(stroke, width, cap)), ("Geometry", geometry)]
        elif opacity == "1":
            attrs = [("Brush", "{StaticResource %s}" % brush(fill)), ("Geometry", geometry)]
        else:
            attrs = [("Brush", "{StaticResource %s}" % tint_key(fill, opacity)), ("Geometry", geometry)]
        out += element(indent + 4, "GeometryDrawing", attrs, True)
    out.append(pad + "</DrawingGroup>")
    if scale:
        out.append(" " * (indent - 4) + "</DrawingGroup>")
    return out


def main():
    svg_dir = os.path.join(HERE, "svg")
    keys = sorted(set(re.sub(r"-(16|32)\.svg$", "", f) for f in os.listdir(svg_dir) if f.endswith(".svg")))
    icons = []
    pens = set()
    tints = set()
    for key in keys:
        large = read(os.path.join(svg_dir, key + "-32.svg"))
        small = read(os.path.join(svg_dir, key + "-16.svg"))
        if not large or not small:
            raise SystemExit("%s has an empty drawing" % key)
        for part in large + small:
            if part[3]:
                pens.add((part[3], part[4], part[5]))
            elif part[2] != "1":
                tints.add((part[1], part[2]))
        icons.append((key, large, small))

    out = [
        "<ResourceDictionary",
        '    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
        '    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"',
        '    xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options"',
        '    xmlns:local="clr-namespace:OpenCAD2D.Wpf"',
        '    mc:Ignorable="po"',
        ">",
        "    <!--",
        "    The colour icons of the ribbon, one RibbonIcon per command, keyed <old icon key>.Color. Written by",
        "    tools/ribbon-icons/build.py from the drawings in tools/ribbon-icons/svg: change an icon there, never here.",
        "    -->",
        "    <ResourceDictionary.MergedDictionaries>",
        '        <ResourceDictionary Source="/OpenCAD2D.Wpf;component/Themes/Dark.xaml" />',
        "    </ResourceDictionary.MergedDictionaries>",
        "",
        "    <!-- The pens: 2 units square for the lines, 4 butt for the badges and text bars, 1 square for the detail. -->",
    ]
    for colour, width, cap in sorted(pens, key=lambda p: (pen_key(*p))):
        attrs = [
            ("x:Key", pen_key(colour, width, cap)),
            ("Brush", "{StaticResource %s}" % brush(colour)),
            ("Thickness", width),
            ("StartLineCap", "Flat" if cap == "butt" else "Square"),
            ("EndLineCap", "Flat" if cap == "butt" else "Square"),
            ("LineJoin", "Miter"),
            ("MiterLimit", "4"),
            ("po:Freeze", "True"),
        ]
        out += element(4, "Pen", attrs, True)
    out.append("")
    out.append("    <!-- The frame every drawing is clipped to, as the SVG files are by their viewBox. -->")
    out += element(4, "RectangleGeometry", [("x:Key", "RibbonIconGrid"), ("Rect", "0,0,32,32"), ("po:Freeze", "True")], True)
    out.append("")
    out.append("    <!-- The tints: a colour at 30 or 60 %, the inside of a closed part. -->")
    for colour, opacity in sorted(tints, key=lambda t: tint_key(*t)):
        name = "Panel" if colour == KNOCK_OUT else ROLES[colour]
        colour_key = "GhPanelColor" if colour == KNOCK_OUT else "GhIcon%sColor" % name
        attrs = [
            ("x:Key", tint_key(colour, opacity)),
            ("Color", "{StaticResource %s}" % colour_key),
            ("Opacity", opacity),
            ("po:Freeze", "True"),
        ]
        out += element(4, "SolidColorBrush", attrs, True)

    for key, large, small in icons:
        out.append("")
        out += element(4, "local:RibbonIcon", [("x:Key", key + ".Color"), ("po:Freeze", "True")], False)
        out.append("        <local:RibbonIcon.Large>")
        out += drawing(large, 12, False)
        out.append("        </local:RibbonIcon.Large>")
        out.append("        <local:RibbonIcon.Small>")
        out += drawing(small, 12, True)
        out.append("        </local:RibbonIcon.Small>")
        out.append("    </local:RibbonIcon>")
    out.append("</ResourceDictionary>")

    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(out) + "\n")
    print(len(icons), "icons,", len(pens), "pens,", len(tints), "tints ->", os.path.abspath(OUT))


main()
