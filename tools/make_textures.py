#!/usr/bin/env python3
"""Процедурные PBR-текстуры 2К для игры «Samsar».

Генерирует наборы текстур (albedo.jpg 2048, normal.png 1024, mask.png 512),
где mask: R = metalness, A = smoothness (формат Unity Standard/URP-совместимый).

Запуск: tools/blender.sh tools/make_textures.py   (или python3 при наличии numpy+Pillow)
Результат: game/Assets/Textures/<material>_{albedo.jpg,normal.png,mask.png}
"""
import os
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "game", "Assets", "Textures")

ALBEDO = 2048
NORMAL = 1024
MASK = 512
SEED = 20260919


# ---------- шум ----------
def value_noise(size, cells, rng):
    """Гладкий value-noise: случайная решётка cells×cells, билинейный апскейл."""
    g = rng.random((cells + 1, cells + 1)).astype(np.float32)
    ys = np.linspace(0, cells, size, endpoint=False, dtype=np.float32)
    xs = np.linspace(0, cells, size, endpoint=False, dtype=np.float32)
    y0 = np.floor(ys).astype(np.int32); x0 = np.floor(xs).astype(np.int32)
    fy = (ys - y0)[:, None]; fx = (xs - x0)[None, :]
    fy = fy * fy * (3 - 2 * fy)
    fx = fx * fx * (3 - 2 * fx)
    a = g[np.ix_(y0, x0)]; b = g[np.ix_(y0, x0 + 1)]
    c = g[np.ix_(y0 + 1, x0)]; d = g[np.ix_(y0 + 1, x0 + 1)]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(size, octaves, base_cells, rng, gain=0.5, lacunarity=2.0):
    out = np.zeros((size, size), np.float32)
    amp, cells, norm = 1.0, base_cells, 0.0
    for _ in range(octaves):
        out += amp * value_noise(size, max(2, int(cells)), rng)
        norm += amp
        amp *= gain
        cells *= lacunarity
    out /= norm
    return out


def tiltable(a):
    """Сделать карту бесшовной по краям (tileable): смешение с зеркальной копией."""
    s = a.shape[0]
    w = np.linspace(0, 1, s, dtype=np.float32)[:, None]
    w = np.minimum(w, 1 - w) * 2.0
    r = a[::-1, :]
    c = a[:, ::-1]
    d = a[::-1, ::-1]
    out = a * w * w.T + r * (1 - w) * w.T + c * w * (1 - w.T) + d * (1 - w) * (1 - w.T)
    return np.clip(out, 0, 1)


def normalize01(a):
    a = a.astype(np.float32)
    lo, hi = np.percentile(a, 1), np.percentile(a, 99)
    if hi - lo < 1e-6:
        return np.zeros_like(a)
    return np.clip((a - lo) / (hi - lo), 0, 1)


def normal_from_height(h, strength=2.0):
    dx = np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)
    dy = np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)
    nx, ny, nz = -dx * strength, -dy * strength, np.ones_like(h)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    rgb = np.stack([nx / ln, ny / ln, nz / ln], -1) * 0.5 + 0.5
    return (rgb * 255).astype(np.uint8)


def mask_png(metal, smooth):
    """R = metalness, A = smoothness."""
    s = metal.shape[0]
    rgba = np.zeros((s, s, 4), np.uint8)
    rgba[..., 0] = np.clip(metal * 255, 0, 255)
    rgba[..., 1] = np.clip(smooth * 255, 0, 255)
    rgba[..., 2] = 255
    rgba[..., 3] = np.clip(smooth * 255, 0, 255)
    return Image.fromarray(rgba, "RGBA")


def down(a, size):
    img = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8), "L")
    return np.asarray(img.resize((size, size), Image.LANCZOS), np.float32) / 255.0


def tint(gray, color):
    return np.stack([gray * color[0], gray * color[1], gray * color[2]], -1)


def save(name, albedo_rgb, height, metal, smooth):
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray((np.clip(albedo_rgb, 0, 1) * 255).astype(np.uint8), "RGB").save(
        os.path.join(OUT, name + "_albedo.jpg"), quality=84, subsampling=1)
    Image.fromarray(normal_from_height(down(height, NORMAL), 2.4), "RGB").save(
        os.path.join(OUT, name + "_normal.png"))
    mask_png(down(metal, MASK), down(smooth, MASK)).save(os.path.join(OUT, name + "_mask.png"))
    print("  ", name, "ok")


