#!/usr/bin/env python3
"""Демо-модель танка: собирается из примитивов, рендерится и экспортируется в GLB.

Проверяет весь пайплайн: скрипт Blender -> модель -> рендер(превью) -> GLB для игры.

Запуск:
    tools/blender.sh tools/tank_demo.py
Результат:
    assets/generated/tank_demo.glb        — модель для движка
    assets/generated/tank_demo_preview.png — превью-рендер
"""
import math
import os
import sys
import time

import bpy
import mathutils

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "assets", "generated")


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, color, roughness=0.7, metallic=0.35):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    return m


def parent_keep_transform(ob, parent):
    """Привязать объект к родителю, сохранив мировое положение и размер."""
    mw = ob.matrix_world.copy()
    ob.parent = parent
    ob.matrix_parent_inverse = parent.matrix_world.inverted()
    ob.matrix_world = mw


def box(name, size, loc, rot=(0, 0, 0), material=None, parent=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    ob = bpy.context.object
    ob.name = name
    ob.scale = (size[0], size[1], size[2])
    ob.rotation_euler = rot
    if material:
        ob.data.materials.append(material)
    if parent:
        parent_keep_transform(ob, parent)
    return ob


def cyl(name, radius, depth, loc, rot=(0, 0, 0), verts=16, material=None, parent=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc)
    ob = bpy.context.object
    ob.name = name
    ob.rotation_euler = rot
    if material:
        ob.data.materials.append(material)
    if parent:
        parent_keep_transform(ob, parent)
    return ob


def build_tank():
    """Корпус, башня, орудие, гусеницы и катки. Ориентация: +Y — вперёд, Z — вверх."""
    steel = mat("Steel", (0.26, 0.29, 0.24))
    dark = mat("TrackRubber", (0.055, 0.05, 0.045), roughness=0.95, metallic=0.05)
    glass = mat("Optics", (0.08, 0.11, 0.13), roughness=0.15, metallic=0.8)

    hull = box("Hull", (2.2, 4.0, 0.75), (0, 0, 0.62), material=steel)
    # лобовой наклонный лист
    box("UpperGlacis", (2.15, 1.35, 0.95), (0, 1.45, 0.95), rot=(math.radians(-40), 0, 0),
        material=steel, parent=hull)
    box("Rear", (2.1, 1.0, 0.7), (0, -1.75, 0.75), material=steel, parent=hull)

    for side in (-1, 1):
        x = side * 1.18
        box("Track" + ("L" if side < 0 else "R"), (0.62, 4.3, 0.95), (x, 0, 0.48), material=dark)
        for i in range(6):
            y = -1.6 + i * 0.64
            cyl("Wheel", 0.34, 0.5, (x, y, 0.42), rot=(0, math.radians(90), 0),
                verts=14, material=dark)
    box("FenderL", (0.66, 4.2, 0.14), (-1.18, 0, 1.0), material=steel)
    box("FenderR", (0.66, 4.2, 0.14), (1.18, 0, 1.0), material=steel)

    # башня
    turret = cyl("Turret", 1.05, 0.62, (0, -0.25, 1.5), verts=12, material=steel)
    box("Mantlet", (0.95, 0.5, 0.5), (0, 0.95, 1.52), material=steel, parent=turret)
    cyl("GunBarrel", 0.115, 2.7, (0, 2.1, 1.52), rot=(math.radians(90), 0, 0),
        verts=12, material=steel, parent=turret)
    box("Cupola", (0.62, 0.62, 0.3), (0, -0.62, 1.92), material=steel, parent=turret)
    # ствол-пулемёт и оптика
    cyl("MG", 0.06, 0.9, (0, 0.9, 1.95), rot=(math.radians(90), 0, 0), verts=8,
        material=dark, parent=turret)
    box("Optics", (0.3, 0.22, 0.2), (-0.42, 0.62, 1.85), material=glass, parent=turret)
    # антенны
    cyl("Antenna", 0.025, 1.5, (-0.7, -0.85, 2.2), rot=(math.radians(-8), 0, 0), verts=6,
        material=dark, parent=turret)

    return [o for o in bpy.data.objects if o.type == "MESH"]


def setup_lighting_camera(target=(0, 0.4, 1.1)):
    world = bpy.data.worlds.new("W")
    bpy.context.scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.30, 0.34, 0.40, 1)
    bg.inputs[1].default_value = 1.15

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("SunL", type="SUN"))
    sun.data.energy = 3.4
    sun.rotation_euler = (math.radians(50), 0, math.radians(-125))
    bpy.context.collection.objects.link(sun)

    fill = bpy.data.objects.new("Fill", bpy.data.lights.new("FillL", type="AREA"))
    fill.data.energy = 400
    fill.data.size = 6
    fill.location = (7, 6, 6)
    fill.rotation_euler = (math.radians(52), 0, math.radians(40))
    bpy.context.collection.objects.link(fill)

    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 45
    cam = bpy.data.objects.new("Cam", cam_data)
    bpy.context.collection.objects.link(cam)
    bpy.context.scene.camera = cam

    # земля
    bpy.ops.mesh.primitive_plane_add(size=120, location=(0, 0, 0))
    ground = bpy.context.object
    ground.name = "Ground"
    ground.data.materials.append(mat("Ground", (0.12, 0.13, 0.1), roughness=1.0, metallic=0.0))
    return cam


