"""Renders the Brisk app icon (assets/brisk.ico + assets/brisk.png).

A rounded "squircle" tile with a blue gradient, a white globe and speed streaks.
Small sizes (<= 32 px) use a simplified glyph so the icon stays crisp in the taskbar.
Run: python assets/make_icon.py
"""
import math
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

S = 1024
HERE = Path(__file__).parent


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def squircle_mask(size, inset, n=5.0):
    """Superellipse mask, supersampled for smooth edges."""
    ss = 4
    big = size * ss
    r = (size / 2 - inset) * ss
    c = big / 2
    pts = []
    for i in range(720):
        a = 2 * math.pi * i / 720
        ca, sa = math.cos(a), math.sin(a)
        x = c + r * math.copysign(abs(ca) ** (2 / n), ca)
        y = c + r * math.copysign(abs(sa) ** (2 / n), sa)
        pts.append((x, y))
    m = Image.new("L", (big, big), 0)
    ImageDraw.Draw(m).polygon(pts, fill=255)
    return m.resize((size, size), Image.LANCZOS)


def vertical_gradient(size, stops):
    g = Image.new("RGBA", (1, size))
    for y in range(size):
        t = y / (size - 1)
        for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
            if t0 <= t <= t1:
                g.putpixel((0, y), lerp(c0, c1, (t - t0) / (t1 - t0)) + (255,))
                break
    return g.resize((size, size))


def render(simple=False):
    ss = 2
    size = S * ss
    inset = 52 * ss  # leave room for the shadow, as on a macOS icon grid

    # Tile: gradient + soft top highlight.
    tile = vertical_gradient(size, [(0, (100, 210, 255)), (0.55, (10, 132, 255)), (1, (0, 72, 214))])
    glow = Image.new("L", (size, size), 0)
    ImageDraw.Draw(glow).ellipse((-size * 0.2, -size * 0.75, size * 1.2, size * 0.45), fill=70)
    glow = glow.filter(ImageFilter.GaussianBlur(60 * ss))
    tile = Image.composite(Image.new("RGBA", (size, size), (255, 255, 255, 255)), tile, glow)

    mask = squircle_mask(size, inset)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))

    # Drop shadow under the tile.
    shadow = Image.new("RGBA", (size, size), (0, 20, 60, 0))
    shadow.putalpha(mask.point(lambda v: v * 0.35))
    shadow = ImageChops.offset(shadow, 0, 14 * ss).filter(ImageFilter.GaussianBlur(18 * ss))
    canvas.alpha_composite(shadow)
    tile.putalpha(mask)
    canvas.alpha_composite(tile)

    # Glyph layer: white globe (+ streaks) with a soft shadow for depth.
    glyph = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(glyph)
    white = (255, 255, 255, 255)
    if simple:
        cx, cy, r = size * 0.5, size * 0.5, size * 0.27
    else:
        cx, cy, r = size * 0.585, size * 0.5, size * 0.215
        # Speed streaks trailing the globe.
        w = 40 * ss
        for dy, length in ((-0.45, 0.13), (0.0, 0.19), (0.45, 0.09)):
            y = cy + dy * r
            x1 = cx - r - 0.05 * size
            d.rounded_rectangle((x1 - length * size, y - w / 2, x1, y + w / 2), radius=w / 2, fill=white)
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=white)

    # Meridians and equator, cut back into the globe in the tile's blue.
    lines = Image.new("L", (size, size), 0)
    ld = ImageDraw.Draw(lines)
    lw = round((26 if simple else 15) * ss)
    ld.ellipse((cx - r * 0.42, cy - r, cx + r * 0.42, cy + r), outline=255, width=lw)
    ld.line((cx, cy - r, cx, cy + r), fill=255, width=lw)
    ld.line((cx - r, cy, cx + r, cy), fill=255, width=lw)
    if not simple:
        for k in (-0.55, 0.55):
            half = r * math.sqrt(1 - k * k)
            ld.line((cx - half, cy + k * r, cx + half, cy + k * r), fill=255, width=lw)
    clip = Image.new("L", (size, size), 0)
    ImageDraw.Draw(clip).ellipse((cx - r + lw, cy - r + lw, cx + r - lw, cy + r - lw), fill=255)
    lines = ImageChops.multiply(lines, clip)
    tint = vertical_gradient(size, [(0, (40, 150, 255)), (1, (0, 88, 220))])
    tint.putalpha(lines)

    gshadow = Image.new("RGBA", (size, size), (0, 30, 90, 0))
    gshadow.putalpha(glyph.getchannel("A").point(lambda v: v * 0.30))
    gshadow = ImageChops.offset(gshadow, 0, 10 * ss).filter(ImageFilter.GaussianBlur(14 * ss))
    glyph.putalpha(ImageChops.multiply(glyph.getchannel("A"), mask))
    canvas.alpha_composite(gshadow)
    canvas.alpha_composite(glyph)
    canvas.alpha_composite(tint)

    # Thin inner highlight along the top edge of the tile.
    edge = ImageChops.subtract(mask, ImageChops.offset(mask, 0, 3 * ss))
    hl = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    hl.putalpha(edge.point(lambda v: v * 0.5))
    canvas.alpha_composite(hl)

    return canvas.resize((S, S), Image.LANCZOS)


def main():
    full = render(simple=False)
    simple = render(simple=True)
    full.save(HERE / "brisk.png")
    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    frames = [(simple if s <= 32 else full).resize((s, s), Image.LANCZOS) for s in sizes]
    frames[-1].save(HERE / "brisk.ico", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
    # Preview sheet for review.
    sheet = Image.new("RGBA", (1024 + 40 + 256 + 160, 1024), (236, 236, 240, 255))
    sheet.alpha_composite(full, (0, 0))
    x = 1064
    sheet.alpha_composite(frames[-1], (x, 40))
    y = 340
    for s, f in zip(sizes, frames):
        if s in (16, 24, 32, 48, 64, 128):
            sheet.alpha_composite(f, (x, y))
            y += s + 20
    sheet.save(HERE / "preview.png")


if __name__ == "__main__":
    main()