# ---------- материалы ----------
def mat_steel(rng, color, name="steel", camo=True):
    """Окрашенная броня: 2–3 тона камуфляжа, грязь, царапины до металла."""
    n1 = tiltable(fbm(ALBEDO, 6, 5, rng))
    grain = tiltable(fbm(ALBEDO, 4, 240, rng))
    dirt = tiltable(fbm(ALBEDO, 4, 12, rng))
    scr_n = tiltable(fbm(ALBEDO, 3, 80, rng))
    scr = np.clip((scr_n - 0.66) * 3.4, 0, 1)
    base = 0.70 + n1 * 0.20 + grain * 0.08
    albedo = tint(base, color)
    if camo:
        blotch = tiltable(fbm(ALBEDO, 3, 7, rng))
        m1 = np.clip((blotch - 0.50) * 6.0, 0, 1)          # пятна второго тона
        m2 = np.clip((tiltable(fbm(ALBEDO, 3, 11, rng)) - 0.58) * 5.0, 0, 1)  # третьего тона
        albedo = albedo * (1 - m1[..., None] * 0.42) + m1[..., None] * (albedo * np.array([0.55, 0.52, 0.50]))
        albedo = albedo * (1 - m2[..., None] * 0.35) + m2[..., None] * (albedo * np.array([1.35, 1.28, 1.12]))
    mud = np.clip((dirt - 0.55) * 3.0, 0, 1)               # налипшая грязь
    albedo = albedo * (1 - mud[..., None] * 0.34) + mud[..., None] * np.array([0.24, 0.19, 0.13])
    albedo = albedo * (1 - scr[..., None] * 0.35) + scr[..., None] * np.array([0.42, 0.44, 0.45])
    height = base * 0.5 + scr * 0.5 + mud * 0.3
    # краска — диэлектрик (металл только на царапинах до грунта), иначе машина выглядит чёрной
    metal = np.clip(0.16 + scr * 0.52 - mud * 0.12, 0, 1)
    smooth = np.clip(0.52 + n1 * 0.10 - scr * 0.16 - mud * 0.34, 0.06, 0.95)
    save(name, np.clip(albedo, 0, 1), height, metal, smooth)


def mat_rusty(rng):
    n = tiltable(fbm(ALBEDO, 7, 5, rng))
    sp = tiltable(fbm(ALBEDO, 5, 40, rng))
    rust = np.clip((n + sp * 0.6 - 0.72) * 4.0, 0, 1)
    base = 0.60 + n * 0.25
    albedo = np.stack([base, base * 0.96, base * 0.88], -1)
    albedo = albedo * (1 - rust[..., None] * np.array([0.25, 0.45, 0.55])) + \
        rust[..., None] * np.array([0.46, 0.24, 0.11])
    metal = 0.85 - rust * 0.55
    smooth = np.clip(0.48 - rust * 0.35, 0.05, 0.95)
    save("metal_rusty", np.clip(albedo, 0, 1), base + rust * 0.4, metal, smooth)


def mat_rubber(rng):
    n = tiltable(fbm(ALBEDO, 6, 12, rng))
    g = tiltable(fbm(ALBEDO, 4, 300, rng))
    base = np.clip(0.16 + n * 0.12 + g * 0.06, 0, 1)
    save("rubber", tint(base, (1.0, 0.99, 0.97)), base, np.full_like(base, 0.35),
         np.clip(0.32 + n * 0.12, 0.05, 0.6))


def mat_concrete(rng):
    n = tiltable(fbm(ALBEDO, 7, 7, rng))
    grit = tiltable(fbm(ALBEDO, 3, 260, rng))
    cracks = tiltable(fbm(ALBEDO, 4, 26, rng))
    crk = np.clip((0.5 - np.abs(cracks - 0.5)) * 6.0, 0, 1) * 0.55
    base = np.clip(0.72 + n * 0.18 + grit * 0.10 - crk * 0.35, 0, 1)
    stains = np.clip((n - 0.55) * 2.0, 0, 1)
    albedo = tint(base, (1.0, 0.99, 0.95)) * (1 - stains[..., None] * 0.18)
    save("concrete", np.clip(albedo, 0, 1), base, np.full_like(base, 0.03),
         np.clip(0.55 + grit * 0.1 - crk * 0.2, 0.05, 0.9))


