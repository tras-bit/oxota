#!/usr/bin/env python3
"""
Генератор моделей техники для SAMSAR («Стальной охотник»).

Создаёт 8 машин (ЛТ, СТ, ТТ, ПТ-САУ) прямо в Blender: корпус со скосами и сварными швами,
ходовая часть с катками и траками, башня с люками/приборами/укладкой, орудие со стволом и дульным тормозом,
дополнительная броня (экраны, ДЗ, ящики, брёвна, канистры).

Дополнительно:
  * процедурные PBR-текстуры (albedo / normal / ORM) — общий набор для техники;
  * UV-развёртка (Smart UV Project);
  * экспорт FBX (для Unity) и GLB (для просмотра);
  * превью-рендеры на Cycles (CPU).

Запуск:
    tools/blender.sh tools/build_tanks.py                     # все машины, средняя детализация
    tools/blender.sh tools/build_tanks.py --lod high           # максимальная детализация
    tools/blender.sh tools/build_tanks.py --res 2048           # PBR-текстуры 2K
    tools/blender.sh tools/build_tanks.py --only boar          # одна машина
Результат:
    assets/generated/tanks/*.fbx, *.glb, *.blend, *_preview.png, manifest.json
    assets/generated/textures/*.png (общие PBR-текстуры)
"""
import argparse
import json
import math
import os
import sys
import time

import bpy
import mathutils
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_TANKS = os.path.join(ROOT, "assets", "generated", "tanks")
OUT_TEX = os.path.join(ROOT, "assets", "generated", "textures")

# ============================ параметры детализации ============================

LOD = {
    # links — количество отдельных траков на одну гусеницу (каждый трак — отдельная деталь)
    "game": dict(wheel_seg=18, links=26, bevel=1, small_parts=True, bolts=True),
    "high": dict(wheel_seg=32, links=56, bevel=2, small_parts=True, bolts=True),
    "ultra": dict(wheel_seg=40, links=72, bevel=3, small_parts=True, bolts=True),
}


# ============================ машины ============================

TANKS = [
    dict(id="sarych", name="Сарыч", cls="LT", width=2.7, length=5.0, hull_h=0.85, ride=0.70,
         turret="round", turret_w=2.0, turret_h=0.75, barrel=3.9, barrel_r=0.11, wheels=6,
         color=(0.30, 0.32, 0.22), trim=(0.72, 0.70, 0.55), extras=("light_kit", "spare_track", "antenna")),
    dict(id="rys", name="Рысь", cls="LT", width=2.8, length=5.3, hull_h=0.90, ride=0.72,
         turret="round", turret_w=2.2, turret_h=0.80, barrel=4.1, barrel_r=0.12, wheels=6,
         color=(0.29, 0.30, 0.21), trim=(0.70, 0.71, 0.60), extras=("skirt", "light_kit", "drums", "antenna")),
    dict(id="boar", name="Вепрь", cls="MT", width=3.2, length=6.2, hull_h=1.05, ride=0.82,
         turret="round", turret_w=2.6, turret_h=0.88, barrel=4.5, barrel_r=0.13, wheels=6,
         color=(0.28, 0.32, 0.23), trim=(0.78, 0.76, 0.68), extras=("stowage", "spare_track", "cables", "antenna")),
    dict(id="grom", name="Гром", cls="MT", width=3.3, length=6.4, hull_h=1.05, ride=0.82,
         turret="round", turret_w=2.7, turret_h=0.92, barrel=5.0, barrel_r=0.15, wheels=6,
         color=(0.30, 0.31, 0.26), trim=(0.76, 0.74, 0.62), extras=("stowage", "rocket_pod", "cables", "antenna")),
    dict(id="korshun", name="Коршун", cls="TD", width=3.1, length=6.7, hull_h=0.85, ride=0.76,
         turret="casemate", turret_w=2.6, turret_h=0.80, barrel=5.8, barrel_r=0.14, wheels=6,
         color=(0.27, 0.29, 0.21), trim=(0.72, 0.68, 0.52), extras=("casemate_kit", "spare_track", "cables", "antenna")),
    dict(id="tur", name="Тур", cls="HT", width=3.6, length=7.2, hull_h=1.22, ride=0.95,
         turret="heavy", turret_w=3.0, turret_h=1.00, barrel=4.8, barrel_r=0.17, wheels=7,
         color=(0.25, 0.28, 0.22), trim=(0.68, 0.66, 0.60), extras=("era", "skirt", "stowage", "drums", "antenna")),
    dict(id="viy", name="Вий", cls="HT", width=3.5, length=7.0, hull_h=1.20, ride=0.93,
         turret="heavy", turret_w=2.9, turret_h=0.98, barrel=4.7, barrel_r=0.16, wheels=7,
         color=(0.30, 0.24, 0.21), trim=(0.82, 0.52, 0.28), extras=("era", "skirt", "log", "stowage", "antenna")),
    dict(id="medved", name="Медведь", cls="HT", width=3.6, length=7.2, hull_h=1.25, ride=0.96,
         turret="heavy", turret_w=3.05, turret_h=1.02, barrel=4.9, barrel_r=0.17, wheels=7,
         color=(0.26, 0.27, 0.23), trim=(0.70, 0.68, 0.62), extras=("era", "skirt", "stowage", "drums", "spare_track", "antenna")),
]


