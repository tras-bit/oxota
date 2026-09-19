#!/usr/bin/env python3
"""Библиотека построения техники (4 класса: ЛТ, СТ, ТТ, ПТ-САУ) для «Samsar».

Версия 2 — настоящие силуэты вместо ящиков:
- корпус: лофт по сечениям (наклонный лобовой лист, спонсоны над гусеницами, кормовой лист);
- башня: литое тело вращения (ЛТ) или гранёный купол (СТ/ТТ) с маской, командирской
  башенкой, приборами наблюдения, ЗИП, дымовыми гранатомётами и антенной;
- ПТ-САУ: неподвижная рубка с наклонным лобовым листом, наводится только орудие;
- ходовая: лента с траками по реальной траектории (звёздочка, ленивец, поддерживающие
  ролики, провис), двойные опорные катки с дисками и болтами, балансиры;
- мелочи, из которых складывается «настоящая» машина: крылья с кронштейнами и грязевыми
  щитками, фары, буксирные крюки, ящики ЗИП, выхлоп с теплозащитой, запасные траки.

Все размеры в метрах, ось +Y — вперёд, Z — вверх. Экспорт в FBX превращает это
в Unity-конвенцию (Up=Y, Front=+Z).
"""
import math
import os
from collections import defaultdict

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

# ---------------------------------------------------------------- параметры машин


class TankSpec:
    def __init__(self, key, title, length, width, hull_h, turret, wheels,
                 gun_len, gun_cal, armor_class="MT", camo="steel_olive",
                 track_w=0.58, wheel_r=0.36, clearance=0.42, turret_h=0.62,
                 track_pitch=0.22, gun_style="long"):
        self.key = key            # lt / mt / ht / td
        self.title = title
        self.length = length      # длина корпуса, м
        self.width = width        # ширина по гусеницам, м
        self.hull_h = hull_h      # высота корпуса от днища до крыши, м
        self.turret = turret      # "round" | "hex" | "casemate"
        self.wheels = wheels      # число опорных катков на борт
        self.gun_len = gun_len    # длина ствола от маски, м
        self.gun_cal = gun_cal    # калибр, м
        self.armor_class = armor_class
        self.camo = camo
        self.track_w = track_w
        self.wheel_r = wheel_r
        self.clearance = clearance
        self.turret_h = turret_h
        self.track_pitch = track_pitch
        self.gun_style = gun_style

    @property
    def hull_bottom(self):
        return self.wheel_r + 0.06

    @property
    def hull_roof(self):
        return self.hull_bottom + self.hull_h

    @property
    def body_half_w(self):
        """Полуширина нижнего (узкого) корпуса — между гусеницами."""
        return self.width * 0.5 - self.track_w - 0.02

    @property
    def top_half_w(self):
        """Полуширина крыши корпуса: лишь немного выступает за борт (иначе корпус «стол»)."""
        return min(self.body_half_w + 0.12, self.width * 0.5 - self.track_w + 0.14)

    @property
    def turret_half_w(self):
        """Полуширина башни — считается от ширины машины, а не от корпуса."""
        return min(self.width * 0.5 - self.track_w * 0.72, self.top_half_w + 0.30)


SPECS = {
    "lt": TankSpec("lt", "ЛТ «Ветер»", 5.6, 2.9, 0.95, "round", 5, 3.3, 0.076, "LT",
                   camo="steel_olive", track_w=0.50, wheel_r=0.34, clearance=0.40, turret_h=0.74),
    "mt": TankSpec("mt", "СТ «Варяг»", 6.6, 3.2, 1.05, "hex", 6, 3.9, 0.100, "MT",
                   camo="steel_sand", track_w=0.56, wheel_r=0.38, clearance=0.42, turret_h=0.78),
    "ht": TankSpec("ht", "ТТ «Гранит»", 7.4, 3.9, 1.20, "hex", 6, 4.3, 0.122, "HT",
                   camo="steel_grey", track_w=0.68, wheel_r=0.42, clearance=0.44, turret_h=0.88),
    "td": TankSpec("td", "ПТ «Гроза»", 7.0, 3.3, 1.15, "casemate", 6, 5.1, 0.128, "TD",
                   camo="steel_green", track_w=0.58, wheel_r=0.39, clearance=0.42, turret_h=0.72),
}


# ---------------------------------------------------------------- материалы
_TEXDIR = ""


def set_texdir(path):
    global _TEXDIR
    _TEXDIR = path


