"""Every sprite the A350's displays drew in a corpus of captures (A350Probe displays with A350_DRAW_JSON), cut out of the
texture it comes from and laid on numbered contact sheets, one per display, so each can be looked at and named.

A shape's image is the sim's own texture handle, so the texture is found by size: a pattern carries its image's width and
height (PatternW, PatternH), and where several files share a size, the one with pixels where the sprites are cut wins. An
unrotated sprite's place in its texture is its box less its pattern origin; a rotated or scaled one is listed apart.

usage: python sprites.py CORPUS_DIR OUT_DIR   (CORPUS_DIR holds one folder per round, each with EFIS_0.json, CTR_0.json ...)
Writes OUT_DIR/<display>.png (the sheets), OUT_DIR/<display>.tsv (number, texture, atlas x y w h, uses, where drawn, tint) and
OUT_DIR/textures.tsv (each handle's texture). Read-only towards the sim; Pillow is the one dependency."""
import collections
import glob
import json
import os
import struct
import sys

if len(sys.argv) < 3:
    raise SystemExit('usage: python sprites.py CORPUS_DIR OUT_DIR')
from PIL import Image, ImageDraw   # noqa: E402

import package   # noqa: E402

CORPUS, OUT = sys.argv[1], sys.argv[2]
PKG = package.find()
if PKG is None:
    raise SystemExit('The iniBuilds A350 package was not found: set A350_PACKAGE to your inibuilds-aircraft-a350 folder.')
TEXTURES = os.path.join(PKG, 'Data', 'Textures')


def png_size(path):
    with open(path, 'rb') as f:
        head = f.read(24)
    return struct.unpack('>II', head[16:24])


BY_SIZE = collections.defaultdict(list)
for p in glob.glob(os.path.join(TEXTURES, '*', '*.png')):
    BY_SIZE[png_size(p)].append(p)
_images = {}


def image(path):
    if path not in _images:
        _images[path] = Image.open(path).convert('RGBA')
    return _images[path]


def coverage(path, rect):
    x, y, w, h = rect
    if w < 1 or h < 1:
        return 0.0
    crop = image(path).crop((x, y, x + w, y + h))
    alpha = crop.getchannel('A')
    return sum(1 for a in alpha.getdata() if a > 16) / float(w * h)


def unrotated(s):
    return (abs(s.get('PatternA', 1) - 1) < 1e-3 and abs(s.get('PatternD', 1) - 1) < 1e-3
            and abs(s.get('PatternB', 0)) < 1e-3 and abs(s.get('PatternC', 0)) < 1e-3)


def colour_name(c):
    r, g, b = c['R'], c['G'], c['B']
    return f'{int(r * 255):02x}{int(g * 255):02x}{int(b * 255):02x}'


uses = collections.defaultdict(lambda: collections.defaultdict(list))   # display -> (handle, rect) -> [(x, y, tint, round)]
rotated = collections.defaultdict(collections.Counter)
sizes = collections.defaultdict(collections.Counter)                     # (display, handle) -> pattern sizes
for f in sorted(glob.glob(os.path.join(CORPUS, '*', '*.json'))):
    display = os.path.basename(f)[:-5]
    try:
        capture = json.load(open(f))
    except (OSError, ValueError):
        continue
    for s in capture.get('Shapes') or []:
        handle, pw, ph = s.get('Image', 0), round(s.get('PatternW', 0)), round(s.get('PatternH', 0))
        if handle <= 1 or pw <= 0 or ph <= 0:
            continue
        sizes[(display, handle)][(pw, ph)] += 1
        if not unrotated(s):
            rotated[display][(handle, round(s['X1'] - s['X0']), round(s['Y1'] - s['Y0']))] += 1
            continue
        rect = (round(s['X0'] - s['PatternX']), round(s['Y0'] - s['PatternY']), round(s['X1'] - s['X0']), round(s['Y1'] - s['Y0']))
        if rect[2] >= pw and rect[3] >= ph:
            continue   # the whole texture as a background
        uses[display][(handle, rect)].append((round(s['X0']), round(s['Y0']), colour_name(s['Colour']), os.path.basename(os.path.dirname(f))))

os.makedirs(OUT, exist_ok=True)
textures = {}
with open(os.path.join(OUT, 'textures.tsv'), 'w', encoding='utf-8') as t:
    for (display, handle), counter in sorted(sizes.items()):
        (pw, ph), _ = counter.most_common(1)[0]
        candidates = BY_SIZE.get((pw, ph), [])
        rects = [r for (h, r) in uses[display] if h == handle][:40]
        best = max(candidates, key=lambda p: sum(coverage(p, r) for r in rects), default=None) if rects else (candidates[0] if len(candidates) == 1 else None)
        textures[(display, handle)] = best
        t.write(f'{display}\t{handle}\t{pw}x{ph}\t{os.path.relpath(best, TEXTURES) if best else "?"}\t{len(candidates)} of that size\n')

for display, sprites in sorted(uses.items()):
    rows = sorted(sprites.items(), key=lambda kv: (kv[0][0], kv[0][1][1], kv[0][1][0]))
    cell, cols = 120, 8
    sheet = Image.new('RGB', (cols * cell, max(1, (len(rows) + cols - 1) // cols) * cell), (40, 40, 48))
    draw = ImageDraw.Draw(sheet)
    with open(os.path.join(OUT, display + '.tsv'), 'w', encoding='utf-8') as out:
        for n, ((handle, rect), seen) in enumerate(rows):
            path = textures.get((display, handle))
            gx, gy = (n % cols) * cell, (n // cols) * cell
            if path:
                x, y, w, h = rect
                crop = image(path).crop((x, y, x + w, y + h))
                crop.thumbnail((cell - 8, cell - 22))
                sheet.paste(crop, (gx + 4, gy + 18), crop)
            draw.text((gx + 3, gy + 2), str(n), fill=(255, 255, 0))
            where = collections.Counter((sx, sy) for sx, sy, _, _ in seen).most_common(1)[0][0]
            tints = ','.join(sorted({c for _, _, c, _ in seen}))
            out.write(f'{n}\t{os.path.relpath(path, TEXTURES) if path else "?"}\t{rect[0]}\t{rect[1]}\t{rect[2]}\t{rect[3]}\t'
                      f'{len(seen)}\t{where[0]},{where[1]}\t{tints}\t{len({r for _, _, _, r in seen})} rounds\n')
    sheet.save(os.path.join(OUT, display + '.png'))
    print(f'{display}: {len(rows)} sprites, {sum(rotated[display].values())} rotated or scaled uses')