def frame_camera(cam, objects, direction=(-1.45, 1.05, 0.62), margin=1.22):
    """Автоматически подобрать положение камеры по габаритам техники."""
    pts = []
    for ob in objects:
        for corner in ob.bound_box:
            pts.append(ob.matrix_world @ mathutils.Vector(corner))
    center = sum(pts, mathutils.Vector()) / len(pts)
    radius = max((p - center).length for p in pts)
    fov = 2 * math.atan(cam.data.sensor_width / (2 * cam.data.lens))
    dist = radius / math.tan(fov / 2) * margin
    d = mathutils.Vector(direction).normalized()
    cam.location = center + d * dist

    tc = bpy.data.objects.new("TrackTo", None)
    tc.location = center
    bpy.context.collection.objects.link(tc)
    trk = cam.constraints.new("TRACK_TO")
    trk.target = tc
    trk.track_axis = "TRACK_NEGATIVE_Z"
    trk.up_axis = "UP_Y"
    print("Габарит (радиус): %.2f м, дистанция камеры: %.2f м" % (radius, dist))
    return center


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    reset_scene()
    t0 = time.time()
    parts = build_tank()
    print("Деталей собрано:", len(parts))
    setup_lighting_camera()
    frame_camera(bpy.context.scene.camera, parts)

    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 64
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    sc.render.film_transparent = False
    try:
        sc.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        pass

    # сохранить сцену
    blend_path = os.path.join(OUT_DIR, "tank_demo.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)

    # превью
    png = os.path.join(OUT_DIR, "tank_demo_preview.png")
    sc.render.filepath = png
    t = time.time()
    bpy.ops.render.render(write_still=True)
    print("Рендер: %.1f сек -> %s" % (time.time() - t, png))

    # экспорт только техники (без земли/света/камеры)
    for ob in bpy.data.objects:
        if ob.name in ("Ground", "Sun", "Fill", "Cam", "TrackTo"):
            ob.hide_render = True
    glb = os.path.join(OUT_DIR, "tank_demo.glb")
    bpy.ops.export_scene.gltf(
        filepath=glb,
        export_format="GLB",
        use_selection=False,
        export_apply=True,
        export_cameras=False,
        export_lights=False,
    )
    print("GLB: %s (%.1f КБ)" % (glb, os.path.getsize(glb) / 1024))
    print("Всего: %.1f сек" % (time.time() - t0))


if __name__ == "__main__":
    main()