def _img(name):
    path = _TEXDIR + "/" + name
    if not os.path.exists(path):
        return None
    try:
        return bpy.data.images.load(path, check_existing=True)
    except Exception:
        return None


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
        inv = nt.nodes.new("ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        inv.location = (-180, -420)
        nt.links.new(sep.outputs["Green"], inv.inputs[1])
        nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
    return m


def default_materials(spec):
    """Материалы машины. Имена важны: Unity раскладывает их по MAT_* (TankAssetBuilder)."""
    camo = spec.camo
    return {
        "armor": make_material("MAT_" + camo, camo + "_albedo.jpg", camo + "_normal.png",
                               camo + "_mask.png", metallic=0.8, roughness=0.45),
        "metal": make_material("MAT_MetalDark", "metal_dark_albedo.jpg", "metal_dark_normal.png",
                               "metal_dark_mask.png", metallic=0.9, roughness=0.4),
        "tire": make_material("MAT_Rubber", "rubber_albedo.jpg", "rubber_normal.png",
                              "rubber_mask.png", metallic=0.05, roughness=0.5),
        "rust": make_material("MAT_Rusty", "metal_rusty_albedo.jpg", "metal_rusty_normal.png",
                              "metal_rusty_mask.png", metallic=0.5, roughness=0.6),
        "glass": make_material("MAT_Optics", "optics_albedo.jpg", "optics_normal.png",
                               "optics_mask.png", metallic=0.9, roughness=0.05),
    }


# ---------------------------------------------------------------- примитивы (bmesh)

def _matrix(center, size=None, rot=None):
    m = Matrix.Translation(Vector(center))
    if rot is not None:
        r = rot.to_matrix().to_4x4() if hasattr(rot, "to_matrix") else Euler(rot).to_matrix().to_4x4()
        m = m @ r
    if size is not None:
        s = Vector(size)
        m = m @ Matrix.Diagonal(Vector((s.x, s.y, s.z, 1.0)))
    return m


def box(bm, center, size, rot=None, bevel=0.02, seg=2):
    """Параллелепипед с фаской — базовая деталь почти всего."""
    r = bmesh.ops.create_cube(bm, size=1.0, matrix=_matrix(center, size, rot))
    verts = r["verts"]
    if bevel > 0:
        edges = list({e for v in verts for e in v.link_edges})
        off = min(bevel, min(size) * 0.35)
        if off > 0.002:
            bmesh.ops.bevel(bm, geom=edges, offset=off, segments=seg, profile=0.5, affect="EDGES")
    return verts


def cyl(bm, center, radius, depth, axis="X", segments=16, rot=None, radius2=None):
    """Цилиндр (или усечённый конус) вдоль выбранной оси."""
    base = {"X": Matrix.Rotation(math.radians(90), 4, "Y"),
            "Y": Matrix.Rotation(math.radians(-90), 4, "X"),
            "Z": Matrix.Identity(4)}[axis]
    m = _matrix(center, None, rot) @ base
    r = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments,
                              radius1=radius, radius2=radius if radius2 is None else radius2,
                              depth=depth, matrix=m)
    return r["verts"]


def lathe(bm, profile, segments, y_scale=1.0, y_off=0.0):
    """Тело вращения по профилю [(радиус, z), ...] — литая башня, купола, кожухи."""
    rings = []
    for radius, z in profile:
        if radius <= 0.004:
            rings.append(("point", bm.verts.new((0.0, y_off, z))))
            continue
        ring = []
        for i in range(segments):
            a = 2 * math.pi * i / segments
            ring.append(bm.verts.new((radius * math.cos(a), y_off + radius * y_scale * math.sin(a), z)))
        rings.append(("ring", ring))
    for (ka, a), (kb, b) in zip(rings, rings[1:]):
        if ka == "ring" and kb == "ring":
            for i in range(segments):
                j = (i + 1) % segments
                _face(bm, (a[i], a[j], b[j], b[i]))
        elif ka == "ring" and kb == "point":
            for i in range(segments):
                j = (i + 1) % segments
                _face(bm, (a[i], a[j], b))
        elif ka == "point" and kb == "ring":
            for i in range(segments):
                j = (i + 1) % segments
                _face(bm, (a, b[j], b[i]))
    if rings and rings[0][0] == "ring":
        _face(bm, tuple(reversed(rings[0][1])))
    if rings and rings[-1][0] == "ring":
        _face(bm, tuple(rings[-1][1]))
    return bm


def _face(bm, verts):
    try:
        bm.faces.new(verts)
    except ValueError:
        pass


