#!/usr/bin/env python3
"""Generates the Battery Rescue Alert icon set from code (original artwork, MIT licensed).
Requires Pillow:  pip install pillow
Outputs: assets/icons/BatteryRescueAlert.ico, app-logo PNGs, and a preview sheet."""
from PIL import Image, ImageDraw
import os, sys

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "icons")
os.makedirs(OUT, exist_ok=True)

NAVY = (14, 59, 76, 255)      # shield
TEAL = (20, 130, 128, 255)    # shield highlight edge
WHITE = (255, 255, 255, 255)
AMBER = (255, 176, 32, 255)   # the "low" segment: warning signal

def draw_icon(size, simple=False):
    S = 1024
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # Shield silhouette
    top, bottom, left, right = 60, 980, 130, 894
    mid = (left + right) // 2
    pts = [(left, top + 70), (mid, top), (right, top + 70), (right, 520),
           (right - 40, 700), (mid, bottom), (left + 40, 700), (left, 520)]
    d.polygon(pts, fill=TEAL)
    inset = 34
    pts2 = [(left + inset, top + 100), (mid, top + 50), (right - inset, top + 100), (right - inset, 520),
            (right - inset - 36, 685), (mid, bottom - 70), (left + inset + 36, 685), (left + inset, 520)]
    d.polygon(pts2, fill=NAVY)
    # Battery body (outline) + terminal
    bx0, by0, bx1, by1 = 232, 330, 760, 640
    w = 44 if not simple else 56
    d.rounded_rectangle((bx0, by0, bx1, by1), radius=52, fill=WHITE)
    d.rounded_rectangle((bx0 + w, by0 + w, bx1 - w, by1 - w), radius=24, fill=NAVY)
    d.rounded_rectangle((bx1 - 6, 410, bx1 + 70, 560), radius=26, fill=WHITE)
    # Charge segments: one amber (warning), the rest empty -> reads as "low battery" at any size
    inner_x0, inner_x1 = bx0 + w + 26, bx1 - w - 26
    seg_y0, seg_y1 = by0 + w + 26, by1 - w - 26
    if simple:
        d.rounded_rectangle((inner_x0, seg_y0, inner_x0 + 120, seg_y1), radius=14, fill=AMBER)
    else:
        gap = 22
        seg_w = 98
        for i in range(1):
            x = inner_x0 + i * (seg_w + gap)
            d.rounded_rectangle((x, seg_y0, x + seg_w, seg_y1), radius=16, fill=AMBER)
        # faint empty slots
        for i in range(1, 3):
            x = inner_x0 + i * (seg_w + gap)
            d.rounded_rectangle((x, seg_y0, x + seg_w, seg_y1), radius=16, outline=(255, 255, 255, 70), width=6)
    # Warning signal arcs above the battery (alert waves)
    cx = mid
    for r, a in ((120, 255), (190, 170)):
        d.arc((cx - r, 250 - r, cx + r, 250 + r), start=215, end=325, fill=AMBER[:3] + (a,), width=26 if not simple else 34)
    return img.resize((size, size), Image.LANCZOS)

sizes = [16, 24, 32, 48, 64, 128, 256]
frames = [draw_icon(s, simple=(s <= 32)) for s in sizes]
frames[-1].save(os.path.join(OUT, "BatteryRescueAlert.ico"), format="ICO",
                sizes=[(s, s) for s in sizes], append_images=frames[:-1])
draw_icon(512).save(os.path.join(OUT, "app-logo-512.png"))
draw_icon(64).save(os.path.join(OUT, "app-logo-64.png"))
# Preview sheet on light and dark backgrounds
sheet = Image.new("RGBA", (900, 330), (255, 255, 255, 255))
dk = Image.new("RGBA", (450, 330), (24, 28, 33, 255))
sheet.paste(dk, (450, 0))
x = 20
for s in (16, 24, 32, 48, 64, 128):
    ic = draw_icon(s, simple=(s <= 32))
    sheet.alpha_composite(ic, (x, 20)); sheet.alpha_composite(ic, (x + 450, 20)); x += s + 18
big = draw_icon(160)
sheet.alpha_composite(big, (20, 160)); sheet.alpha_composite(big, (470, 160))
sheet.convert("RGB").save(os.path.join(OUT, "icon-preview.png"))
print("icons written to", os.path.abspath(OUT))
