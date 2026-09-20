#!/usr/bin/env python3
"""Библиотека объектов окружения для карты «Samsar»: дома, ангары, станция, рельсы, деревья, заборы.

Все объекты строятся в начале координат (низ на z=0), масштаб — реальные метры.
"""
import math

import bpy


def _mat(name, albedo, normal, mask, metallic, roughness, tint=None):
    from tank_lib import make_material
    return make_material(name, albedo, normal, mask, metallic=metallic,
                         roughness=roughness, tint_color=tint)


def mats():
    return {
        "concrete": _mat("MAT_Concrete", "concrete_albedo.jpg", "concrete_normal.png",
                         "concrete_mask.png", 0.03, 0.6),
        "brick": _mat("MAT_Wood", "wood_albedo.jpg", "wood_normal.png",
                      "wood_mask.png", 0.02, 0.45),
        "metal": _mat("MAT_Rusty", "metal_rusty_albedo.jpg", "metal_rusty_normal.png",
                      "metal_rusty_mask.png", 0.6, 0.55),
        "steel": _mat("MAT_SteelOlive", "steel_olive_albedo.jpg", "steel_olive_normal.png",
                      "steel_olive_mask.png", 0.8, 0.45),
        "glass": _mat("MAT_Optics", "optics_albedo.jpg", "optics_normal.png",
                      "optics_mask.png", 0.9, 0.08),
        "leaf": _mat("MAT_Leaf", "ground_grass_albedo.jpg", "ground_grass_normal.png",
                     "ground_grass_mask.png", 0.0, 0.8, tint=(0.30, 0.52, 0.22)),
        # сгоревший металл (в Unity маппится в MAT_MetalDark — тёмная сталь)
        "burnt": _mat("MAT_BurntMetal", "steel_olive_albedo.jpg", "steel_olive_normal.png",
                      "steel_olive_mask.png", 0.35, 0.78, tint=(0.09, 0.08, 0.075)),
    }


def _keep(ob, parent):
    mw = ob.matrix_world.copy()
    ob.parent = parent
    ob.matrix_parent_inverse = parent.matrix_world.inverted()
    ob.matrix_world = mw
    return ob