def loft(bm, sections, cap=True):
    """Сшивает замкнутые контуры одинаковой длины в тело по сечениям."""
    rings = [[bm.verts.new(p) for p in section] for section in sections]
    n = len(rings[0])
    for a, b in zip(rings, rings[1:]):
        for i in range(n):
            j = (i + 1) % n
            _face(bm, (a[i], a[j], b[j], b[i]))
    if cap:
        _face(bm, tuple(reversed(rings[0])))
        _face(bm, tuple(rings[-1]))
    return [v for ring in rings for v in ring]


# ---------------------------------------------------------------- объект из bmesh

def _shade(me, angle_deg):
    """Гладкое затенение с острыми рёбрами по углу (нормали уезжают в FBX)."""
    me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
    norms = [f.normal.copy() for f in me.polygons]
    by_edge = defaultdict(list)
    for f in me.polygons:
        for ek in f.edge_keys:
            by_edge[ek].append(f.index)
    lim = math.radians(angle_deg)
    for e in me.edges:
        e.use_edge_sharp = False
        faces = by_edge.get(e.key, ())
        if len(faces) == 2:
            a, b = norms[faces[0]], norms[faces[1]]
            if a.length > 0.0 and b.length > 0.0 and a.angle(b, 0.0) > lim:
                e.use_edge_sharp = True


def finish(name, bm, mats, parent=None, uv=2.0, smooth_angle=35.0, uv_project=True):
    """bmesh → объект: нормали, UV-проекция «по кубу» с постоянным шагом, сглаживание."""
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)

    if not isinstance(mats, (list, tuple)):
        mats = [mats]
    for m in mats:
        me.materials.append(m)

    if uv_project and me.polygons:
        for o in bpy.context.selected_objects:
            o.select_set(False)
        bpy.context.view_layer.objects.active = ob
        ob.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        try:
            bpy.ops.uv.cube_project(cube_size=uv)
        except Exception:
            pass
        bpy.ops.object.mode_set(mode="OBJECT")

    _shade(me, smooth_angle)
    if parent:
        keep(ob, parent)
    return ob


def keep(ob, parent):
    mw = ob.matrix_world.copy()
    ob.parent = parent
    ob.matrix_parent_inverse = parent.matrix_world.inverted()
    ob.matrix_world = mw
    return ob


# ---------------------------------------------------------------- корпус
def hull_sections(spec):
    """Сечения корпуса: (y, доля ширины днища, доля ширины крыши, z днища, z спонсона, z крыши)."""
    L = spec.length
    zb, zt = spec.hull_bottom, spec.hull_roof
    bw, hw = spec.body_half_w, spec.top_half_w
    flare = zb + spec.hull_h * 0.30

    def sec(y, bwf=1.0, hwf=1.0, zb_=zb, fl_=flare, zt_=zt):
        return (y, bw * bwf, hw * hwf, zb_, min(max(fl_, zb_ + 0.06), zt_ - 0.06), zt_)

    return [
        sec(-L * 0.500, 0.90, 0.88, zb + 0.20, flare - 0.06, zt - 0.20),   # кормовой лист
        sec(-L * 0.485, 0.99, 0.96, zb + 0.04, flare - 0.02, zt - 0.04),
        sec(-L * 0.300, 1.00, 1.00),                                      # моторный отсек
        sec(-L * 0.120, 1.00, 1.00),
        sec(L * 0.080, 1.00, 1.00),                                       # отделение управления
        sec(L * 0.260, 1.00, 1.00),
        sec(L * 0.330, 1.00, 0.99, zb, flare, zt - 0.10),                 # начало лобового листа
        sec(L * 0.410, 1.00, 0.96, zb + 0.02, flare - 0.02, zt - 0.52),
        sec(L * 0.470, 0.99, 0.90, zb + 0.12, zb + 0.30, zb + 0.34),
        sec(L * 0.500, 0.96, 0.86, zb + 0.20, zb + 0.30, zb + 0.28),      # нос
    ]


def hull_contour(y, bw, hw, zb, fl, zt, ch=0.14):
    """Контур сечения корпуса (8 точек): узкий низ, ступень-спонсон, вертикальный борт."""
    fl = min(max(fl, zb + 0.03), zt - 0.03)
    ch = max(0.03, min(ch, (zt - fl) * 0.9))
    half = [(bw, zb), (bw, fl), (hw, fl + ch), (hw, zt)]
    return [(x, y, z) for x, z in half] + [(-x, y, z) for x, z in reversed(half)]


def cabin_contour(y, bw, hw, zb, zt, ch=0.05):
    """Контур сечения рубки ПТ-САУ (6 точек): наклонные борта."""
    half = [(bw, zb), (bw, zt - ch), (hw, zt)]
    return [(x, y, z) for x, z in half] + [(-x, y, z) for x, z in reversed(half)]


