"""Draw the cooling column texture and a preview of it.

Writes ZLevels/Common/Textures/Building/CoolingColumn.png, 300 x 300 RGBA like the mod's other building textures,
and .artifacts/art/cooling-column-preview.png for review.

Run from the repository root: python tools/art/make_cooling_column_art.py
"""

import math
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
TEXTURE = ROOT / "ZLevels" / "Common" / "Textures" / "Building" / "CoolingColumn.png"
PREVIEW = ROOT / ".artifacts" / "art" / "cooling-column-preview.png"
SIZE = 300
SSAA = 4  # Draw at 4x and shrink, so the edges are smooth.

STEEL_LIGHT = (200, 208, 215, 255)
STEEL = (140, 151, 160, 255)
STEEL_DARK = (86, 95, 104, 255)
FLANGE = (112, 122, 130, 255)
FLANGE_LIGHT = (164, 173, 180, 255)
WHEEL = (200, 42, 42, 255)
WHEEL_DARK = (124, 20, 20, 255)


def draw_column(scale):
    size = SIZE * scale
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    def box(x0, y0, x1, y1):
        return [round(x0 * scale), round(y0 * scale), round(x1 * scale), round(y1 * scale)]

    # The pipe body is a vertical cylinder, lit from the left.
    draw.rectangle(box(114, 36, 186, 264), fill=STEEL)
    draw.rectangle(box(114, 36, 130, 264), fill=STEEL_LIGHT)
    draw.rectangle(box(170, 36, 186, 264), fill=STEEL_DARK)

    # Flanges at both ends of the pipe.
    draw.rectangle(box(96, 26, 204, 48), fill=FLANGE)
    draw.rectangle(box(96, 26, 204, 33), fill=FLANGE_LIGHT)
    draw.rectangle(box(96, 252, 204, 274), fill=FLANGE)

    # The valve wheel sits on the front face: a dark rim, a red disc, three spokes, and a metal hub.
    cx, cy, radius = 150, 140, 54
    draw.ellipse(box(cx - radius, cy - radius, cx + radius, cy + radius), fill=WHEEL_DARK)
    inner = radius - 7
    draw.ellipse(box(cx - inner, cy - inner, cx + inner, cy + inner), fill=WHEEL)
    for angle in (90, 210, 330):
        rad = math.radians(angle)
        end_x = cx + math.cos(rad) * (inner - 2)
        end_y = cy - math.sin(rad) * (inner - 2)
        draw.line([round(cx * scale), round(cy * scale), round(end_x * scale), round(end_y * scale)],
                  fill=WHEEL_DARK, width=round(9 * scale))
    hub = 11
    draw.ellipse(box(cx - hub, cy - hub, cx + hub, cy + hub), fill=STEEL_LIGHT, outline=STEEL_DARK, width=round(2 * scale))
    return image


def write_preview(texture):
    canvas = Image.new("RGBA", (960, 480), (74, 90, 62, 255))
    canvas.alpha_composite(texture, (60, 90))
    canvas.alpha_composite(texture.resize((360, 360), Image.Resampling.LANCZOS), (500, 60))
    PREVIEW.parent.mkdir(parents=True, exist_ok=True)
    canvas.convert("RGB").save(PREVIEW)


def main():
    texture = draw_column(SSAA).resize((SIZE, SIZE), Image.Resampling.LANCZOS)
    TEXTURE.parent.mkdir(parents=True, exist_ok=True)
    texture.save(TEXTURE, optimize=True)
    write_preview(texture)
    print("texture", TEXTURE, texture.size, texture.mode)
    print("preview", PREVIEW)


if __name__ == "__main__":
    main()