def box(name, size, loc, rot=(0, 0, 0), material=None, parent=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    ob = bpy.context.object
    ob.name = name
    ob.scale = size
    ob.rotation_euler = rot
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if material:
        ob.data.materials.append(material)
    if parent:
        _keep(ob, parent)
    return ob


def cyl(name, r, depth, loc, rot=(0, 0, 0), verts=12, material=None, parent=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc)
    ob = bpy.context.object
    ob.name = name
    ob.rotation_euler = rot
    if material:
        ob.data.materials.append(material)
    if parent:
        _keep(ob, parent)
    return ob


def root(name):
    ob = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(ob)
    return ob


# ---------------- здания ----------------
def house(M, w=9.0, d=7.0, h=3.2, floors=2, door="+Y"):
    """Жилой дом с интерьером: проёмы в стенах, перекрытия, крыша. Можно заехать внутрь."""
    r = root("PROP_House")
    t = 0.35
    for f in range(floors):
        z = f * h + h / 2
        # стены с проёмами (каждая стена из 3 сегментов)
        gap = 2.6
        box("wall_front_l", (w / 2 - gap / 2, t, h), (-(gap / 2 + w / 4), d / 2, z), material=M["concrete"], parent=r)
        box("wall_front_r", (w / 2 - gap / 2, t, h), (gap / 2 + w / 4, d / 2, z), material=M["concrete"], parent=r)
        box("wall_front_top", (gap, t, h - 2.6), (0, d / 2, z + 1.3), material=M["concrete"], parent=r)
        box("wall_back_l", (w / 2 - gap / 2, t, h), (-(gap / 2 + w / 4), -d / 2, z), material=M["concrete"], parent=r)
        box("wall_back_r", (w / 2 - gap / 2, t, h), (gap / 2 + w / 4, -d / 2, z), material=M["concrete"], parent=r)
        box("wall_back_top", (gap, t, h - 2.6), (0, -d / 2, z + 1.3), material=M["concrete"], parent=r)
        box("wall_left", (t, d, h), (-w / 2, 0, z), material=M["concrete"], parent=r)
        box("wall_right", (t, d, h), (w / 2, 0, z), material=M["concrete"], parent=r)
        box("floor_%d" % f, (w, d, 0.3), (0, 0, f * h + 0.15), material=M["concrete"], parent=r)
        # окна
        for i, x in enumerate((-w / 3, w / 3)):
            box("win_l%d_%d" % (f, i), (1.4, 0.1, 1.2), (x, d / 2 + t / 2, z + 0.4),
                material=M["glass"], parent=r)
            box("win_r%d_%d" % (f, i), (1.4, 0.1, 1.2), (x, -d / 2 - t / 2, z + 0.4),
                material=M["glass"], parent=r)
    ztop = floors * h
    box("roof", (w + 0.8, d + 0.8, 0.35), (0, 0, ztop + 0.17), material=M["steel"], parent=r)
    box("attic", (w * 0.6, d * 0.5, 0.9), (0, -d * 0.15, ztop + 0.8), material=M["brick"], parent=r)
    cyl("chimney", 0.35, 1.6, (w * 0.3, -d * 0.3, ztop + 0.9), verts=10, material=M["concrete"], parent=r)
    return r


def warehouse(M, w=22.0, d=34.0, h=7.0):
    """Ангар/цех: внутрь можно заехать (сквозные ворота по торцам)."""
    r = root("PROP_Warehouse")
    t = 0.4
    gate = 6.0
    for s in (-1, 1):
        box("end_l", (w / 2 - gate / 2, t, h), (-(gate / 2 + w / 4), s * d / 2, h / 2),
            material=M["concrete"], parent=r)
        box("end_r", (w / 2 - gate / 2, t, h), (gate / 2 + w / 4, s * d / 2, h / 2),
            material=M["concrete"], parent=r)
        box("end_top", (gate, t, h - gate * 0.8), (0, s * d / 2, h - (h - gate * 0.8) / 2),
            material=M["concrete"], parent=r)
        box("gate_frame", (gate + 0.4, 0.5, 0.4), (0, s * d / 2, gate * 0.8),
            material=M["metal"], parent=r)
    box("side_l", (t, d, h), (-w / 2, 0, h / 2), material=M["concrete"], parent=r)
    box("side_r", (t, d, h), (w / 2, 0, h / 2), material=M["concrete"], parent=r)
    box("floor", (w, d, 0.3), (0, 0, 0.15), material=M["concrete"], parent=r)
    box("roof", (w + 1.0, d + 1.0, 0.3), (0, 0, h + 0.15), material=M["metal"], parent=r)
    # фермы
    for i in range(6):
        y = -d / 2 + 3 + i * (d - 6) / 5
        box("truss%d" % i, (w, 0.18, 0.18), (0, y, h - 0.6), material=M["metal"], parent=r)
    # станки и ящики внутри
    for i in range(3):
        box("crate%d" % i, (1.6, 1.6, 1.6), (-w / 3 + i * 0.6, d * 0.2 - i * 5, 0.9),
            material=M["brick"], parent=r)
        box("machine%d" % i, (2.6, 1.4, 1.5), (w / 3, -d * 0.2 + i * 6, 0.85),
            material=M["metal"], parent=r)
    return r


def station(M):
    """Железнодорожная станция с платформой и навесом."""
    r = root("PROP_Station")
    box("platform", (34, 9, 1.1), (0, 0, 0.55), material=M["concrete"], parent=r)
    box("building", (22, 8, 4.6), (0, 5.5, 3.4), material=M["brick"], parent=r)
    box("roof", (24, 9.5, 0.4), (0, 5.5, 5.9), material=M["metal"], parent=r)
    for i in range(6):
        x = -12 + i * 4.8
        box("win%d" % i, (1.6, 0.1, 1.4), (x, 1.6, 3.6), material=M["glass"], parent=r)
        cyl("pillar%d" % i, 0.16, 4.6, (x, -3.6, 3.4), verts=10, material=M["metal"], parent=r)
    box("canopy", (34, 5.0, 0.3), (0, -3.6, 5.8), material=M["metal"], parent=r)
    box("sign", (7, 0.2, 1.2), (-7, 1.2, 6.6), material=M["steel"], parent=r)
    cyl("lamp_post", 0.14, 5.2, (13, -4.2, 3.7), verts=10, material=M["metal"], parent=r)
    box("lamp_head", (0.8, 0.4, 0.25), (11.6, -4.2, 6.3), material=M["metal"], parent=r)
    return r


def tower(M):
    """Водонапорная башня — ориентир и укрытие."""
    r = root("PROP_Tower")
    for i in range(4):
        a = math.radians(45 + i * 90)
        for t in range(3):
            cyl("leg%d_%d" % (i, t), 0.14, 5.0,
                (math.cos(a) * 1.6, math.sin(a) * 1.6, 2.5 + t * 4.6),
                rot=(math.radians(4) * math.cos(a), math.radians(4) * math.sin(a), 0),
                verts=8, material=M["metal"], parent=r)
    for t in range(3):
        box("ring%d" % t, (3.4, 3.4, 0.14), (0, 0, 3.4 + t * 4.4), material=M["metal"], parent=r)
    cyl("tank", 3.1, 4.4, (0, 0, 12.2), verts=16, material=M["metal"], parent=r)
    cyl("cap", 3.3, 0.35, (0, 0, 14.5), verts=16, material=M["metal"], parent=r)
    return r


# ---------------- растительность и мелочь ----------------
def tree(M, kind=0, h=9.0):
    """Дерево: ствол + крона. kind 0 — ель, 1 — лиственное."""
    r = root("PROP_Tree")
    cyl("trunk", 0.28, h * 0.55, (0, 0, h * 0.275), verts=10, material=M["brick"], parent=r)
    if kind == 0:
        for i in range(4):
            k = 1 - i * 0.2
            bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=2.4 * k, radius2=0.2,
                                            depth=h * 0.32,
                                            location=(0, 0, h * 0.45 + i * h * 0.15))
            ob = bpy.context.object
            ob.name = "crown%d" % i
            ob.data.materials.append(M["leaf"])
            _keep(ob, r)
    else:
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=h * 0.3,
                                              location=(0, 0, h * 0.72))
        ob = bpy.context.object
        ob.name = "crown"
        ob.scale = (1.15, 1.15, 0.95)
        ob.data.materials.append(M["leaf"])
        _keep(ob, r)
        for i in range(3):
            a = math.radians(i * 120)
            cyl("branch%d" % i, 0.1, h * 0.3,
                (math.cos(a) * h * 0.12, math.sin(a) * h * 0.12, h * 0.58),
                rot=(math.radians(28) * math.sin(a), math.radians(28) * math.cos(a), 0),
                verts=6, material=M["brick"], parent=r)
    return r