def build_hull(spec, mats):
    """Корпус: бронекорпус + крылья/ЗИП + тёмный металл + фары + грязевые щитки."""
    L, W = spec.length, spec.width
    zb, zt = spec.hull_bottom, spec.hull_roof
    bw, hw = spec.body_half_w, spec.top_half_w
    out = []

    bm = bmesh.new()
    loft(bm, [hull_contour(*s) for s in hull_sections(spec)])
    out.append(finish(spec.key, bm, mats["armor"], uv=2.0))

    bm = bmesh.new()
    zf = spec.wheel_r * 2 + 0.32
    for s in (-1, 1):
        x = s * (hw + 0.14)
        box(bm, (x, -L * 0.01, zf), (0.30, L * 1.00, 0.05), bevel=0.01)                 # крыло над гусеницей
        for i in range(5):                                                              # кронштейны к борту
            box(bm, (s * (hw + 0.07), -L * 0.38 + i * L * 0.19, zf - 0.02), (0.24, 0.08, 0.08), bevel=0.01)
        box(bm, (x, L * 0.505, zf - 0.20), (0.30, 0.10, 0.50), bevel=0.01)              # передний щиток
        box(bm, (x, -L * 0.525, zf - 0.24), (0.30, 0.06, 0.56),
            rot=(math.radians(-18), 0, 0), bevel=0.01)                                  # задний щиток
    box(bm, (hw - 0.20, -L * 0.30, zt + 0.17), (0.34, 0.92, 0.30), bevel=0.02)          # ящик ЗИП
    box(bm, (-(hw - 0.20), -L * 0.16, zt + 0.15), (0.34, 0.62, 0.26), bevel=0.02)       # второй ящик
    for i in range(4):                                                                  # решётки МТО
        box(bm, (0.55 - i * 0.02, -L * 0.40 + i * L * 0.10, zt + 0.015), (0.62, L * 0.075, 0.05), bevel=0.008)
    box(bm, (-hw * 0.55, -L * 0.30, zt + 0.03), (0.30, L * 0.34, 0.06), bevel=0.01)
    for i in range(6):                                                                  # запасные траки
        box(bm, (-0.45 + (i % 2) * 0.9, L * 0.395, zt - 0.60 + i * 0.088),
            (0.42, 0.10, 0.075), rot=(math.radians(-38), 0, 0), bevel=0.01)
    for s in (-1, 1):                                                                   # буксирные крюки
        box(bm, (s * (bw - 0.06), L * 0.465, zb + 0.22), (0.09, 0.26, 0.16), bevel=0.01)
        box(bm, (s * (bw - 0.05), -L * 0.49, zb + 0.26), (0.09, 0.22, 0.14), bevel=0.01)
    out.append(finish(spec.key + "_hull_details", bm, mats["armor"], uv=1.5))

    bm = bmesh.new()
    box(bm, (hw - 0.13, L * 0.20, zt + 0.13), (0.07, 1.10, 0.07), bevel=0.01)           # лом
    box(bm, (-(hw - 0.15), L * 0.07, zt + 0.11), (0.16, 0.34, 0.06), bevel=0.01)        # лопата
    box(bm, (-(hw - 0.15), -L * 0.24, zt + 0.12), (0.12, 0.60, 0.06), bevel=0.01)       # топор/пила
    for s in (-1, 1):                                                                   # выхлоп
        cyl(bm, (s * (bw * 0.55), -L * 0.50, zt - 0.06), 0.075, 0.62, axis="Y", segments=12)
        box(bm, (s * (bw * 0.55), -L * 0.44, zt - 0.06), (0.24, 0.50, 0.24), bevel=0.02)
    for i in range(3):                                                                  # поручни
        box(bm, (hw - 0.10, -L * 0.10 + i * 0.42, zt + 0.19), (0.05, 0.05, 0.05))
    cyl(bm, (-0.42, L * 0.24, zt + 0.01), 0.30, 0.05, axis="Z", segments=16)             # люк механика-водителя
    for i in range(2):                                                                   # перископы
        box(bm, (-0.42 + i * 0.30 - 0.15, L * 0.315, zt + 0.05), (0.18, 0.10, 0.09), bevel=0.01)
    box(bm, (-0.42, L * 0.205, zt + 0.05), (0.30, 0.05, 0.10), bevel=0.01)
    for i in range(7):                                                                   # буксирный трос по крыше
        box(bm, (hw - 0.30 + i * 0.02, -L * 0.10 + i * 0.02, zt + 0.05), (0.06, 0.34, 0.06), bevel=0.01)
    out.append(finish(spec.key + "_hull_metal", bm, mats["metal"], uv=0.8))

    bm = bmesh.new()                                                                    # корпуса фар
    lz = zt - spec.hull_h * 0.34
    for s in (-1, 1):
        box(bm, (s * (hw - 0.30), L * 0.415, lz - 0.13), (0.16, 0.12, 0.16), bevel=0.01)  # кронштейн
        cyl(bm, (s * (hw - 0.30), L * 0.415, lz), 0.11, 0.17, axis="Y", segments=14)
    bm2 = bmesh.new()                                                                   # стёкла фар
    for s in (-1, 1):
        cyl(bm2, (s * (hw - 0.30), L * 0.415 + 0.085, lz), 0.095, 0.04, axis="Y", segments=14)
    out.append(finish(spec.key + "_lights", bm, mats["metal"], uv=0.6))
    out.append(finish(spec.key + "_light_glass", bm2, mats["glass"], uv=0.6))

    bm = bmesh.new()                                                                    # грязевые щитки
    for s in (-1, 1):
        for sy, ang in ((1, 22), (-1, -16)):
            box(bm, (s * (W * 0.5 - 0.05), sy * (L * 0.52), zb + 0.16), (0.30, 0.06, 0.42),
                rot=(math.radians(ang), 0, 0), bevel=0.01)
    out.append(finish(spec.key + "_flaps", bm, mats["tire"], uv=1.0))
    return out


