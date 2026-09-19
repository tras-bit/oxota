#!/usr/bin/env python3
"""Библиотека построения техники (4 класса: ЛТ, СТ, ТТ, ПТ-САУ) для «Samsar».

Собирает детализированную модель из примитивов: корпус с наклонными листами,
ходовая часть с катками и траками, башня/рубка, орудие, оптика, ящики ЗИП.
Все размеры в метрах, ось +Y — вперёд, Z — вверх.
"""
import math

import bpy


class TankSpec:
    def __init__(self, key, title, length, width, hull_h, turret, wheels,
                 gun_len, gun_cal, armor_class="MT", tint=None, camo=None):
        self.key = key            # lt / mt / ht / td
        self.title = title
        self.length = length      # длина корпуса, м
        self.width = width        # ширина по гусеницам, м
        self.hull_h = hull_h      # высота корпуса, м
        self.turret = turret      # "round" | "hex" | "casemate"
        self.wheels = wheels      # число опорных катков на борт
        self.gun_len = gun_len    # длина ствола, м
        self.gun_cal = gun_cal    # калибр, м
        self.armor_class = armor_class
        self.tint = tint or (1.0, 1.0, 1.0)
        self.camo = camo or "steel_olive"


SPECS = {
    "lt": TankSpec("lt", "ЛТ «Ветер»", 5.6, 2.9, 0.95, "round", 4, 2.9, 0.076, "LT",
                   camo="steel_olive"),
    "mt": TankSpec("mt", "СТ «Варяг»", 6.6, 3.2, 1.05, "hex", 5, 3.4, 0.100, "MT",
                   camo="steel_sand"),
    "ht": TankSpec("ht", "ТТ «Гранит»", 7.4, 3.9, 1.20, "hex", 6, 3.6, 0.122, "HT",
                   camo="steel_grey"),
    "td": TankSpec("td", "ПТ «Гроза»", 7.0, 3.3, 1.15, "casemate", 6, 4.2, 0.128, "TD",
                   camo="steel_green"),
}


# ---------- материалы ----------
def _img(name):
    path = _TEXDIR + "/" + name
    import os
    if not os.path.exists(path) and name.endswith(".jpg"):
        return None
    try:
        return bpy.data.images.load(path, check_existing=True)
    except Exception:
        return None


_TEXDIR = ""


def set_texdir(path):
    global _TEXDIR
    _TEXDIR = path


