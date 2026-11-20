import sys
from pathlib import Path
from xml.etree import ElementTree

from reportlab.lib.units import mm
from reportlab.pdfgen.canvas import Canvas


assets = Path(__file__).resolve().parents[1] / "assets"
source = Path(sys.argv[1]) if len(sys.argv) > 1 else assets / "foglio-loop-a4.svg"
sheet = ElementTree.parse(source).getroot()
_, _, width, height = map(float, sheet.attrib["viewBox"].split())
pdf = Canvas(str(source.with_suffix(".pdf")), (width * mm, height * mm), invariant=1)
heading = sheet.find("{http://www.w3.org/2000/svg}text")
title = "Visual Music Block Looper - Foglio A4"
if heading is not None and source.name != "foglio-loop-a4.svg":
    title = heading.text
pdf.setTitle(title)


def coordinate(element, name):
    return float(element.attrib[name]) * mm


def svg_color(value):
    if value.startswith("#") and len(value) == 4:
        return "#" + "".join(channel * 2 for channel in value[1:])
    return value


for element in sheet:
    kind = element.tag.rsplit("}", 1)[-1]
    has_stroke = "stroke" in element.attrib
    if has_stroke:
        pdf.setStrokeColor(svg_color(element.attrib["stroke"]))
        pdf.setLineWidth(coordinate(element, "stroke-width"))

    if kind == "rect":
        x, y, w, h = (coordinate(element, name) for name in ("x", "y", "width", "height"))
        fill = element.attrib["fill"]
        if fill != "none":
            pdf.setFillColor(svg_color(fill))
        pdf.rect(x, height * mm - y - h, w, h, stroke=has_stroke, fill=fill != "none")
    elif kind == "line":
        x1, y1, x2, y2 = (coordinate(element, name) for name in ("x1", "y1", "x2", "y2"))
        pdf.line(x1, height * mm - y1, x2, height * mm - y2)
    elif kind == "text":
        font = "Helvetica-Bold" if element.get("font-weight") == "bold" else "Helvetica"
        pdf.setFillColor("black")
        pdf.setFont(font, coordinate(element, "font-size"))
        pdf.drawString(coordinate(element, "x"), height * mm - coordinate(element, "y"), element.text)

pdf.showPage()
pdf.save()