# ---------------------------------------------------------------- ходовая
def track_path(spec):
    """Замкнутая траектория ленты (y, z): низ → звёздочка сзади → верх → ленивец спереди."""
    L, r = spec.length, spec.wheel_r
    y_s = -(L * 0.5 - 0.16)
    y_i = (L * 0.5 - 0.16)
    z_sp, r_sp = r * 0.75 + 0.34, 0.34
    z_id, r_id = r * 0.72 + 0.30, 0.30
    z_top = r * 2 + 0.20

    pts = []

    def arc(cy, cz, rad, a0, a1, n=12):
        for i in range(n + 1):
            a = math.radians(a0 + (a1 - a0) * i / n)
            pts.append((cy + rad * math.cos(a), cz + rad * math.sin(a)))

    for i in range(5):                                        # нижняя ветвь
        pts.append((y_i + (y_s - y_i) * i / 4, 0.02))
    arc(y_s, z_sp, r_sp, -90, -270, 12)                       # вокруг звёздочки
    for i in range(11):                                       # верхняя ветвь с провисом
        t = i / 10
        pts.append((y_s + (y_i - y_s) * t, z_top - math.sin(math.pi * t) * 0.06))
    arc(y_i, z_id, r_id, 90, -90, 12)                         # вокруг ленивца
    return pts, (y_s, z_sp, r_sp), (y_i, z_id, r_id)


def resample(path, pitch):
    """Разложить замкнутый контур с равным шагом: [(точка, угол наклона), ...]."""
    pts = list(path)
    n = len(pts)
    segs, total = [], 0.0
    for i in range(n):
        a, b = pts[i], pts[(i + 1) % n]
        d = math.hypot(b[0] - a[0], b[1] - a[1])
        segs.append((a, b, d))
        total += d
    count = max(8, int(round(total / pitch)))
    step = total / count
    out, idx, acc = [], 0, 0.0
    for k in range(count):
        target = k * step
        while idx < len(segs) - 1 and acc + segs[idx][2] < target:
            acc += segs[idx][2]
            idx += 1
        a, b, d = segs[idx]
        t = (target - acc) / d if d > 1e-6 else 0.0
        out.append(((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t),
                    math.atan2(b[1] - a[1], b[0] - a[0])))
    return out


