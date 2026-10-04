"""Workshop 用の icon.png / preview.png を生成する（Pillow が必要）。

使い方: python tools/make_about_images.py
出力先: src/SodRpg.Mod/about/
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src", "SodRpg.Mod", "about")
FONT_CANDIDATES = [
    "C:/Windows/Fonts/YuGothB.ttc",
    "C:/Windows/Fonts/meiryob.ttc",
    "/System/Library/Fonts/ヒラギノ角ゴシック W6.ttc",
    "/usr/share/fonts/noto-cjk/NotoSansCJK-Bold.ttc",
]


def font(size):
    for path in FONT_CANDIDATES:
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def background(w, h, seed=7):
    rnd = random.Random(seed)
    img = Image.new("RGB", (w, h))
    px = img.load()
    for y in range(h):
        t = y / h
        for x in range(w):
            r = int(18 + 30 * t + 12 * math.sin(x / w * 3.1))
            g = int(14 + 10 * t)
            b = int(40 + 50 * (1 - t))
            px[x, y] = (r, g, b)
    draw = ImageDraw.Draw(img)
    for _ in range(int(w * h / 900)):
        x, y = rnd.randrange(w), rnd.randrange(h)
        s = rnd.choice([1, 1, 1, 2])
        c = rnd.randrange(150, 255)
        draw.ellipse([x, y, x + s, y + s], fill=(c, c, min(255, c + 30)))
    return img


def relic(draw, cx, cy, r, color):
    pts = [(cx, cy - r), (cx + r * 0.62, cy), (cx, cy + r), (cx - r * 0.62, cy)]
    draw.polygon(pts, fill=color)
    inner = [(cx, cy - r * 0.55), (cx + r * 0.32, cy), (cx, cy + r * 0.55), (cx - r * 0.32, cy)]
    draw.polygon(inner, fill=(255, 245, 220))


def glow(img, cx, cy, r, color):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=color + (150,))
    layer = layer.filter(ImageFilter.GaussianBlur(r / 2.2))
    img.paste(layer, (0, 0), layer)


def make_icon():
    s = 256
    img = background(s, s, seed=3)
    glow(img, s // 2, s // 2, 90, (255, 170, 60))
    d = ImageDraw.Draw(img)
    relic(d, s // 2, s // 2 - 8, 82, (255, 181, 46))
    d.text((s // 2, s - 34), "夢鍛", font=font(40), fill=(255, 230, 180), anchor="mm")
    img.save(os.path.join(OUT, "icon.png"))


def make_preview():
    w, h = 1280, 720
    img = background(w, h, seed=11)
    colors = [(214, 214, 214), (98, 217, 98), (79, 168, 255), (196, 117, 255), (255, 181, 46)]
    for i, c in enumerate(colors):
        cx = 170 + i * 235
        glow(img, cx, 470, 70, c)
        relic(ImageDraw.Draw(img), cx, 470, 64, c)
    d = ImageDraw.Draw(img)
    d.text((w // 2, 120), "Dreamforge RPG", font=font(84), fill=(255, 225, 160), anchor="mm")
    d.text((w // 2, 215), "夢鍛RPG ─ 夢の遺物と深度", font=font(48), fill=(230, 220, 255), anchor="mm")
    d.text((w // 2, 300), "Persistent relics · Secure or Delve · Star map · Co-op", font=font(32), fill=(200, 200, 230), anchor="mm")
    d.text((w // 2, 640), "確保するか、もっと深く潜るか。", font=font(40), fill=(255, 200, 150), anchor="mm")
    img.save(os.path.join(OUT, "preview.png"))


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    make_icon()
    make_preview()
    print("written:", os.path.relpath(OUT, ROOT))
