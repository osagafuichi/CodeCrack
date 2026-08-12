#!/usr/bin/env python3
"""Generate winapp/CodeCrackApp/Assets/CodeCrack.ico reproducibly.

A clean flat placeholder app icon: a magnifier over a code glyph ("</>") on a
rounded blue tile -- CodeCrack's "inspect the code, find the bug" idea. Drawn
purely from vector primitives (no font dependency) so the output is byte-stable
across machines. Each icon size is rendered at 4x supersampling and downscaled
with LANCZOS for crisp edges, then all sizes are packed into one multi-res .ico.

Usage:  python scripts/make-icon.py
Requires: Pillow (pip install pillow).
"""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw

# Multi-resolution .ico: the sizes Windows actually pulls from (title bar,
# taskbar, alt-tab, large-icon views).
SIZES = [16, 32, 48, 256]

# Flat palette (fixed => reproducible).
BG_TOP = (37, 99, 235)      # blue-600
BG_BOT = (29, 78, 216)      # blue-700
GLASS = (255, 255, 255)     # magnifier ring + handle
CODE = (191, 219, 254)      # code glyph "</>" (blue-200)


def _lerp(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))  # type: ignore[return-value]


def _render(px: int) -> Image.Image:
    """Render one square icon at `px` pixels using 4x supersampling."""
    s = px * 4
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Rounded tile with a subtle vertical gradient (drawn as horizontal bands,
    # then masked to a rounded rectangle so corners stay clean at every size).
    tile = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    td = ImageDraw.Draw(tile)
    for y in range(s):
        td.line([(0, y), (s, y)], fill=_lerp(BG_TOP, BG_BOT, y / max(1, s - 1)))
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.22), fill=255)
    img.paste(tile, (0, 0), mask)

    # Magnifier ring, centred slightly high-left to leave room for the handle.
    ring = max(3, int(s * 0.075))
    r = s * 0.28
    mx, my = s * 0.44, s * 0.44
    # Handle first (behind the ring): a thick rounded bar out the lower-right.
    hx0, hy0 = mx + r * 0.70, my + r * 0.70
    hx1, hy1 = mx + r * 1.55, my + r * 1.55
    d.line([(hx0, hy0), (hx1, hy1)], fill=GLASS, width=int(ring * 1.6))
    d.ellipse([hx1 - ring * 0.8, hy1 - ring * 0.8, hx1 + ring * 0.8, hy1 + ring * 0.8], fill=GLASS)
    d.ellipse([mx - r, my - r, mx + r, my + r], outline=GLASS, width=ring)

    # Code glyph "</>" centred inside the lens.
    stroke = max(2, int(s * 0.045))
    w, h = r * 0.42, r * 0.42
    gap = r * 0.30
    # "<" (left): vertex on the far left, arms opening toward centre.
    d.line([(mx - gap - w * 0.6, my), (mx - gap, my - h * 0.55)], fill=CODE, width=stroke)
    d.line([(mx - gap - w * 0.6, my), (mx - gap, my + h * 0.55)], fill=CODE, width=stroke)
    # ">" (right): vertex on the far right, arms opening toward centre.
    d.line([(mx + gap + w * 0.6, my), (mx + gap, my - h * 0.55)], fill=CODE, width=stroke)
    d.line([(mx + gap + w * 0.6, my), (mx + gap, my + h * 0.55)], fill=CODE, width=stroke)
    # "/" (centre slash)
    d.line([(mx - w * 0.30, my + h * 0.7), (mx + w * 0.30, my - h * 0.7)], fill=CODE, width=stroke)

    return img.resize((px, px), Image.LANCZOS)


def main() -> None:
    out = Path(__file__).resolve().parent.parent / "winapp" / "CodeCrackApp" / "Assets" / "CodeCrack.ico"
    out.parent.mkdir(parents=True, exist_ok=True)
    frames = [_render(px) for px in SIZES]
    # Pillow writes a real multi-image .ico when given `sizes`; base it on the
    # 256px frame and let it embed each requested size.
    base = frames[-1]
    base.save(out, format="ICO", sizes=[(px, px) for px in SIZES])
    print(f"wrote {out} ({', '.join(f'{p}x{p}' for p in SIZES)})")


if __name__ == "__main__":
    main()
