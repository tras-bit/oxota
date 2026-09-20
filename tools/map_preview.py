#!/usr/bin/env python3
"""Схема боевой карты по числам из редакторских скриптов.

Игра собирает карту внутри Unity, посмотреть на неё в песочнице нельзя. Зато можно вытащить
из кода координаты дорог, города, промзоны, станции, леса, точек старта и радиусы зоны —
и нарисовать карту в PNG. Так видно, не наезжают ли дороги на дома, влезают ли точки старта
в лес, попадает ли финальная зона в город.

    python3 tools/map_preview.py
    → assets/generated/map_preview.png (1400×1400, север сверху)
"""
import os
import re
import struct
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCENE_CS = os.path.join(ROOT, "game", "Assets", "Editor", "BattleSceneBuilder.cs")
TERRAIN_CS = os.path.join(ROOT, "game", "Assets", "Editor", "TerrainBuilder.cs")
RULES_CS = os.path.join(ROOT, "game", "Assets", "Scripts", "Core", "GameSession.cs")
OUT = os.path.join(ROOT, "assets", "generated", "map_preview.png")

SIZE_M = 3000.0          # размер карты, м
HALF = SIZE_M / 2.0
PX = 1400                # сторона картинки, пикселей


# ---------------------------------------------------------------- рисование без библиотек
class Canvas:
    def __init__(self, w, h, bg=(58, 78, 46)):
        self.w, self.h = w, h
        self.px = bytearray(bg * (w * h))

    def set(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            i = (y * self.w + x) * 3
            self.px[i:i + 3] = bytes(c)

    def rect(self, x0, y0, x1, y1, c, fill=True):
        x0, x1 = int(min(x0, x1)), int(max(x0, x1))
        y0, y1 = int(min(y0, y1)), int(max(y0, y1))
        if fill:
            for y in range(y0, y1 + 1):
                for x in range(x0, x1 + 1):
                    self.set(x, y, c)
        else:
            for x in range(x0, x1 + 1):
                self.set(x, y0, c); self.set(x, y1, c)
            for y in range(y0, y1 + 1):
                self.set(x0, y, c); self.set(x1, y, c)

    def disc(self, cx, cy, r, c, fill=True):
        r = int(r)
        for y in range(int(cy) - r, int(cy) + r + 1):
            for x in range(int(cx) - r, int(cx) + r + 1):
                d = ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5
                if (fill and d <= r) or (not fill and r - 1.2 <= d <= r):
                    self.set(x, y, c)

    def line(self, x0, y0, x1, y1, c, width=1.0):
        steps = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
        for i in range(steps + 1):
            t = i / max(1, steps)
            x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            if width <= 1.5:
                self.set(int(x), int(y), c)
            else:
                self.disc(x, y, width * 0.5, c)

    def save(self, path):
        raw = bytearray()
        for y in range(self.h):
            raw.append(0)
            raw += self.px[y * self.w * 3:(y + 1) * self.w * 3]

        def chunk(tag, data):
            return (struct.pack(">I", len(data)) + tag + data +
                    struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

        png = (b"\x89PNG\r\n\x1a\n" +
               chunk(b"IHDR", struct.pack(">IIBBBBB", self.w, self.h, 8, 2, 0, 0, 0)) +
               chunk(b"IDAT", zlib.compress(bytes(raw), 6)) + chunk(b"IEND", b""))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as f:
            f.write(png)


def to_px(x, z):
    """Мир (x — восток, z — север) → пиксели картинки (север сверху)."""
    return (x + HALF) / SIZE_M * PX, (HALF - z) / SIZE_M * PX


# ---------------------------------------------------------------- разбор координат из кода
def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def num(s):
    """C#-число: «-1500f» → -1500.0"""
    return float(s.rstrip("fF"))


def parse_roads(text):
    """Дорожная сеть: Roads = { new[] { new Vector2(x, z), ... }, ... }"""
    block = re.search(r"Roads\s*=\s*\{(.*?)\n\s*\};", text, re.S)
    if not block:
        return []
    roads = []
    for road in re.findall(r"new\[\]\s*\{(.*?)\}", block.group(1), re.S):
        pts = [(num(a), num(b)) for a, b in re.findall(r"new Vector2\(([-\d.f]+),\s*([-\d.f]+)\)", road)]
        if len(pts) >= 2:
            roads.append(pts)
    return roads


def parse_warehouses(text):
    block = re.search(r"Vector3\[\] warehouses\s*=\s*\{(.*?)\};", text, re.S)
    if not block:
        return []
    return [(num(a), num(c)) for a, b, c in
            re.findall(r"new Vector3\(([-\d.f]+),\s*([-\d.f]+),\s*([-\d.f]+)\)", block.group(1))]


def parse_wrecks(text):
    block = re.search(r"Vector3\[\] wrecks\s*=\s*\{(.*?)\};", text, re.S)
    if not block:
        return []
    return [(num(a), num(c)) for a, b, c in
            re.findall(r"new Vector3\(([-\d.f]+),\s*([-\d.f]+),\s*([-\d.f]+)\)", block.group(1))]


def parse_spawns(text):
    m = re.search(r"int n = (\d+);", text)
    n = int(m.group(1)) if m else 32
    m2 = re.search(r"float r = ([\d.f]+) \+ \(i % 3\) \* ([\d.f]+);", text)
    r0, step = (num(m2.group(1)), num(m2.group(2))) if m2 else (980.0, 130.0)
    return n, r0, step


def parse_zone(text):
    m = re.search(r"ZoneRadius\s*=\s*\{([^}]*)\}", text)
    return [num(v.strip()) for v in m.group(1).split(",") if v.strip()] if m else []


def parse_rule(text, name, default):
    m = re.search(name + r"\s*(?:=>|=)\s*([\d.f]+)", text)
    return num(m.group(1)) if m else default


# ---------------------------------------------------------------- сама карта
def main():
    scene, terrain, rules = read(SCENE_CS), read(TERRAIN_CS), read(RULES_CS)
    roads = parse_roads(terrain)
    warehouses = parse_warehouses(scene)
    wrecks = parse_wrecks(scene)
    spawn_n, spawn_r0, spawn_step = parse_spawns(scene)
    zones = parse_zone(rules)

    c = Canvas(PX, PX, (74, 96, 58))                     # поля и трава

    # лес (запад): тот же прямоугольник, что в BattleSceneBuilder
    x0, y0 = to_px(-1450, 1300)
    x1, y1 = to_px(-200, -1300)
    c.rect(x0, y0, x1, y1, (38, 62, 36))

    # город: сетка 5×5 домов вокруг центра, шаг 90 (центр — площадь финала)
    for bi in range(5):
        for bj in range(5):
            if bi == 2 and bj == 2:
                continue
            x, z = -180 + bi * 90, -180 + bj * 90
            px, py = to_px(x, z)
            c.rect(px - 9, py - 9, px + 9, py + 9, (146, 140, 130))

    # промзона: ангары
    for wx, wz in warehouses:
        px, py = to_px(wx, wz)
        c.rect(px - 16, py - 11, px + 16, py + 11, (168, 116, 84))

    # станция и водонапорные башни
    for sx, sz in ((-700, 348), (-1000, -400), (1100, 900)):
        px, py = to_px(sx, sz)
        c.rect(px - 22, py - 8, px + 22, py + 8, (120, 132, 150))

    # железная дорога
    for z in (-420, 330):
        px0, py0 = to_px(-1500, z)
        px1, py1 = to_px(1500, z)
        c.line(px0, py0, px1, py1, (96, 86, 78), 2)

    # дороги: обочина, асфальт, осевая
    for road in roads:
        for i in range(len(road) - 1):
            a, b = to_px(*road[i]), to_px(*road[i + 1])
            c.line(a[0], a[1], b[0], b[1], (110, 104, 96), 9)
            c.line(a[0], a[1], b[0], b[1], (72, 72, 74), 5)
            c.line(a[0], a[1], b[0], b[1], (196, 196, 180), 1)

    # сгоревшие танки — ориентиры прошлых боёв
    for wx, wz in wrecks:
        px, py = to_px(wx, wz)
        c.disc(px, py, 5, (28, 24, 22))

    # кольцо выездов и зона сужения
    for i in range(len(zones)):
        r = zones[i]
        shade = (200, 190, 90) if i == 0 else (206, 150, 80) if i < len(zones) - 1 else (200, 70, 60)
        c.disc(to_px(0, 0)[0], to_px(0, 0)[1], r / SIZE_M * PX, shade, fill=False)

    # точки старта
    for i in range(spawn_n):
        import math
        a = i / spawn_n * math.pi * 2
        r = spawn_r0 + (i % 3) * spawn_step
        px, py = to_px(math.cos(a) * r, math.sin(a) * r)
        c.disc(px, py, 6, (230, 90, 80))

    # рамка
    c.rect(0, 0, PX - 1, PX - 1, (30, 34, 28), fill=False)
    c.save(OUT)

    problems = []
    for i in range(spawn_n):
        import math
        a = i / spawn_n * math.pi * 2
        r = spawn_r0 + (i % 3) * spawn_step
        x, z = math.cos(a) * r, math.sin(a) * r
        if not (-1450 <= x <= 1450 and -1450 <= z <= 1450):
            problems.append("точка старта %d за краем карты: %.0f, %.0f" % (i, x, z))

    print("Дорог: %d, ангаров: %d, точек старта: %d, фаз зоны: %s"
          % (len(roads), len(warehouses), spawn_n, zones))
    print("Диапазон точек старта: %.0f…%.0f м — внутри карты 3000 м"
          % (spawn_r0, spawn_r0 + 2 * spawn_step))
    # попадает ли финальная зона в город (дома стоят кольцом 180 м от центра)
    if zones:
        print("Финальная зона: %.0f м — %s"
              % (zones[-1], "идёт в центре города (застройка)" if zones[-1] <= 190 else "частично в поле"))
    for p in problems:
        print(" ! " + p)
    print("Схема:", os.path.relpath(OUT, ROOT))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