def mat_wood(rng):
    planks = np.tile(np.linspace(0, 1, ALBEDO, dtype=np.float32), (ALBEDO, 1))
    jitter = value_noise(ALBEDO, 24, rng)
    grain = tiltable(fbm(ALBEDO, 5, 140, rng))
    base = np.clip(0.45 + np.sin(planks * 46 + jitter * 2.2) * 0.05 + grain * 0.16, 0, 1)
    albedo = tint(base, (0.75, 0.52, 0.31))
    save("wood", albedo, base, np.full_like(base, 0.02),
         np.clip(0.35 + grain * 0.15, 0.05, 0.8))


def mat_ground(name, color, cells, detail, rough, metal=0.0):
    def _f(rng):
        n = tiltable(fbm(ALBEDO, 6, cells, rng))
        d = tiltable(fbm(ALBEDO, 4, detail, rng))
        base = np.clip(0.55 + n * 0.30 + d * 0.22, 0, 1)
        albedo = tint(base, color)
        save(name, albedo, base, np.full_like(base, metal),
             np.clip(rough + n * 0.1, 0.05, 1.0))
    return _f


def mat_dark_metal(rng):
    """Тёмная сталь: траки, опорные катки, ствол, мелкий металл. Тёмная, тёплая, потёртая."""
    n = tiltable(fbm(ALBEDO, 6, 6, rng))           # крупные потёртости
    mid = tiltable(fbm(ALBEDO, 5, 24, rng))        # пятна износа
    fine = tiltable(fbm(ALBEDO, 4, 260, rng))      # мелкая шероховатость
    scratches = np.clip((fine - 0.60) * 3.2, 0, 1) # царапины до металла
    wear = np.clip((n - 0.52) * 2.4, 0, 1)         # зашлифованные места
    base = 0.14 + n * 0.10 + mid * 0.06
    albedo = tint(base, (1.00, 1.02, 1.06))        # чуть «холодная» сталь
    albedo = albedo * (1 - wear[..., None] * 0.45) + wear[..., None] * np.array([0.34, 0.35, 0.36])
    albedo = albedo * (1 - scratches[..., None] * 0.5) + scratches[..., None] * np.array([0.52, 0.53, 0.55])
    height = base + wear * 0.3 + scratches * 0.2
    metal = np.clip(0.94 - scratches * 0.10, 0, 1)
    smooth = np.clip(0.34 + wear * 0.34 + scratches * 0.30, 0.08, 0.92)
    save("metal_dark", np.clip(albedo, 0, 1), height, metal, smooth)


def mat_optics(rng):
    base = np.full((ALBEDO, ALBEDO), 0.12, np.float32)
    albedo = tint(base, (0.75, 0.95, 1.0))
    save("optics", albedo, base, np.full_like(base, 0.9), np.full_like(base, 0.95))


def main():
    print("Генерация текстур в", OUT)
    rng = np.random.default_rng(SEED)
    mat_steel(rng, (0.30, 0.36, 0.24), "steel_olive")                     # оливковый
    mat_steel(np.random.default_rng(SEED + 1), (0.50, 0.45, 0.29), "steel_sand")   # песочный
    mat_steel(np.random.default_rng(SEED + 2), (0.33, 0.36, 0.42), "steel_grey")   # серо-синий
    mat_steel(np.random.default_rng(SEED + 3), (0.20, 0.28, 0.18), "steel_green")  # тёмно-зелёный
    # камуфляжи спецмашин: раньше все три были оливковыми, как ЛТ
    mat_steel(np.random.default_rng(SEED + 4), (0.33, 0.27, 0.18), "steel_bronze")  # «Бастион», бронза
    mat_steel(np.random.default_rng(SEED + 5), (0.46, 0.24, 0.20), "steel_red")     # «Арлекин», красно-бурый
    mat_steel(np.random.default_rng(SEED + 6), (0.15, 0.15, 0.17), "steel_black")   # «Ворон», чёрный
    mat_rusty(rng)
    mat_dark_metal(rng)
    mat_rubber(rng)
    mat_concrete(rng)
    mat_wood(rng)
    mat_optics(rng)
    mat_ground("ground_grass", (0.42, 0.62, 0.30), 5, 300, 0.72)(rng)
    mat_ground("ground_dirt", (0.62, 0.52, 0.38), 6, 260, 0.80)(rng)
    mat_ground("ground_rock", (0.55, 0.55, 0.56), 4, 340, 0.62)(rng)
    mat_ground("ground_asphalt", (0.34, 0.35, 0.37), 7, 420, 0.55)(rng)
    print("Готово:", len([f for f in os.listdir(OUT) if f.endswith(('.jpg', '.png'))]), "файлов")


if __name__ == "__main__":
    main()
