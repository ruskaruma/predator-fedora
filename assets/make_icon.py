#!/usr/bin/env python3
"""Renders the PredatorCore app icon: a faceted shield emblem on dark glass.

Drawn at 4x and downsampled for clean edges. Outputs icon.png (with tile), iconTransparent.png
(emblem only) and hicolor sizes for the desktop entry.
Usage: python3 assets/make_icon.py [output_dir] [--emblem logo.png]

--emblem renders any logo (alpha or dark-on-light artwork) with the same glass treatment
instead of the built-in shield. Only the left-most mark is used when the file also has a wordmark.
"""
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

S = 2048                      # working canvas
_args = [a for a in sys.argv[1:]]
EMBLEM_FILE = _args[_args.index("--emblem") + 1] if "--emblem" in _args else None
_positional = [a for i, a in enumerate(_args) if a != "--emblem" and (i == 0 or _args[i - 1] != "--emblem")]
OUT = _positional[0] if _positional else os.path.join(os.path.dirname(__file__), "..", "src", "PredatorCore")


def vgradient(size, top, bottom):
    """Vertical RGBA gradient from `top` to `bottom`."""
    w, h = size
    mask = Image.linear_gradient("L").resize((w, h))
    return Image.composite(Image.new("RGBA", size, bottom), Image.new("RGBA", size, top), mask)


def dgradient(size, a, b):
    """Diagonal gradient (top-left a → bottom-right b)."""
    w, h = size
    mask = Image.linear_gradient("L").rotate(45, expand=True).resize((w, h))
    return Image.composite(Image.new("RGBA", size, b), Image.new("RGBA", size, a), mask)


def poly_mask(points, size=(S, S)):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon(points, fill=255)
    return m


def scale(points, k=S / 1024):
    return [(x * k, y * k) for x, y in points]


def tile_mask():
    m = Image.new("L", (S, S), 0)
    ImageDraw.Draw(m).rounded_rectangle([96, 96, S - 96, S - 96], radius=440, fill=255)
    return m


# ---------- emblem geometry (1024 space) ----------
SHIELD = [(512, 150), (812, 262), (782, 560), (512, 880), (242, 560), (212, 262)]
LEFT_FACET = [(512, 150), (512, 880), (242, 560), (212, 262)]
VISOR_L = [(300, 420), (486, 492), (478, 548), (318, 486)]
VISOR_R = [(1024 - x, y) for x, y in VISOR_L]
RIDGE = [(512, 560), (536, 600), (512, 760), (488, 600)]
CROWN = [(512, 150), (560, 232), (512, 300), (464, 232)]


def emblem():
    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    shield = poly_mask(scale(SHIELD))

    # glow behind the emblem
    glow = Image.new("RGBA", (S, S), (0, 214, 255, 0))
    glow.putalpha(shield.filter(ImageFilter.GaussianBlur(90)).point(lambda v: int(v * 0.75)))
    layer = Image.alpha_composite(layer, glow)

    # two-tone faceted metal: bright right facet, darker left facet
    body = vgradient((S, S), (140, 250, 255, 255), (0, 120, 210, 255))
    layer.paste(body, (0, 0), shield)
    dark = vgradient((S, S), (40, 190, 240, 255), (0, 70, 150, 255))
    layer.paste(dark, (0, 0), ImageChops.multiply(poly_mask(scale(LEFT_FACET)), shield))

    # crown notch and centre ridge as lighter bevels
    d = ImageDraw.Draw(layer)
    d.polygon(scale(CROWN), fill=(210, 255, 255, 235))
    d.polygon(scale(RIDGE), fill=(160, 245, 255, 200))

    # visor slits: cut dark, then light them from inside in orange
    for eye in (VISOR_L, VISOR_R):
        m = poly_mask(scale(eye))
        layer.paste((6, 10, 16, 255), (0, 0), m)
        inner = Image.new("RGBA", (S, S), (255, 138, 0, 0))
        inner.putalpha(m.filter(ImageFilter.GaussianBlur(10)).point(lambda v: int(v * 0.95)))
        layer = Image.alpha_composite(layer, inner)
        core = Image.new("RGBA", (S, S), (255, 214, 140, 0))
        core.putalpha(m.filter(ImageFilter.MinFilter(21)).filter(ImageFilter.GaussianBlur(6)))
        layer = Image.alpha_composite(layer, core)

    # crisp bright outline
    edge = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(edge).line(scale(SHIELD) + [scale(SHIELD)[0]], fill=(200, 255, 255, 220), width=10, joint="curve")
    edge.putalpha(ImageChops.multiply(edge.getchannel("A"), shield.filter(ImageFilter.MaxFilter(11))))
    return Image.alpha_composite(layer, edge)


