#!/usr/bin/env python3
"""Превью техники для быстрой проверки геометрии (без сборки игровых FBX).

    tools/blender.sh tools/tank_preview.py            # все четыре машины, три ракурса
    tools/blender.sh tools/tank_preview.py lt         # только ЛТ

Рендеры: assets/generated/preview/<key>_*.png — «три четверти спереди», вид сбоку,
вид сзади-сверху. Нужны, чтобы проверять силуэт, не открывая Unity.
"""
import math
import os
import sys

import bpy
import mathutils

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))

import build_models as bm      # noqa: E402  (переиспользуем свет, камеру и рендер)
import tank_lib                # noqa: E402

OUT = os.path.join(ROOT, "assets", "generated", "preview")


def place(tank, x, y, rz=0.0):
    tank.location = (x, y, 0.0)
    tank.rotation_euler = (0.0, 0.0, math.radians(rz))


def render_view(root_objs, path, direction, margin, lens, res=(1400, 900), samples=40):
    bm.new_camera(root_objs, direction=direction, margin=margin, lens=lens)
    bm.render(path, res=res, samples=samples)
    print("   ->", os.path.relpath(path, ROOT))


VIEWS = {
    "front34": ((-0.95, 1.30, 0.40), 1.02, 52),
    "side": ((1.0, 0.05, 0.18), 1.04, 52),
    "rear34": ((0.80, -1.25, 0.70), 1.05, 52),
    "front": ((0.02, 1.45, 0.22), 1.03, 50),
    "top": ((0.6, 0.7, 2.2), 1.05, 50),
}


def preview(key, views=("front34", "side")):
    spec = tank_lib.SPECS[key]
    bm.reset()
    bm.setup_world()
    tank = tank_lib.build_tank(spec)
    meshes = [o for o in bm.children_recursive(tank) if o.type == "MESH"]
    place(tank, 0, 0)
    for name in views:
        d, margin, lens = VIEWS[name]
        render_view(meshes, os.path.join(OUT, "%s_%s.png" % (key, name)), d, margin, lens,
                    res=(1280, 820), samples=28)
    return tank


def main():
    args = sys.argv[1:]
    keys = [a for a in args if a in tank_lib.SPECS] or list(tank_lib.SPECS)
    views = tuple(a for a in args if a in VIEWS) or ("front34", "side")
    os.makedirs(OUT, exist_ok=True)
    for k in keys:
        print("== превью", k, "ракурсы:", ", ".join(views))
        preview(k, views)


if __name__ == "__main__":
    main()