# ============================ вспомогательные функции Blender ============================

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def new_material(name, base_color, metallic=0.55, roughness=0.55):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*base_color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    return m


def add_cube(name, size, loc, rot=(0, 0, 0), mat=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    ob = bpy.context.object
    ob.name = name
    ob.scale = size
    ob.rotation_euler = rot
    if mat:
        ob.data.materials.append(mat)
    return ob


def add_cyl(name, radius, depth, loc, rot=(0, 0, 0), verts=16, mat=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc)
    ob = bpy.context.object
    ob.name = name
    ob.rotation_euler = rot
    if mat:
        ob.data.materials.append(mat)
    return ob


def add_sphere(name, radius, loc, mat=None, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, location=loc, segments=16, ring_count=8)
    ob = bpy.context.object
    ob.name = name
    ob.scale = scale
    if mat:
        ob.data.materials.append(mat)
    return ob


def wedge(name, size, loc, rot, mat):
    """Клин — наклонный бронелист (из куба со скошенным ребром)."""
    ob = add_cube(name, size, loc, rot, mat)
    return ob


def bevel(ob, width=0.03, segments=1):
    if segments <= 0:
        return
    bpy.context.view_layer.objects.active = ob
    mod = ob.modifiers.new("bevel", "BEVEL")
    mod.width = width
    mod.segments = segments
    mod.limit_method = "ANGLE"
    mod.angle_limit = math.radians(40)
    bpy.ops.object.modifier_apply(modifier=mod.name)


def join_objects(objects, name):
    if not objects:
        return None
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    if len(objects) > 1:
        bpy.ops.object.join()
    merged = bpy.context.object
    merged.name = name
    return merged


def set_origin(ob, point):
    bpy.context.scene.cursor.location = mathutils.Vector(point)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.context.scene.cursor.location = (0, 0, 0)


def smart_uv(ob, angle=66):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")


def to_blender_space(objects):
    """
    Детали строятся в раскладке «Y вверх, Z вперёд» (удобно совмещать с Unity),
    а Blender работает в Z-вверх. Поворот +90° по X даёт корректную ориентацию
    и для рендера, и для экспорта в Unity (машина смотрит в -Y Blender = +Z Unity).
    """
    R = mathutils.Matrix.Rotation(math.radians(90), 4, "X")
    for ob in objects:
        if ob.parent is None:
            ob.matrix_world = R @ ob.matrix_world
    return R


def built_to_world(point):
    return mathutils.Vector(point) @ mathutils.Matrix.Rotation(math.radians(90), 4, "X").transposed().to_3x3().transposed()


def shade_auto_smooth(ob, angle=35):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.shade_smooth()
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle))
    except Exception:
        try:
            ob.data.use_auto_smooth = True
            ob.data.auto_smooth_angle = math.radians(angle)
        except Exception:
            bpy.ops.object.shade_flat()


# ============================ сборка машины ============================

def build_running_gear(spec, mats, cfg):
    """Катки, ведущие колёса, ленивцы, траки, поддерживающие ролики."""
    parts = []
    w = spec["width"]
    L = spec["length"]
    ride = spec["ride"]
    track_w = w * 0.29
    wheel_r = max(0.32, ride * 0.44)
    x_offset = w * 0.5 - track_w * 0.5
    seg = cfg["wheel_seg"]

    for side in (-1, 1):
        x = side * x_offset
        # гусеничная лента: отдельные траки по замкнутому контуру
        parts.extend(build_track_links(x, track_w, ride, L, cfg, mats))

        # катки
        n = spec["wheels"]
        for i in range(n):
            t = i / (n - 1) if n > 1 else 0.5
            z = (t - 0.5) * L * 0.82
            wheel = add_cyl("road_wheel", wheel_r, track_w * 0.72, (x, wheel_r, z), rot=(0, math.radians(90), 0),
                            verts=seg, mat=mats["rubber"])
            parts.append(wheel)
            # диск и ступица
            parts.append(add_cyl("wheel_disc", wheel_r * 0.62, track_w * 0.78, (x, wheel_r, z),
                                 rot=(0, math.radians(90), 0), verts=seg, mat=mats["steel"]))
            parts.append(add_cyl("wheel_hub", wheel_r * 0.2, track_w * 0.9, (x, wheel_r, z),
                                 rot=(0, math.radians(90), 0), verts=12, mat=mats["dark"]))

        # ведущее колесо со зубьями и ленивец
        sprocket = add_cyl("sprocket", wheel_r * 1.02, track_w * 0.62, (x, ride * 0.82, L * 0.48),
                           rot=(0, math.radians(90), 0), verts=seg, mat=mats["steel"])
        parts.append(sprocket)
        teeth = 12
        for t in range(teeth):
            a = t / teeth * math.pi * 2
            parts.append(add_cube("tooth", (track_w * 0.24, 0.06, 0.12),
                                  (x, ride * 0.82 + math.sin(a) * wheel_r * 0.95, L * 0.48 + math.cos(a) * wheel_r * 0.95),
                                  rot=(a, 0, 0), mat=mats["steel"]))
        parts.append(add_cyl("idler", wheel_r * 0.92, track_w * 0.62, (x, ride * 0.72, -L * 0.5),
                             rot=(0, math.radians(90), 0), verts=seg, mat=mats["steel"]))

        # поддерживающие ролики
        for z in (-L * 0.28, 0.0, L * 0.28):
            parts.append(add_cyl("return_roller", wheel_r * 0.34, track_w * 0.5, (x, ride * 1.0, z),
                                 rot=(0, math.radians(90), 0), verts=12, mat=mats["dark"]))
    return parts