def fence(M, length=6.0):
    """Секция забора (разрушаемая)."""
    r = root("PROP_Fence")
    for i in range(int(length / 2) + 1):
        cyl("post%d" % i, 0.09, 1.6, (-length / 2 + i * 2.0, 0, 0.8), verts=8,
            material=M["brick"], parent=r)
    for j, z in ((0, 1.35), (1, 0.75)):
        box("rail%d" % j, (length, 0.09, 0.14), (0, 0, z), material=M["brick"], parent=r)
    return r


def container(M, color_mat=None):
    """Морской контейнер — укрытие."""
    r = root("PROP_Container")
    m = color_mat or M["metal"]
    box("body", (2.44, 6.06, 2.59), (0, 0, 1.295), material=m, parent=r)
    for i in range(12):
        box("rib%d" % i, (2.5, 0.08, 2.5), (0, -2.8 + i * 0.5, 1.295), material=m, parent=r)
    box("door_l", (1.15, 0.08, 2.5), (-0.6, 3.05, 1.295), material=m, parent=r)
    box("door_r", (1.15, 0.08, 2.5), (0.6, 3.05, 1.295), material=m, parent=r)
    return r


def block(M):
    """Бетонный блок-укрытие (разрушаемый)."""
    r = root("PROP_Block")
    box("body", (3.2, 1.2, 1.6), (0, 0, 0.8), material=M["concrete"], parent=r)
    box("top", (3.4, 1.35, 0.2), (0, 0, 1.7), material=M["concrete"], parent=r)
    return r