def mark_mask_from_file(path):
    """Alpha mask of the left-most mark in a logo file, centred on the canvas."""
    src = Image.open(path).convert("RGBA")
    alpha = src.getchannel("A")
    if alpha.getextrema() == (255, 255):  # no transparency: treat dark pixels as the mark
        alpha = ImageChops.invert(src.convert("L"))
    alpha = alpha.point(lambda v: 255 if v > 90 else 0)

    # keep only the first column run of ink (the symbol), dropping any wordmark after a gap
    w, h = alpha.size
    cols = [alpha.crop((x, 0, x + 1, h)).getbbox() is not None for x in range(w)]
    start = cols.index(True)
    end, gap = start, 0
    for x in range(start, w):
        if cols[x]:
            end, gap = x, 0
        else:
            gap += 1
            if gap > h * 0.08:
                break
    mark = alpha.crop((start, 0, end + 1, h))
    mark = mark.crop(mark.getbbox())

    box = int(S * 0.70)
    k = box / max(mark.size)
    mark = mark.resize((int(mark.width * k), int(mark.height * k)), Image.LANCZOS)
    canvas = Image.new("L", (S, S), 0)
    canvas.paste(mark, ((S - mark.width) // 2, (S - mark.height) // 2))
    return canvas


def emblem_from_mask(mask):
    """Cyan metal fill with a darker left facet, glow and a lit edge: same finish as the shield."""
    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    glow = Image.new("RGBA", (S, S), (0, 214, 255, 0))
    glow.putalpha(mask.filter(ImageFilter.GaussianBlur(70)).point(lambda v: min(255, int(v * 1.1))))
    layer = Image.alpha_composite(layer, glow)

    layer.paste(vgradient((S, S), (170, 252, 255, 255), (0, 120, 215, 255)), (0, 0), mask)
    # darker core so the shape reads as bevelled metal rather than a flat fill
    core = mask.filter(ImageFilter.MinFilter(31)).filter(ImageFilter.GaussianBlur(24))
    shade = Image.new("RGBA", (S, S), (0, 40, 90, 0))
    shade.putalpha(core.point(lambda v: int(v * 0.18)))
    layer = Image.alpha_composite(layer, shade)

    # thin bright bevel along the top edges of the shape
    bevel = ImageChops.subtract(mask, ImageChops.offset(mask, 0, 10))
    hi = Image.new("RGBA", (S, S), (225, 255, 255, 0))
    hi.putalpha(bevel.filter(ImageFilter.GaussianBlur(2)).point(lambda v: int(v * 0.9)))
    return Image.alpha_composite(layer, hi)


def glass_tile():
    mask = tile_mask()
    tile = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    tile.paste(vgradient((S, S), (20, 27, 36, 255), (3, 5, 8, 255)), (0, 0), mask)

    # faint cyan ambience from below
    amb = Image.new("RGBA", (S, S), (0, 180, 255, 0))
    a = Image.new("L", (S, S), 0)
    ImageDraw.Draw(a).ellipse([200, 1100, S - 200, 2400], fill=110)
    amb.putalpha(ImageChops.multiply(a.filter(ImageFilter.GaussianBlur(220)), mask))
    tile = Image.alpha_composite(tile, amb)
    return tile, mask


def glass_overlay(mask):
    """Specular highlight on the upper half plus a thin lit rim: the 'glass' finish."""
    over = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    spec = Image.new("L", (S, S), 0)
    ImageDraw.Draw(spec).ellipse([-420, -1650, S + 420, 860], fill=255)
    spec = spec.filter(ImageFilter.GaussianBlur(30))
    fade = Image.linear_gradient("L").resize((S, S)).point(lambda v: max(0, 58 - v // 4))
    spec = ImageChops.multiply(ImageChops.multiply(spec, fade), mask)
    white = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    white.putalpha(spec)
    over = Image.alpha_composite(over, white)

    rim = Image.new("L", (S, S), 0)
    ImageDraw.Draw(rim).rounded_rectangle([100, 100, S - 100, S - 100], radius=436, outline=255, width=8)
    rim_fade = Image.linear_gradient("L").resize((S, S)).point(lambda v: 200 - int(v * 0.6))
    rim_layer = Image.new("RGBA", (S, S), (180, 240, 255, 0))
    rim_layer.putalpha(ImageChops.multiply(rim, rim_fade))
    return Image.alpha_composite(over, rim_layer)


def main():
    os.makedirs(OUT, exist_ok=True)
    tile, mask = glass_tile()
    em = emblem_from_mask(mark_mask_from_file(EMBLEM_FILE)) if EMBLEM_FILE else emblem()
    em_small = em.resize((int(S * 0.78), int(S * 0.78)), Image.LANCZOS)
    offset = ((S - em_small.width) // 2, (S - em_small.height) // 2 + 20)

    icon = tile.copy()
    icon.alpha_composite(em_small, offset)
    icon = Image.alpha_composite(icon, glass_overlay(mask))

    # soft drop shadow so it sits on light and dark docks
    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    shadow.putalpha(mask.filter(ImageFilter.GaussianBlur(40)).point(lambda v: int(v * 0.45)))
    final = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    final.alpha_composite(shadow, (0, 24))
    final = Image.alpha_composite(final, icon)

    final.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "icon.png"))
    em.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "iconTransparent.png"))

    # hicolor sizes for the desktop entry; for the repo defaults they live in assets/, not the app bundle
    hicolor = os.path.join(os.path.dirname(os.path.abspath(__file__)), "hicolor") if not _positional \
        else os.path.join(OUT, "hicolor")
    for size in (16, 24, 32, 48, 64, 128, 256, 512):
        d = os.path.join(hicolor, f"{size}x{size}", "apps")
        os.makedirs(d, exist_ok=True)
        final.resize((size, size), Image.LANCZOS).save(os.path.join(d, "predatorcore.png"))
    print("icons written to", OUT)


if __name__ == "__main__":
    main()