def build_tracks(spec, mats):
    """Гусеницы: лента, траки, катки, звёздочка, ленивец, ролики, балансиры."""
    L, W, r = spec.length, spec.width, spec.wheel_r
    tw = spec.track_w
    x_out = W * 0.5 - tw * 0.5
    out = []
    path, (y_s, z_sp, r_sp), (y_i, z_id, r_id) = track_path(spec)
    samples = resample(path, spec.track_pitch)
    span = L * 0.62
    n = spec.wheels

    for s in (-1, 1):
        x = s * x_out

        bm = bmesh.new()                                       # сплошная лента
        rings = []
        for (y, z), ang in samples:
            rot = Matrix.Rotation(ang, 3, "X")
            ring = []
            for dx, dz in ((-tw * 0.5, 0.06), (tw * 0.5, 0.06), (tw * 0.5, -0.05), (-tw * 0.5, -0.05)):
                off = rot @ Vector((0.0, 0.0, dz))
                ring.append(bm.verts.new((x + dx, y + off.y, z + off.z)))
            rings.append(ring)
        for a, b in zip(rings, rings[1:] + rings[:1]):
            for i in range(4):
                j = (i + 1) % 4
                _face(bm, (a[i], a[j], b[j], b[i]))
        out.append(finish(spec.key + "_track%d" % s, bm, mats["metal"], uv=1.0, smooth_angle=50))

        bm = bmesh.new()                                       # траки
        p = spec.track_pitch
        for (y, z), ang in samples:
            rot = Euler((ang, 0, 0))
            box(bm, (x, y, z + 0.030), (tw * 0.99, p * 0.92, 0.075), rot=rot, bevel=0.006, seg=1)
            box(bm, (x, y, z + 0.085), (0.09, p * 0.40, 0.075), rot=rot, bevel=0.004, seg=1)
            for side in (-0.34, 0.34):
                cyl(bm, (x + side * tw, y, z + 0.045), 0.018, tw * 0.34, axis="X", segments=6)
        out.append(finish(spec.key + "_link%d" % s, bm, mats["metal"], uv=0.5, smooth_angle=50))

        for i in range(n):                                     # опорные катки (двойные)
            y = -span / 2 + span * i / max(1, n - 1)
            bm = bmesh.new()
            for sx in (-0.26, 0.26):
                cyl(bm, (x + sx * tw, y, r), r, tw * 0.38, axis="X", segments=20)
            out.append(finish(spec.key + "_wheel%d_%d" % (s, i), bm, mats["tire"], uv=0.8, smooth_angle=60))

            bm = bmesh.new()
            for sx in (-1, 1):                                 # ступица с болтами (видна с борта)
                cyl(bm, (x + sx * tw * 0.34, y, r), r * 0.34, tw * 0.20, axis="X", segments=14)
                for k in range(6):
                    a = math.radians(k * 60)
                    cyl(bm, (x + sx * tw * 0.40, y + math.cos(a) * r * 0.24, r + math.sin(a) * r * 0.24),
                        0.020, tw * 0.14, axis="X", segments=6)
            cyl(bm, (x, y, r), r * 0.60, tw * 0.56, axis="X", segments=18)
            cyl(bm, (x, y, r), r * 0.22, tw * 0.54, axis="X", segments=12)
            for k in range(6):
                a = math.radians(k * 60)
                cyl(bm, (x, y + math.cos(a) * r * 0.40, r + math.sin(a) * r * 0.40),
                    0.032, tw * 0.30, axis="X", segments=6)
            out.append(finish(spec.key + "_wheel_rim%d_%d" % (s, i), bm, mats["metal"], uv=0.8, smooth_angle=60))

        bm = bmesh.new()                                       # ведущее колесо
        cyl(bm, (x, y_s, z_sp), r_sp * 0.55, tw * 0.30, axis="X", segments=16)
        for sx in (-0.22, 0.22):
            cyl(bm, (x + sx * tw, y_s, z_sp), r_sp, tw * 0.16, axis="X", segments=16)
        for k in range(12):
            a = math.radians(k * 30)
            box(bm, (x, y_s + math.cos(a) * r_sp * 0.97, z_sp + math.sin(a) * r_sp * 0.97),
                (tw * 0.52, 0.07, 0.09), bevel=0.008, seg=1)
        out.append(finish(spec.key + "_sprocket%d" % s, bm, mats["metal"], uv=0.8, smooth_angle=50))

        bm = bmesh.new()                                       # ленивец
        cyl(bm, (x, y_i, z_id), r_id * 0.92, tw * 0.34, axis="X", segments=16)
        cyl(bm, (x, y_i, z_id), r_id * 0.42, tw * 0.44, axis="X", segments=12)
        out.append(finish(spec.key + "_idler%d" % s, bm, mats["metal"], uv=0.8, smooth_angle=50))

        bm = bmesh.new()                                       # поддерживающие ролики
        for i in range(3):
            y = -span * 0.34 + span * 0.34 * i
            cyl(bm, (x, y, r * 2 + 0.06), 0.12, tw * 0.42, axis="X", segments=12)
        out.append(finish(spec.key + "_roller%d" % s, bm, mats["metal"], uv=0.8, smooth_angle=50))

        bm = bmesh.new()                                       # балансиры и амортизаторы
        for i in range(n):
            y = -span / 2 + span * i / max(1, n - 1)
            box(bm, (x * 0.70, y, r * 0.58), (tw * 0.5, 0.14, 0.16), bevel=0.01)
            if i < n - 1:
                box(bm, (x * 0.70, y + span / (2 * (n - 1)) * 0.75, r * 0.95),
                    (tw * 0.42, 0.46, 0.10), rot=(math.radians(16), 0, 0), bevel=0.01)
        out.append(finish(spec.key + "_susp%d" % s, bm, mats["metal"], uv=0.8, smooth_angle=50))
    return out


