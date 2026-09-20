#!/usr/bin/env python3
"""Кадр боя: собираем из игровых моделей сценку и рендерим её — чтобы глазами проверить,
как выглядит бой (масштаб техники к домам, ангар, деревья, дорога, следы гусениц).

    tools/blender.sh tools/battle_shot.py            # один кадр
    tools/blender.sh tools/battle_shot.py high       # качество выше (дольше)

Рендеры: assets/generated/battle_shot.png и battle_shot_high.png.
Это не игровой скриншот (Unity в песочнице нет), а проверка моделей «в сцене» — то же,
что увидит игрок, когда соберёт боевую сцену и нажмёт Play.

Важно: земля берётся из setup_world() (плоскость Ground 200×200). Своих плоскостей на том же
уровне не создаём — совпадающие поверхности в Cycles дают чёрные пятна.
"""
import math
import os
import sys

import bpy

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))

import build_models as bm      # noqa: E402
import props_lib               # noqa: E402
import tank_lib                # noqa: E402

OUT = os.path.join(ROOT, "assets", "generated")


def place(ob, x, y, rz=0.0, z=0.0):
    ob.location = (x, y, z)
    ob.rotation_euler = (0.0, 0.0, math.radians(rz))


def strip(name, mat, size, scale, x, y, z=0.06, rz=0.0):
    """Плоская нашивка на земле (дорога, поле, следы) — чуть выше земли, без совпадения."""
    bpy.ops.mesh.primitive_plane_add(size=size, location=(x, y, z))
    ob = bpy.context.object
    ob.name = name
    ob.scale = (scale[0], scale[1], 1.0)
    ob.rotation_euler = (0.0, 0.0, math.radians(rz))
    ob.data.materials.append(mat)
    return ob


def main():
    high = "high" in sys.argv[1:]
    os.makedirs(OUT, exist_ok=True)

    bm.reset()
    bm.setup_world(sun_energy=3.0, bg=1.3)
    M = props_lib.mats()
    ground = bpy.data.objects.get("Ground")

    # дорога и укатанное поле — тонкие нашивки над землёй
    road = strip("ROAD", tank_lib.make_material(
        "MAT_AsphaltShot", "ground_asphalt_albedo.jpg", "ground_asphalt_normal.png",
        "ground_asphalt_mask.png", metallic=0.0, roughness=0.7, tint_color=(0.30, 0.30, 0.31)),
        size=2.0, scale=(60.0, 7.0), x=0.0, y=30.0)
    field = strip("FIELD", tank_lib.make_material(
        "MAT_DirtShot", "ground_dirt_albedo.jpg", "ground_dirt_normal.png",
        "ground_dirt_mask.png", metallic=0.0, roughness=0.9, tint_color=(0.62, 0.53, 0.38)),
        size=2.0, scale=(26.0, 18.0), x=-46.0, y=34.0, z=0.05)
    track_mat = tank_lib.make_material(
        "MAT_TrackMarkShot", "ground_dirt_albedo.jpg", "ground_dirt_normal.png",
        "ground_dirt_mask.png", metallic=0.0, roughness=0.9, tint_color=(0.32, 0.27, 0.21))

    # окружение: ангар-цех, дом, станция на заднем плане, деревья
    hero = []
    warehouse = props_lib.warehouse(M)
    place(warehouse, -36.0, -4.0, -8.0)
    hero.append(warehouse)

    house = props_lib.house(M)
    place(house, 27.0, -13.0, 24.0)
    hero.append(house)

    station = props_lib.station(M)
    place(station, 4.0, 78.0, 180.0)

    for name, path in (("tree_spruce", (-62, 10)), ("tree_birch", (-52, 24)), ("tree_spruce", (-60, 26)),
                       ("tree_birch", (44, -34)), ("tree_spruce", (52, -28)), ("tree_birch", (40, -40))):
        tree = props_lib.BUILDERS[name](M)
        place(tree, path[0], path[1], (path[0] * 3 + path[1]) % 360)
        hero.append(tree)

    for i, (x, y) in enumerate([(-40, 42), (-28, 46), (34, 46), (44, 40)]):
        prop = props_lib.BUILDERS["container" if i % 2 else "block"](M)
        place(prop, x, y, i * 47.0)

    # техника: взвод идёт по дороге, ПТ в укрытии у дома, ЛТ на фланге
    for key, (x, y, rz) in (("mt", (-7.0, 5.0, 58.0)), ("ht", (5.0, 13.0, 62.0)),
                            ("td", (31.0, -3.0, 250.0)), ("lt", (-31.0, 27.0, 205.0))):
        tank = tank_lib.build_tank(tank_lib.SPECS[key])
        place(tank, x, y, rz)
        hero.append(tank)

    # следы гусениц у машины на переднем плане
    for side in (-1, 1):
        strip("TRACK%s" % ("L" if side < 0 else "R"), track_mat, size=2.0,
              scale=(0.72, 9.0), x=-7.0 + side * 1.7, y=-1.0, z=0.05, rz=58.0)

    # воронка и дым: уничтоженная машина за ангаром
    wreck = props_lib.BUILDERS["rubble"](M)
    place(wreck, -20.0, -26.0, 15.0)
    # обломки вокруг воронки (дыма в статичном кадре не делаем: шары читаются плохо)
    for i, (dx, dy) in enumerate(((-4.5, 2.0), (3.8, -3.2), (1.5, 4.4))):
        chunk = props_lib.BUILDERS["block"](M)
        place(chunk, -20.0 + dx, -26.0 + dy, i * 61.0)

    # камера строится по мешам: корни моделей — пустышки, их надо развернуть в детали
    closeup = [root for root in hero if root.name.startswith("PROP_Warehouse") or
               root.name.startswith("PROP_House") or root.name.startswith("TANK_")]
    print("корней в кадре:", len(closeup))
    hero_meshes = [o for root in (closeup or hero)
                   for o in bm.children_recursive(root) if o.type == "MESH"]
    print("в кадре деталей:", len(hero_meshes))
    bm.new_camera(hero_meshes, direction=(-0.60, 1.30, 0.20), margin=1.0, lens=50)
    path = os.path.join(OUT, "battle_shot_high.png" if high else "battle_shot.png")
    bm.render(path, res=(1920, 1080) if high else (1500, 850), samples=110 if high else 44)
    print("готово:", os.path.relpath(path, ROOT))


if __name__ == "__main__":
    main()