def rubble(M):
    """Куча обломков."""
    r = root("PROP_Rubble")
    for i in range(5):
        box("chunk%d" % i, (1.2 - i * 0.1, 1.0, 0.5), (i * 0.5 - 1.0, (i % 3) * 0.5 - 0.5, 0.3),
            rot=(0, 0, math.radians(i * 23)), material=M["concrete"], parent=r)
    return r


def rail_segment(M, length=25.0):
    """Участок железной дороги: шпалы + два рельса."""
    r = root("PROP_Rail")
    box("ballast", (3.4, length, 0.35), (0, 0, 0.175), material=M["concrete"], parent=r)
    n = int(length / 0.65)
    for i in range(n):
        y = -length / 2 + i * 0.65
        box("sleeper%d" % i, (2.6, 0.28, 0.2), (0, y, 0.44), material=M["brick"], parent=r)
    for s in (-1, 1):
        box("rail%d" % s, (0.16, length, 0.18), (s * 0.72, 0, 0.62), material=M["steel"], parent=r)
    return r


def bale(M):
    """Стог сена — укрытие в поле."""
    r = root("PROP_Bale")
    cyl("bale", 1.1, 1.5, (0, 0, 1.1), rot=(0, math.radians(90), 0), verts=14,
        material=M["leaf"], parent=r)
    return r


BUILDERS = {
    "house": lambda M: house(M),
    "house_small": lambda M: house(M, w=7.0, d=6.0, h=3.0, floors=1),
    "warehouse": lambda M: warehouse(M),
    "station": lambda M: station(M),
    "tower": lambda M: tower(M),
    "tree_spruce": lambda M: tree(M, 0, 11.0),
    "tree_birch": lambda M: tree(M, 1, 9.0),
    "fence": lambda M: fence(M),
    "container": lambda M: container(M),
    "block": lambda M: block(M),
    "rubble": lambda M: rubble(M),
    "rail_segment": lambda M: rail_segment(M),
    "bale": lambda M: bale(M),
    "wreck": lambda M: wreck(M),
}


def wreck(M):
    """Сгоревший танк: корпус на брюхе, башня сорвана и завалилась — укрытие и ориентир."""
    r = root("PROP_Wreck")
    b = M["burnt"]
    # корпус, осевший на пробитые гусеницы
    box("hull", (2.4, 5.2, 0.85), (0, 0, 0.55), rot=(0, 0, math.radians(3)), material=b, parent=r)
    box("glacis", (2.4, 1.35, 0.24), (0, 2.6, 0.80), rot=(math.radians(-32), 0, 0), material=b, parent=r)
    box("deck", (2.15, 2.7, 0.16), (0, -0.9, 1.02), material=b, parent=r)
    box("engine", (2.0, 1.2, 0.36), (0, -2.2, 0.92), rot=(math.radians(14), 0, 0), material=b, parent=r)
    # башня сорвана и лежит рядом, ствол задран
    box("turret", (1.9, 2.5, 0.62), (1.5, -1.4, 0.42), rot=(0, math.radians(26), math.radians(14)), material=b, parent=r)
    cyl("gun", 0.09, 3.0, (2.4, -0.5, 0.78), rot=(math.radians(-74), 0, math.radians(20)),
        verts=10, material=b, parent=r)
    # гусеницы: одна на месте, вторая сползла; сорванные катки
    box("track_l", (0.45, 5.6, 0.6), (-1.4, 0, 0.42), material=M["metal"], parent=r)
    box("track_r", (0.45, 3.6, 0.5), (1.7, 0.7, 0.28), rot=(0, math.radians(9), 0), material=M["metal"], parent=r)
    for i in range(3):
        cyl("wheel%d" % i, 0.33, 0.22, (-1.55, -1.7 + i * 1.4, 0.34),
            rot=(math.radians(90), 0, 0), verts=14, material=M["metal"], parent=r)
    box("stowage", (0.75, 0.5, 0.35), (-0.7, 2.3, 1.18), rot=(0.1, 0.4, 0.15), material=b, parent=r)
    return r