# ---------------------------------------------------------------- башня и орудие
def build_turret(spec, mats):
    """Башня/рубка, маска, орудие, дульный тормоз, башенка, ЗИП, дымы, антенна, оптика."""
    L = spec.length
    zt = spec.hull_roof
    hw = spec.turret_half_w
    h = spec.turret_h
    out = []
    turret_y = -L * 0.045
    casemate = spec.turret == "casemate"

    bm = bmesh.new()
    if casemate:
        # рубка: неподвижная надстройка с наклонным лобовым листом
        Lc = L * 0.62
        zb = zt - 0.02
        top = zt + h
        secs = [
            (turret_y - Lc * 0.50, hw * 0.84, hw * 0.78, zb, top - 0.44),
            (turret_y - Lc * 0.38, hw * 0.94, hw * 0.90, zb, top - 0.14),
            (turret_y - Lc * 0.10, hw, hw * 0.98, zb, top),
            (turret_y + Lc * 0.20, hw * 0.99, hw * 0.97, zb, top - 0.04),
            (turret_y + Lc * 0.38, hw * 0.93, hw * 0.88, zb + 0.02, top - 0.40),
            (turret_y + Lc * 0.50, hw * 0.86, hw * 0.80, zb + 0.10, top - 0.60),
        ]
        loft(bm, [cabin_contour(y, bw_, hw_, zb_, zt_) for (y, bw_, hw_, zb_, zt_) in secs])
        front_y = turret_y + Lc * 0.44
        gun_z = zb + h * 0.52
        out.append(finish(spec.key + "_cabin", bm, mats["armor"], uv=2.0))
    else:
        round_t = spec.turret == "round"
        segments = 24 if round_t else 8
        r0 = hw
        ys = 1.26 if round_t else 1.06
        profile = (
            [(0.58, 0.00), (0.88, 0.06), (1.00, 0.18), (1.00, 0.55),
             (0.97, 0.70), (0.86, 0.85), (0.55, 0.95), (0.00, 1.00)]
            if round_t else
            [(0.70, 0.00), (0.94, 0.05), (1.00, 0.15), (1.00, 0.70),
             (0.95, 0.82), (0.74, 0.92), (0.34, 0.98), (0.00, 1.00)]
        )
        # крыша корпуса + высота башни: раньше профиль стоял на земле и башни не было видно
        lathe(bm, [(r0 * r, zt + z * h) for r, z in profile], segments,
              y_scale=ys, y_off=turret_y)
        front_y = turret_y + r0 * ys
        gun_z = zt + h * 0.48
        out.append(finish(spec.key + "_turret", bm, mats["armor"], uv=2.0,
                          smooth_angle=42 if round_t else 24))

    # --- броневые детали башни/рубки
    bm = bmesh.new()
    if casemate:
        box(bm, (0, front_y - 0.04, gun_z), (hw * 1.34, 0.34, 0.58),
            rot=(math.radians(-40), 0, 0), bevel=0.03)
        box(bm, (0.52, turret_y - L * 0.16, zt + spec.turret_h + 0.06), (0.62, 0.72, 0.16), bevel=0.02)
    else:
        box(bm, (0, front_y - 0.08, gun_z), (hw * 0.88, 0.40, 0.54), bevel=0.035)
        cyl(bm, (0, front_y + 0.06, gun_z), spec.gun_cal * 1.35, 0.30, axis="Y", segments=18)
        for s in (-1, 1):
            box(bm, (s * hw * 0.52, front_y - 0.16, gun_z), (0.12, 0.30, 0.40), bevel=0.02)
    cup_y = turret_y - (0.24 if not casemate else 0.30)
    cup_z = (zt + h * 0.90) if not casemate else (zt + h + 0.14)
    box(bm, (0.44, cup_y, cup_z), (0.46, 0.54, 0.20), bevel=0.03)                     # башенка
    cyl(bm, (0.44, cup_y, cup_z + 0.13), 0.19, 0.05, axis="Z", segments=14)           # крышка люка
    if not casemate:                                                                  # корзина ЗИП
        for i in range(4):
            box(bm, (-0.54 + i * 0.36, turret_y - hw * 0.98, zt + h * 0.34), (0.30, 0.06, 0.36), bevel=0.01)
    for s in (-1, 1):                                                                 # дымовые гранатомёты
        for i in range(3):
            cyl(bm, (s * (hw * 0.66), front_y - 0.48 + i * 0.15, gun_z + 0.14), 0.042, 0.20,
                axis="Y", segments=8, rot=(math.radians(-16), 0, 0))
    out.append(finish(spec.key + "_turret_armor", bm, mats["armor"], uv=1.2))

    bm = bmesh.new()                                                                   # тёмный металл
    cyl(bm, (0.44, cup_y + 0.32, cup_z + 0.26), 0.035, 0.9, axis="Y", segments=8)      # пулемёт
    box(bm, (0.44, cup_y + 0.02, cup_z + 0.24), (0.10, 0.30, 0.12), bevel=0.01)
    cyl(bm, (-hw * 0.58, turret_y - 0.30, zt + h * 0.86), 0.02, 1.9, axis="Z", segments=6)  # антенна
    cyl(bm, (-hw * 0.58, turret_y - 0.30, zt + h * 0.86), 0.07, 0.14, axis="Z", segments=8)
    out.append(finish(spec.key + "_turret_metal", bm, mats["metal"], uv=0.8))

    bm = bmesh.new()                                                                   # оптика
    box(bm, (-0.42, front_y - 0.32, gun_z + 0.30), (0.30, 0.20, 0.18), bevel=0.02)     # прицел
    if not casemate:
        for i in range(6):                                                             # приборы башенки
            a = math.radians(i * 60)
            box(bm, (0.44 + 0.25 * math.cos(a), cup_y + 0.27 * math.sin(a), cup_z + 0.06),
                (0.12, 0.09, 0.09), bevel=0.01)
    out.append(finish(spec.key + "_turret_optics", bm, mats["glass"], uv=0.8))

    # --- орудие
    sleeve = 0.9
    y0 = front_y - 0.02
    bm = bmesh.new()
    cyl(bm, (0, y0 + sleeve * 0.5, gun_z), spec.gun_cal * 1.15, sleeve, axis="Y", segments=18)
    cyl(bm, (0, y0 + sleeve + 0.14, gun_z), spec.gun_cal * 0.72, 0.30, axis="Y", segments=16)
    cyl(bm, (0, y0 + sleeve + (spec.gun_len - sleeve) * 0.5, gun_z),
        spec.gun_cal * 0.60, spec.gun_len - sleeve, axis="Y", segments=18, radius2=spec.gun_cal * 0.50)
    if spec.gun_style == "long":                                                       # теплоизоляция
        cyl(bm, (0, y0 + sleeve + (spec.gun_len - sleeve) * 0.45, gun_z),
            spec.gun_cal * 0.74, (spec.gun_len - sleeve) * 0.42, axis="Y", segments=16,
            radius2=spec.gun_cal * 0.70)
    out.append(finish(spec.key + "_gun", bm, mats["metal"], uv=0.8, smooth_angle=50))

    bm = bmesh.new()                                                                   # дульный тормоз
    tip_y = y0 + spec.gun_len
    for i in range(2):
        cyl(bm, (0, tip_y + 0.06 + i * 0.32, gun_z), spec.gun_cal * 1.15, 0.24, axis="Y", segments=16)
    box(bm, (0, tip_y - 0.02, gun_z), (spec.gun_cal * 2.0, 0.16, 0.14), bevel=0.01)
    out.append(finish(spec.key + "_muzzle", bm, mats["metal"], uv=0.6, smooth_angle=50))
    return out


# ---------------------------------------------------------------- сборка
def build_tank(spec):
    mats = default_materials(spec)
    root = bpy.data.objects.new("TANK_" + spec.key, None)
    bpy.context.collection.objects.link(root)
    for ob in build_hull(spec, mats) + build_tracks(spec, mats) + build_turret(spec, mats):
        keep(ob, root)
    return root


if __name__ == "__main__":                       # быстрый тест: blender -b -P tank_lib.py -- mt
    import sys
    key = sys.argv[-1] if sys.argv[-1] in SPECS else "mt"
    bpy.ops.wm.read_factory_settings(use_empty=True)
    set_texdir(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                            "game", "Assets", "Textures"))
    r = build_tank(SPECS[key])
    meshes = [o for o in r.children_recursive if o.type == "MESH"]
    print("деталей:", len(meshes),
          "| полигонов:", sum(len(o.data.polygons) for o in meshes))
