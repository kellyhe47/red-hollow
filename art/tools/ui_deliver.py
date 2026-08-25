#!/usr/bin/env python3
"""Deliver a UI/props asset: exact-size resize + mandatory alpha/size checks.

Usage:
  python3 art/tools/ui_deliver.py <input.png> <out_path.png> <W>x<H> [--opaque]

Resizes with lanczos, asserts exact output size, and (unless --opaque) runs the
alpha checks from docs/comfy-prompts/04-ui-props-agent.md: composites over
magenta and dark blue and fails on white/checkerboard fringe (bright low-alpha
pixels hugging the silhouette), plus warns if alpha is all-opaque (matte
probably missing). Writes companion check composites next to the output as
<out>_check_magenta.png / <out>_check_blue.png for eyeballing.
"""
import sys
from pathlib import Path

from PIL import Image


def fail(msg):
    print(f"FAIL: {msg}")
    sys.exit(1)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    opaque = "--opaque" in sys.argv
    if len(args) != 3:
        fail(__doc__)
    src, dst, size = Path(args[0]), Path(args[1]), args[2]
    w, h = (int(x) for x in size.lower().split("x"))

    im = Image.open(src).convert("RGBA")
    im = im.resize((w, h), Image.LANCZOS)
    if im.size != (w, h):
        fail(f"resize produced {im.size}, wanted {(w, h)}")

    if not opaque:
        px = im.load()
        alphas = [px[x, y][3] for y in range(h) for x in range(0, w, max(1, w // 256))]
        n = len(alphas)
        opaque_frac = sum(1 for a in alphas if a > 247) / n
        if opaque_frac > 0.995:
            print("WARN: alpha is ~fully opaque; matte missing?")
        # white-fringe check: semi-transparent pixels that are near-white
        fringe = 0
        soft = 0
        for y in range(h):
            for x in range(w):
                r, g, b, a = px[x, y]
                if 8 < a < 200:
                    soft += 1
                    if r > 230 and g > 230 and b > 230:
                        fringe += 1
        if soft and fringe / soft > 0.10:
            fail(f"white fringe: {fringe}/{soft} soft pixels near-white (halo)")
        for name, bg in (("magenta", (255, 0, 255, 255)), ("blue", (0, 27, 77, 255))):
            comp = Image.new("RGBA", im.size, bg)
            comp.alpha_composite(im)
            comp.convert("RGB").save(dst.parent / f"{dst.stem}_check_{name}.png")

    dst.parent.mkdir(parents=True, exist_ok=True)
    im.save(dst)
    out = Image.open(dst)
    if out.size != (w, h):
        fail(f"written file is {out.size}, wanted {(w, h)}")
    print(f"OK: {dst} {w}x{h}" + ("" if opaque else " (alpha checks passed)"))


if __name__ == "__main__":
    main()