def build_track_links(x, track_w, ride, L, cfg, mats):
    """Отдельные траки: нижняя ветвь, ленивец, верхняя ветвь, ведущее колесо."""
    parts = []
    n = cfg["links"]
    half = n // 2
    link_len = L * 1.02 / half
    r = ride * 0.5

    def link(pos, angle):
        parts.append(add_cube("track_link", (track_w, 0.1, link_len * 0.92), pos,
                              rot=(angle, 0, 0), mat=mats["track"]))
        parts.append(add_cube("track_horn", (track_w * 0.22, 0.05, link_len * 0.3),
                              (pos[0], pos[1] + r * 0.5, pos[2]), mat=mats["track"]))

    # нижняя ветвь
    for i in range(half // 2):
        t = i / max(1, half // 2 - 1)
        z = (t - 0.5) * L * 0.96
        link((x, 0.09, z), 0.0)
    # верхняя ветвь
    for i in range(half // 2):
        t = i / max(1, half // 2 - 1)
        z = (0.5 - t) * L * 0.9
        link((x, ride * 1.06, z), 0.0)
    # ленивец (передняя дуга)
    for i in range(6):
        a = math.pi * 0.5 + i / 5 * math.pi
        link((x, ride * 0.5 + math.sin(a) * r + r * 0.1, -L * 0.5 + math.cos(a) * r), -a + math.pi * 0.5)
    # ведущее колесо (задняя дуга)
    for i in range(6):
        a = math.pi * 0.5 + i / 5 * math.pi
        link((x, ride * 0.62 + math.sin(a) * r + r * 0.1, L * 0.5 - math.cos(a) * r), a - math.pi * 0.5)
    return parts


def add_bolts(parts, positions, mats, size=0.045):
    for p in positions:
        parts.append(add_cyl("bolt", size, 0.03, p, rot=(math.radians(90), 0, 0), verts=6, mat=mats["steel"]))


def build_hull(spec, mats, cfg):
    parts = []
    w, L, h = spec["width"], spec["length"], spec["hull_h"]
    ride = spec["ride"]

    # основная коробка
    parts.append(add_cube("hull_body", (w * 0.92, h, L * 0.86), (0, ride + h * 0.5, 0), mat=mats["body"]))
    # лобовая деталь
    parts.append(add_cube("glacis", (w * 0.9, h * 1.05, L * 0.3), (0, ride + h * 0.78, L * 0.34),
                          rot=(math.radians(-34), 0, 0), mat=mats["body"]))
    # нижний лобовой лист
    parts.append(add_cube("lower_glacis", (w * 0.88, 0.16, L * 0.22), (0, ride + 0.1, L * 0.44),
                          rot=(math.radians(-26), 0, 0), mat=mats["body"]))
    # корма
    parts.append(add_cube("rear_plate", (w * 0.88, h * 0.95, L * 0.16), (0, ride + h * 0.55, -L * 0.44),
                          rot=(math.radians(20), 0, 0), mat=mats["body"]))
    # моторное отделение
    parts.append(add_cube("engine_deck", (w * 0.86, h * 0.34, L * 0.42), (0, ride + h * 1.2, -L * 0.2), mat=mats["body"]))
    # жалюзи
    for i in range(5):
        parts.append(add_cube("louver", (w * 0.5, 0.05, 0.1),
                              (0, ride + h * 1.4, -L * 0.3 + i * 0.22), rot=(math.radians(28), 0, 0), mat=mats["dark"]))
    # выхлопные патрубки
    for side in (-1, 1):
        parts.append(add_cyl("exhaust", 0.11, 0.5, (side * w * 0.3, ride + h * 1.35, -L * 0.4),
                             rot=(math.radians(90), 0, 0), verts=10, mat=mats["rust"]))

    # крылья и полки
    for side in (-1, 1):
        parts.append(add_cube("fender", (w * 0.3, 0.08, L * 1.02), (side * (w * 0.5 - w * 0.145), ride * 1.12, 0), mat=mats["body"]))
        parts.append(add_cube("fender_lip", (0.06, 0.3, L * 1.02), (side * w * 0.5, ride * 1.2, 0), mat=mats["body"]))

    # сварные швы (тонкие полосы по рёбрам)
    if cfg["small_parts"]:
        for side in (-1, 1):
            parts.append(add_cube("weld_long", (0.05, 0.05, L * 0.86), (side * w * 0.46, ride + h * 0.98, 0), mat=mats["weld"]))
            parts.append(add_cube("weld_low", (0.05, 0.05, L * 0.86), (side * w * 0.46, ride + 0.06, 0), mat=mats["weld"]))
        parts.append(add_cube("weld_front", (w * 0.9, 0.05, 0.05), (0, ride + h * 1.02, L * 0.4), mat=mats["weld"]))

    # болты по периметру крыши и по бортам
    if cfg.get("bolts"):
        bolts = []
        for i in range(8):
            bolts.append((w * 0.42, ride + h * 1.02, -L * 0.4 + i * L * 0.1))
            bolts.append((-w * 0.42, ride + h * 1.02, -L * 0.4 + i * L * 0.1))
        for side in (-1, 1):
            for i in range(6):
                bolts.append((side * w * 0.47, ride + h * 0.35 + i * 0.05, L * 0.3 - i * 0.25))
        add_bolts(parts, bolts, mats)

    # поручни на корпусе
    for side in (-1, 1):
        for i in range(3):
            parts.append(add_cube("handrail", (0.04, 0.04, 0.9),
                                  (side * w * 0.47, ride + h * 1.18, -L * 0.3 + i * 0.9), mat=mats["dark"]))

    # буксирные крюки, фары, ящики ЗИП
    for side in (-1, 1):
        parts.append(add_cube("hook_plate", (0.3, 0.22, 0.08), (side * w * 0.28, ride + h * 0.7, L * 0.5), mat=mats["steel"]))
        parts.append(add_cyl("headlight", 0.13, 0.12, (side * w * 0.34, ride + h * 1.05, L * 0.44),
                             verts=12, mat=mats["glass"]))
    parts.append(add_cube("toolbox", (0.5, 0.3, 1.2), (-w * 0.42, ride + h * 1.3, -L * 0.34), mat=mats["steel"]))

    # дополнительное оборудование по машине
    extras = spec["extras"]
    if "skirt" in extras:
        for side in (-1, 1):
            for i in range(4):
                parts.append(add_cube("side_skirt", (0.06, 0.5, L * 0.16),
                                      (side * w * 0.52, ride * 1.15, -L * 0.28 + i * L * 0.18), mat=mats["body"]))
    if "era" in extras:
        # блоки динамической защиты
        for side in (-1, 1):
            for i in range(5):
                for j in range(2):
                    parts.append(add_cube("era_block", (0.12, 0.22, 0.34),
                                          (side * w * 0.5, ride + h * (0.45 + j * 0.5), -L * 0.24 + i * 0.4),
                                          rot=(0, 0, math.radians(side * 8)), mat=mats["era"]))
                    parts.append(add_cube("era_front", (w * 0.22, 0.24, 0.12),
                                          (side * w * 0.24, ride + h * 0.95 + j * 0.24, L * 0.42),
                                          rot=(math.radians(-34), 0, 0), mat=mats["era"]))
    if "log" in extras:
        parts.append(add_cyl("log", 0.17, L * 0.7, (0, ride + h * 1.35, -L * 0.24),
                             rot=(0, math.radians(90), 0), verts=12, mat=mats["wood"]))
    if "drums" in extras:
        for side in (-1, 1):
            parts.append(add_cyl("fuel_drum", 0.26, 0.7, (side * w * 0.44, ride + h * 1.4, -L * 0.42),
                                 rot=(0, math.radians(90), 0), verts=14, mat=mats["rust"]))
            parts.append(add_cube("drum_strap", (0.06, 0.5, 0.06), (side * w * 0.44, ride + h * 1.4, -L * 0.42 + 0.2), mat=mats["dark"]))
    if "cables" in extras:
        parts.append(add_cyl("tow_cable", 0.045, L * 0.8, (-w * 0.47, ride + h * 1.22, -L * 0.05),
                             verts=8, mat=mats["dark"]))
        parts.append(add_cyl("tow_cable2", 0.045, L * 0.8, (w * 0.47, ride + h * 1.22, -L * 0.05),
                             verts=8, mat=mats["dark"]))
    if "spare_track" in extras:
        for i in range(6):
            parts.append(add_cube("spare_link", (w * 0.2, 0.06, 0.16), (0, ride + h * 1.4, L * 0.2 + i * 0.18), mat=mats["track"]))
    if "rocket_pod" in extras:
        for i in range(4):
            parts.append(add_cyl("rocket_tube", 0.09, 1.0, (0.6 + i * 0.16, ride + h * 1.5, -L * 0.1),
                                 rot=(math.radians(12), 0, 0), verts=10, mat=mats["dark"]))
    return parts


def build_turret(spec, mats, cfg):
    parts = []
    w, L, h = spec["width"], spec["length"], spec["hull_h"]
    tw, th = spec["turret_w"], spec["turret_h"]
    ride = spec["ride"]
    base_y = ride + h * 1.32
    kind = spec["turret"]
    seg = 16 if cfg["small_parts"] else 12

    if kind == "casemate":
        parts.append(add_cube("casemate", (tw, th, L * 0.42), (0, base_y + th * 0.5, -L * 0.06), mat=mats["body"]))
        parts.append(add_cube("casemate_front", (tw * 0.96, th * 0.85, L * 0.2), (0, base_y + th * 0.62, L * 0.22),
                              rot=(math.radians(-26), 0, 0), mat=mats["body"]))
        parts.append(add_cube("casemate_roof", (tw * 0.72, 0.08, L * 0.3), (0, base_y + th * 1.02, -L * 0.1), mat=mats["body"]))
        parts.append(add_cube("engine_hatch", (0.7, 0.07, 0.7), (tw * 0.22, base_y + th * 1.06, -L * 0.24), mat=mats["dark"]))
    elif kind == "heavy":
        parts.append(add_cyl("turret", tw * 0.5, th, (0, base_y + th * 0.5, -L * 0.02),
                             rot=(math.radians(90), 0, 0), verts=seg, mat=mats["body"]))
        parts.append(add_cube("turret_rear", (tw * 0.82, th * 0.9, L * 0.22), (0, base_y + th * 0.45, -L * 0.2), mat=mats["body"]))
        parts.append(add_cube("cupola", (0.72, 0.34, 0.72), (-0.15, base_y + th * 1.15, -0.35), mat=mats["body"]))
        parts.append(add_cyl("cupola_hatch", 0.3, 0.08, (-0.15, base_y + th * 1.34, -0.35),
                             rot=(math.radians(90), 0, 0), verts=14, mat=mats["dark"]))
        parts.append(add_cube("loader_hatch", (0.55, 0.07, 0.55), (0.45, base_y + th * 1.06, -0.1), mat=mats["dark"]))
    else:
        parts.append(add_cyl("turret", tw * 0.5, th, (0, base_y + th * 0.5, -L * 0.02),
                             rot=(math.radians(90), 0, 0), verts=seg, mat=mats["body"]))
        parts.append(add_cube("turret_bustle", (tw * 0.7, th * 0.8, L * 0.16), (0, base_y + th * 0.4, -L * 0.18), mat=mats["body"]))
        parts.append(add_cube("cupola", (0.62, 0.3, 0.62), (0, base_y + th * 1.1, -0.3), mat=mats["body"]))
        parts.append(add_cyl("cupola_hatch", 0.26, 0.07, (0, base_y + th * 1.28, -0.3),
                             rot=(math.radians(90), 0, 0), verts=12, mat=mats["dark"]))

    # маска орудия
    parts.append(add_cube("mantlet", (tw * 0.52, th * 0.78, 0.42), (0, base_y + th * 0.55, L * 0.14 + 0.16), mat=mats["body"]))
    # приборы наблюдения
    for i, x in enumerate((-tw * 0.28, tw * 0.3)):
        parts.append(add_cube("periscope", (0.2, 0.16, 0.24), (x, base_y + th * 1.05, L * 0.12),
                              rot=(0, 0, math.radians(x * 6)), mat=mats["glass"]))
        parts.append(add_cube("periscope_guard", (0.26, 0.05, 0.28), (x, base_y + th * 1.16, L * 0.12), mat=mats["steel"]))
    # командирский пулемёт
    if cfg["small_parts"]:
        parts.append(add_cyl("mg_barrel", 0.045, 0.9, (0.5, base_y + th * 1.22, L * 0.1),
                             verts=8, mat=mats["dark"]))
        parts.append(add_cube("mg_mount", (0.16, 0.16, 0.3), (0.5, base_y + th * 1.1, L * 0.02), mat=mats["steel"]))
    # дымовые гранатомёты
    for side in (-1, 1):
        for i in range(3):
            parts.append(add_cyl("smoke_launcher", 0.06, 0.26,
                                 (side * tw * 0.42, base_y + th * 0.85, L * 0.05 + i * 0.13),
                                 rot=(math.radians(20), 0, 0), verts=8, mat=mats["dark"]))
    # укладка на башне
    if "stowage" in spec["extras"]:
        parts.append(add_cube("stowage_box", (tw * 0.6, 0.42, 0.9), (0, base_y + th * 0.95, -L * 0.3), mat=mats["steel"]))
        parts.append(add_cube("stowage_lid", (tw * 0.62, 0.06, 0.94), (0, base_y + th * 1.2, -L * 0.3), mat=mats["dark"]))
    if "spare_track" in spec["extras"]:
        for i in range(5):
            parts.append(add_cube("turret_track", (0.5, 0.08, 0.16), (0.2, base_y + th * 1.05, -L * 0.3 - 0.1 + i * 0.2), mat=mats["track"]))
    if "cables" in spec["extras"]:
        parts.append(add_cube("cable_coil", (0.5, 0.5, 0.14), (-tw * 0.3, base_y + th * 0.9, -L * 0.34), mat=mats["dark"]))
    if "casemate_kit" in spec["extras"]:
        parts.append(add_cube("range_finder", (0.7, 0.2, 0.3), (-tw * 0.28, base_y + th * 1.1, -L * 0.16), mat=mats["glass"]))
        parts.append(add_cube("ammo_hatch", (0.6, 0.06, 0.5), (tw * 0.25, base_y + th * 1.05, -L * 0.24), mat=mats["dark"]))
    # антенна
    if "antenna" in spec["extras"]:
        parts.append(add_cyl("antenna_base", 0.08, 0.16, (tw * 0.4, base_y + th * 1.12, -L * 0.33), verts=8, mat=mats["dark"]))
        parts.append(add_cyl("antenna", 0.02, 2.2, (tw * 0.4, base_y + th * 1.12 + 1.1, -L * 0.33),
                             rot=(math.radians(6), 0, 0), verts=6, mat=mats["dark"]))
    return parts


def build_gun(spec, mats, cfg):
    parts = []
    w, L, h = spec["width"], spec["length"], spec["hull_h"]
    tw, th = spec["turret_w"], spec["turret_h"]
    ride = spec["ride"]
    base_y = ride + h * 1.32
    bl = spec["barrel"]
    br = spec["barrel_r"]
    z0 = L * 0.14 + 0.3

    parts.append(add_cyl("barrel", br, bl, (0, base_y + th * 0.55, z0 + bl * 0.5),
                         verts=18, mat=mats["steel"]))
    parts.append(add_cyl("barrel_base", br * 1.35, bl * 0.22, (0, base_y + th * 0.55, z0 + bl * 0.11),
                         verts=18, mat=mats["steel"]))
    parts.append(add_cyl("bore_evacuator", br * 1.7, 0.7, (0, base_y + th * 0.55, z0 + bl * 0.42),
                         verts=16, mat=mats["steel"]))
    # дульный тормоз
    parts.append(add_cyl("muzzle_brake", br * 1.9, 0.42, (0, base_y + th * 0.55, z0 + bl - 0.2),
                         verts=16, mat=mats["steel"]))
    for i in range(4):
        parts.append(add_cube("muzzle_slot", (0.02, br * 2.6, 0.06),
                              (br * 1.6 * (1 if i % 2 == 0 else -1), base_y + th * 0.55, z0 + bl - 0.32 + i * 0.06),
                              mat=mats["dark"]))
    # чехол/теплозащитный кожух
    if "cables" in spec["extras"] or cfg["small_parts"]:
        parts.append(add_cube("thermal_sleeve", (br * 2.3, br * 2.3, bl * 0.3),
                              (0, base_y + th * 0.55, z0 + bl * 0.22), mat=mats["dark"]))
    return parts


def build_tank(spec, cfg, mats):
    hull_parts = build_hull(spec, mats, cfg) + build_running_gear(spec, mats, cfg)
    turret_parts = build_turret(spec, mats, cfg)
    gun_parts = build_gun(spec, mats, cfg)

    all_parts = hull_parts + turret_parts + gun_parts
    R = to_blender_space(all_parts)

    if cfg["bevel"] > 0:
        for ob in hull_parts + turret_parts + gun_parts:
            if ob.type == "MESH" and max(ob.dimensions) > 0.25:
                bevel(ob, width=0.02 if cfg["bevel"] == 1 else 0.03, segments=cfg["bevel"])

    hull = join_objects(hull_parts, "Hull")
    turret = join_objects(turret_parts, "Turret")
    gun = join_objects(gun_parts, "Barrel")

    # точки вращения (в мировых координатах Blender: X — ширина, Y — длина, Z — высота)
    ride, h, th = spec["ride"], spec["hull_h"], spec["turret_h"]
    turret_pivot = tuple(R @ mathutils.Vector((0.0, ride + h * 1.32, -spec["length"] * 0.02)))
    barrel_pivot = tuple(R @ mathutils.Vector((0.0, ride + h * 1.32 + th * 0.55, spec["length"] * 0.14 + 0.3)))
    set_origin(turret, turret_pivot)
    set_origin(gun, barrel_pivot)

    turret.parent = hull
    turret.matrix_parent_inverse = hull.matrix_world.inverted()
    gun.parent = turret
    gun.matrix_parent_inverse = turret.matrix_world.inverted()

    # маркер дульного среза
    muzzle = bpy.data.objects.new("MuzzlePoint", None)
    bpy.context.collection.objects.link(muzzle)
    muzzle.parent = gun
    muzzle.location = R.to_3x3() @ mathutils.Vector((0, 0, spec["barrel"] * 0.98))

    for ob in (hull, turret, gun):
        smart_uv(ob)
        shade_auto_smooth(ob, 32 if ob is hull else 28)
    return hull, turret, gun, muzzle


# ============================ PBR-текстуры ============================

def fbm_np(w, h, scale, octaves, seed):
    rng = np.random.default_rng(seed)
    out = np.zeros((h, w), dtype=np.float32)
    amp = 1.0
    total = 0.0
    size = max(2, int(scale))
    for o in range(octaves):
        base = rng.random((size, size)).astype(np.float32)
        # растянуть до размера текстуры (билинейно, через numpy repeat + сглаживание)
        ys = np.linspace(0, size - 1, h)
        xs = np.linspace(0, size - 1, w)
        y0 = np.floor(ys).astype(int); y1 = np.clip(y0 + 1, 0, size - 1)
        x0 = np.floor(xs).astype(int); x1 = np.clip(x0 + 1, 0, size - 1)
        fy = (ys - y0)[:, None]; fx = (xs - x0)[None, :]
        top = base[y0][:, x0] * (1 - fx) + base[y0][:, x1] * fx
        bot = base[y1][:, x0] * (1 - fx) + base[y1][:, x1] * fx
        layer = top * (1 - fy) + bot * fy
        out += layer * amp
        total += amp
        amp *= 0.5
        size *= 2
    return out / max(total, 1e-6)


def save_image(name, pixels, res, colorspace="sRGB", path=None):
    img = bpy.data.images.new(name, width=res, height=res, alpha=False)
    img.colorspace_settings.name = colorspace
    flat = np.zeros((res * res, 4), dtype=np.float32)
    flat[:, :3] = pixels.reshape(-1, 3)
    flat[:, 3] = 1.0
    img.pixels.foreach_set(flat.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def build_textures(res):
    """Общий PBR-набор для техники: металл с грязью, царапинами и потёртостями."""
    os.makedirs(OUT_TEX, exist_ok=True)
    t0 = time.time()
    s = res

    grunge = fbm_np(s, s, 8, 6, 11)
    fine = fbm_np(s, s, 64, 4, 22)
    rust = fbm_np(s, s, 24, 5, 33)
    scratch = np.clip((fbm_np(s, s, 32, 3, 44) - 0.55) * 6.0, 0, 1)

    # ==== albedo ====
    base = np.array([0.33, 0.35, 0.30], dtype=np.float32)
    dirty = np.array([0.20, 0.17, 0.13], dtype=np.float32)
    rustc = np.array([0.32, 0.16, 0.08], dtype=np.float32)

    albedo = np.zeros((s, s, 3), dtype=np.float32)
    for c in range(3):
        albedo[:, :, c] = base[c]
    m_grunge = np.clip((grunge - 0.35) * 1.6, 0, 1)[:, :, None]
    albedo = albedo * (1 - m_grunge * 0.55) + dirty * (m_grunge * 0.55)
    m_rust = np.clip((rust - 0.62) * 3.0, 0, 1)[:, :, None]
    albedo = albedo * (1 - m_rust) + rustc * m_rust
    m_scr = (scratch * 0.35)[:, :, None]
    albedo = np.clip(albedo + m_scr * 0.35, 0, 1)
    albedo_lin = np.power(albedo, 2.2)   # sRGB -> linear для записи в sRGB-текстуру не нужен; Blender сам конвертирует
    save_image("tank_albedo", albedo, s, "sRGB", os.path.join(OUT_TEX, "tank_albedo.png"))

    # ==== roughness/metallic/ao в один файл (ORM) ====
    rough = 0.42 + grunge * 0.45 - scratch * 0.2
    rough = np.clip(rough, 0.12, 0.95)
    metal = np.clip(0.85 - m_grunge[:, :, 0] * 0.35 - m_rust[:, :, 0] * 0.5, 0.15, 1.0)
    ao = np.clip(1.0 - (1.0 - fine) * 0.25, 0.6, 1.0)
    orm = np.stack([ao, rough, metal], axis=-1)
    save_image("tank_orm", orm, s, "Non-Color", os.path.join(OUT_TEX, "tank_orm.png"))

    # ==== normal из карты высот (царапины + вмятины) ====
    height = fbm_np(s, s, 16, 5, 55) * 0.6 + fbm_np(s, s, 96, 3, 66) * 0.4 - scratch * 0.25
    gy, gx = np.gradient(height)
    strength = 3.2
    nx = -gx * strength
    ny = -gy * strength
    nz = np.ones_like(height)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack([nx / ln, ny / ln, nz / ln], axis=-1) * 0.5 + 0.5
    save_image("tank_normal", normal, s, "Non-Color", os.path.join(OUT_TEX, "tank_normal.png"))

    print("Текстуры %dx%d готовы за %.1f c" % (s, s, time.time() - t0))
    return {
        "albedo": os.path.join(OUT_TEX, "tank_albedo.png"),
        "orm": os.path.join(OUT_TEX, "tank_orm.png"),
        "normal": os.path.join(OUT_TEX, "tank_normal.png"),
    }


# ============================ сцена для превью ============================

def setup_preview(center, radius, direction=(-1.15, -1.65, 0.72)):
    world = bpy.data.worlds.new("W")
    bpy.context.scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.30, 0.34, 0.40, 1)
    bg.inputs[1].default_value = 1.3

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("SunL", type="SUN"))
    sun.data.energy = 4.2
    bpy.context.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(52), 0, math.radians(-120))

    fill = bpy.data.objects.new("Fill", bpy.data.lights.new("FillL", type="AREA"))
    fill.data.energy = 700
    fill.data.size = 8
    fill.location = (-8, 7, 7)
    fill.rotation_euler = (math.radians(55), 0, math.radians(-42))
    bpy.context.collection.objects.link(fill)

    cap = bpy.data.lights.new("RimL", type="AREA")
    rim = bpy.data.objects.new("Rim", cap)
    rim.data.energy = 300
    rim.data.size = 6
    rim.location = (7, -6, 5)
    rim.rotation_euler = (math.radians(60), 0, math.radians(140))
    bpy.context.collection.objects.link(rim)

    ground = add_cube("Ground", (400, 0.2, 400), (0, -0.1, 0),
                      mat=new_material("GroundMat", (0.24, 0.25, 0.20), 0.0, 0.95))

    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 45
    cam = bpy.data.objects.new("Cam", cam_data)
    bpy.context.collection.objects.link(cam)
    bpy.context.scene.camera = cam

    d = mathutils.Vector(direction).normalized()
    fov = 2 * math.atan(cam_data.sensor_width / (2 * cam_data.lens))
    dist = radius / math.tan(fov / 2) * 1.12
    cam.location = mathutils.Vector(center) + d * dist

    target = bpy.data.objects.new("Look", None)
    target.location = center
    bpy.context.collection.objects.link(target)
    trk = cam.constraints.new("TRACK_TO")
    trk.target = target
    trk.track_axis = "TRACK_NEGATIVE_Z"
    trk.up_axis = "UP_Y"
    return cam, ground


def tank_bounds(objects):
    pts = []
    for ob in objects:
        for c in ob.bound_box:
            pts.append(ob.matrix_world @ mathutils.Vector(c))
    center = sum(pts, mathutils.Vector()) / len(pts)
    radius = max((p - center).length for p in pts)
    return tuple(center), radius


def render_preview(path, samples=40, res=(960, 540)):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.filepath = path
    try:
        sc.view_settings.look = "AgX - Medium High Contrast"
    except Exception:
        try:
            sc.view_settings.look = "Medium High Contrast"
        except Exception:
            pass
    # немного светлее: превью читаемее на слабых экранах
    try:
        sc.view_settings.exposure = 0.75
    except Exception:
        pass
    bpy.ops.render.render(write_still=True)
    print("Превью: " + path)


# ============================ экспорт ============================

def export_tank(spec, hull, turret, gun, muzzle, out_dir):
    # выбрать только технику
    bpy.ops.object.select_all(action="DESELECT")
    for ob in (hull, turret, gun, muzzle):
        ob.select_set(True)
    bpy.context.view_layer.objects.active = hull

    base = os.path.join(out_dir, spec["id"])
    bpy.ops.export_scene.fbx(
        filepath=base + ".fbx",
        use_selection=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        path_mode="COPY",
        embed_textures=False,
    )
    bpy.ops.export_scene.gltf(
        filepath=base + ".glb",
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
    )
    return base


def stats_of(objs):
    tris = 0
    verts = 0
    for ob in objs:
        if ob.type != "MESH":
            continue
        me = ob.data
        me.calc_loop_triangles()
        tris += len(me.loop_triangles)
        verts += len(me.vertices)
    return tris, verts


def build_manifest(spec, hull, turret, gun):
    tris, verts = stats_of([hull, turret, gun])
    return dict(id=spec["id"], name=spec["name"], cls=spec["cls"], triangles=tris, vertices=verts,
                barrel_length=spec["barrel"], extras=list(spec["extras"]))


# ============================ основной сценарий ============================

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--lod", default="game", choices=list(LOD.keys()))
    ap.add_argument("--res", type=int, default=1024, help="разрешение PBR-текстур (2048 для 2K)")
    ap.add_argument("--only", default=None, help="собрать только одну машину по id")
    ap.add_argument("--no-render", action="store_true")
    ap.add_argument("--no-textures", action="store_true")
    ap.add_argument("--samples", type=int, default=40)
    args = ap.parse_args()

    cfg = dict(LOD[args.lod])
    os.makedirs(OUT_TANKS, exist_ok=True)
    os.makedirs(OUT_TEX, exist_ok=True)

    textures = None
    if not args.no_textures:
        reset_scene()
        textures = build_textures(args.res)

    tanks = [t for t in TANKS if (args.only is None or t["id"] == args.only)]
    if not tanks:
        sys.exit("Машина не найдена: " + str(args.only))

    manifest = []
    for spec in tanks:
        t0 = time.time()
        reset_scene()
        mats = {
            "body": new_material("Body" + spec["id"], spec["color"], 0.42, 0.55),
            "steel": new_material("Steel" + spec["id"], (0.30, 0.31, 0.31), 0.75, 0.35),
            "dark": new_material("Dark" + spec["id"], (0.10, 0.10, 0.10), 0.45, 0.6),
            "track": new_material("Track" + spec["id"], (0.12, 0.11, 0.10), 0.35, 0.8),
            "rubber": new_material("Rubber" + spec["id"], (0.07, 0.07, 0.07), 0.05, 0.95),
            "glass": new_material("Glass" + spec["id"], (0.18, 0.28, 0.33), 0.85, 0.15),
            "rust": new_material("Rust" + spec["id"], (0.34, 0.19, 0.10), 0.45, 0.8),
            "wood": new_material("Wood" + spec["id"], (0.26, 0.18, 0.10), 0.0, 0.85),
            "weld": new_material("Weld" + spec["id"], (0.26, 0.26, 0.24), 0.6, 0.6),
            "era": new_material("Era" + spec["id"], (0.22, 0.22, 0.20), 0.35, 0.7),
        }
        hull, turret, gun, muzzle = build_tank(spec, cfg, mats)
        tris, verts = stats_of([hull, turret, gun])
        base = export_tank(spec, hull, turret, gun, muzzle, OUT_TANKS)

        if not args.no_render:
            # центр кадра — корпус с башней, радиус — с учётом ствола
            center, _ = tank_bounds([hull, turret])
            _, radius = tank_bounds([hull, turret, gun])
            setup_preview(center, radius)
            render_preview(os.path.join(OUT_TANKS, spec["id"] + "_preview.png"), args.samples)

        blend_path = os.path.join(OUT_TANKS, spec["id"] + ".blend")
        bpy.ops.wm.save_as_mainfile(filepath=blend_path)
        entry = build_manifest(spec, hull, turret, gun)
        manifest.append(entry)
        print("[%s] %s: %d треугольников, %d вершин, %.1f c" % (
            spec["cls"], spec["name"], tris, verts, time.time() - t0))

    man_path = os.path.join(OUT_TANKS, "manifest.json")
    if os.path.exists(man_path) and args.only:
        try:
            old = json.load(open(man_path))
            by_id = {e["id"]: e for e in old}
            for e in manifest:
                by_id[e["id"]] = e
            manifest = list(by_id.values())
        except Exception:
            pass
    json.dump(dict(lod=args.lod, texture_res=args.res, tanks=manifest), open(man_path, "w"), ensure_ascii=False, indent=2)
    print("Готово. Моделей: %d. Манифест: %s" % (len(manifest), man_path))


if __name__ == "__main__":
    main()