def make_material(name, albedo, normal=None, mask=None, base_color=(1, 1, 1),
                  metallic=0.6, roughness=0.5, tint_color=None):
    """PBR-материал: albedo + normal + mask (R=metal, A=smoothness) по схеме Unity."""
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*(tint_color or base_color), 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness

    tex = _img(albedo)
    if tex:
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = tex
        n.location = (-700, 300)
        nt.links.new(n.outputs["Color"], bsdf.inputs["Base Color"])
    nrm = _img(normal) if normal else None
    if nrm:
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = nrm
        n.location = (-700, 0)
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.location = (-350, 0)
        nt.links.new(n.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    msk = _img(mask) if mask else None
    if msk:
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = msk
        n.location = (-700, -320)
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        sep.location = (-380, -320)
        nt.links.new(n.outputs["Color"], sep.inputs["Color"])
        nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
        # smoothness -> roughness (инверсия)
        inv = nt.nodes.new("ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        inv.location = (-180, -420)
        nt.links.new(sep.outputs["Green"], inv.inputs[1])
        nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
    return m


def _tintkey(tint):
    return "%02x%02x%02x" % tuple(int(max(0.0, min(1.0, c)) * 255) for c in tint)


def default_materials(spec):
    tex = spec.camo                      # имя набора текстур брони
    return {
        "armor": make_material("MAT_" + tex, tex + "_albedo.jpg", tex + "_normal.png",
                               tex + "_mask.png", metallic=0.8, roughness=0.45),
        "dark": make_material("MAT_Rubber", "rubber_albedo.jpg", "rubber_normal.png",
                              "rubber_mask.png", metallic=0.05, roughness=0.35),
        "rust": make_material("MAT_Rusty", "metal_rusty_albedo.jpg", "metal_rusty_normal.png",
                              "metal_rusty_mask.png", metallic=0.5, roughness=0.6),
        "glass": make_material("MAT_Optics", "optics_albedo.jpg", "optics_normal.png",
                               "optics_mask.png", metallic=0.9, roughness=0.05),
    }


# ---------- примитивы ----------
def keep(ob, parent):
    mw = ob.matrix_world.copy()
    ob.parent = parent
    ob.matrix_parent_inverse = parent.matrix_world.inverted()
    ob.matrix_world = mw
    return ob


def box(name, size, loc, rot=(0, 0, 0), material=None, parent=None, bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    ob = bpy.context.object
    ob.name = name
    ob.scale = size
    ob.rotation_euler = rot
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if bevel > 0:
        mod = ob.modifiers.new("bev", "BEVEL")
        mod.width = bevel
        mod.segments = 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    if material:
        ob.data.materials.append(material)
    if parent:
        keep(ob, parent)
    return ob


def cyl(name, r, depth, loc, rot=(0, 0, 0), verts=18, material=None, parent=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc)
    ob = bpy.context.object
    ob.name = name
    ob.rotation_euler = rot
    if material:
        ob.data.materials.append(material)
    if parent:
        keep(ob, parent)
    return ob


def hull_shape(name, spec, mat):
    """Корпус: нижняя коробка + наклонный лобовой лист + корма + надстройка."""
    L, W, H = spec.length, spec.width, spec.hull_h
    body_w = W - 0.75                     # корпус уже гусениц
    hull = box(name, (body_w, L, H), (0, 0, H * 0.5 + 0.30), material=mat, bevel=0.03)
    # лобовой лист под наклоном
    box(name + "_glacis", (body_w - 0.04, L * 0.36, H * 1.1),
        (0, L * 0.40, H * 0.62 + 0.26), rot=(math.radians(-42), 0, 0),
        material=mat, parent=hull, bevel=0.02)
    # нижний лобовой лист
    box(name + "_glacis_low", (body_w - 0.04, L * 0.2, H * 0.8),
        (0, L * 0.46, H * 0.28), rot=(math.radians(-14), 0, 0), material=mat, parent=hull)
    # надстройка/подбашенная плита
    box(name + "_deck", (body_w + 0.06, L * 0.72, 0.16),
        (0, -L * 0.06, H + 0.28), material=mat, parent=hull, bevel=0.02)
    # кормовой лист и решётки
    box(name + "_rear", (body_w - 0.04, L * 0.14, H * 0.85),
        (0, -L * 0.46, H * 0.55 + 0.28), rot=(math.radians(12), 0, 0),
        material=mat, parent=hull)
    for i in range(3):
        box(name + "_grille%d" % i, (body_w * 0.26, L * 0.1, 0.06),
            (-body_w * 0.28 + i * body_w * 0.28, -L * 0.3, H + 0.37),
            material=mat, parent=hull)
    # крылья
    for s in (-1, 1):
        box(name + "_fender%d" % s, (0.7, L * 1.04, 0.09),
            (s * (body_w / 2 + 0.30), 0, H + 0.23), material=mat, parent=hull)
        # ящики ЗИП на крыльях
        box(name + "_box%d" % s, (0.55, 1.1, 0.34),
            (s * (body_w / 2 + 0.30), -L * 0.28, H + 0.44), material=mat, parent=hull)
        # фары
        cyl(name + "_lamp%d" % s, 0.11, 0.12, (s * (body_w / 2 - 0.05), L * 0.47, H + 0.30),
            rot=(math.radians(90), 0, 0), verts=12, material=mat, parent=hull)
        # буксирные крюки
        box(name + "_hook%d" % s, (0.1, 0.22, 0.12),
            (s * (body_w / 2 - 0.2), L * 0.52, H * 0.35), material=mat, parent=hull)
    # запасные траки на лбу
    for i in range(4):
        box(name + "_spare%d" % i, (0.42, 0.1, 0.05),
            (-0.5 + i * 0.34, L * 0.44, H * 0.95), rot=(math.radians(-42), 0, 0),
            material=mat, parent=hull)
    # выхлоп
    cyl(name + "_exhaust", 0.09, 0.7, (-body_w * 0.28, -L * 0.42, H + 0.6),
        rot=(math.radians(80), 0, 0), verts=10, material=mat, parent=hull)
    return hull


def running_gear(name, spec, mat, dark):
    """Гусеницы: лента, опорные катки, ведущее колесо, ленивец, поддерживающие ролики, траки."""
    L, W, H = spec.length, spec.width, spec.hull_h
    x = W / 2 - 0.34
    out = []
    for s in (-1, 1):
        track = box("%s_track%d" % (name, s), (0.66, L * 1.1, H * 0.95),
                    (s * x, 0, H * 0.55), material=dark)
        out.append(track)
        # траки по верхней и нижней ветви
        n_links = 14
        for i in range(n_links):
            y = -L * 0.52 + i * (L * 1.04 / (n_links - 1))
            for z, tag in ((H * 1.02, "t"), (H * 0.10, "b")):
                box("%s_link%d%s%d" % (name, s, tag, i), (0.72, 0.16, 0.07),
                    (s * x, y, z), material=dark, parent=track)
        # опорные катки
        span = L * 0.82
        for i in range(spec.wheels):
            y = -span / 2 + i * (span / max(1, spec.wheels - 1))
            cyl("%s_wheel%d_%d" % (name, s, i), 0.42, 0.5, (s * x, y, 0.45),
                rot=(0, math.radians(90), 0), verts=20, material=dark, parent=track)
        # ведущее колесо и ленивец
        cyl("%s_sprocket%d" % (name, s), 0.36, 0.44, (s * x, -L * 0.56, 0.62),
            rot=(0, math.radians(90), 0), verts=14, material=dark, parent=track)
        cyl("%s_idler%d" % (name, s), 0.34, 0.42, (s * x, L * 0.56, 0.62),
            rot=(0, math.radians(90), 0), verts=14, material=dark, parent=track)
        for i in range(3):
            y = -L * 0.25 + i * L * 0.25
            cyl("%s_roller%d_%d" % (name, s, i), 0.14, 0.34, (s * x, y, H * 1.02),
                rot=(0, math.radians(90), 0), verts=10, material=dark, parent=track)
    return out


def turret_shape(name, spec, mat, dark, glass):
    """Башня: круглая / гранёная / неподвижная рубка (ПТ-САУ)."""
    L, W, H = spec.length, spec.width, spec.hull_h
    body_w = W - 0.75
    z0 = H + 0.30
    if spec.turret == "casemate":
        # рубка с наклонными листами — орудие в лобовом листе
        cas = box(name + "_casemate", (body_w - 0.08, L * 0.62, H * 0.95),
                  (0, -L * 0.02, z0 + H * 0.42), material=mat, bevel=0.03)
        box(name + "_cas_front", (body_w - 0.12, L * 0.3, H * 1.0),
            (0, L * 0.24, z0 + H * 0.42), rot=(math.radians(-52), 0, 0),
            material=mat, parent=cas, bevel=0.02)
        box(name + "_cas_rear", (body_w - 0.12, L * 0.16, H * 0.9),
            (0, -L * 0.34, z0 + H * 0.4), rot=(math.radians(38), 0, 0),
            material=mat, parent=cas)
        cyl(name + "_gun", spec.gun_cal / 2, spec.gun_len,
            (0, L * 0.30 + spec.gun_len / 2 - 0.4, z0 + H * 0.55),
            rot=(math.radians(90), 0, 0), verts=20, material=mat, parent=cas)
        cyl(name + "_muzzle", spec.gun_cal * 0.85, 0.55,
            (0, L * 0.30 + spec.gun_len - 0.35, z0 + H * 0.55),
            rot=(math.radians(90), 0, 0), verts=16, material=mat, parent=cas)
        box(name + "_mantlet", (0.7, 0.36, 0.5),
            (0, L * 0.26, z0 + H * 0.55), material=mat, parent=cas)
        box(name + "_sight", (0.26, 0.2, 0.2), (-0.42, L * 0.2, z0 + H * 0.95),
            material=glass, parent=cas)
        box(name + "_hatch", (0.62, 0.62, 0.1), (0.4, -L * 0.1, z0 + H * 0.9),
            material=mat, parent=cas)
        cyl(name + "_aa_mg", 0.05, 0.8, (0.4, -L * 0.05, z0 + H * 1.05),
            rot=(math.radians(90), 0, 0), verts=8, material=dark, parent=cas)
        base = cas
    else:
        verts = 16 if spec.turret == "round" else 8
        r = body_w * 0.52
        turret = cyl(name + "_turret", r, H * 0.62, (0, -L * 0.06, z0 + H * 0.31),
                     verts=verts, material=mat)
        turret.scale = (1.0, 1.05, 1.0)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        turret.scale = (1.0, 1.0, 1.0)
        mod = turret.modifiers.new("bev", "BEVEL")
        mod.width = 0.06
        mod.segments = 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
        box(name + "_mantlet", (body_w * 0.45, 0.42, H * 0.52),
            (0, r * 0.85, z0 + H * 0.30), material=mat, parent=turret, bevel=0.03)
        cyl(name + "_gun", spec.gun_cal / 2, spec.gun_len,
            (0, r * 0.85 + spec.gun_len / 2 - 0.3, z0 + H * 0.30),
            rot=(math.radians(90), 0, 0), verts=20, material=mat, parent=turret)
        cyl(name + "_muzzle", spec.gun_cal * 0.9, spec.gun_len * 0.16,
            (0, r * 0.85 + spec.gun_len - 0.28, z0 + H * 0.30),
            rot=(math.radians(90), 0, 0), verts=16, material=mat, parent=turret)
        # командирская башенка, люк, пулемёт, оптика
        cyl(name + "_cupola", 0.36, 0.22, (0.18, -r * 0.55, z0 + H * 0.72),
            verts=14, material=mat, parent=turret)
        cyl(name + "_hatch", 0.32, 0.06, (0.18, -r * 0.55, z0 + H * 0.85),
            verts=14, material=mat, parent=turret)
        box(name + "_vision", (0.2, 0.12, 0.12), (0.18, -r * 0.85, z0 + H * 0.74),
            material=glass, parent=turret)
        box(name + "_sight", (0.3, 0.2, 0.2), (-0.34, r * 0.5, z0 + H * 0.52),
            material=glass, parent=turret)
        cyl(name + "_aa_mg", 0.05, 0.85, (0.18, -r * 0.5, z0 + H * 0.95),
            rot=(math.radians(90), 0, 0), verts=8, material=dark, parent=turret)
        box(name + "_basket", (body_w * 0.7, r * 0.9, 0.34),
            (0, -r * 0.85, z0 + H * 0.22), material=mat, parent=turret)
        # дымовые гранатомёты
        for i in range(3):
            for s in (-1, 1):
                cyl(name + "_smoke%d%d" % (i, s), 0.05, 0.22,
                    (s * (r * 0.75), r * 0.25 + i * 0.16, z0 + H * 0.5),
                    rot=(math.radians(60), 0, 0), verts=8, material=dark, parent=turret)
        cyl(name + "_antenna", 0.022, 1.7, (-r * 0.7, -r * 0.6, z0 + H * 0.9),
            rot=(math.radians(-6), 0, 0), verts=6, material=dark, parent=turret)
        base = turret
    return base


def build_tank(spec):
    mats = default_materials(spec)
    root = bpy.data.objects.new("TANK_" + spec.key, None)
    bpy.context.collection.objects.link(root)
    hull = hull_shape(spec.key, spec, mats["armor"])
    keep(hull, root)
    for ob in running_gear(spec.key, spec, mats["armor"], mats["dark"]):
        keep(ob, root)
    keep(turret_shape(spec.key, spec, mats["armor"], mats["dark"], mats["glass"]), root)
    return root
