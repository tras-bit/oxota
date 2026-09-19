#!/usr/bin/env python3
"""Генерация 3D-ассетов для игры «Samsar»: техника (4 класса) и объекты окружения.

Запуск:
    tools/blender.sh tools/build_models.py tanks     # техника + групповой рендер
    tools/blender.sh tools/build_models.py props     # объекты окружения + рендер
    tools/blender.sh tools/build_models.py all

Результат:
    game/Assets/Models/Tanks/<key>.fbx      (Unity, с эмбедded-текстурами)
    game/Assets/Models/Props/<name>.fbx
    assets/generated/models/*.glb           (превью/веб)
    assets/generated/lineup.png, props.png  (рендеры)
"""
import math
import os
import sys
import time

import bpy
import mathutils

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEXDIR = os.path.join(ROOT, "game", "Assets", "Textures")
GAME_MODELS = os.path.join(ROOT, "game", "Assets", "Models")
GEN = os.path.join(ROOT, "assets", "generated")
GEN_MODELS = os.path.join(GEN, "models")

sys.path.insert(0, os.path.join(ROOT, "tools"))
import tank_lib  # noqa: E402
import props_lib  # noqa: E402


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tank_lib.set_texdir(TEXDIR)


def children_recursive(ob):
    out = [ob]
    for c in ob.children:
        out += children_recursive(c)
    return out


def select_only(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def flatten(root):
    """Запекаем трансформы деталей в геометрию: у каждой детали единичная матрица.

    Экспортёр FBX держит оси и масштаб в трансформах узлов; одна ошибка в узле — и модель
    в Unity стоит на хвосте, едет боком или выглядит стометровой. Если геометрия уже
    в мировых координатах, ломаться нечему. Дети при этом остаются под корнем.
    """
    root.location = (0.0, 0.0, 0.0)
    root.rotation_euler = (0.0, 0.0, 0.0)
    root.scale = (1.0, 1.0, 1.0)
    bpy.context.view_layer.update()
    for ob in children_recursive(root):
        if ob.type != "MESH":
            continue
        mw = ob.matrix_world.copy()
        ob.parent = None
        ob.matrix_world = mw
        select_only([ob])
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        ob.parent = root
        ob.matrix_parent_inverse = mathutils.Matrix.Identity(4)
        ob.matrix_world = mathutils.Matrix.Identity(4)
    bpy.context.view_layer.update()


def face_unity(root):
    """«Вперёд» — в +Z: в Blender модели строятся носом в +Y, в Unity вперёд смотрит +Z.

    Поворот делаем ДО запекания, чтобы он оказался в самой геометрии, а не в матрицах деталей.
    """
    R = mathutils.Matrix.Rotation(math.pi, 4, "Z")
    for ob in children_recursive(root):
        if ob.type == "MESH":
            ob.matrix_world = R @ ob.matrix_world
    bpy.context.view_layer.update()
    flatten(root)


def export_fbx(root_ob, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    select_only(children_recursive(root_ob))   # включает корень: масштаб единиц применяется один раз
    # bake_space_transform=True — иерархия уже плоская (см. flatten/face_unity), поэтому экспорт
    # кладёт геометрию прямо в осях Unity: Y — вверх, +Z — вперёд, единицы метрические.
    kwargs = dict(filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
                  path_mode="AUTO", embed_textures=False, mesh_smooth_type="FACE",
                  axis_forward="-Z", axis_up="Y", bake_space_transform=True)
    try:
        bpy.ops.export_scene.fbx(**kwargs)
    except TypeError:
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, path_mode="AUTO",
                                 embed_textures=False)
    return os.path.getsize(path)


def export_glb(root_ob, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    select_only(children_recursive(root_ob))
    try:
        bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True,
                                  export_apply=True)
    except TypeError:
        bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True)


def setup_world(sun_energy=3.4, bg=1.15):
    w = bpy.data.worlds.new("W")
    bpy.context.scene.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.30, 0.34, 0.40, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = bg
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("SunL", type="SUN"))
    sun.data.energy = sun_energy
    sun.data.angle = math.radians(3)
    sun.rotation_euler = (math.radians(52), 0, math.radians(22))
    bpy.context.collection.objects.link(sun)
    fill = bpy.data.objects.new("Fill", bpy.data.lights.new("FillL", type="AREA"))
    fill.data.energy = 500
    fill.data.size = 8
    fill.location = (9, -8, 8)
    fill.rotation_euler = (math.radians(52), 0, math.radians(-40))
    bpy.context.collection.objects.link(fill)
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, 0))
    ground = bpy.context.object
    ground.name = "Ground"
    ground.data.materials.append(tank_lib.make_material(
        "MAT_GroundGrass", "ground_grass_albedo.jpg", "ground_grass_normal.png",
        "ground_grass_mask.png", metallic=0.0, roughness=0.8))
    return ground


def new_camera(objs, direction=(-1.45, 1.05, 0.62), margin=1.18, lens=45):
    """Свежая камера на нужный ракурс: старые камеры и цели удаляются, сцена обновляется."""
    for ob in list(bpy.data.objects):
        if ob.name.startswith("Cam") or ob.name.startswith("TrackTo"):
            bpy.data.objects.remove(ob, do_unlink=True)
    bpy.context.view_layer.update()
    return render_camera(objs, direction, margin, lens)


def render_camera(objs, direction=(-1.45, 1.05, 0.62), margin=1.18, lens=45):
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = lens
    cam = bpy.data.objects.new("Cam", cam_data)
    bpy.context.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    pts = []
    for ob in objs:
        if ob.type != "MESH":
            continue
        for c in ob.bound_box:
            pts.append(ob.matrix_world @ mathutils.Vector(c))
    center = sum(pts, mathutils.Vector()) / len(pts)
    radius = max((p - center).length for p in pts)
    fov = 2 * math.atan(cam.data.sensor_width / (2 * cam.data.lens))
    dist = radius / math.tan(fov / 2) * margin
    cam.location = center + mathutils.Vector(direction).normalized() * dist
    tc = bpy.data.objects.new("TrackTo", None)
    tc.location = center
    bpy.context.collection.objects.link(tc)
    trk = cam.constraints.new("TRACK_TO")
    trk.target = tc
    trk.track_axis = "TRACK_NEGATIVE_Z"
    trk.up_axis = "UP_Y"
    return cam


def render(path, res=(1600, 900), samples=64):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = res
    try:
        sc.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        pass
    os.makedirs(os.path.dirname(path), exist_ok=True)
    sc.render.filepath = path
    t = time.time()
    bpy.ops.render.render(write_still=True)
    print("   рендер %.0f сек -> %s" % (time.time() - t, path))


def verify(paths):
    """QA: импортировать выгруженные FBX в чистую сцену и замерить габариты."""
    print("== Проверка импортом ==")
    for path in paths:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        try:
            bpy.ops.import_scene.fbx(filepath=path)
        except Exception as e:
            print("   !! ошибка импорта %s: %s" % (os.path.basename(path), e))
            continue
        mesh = [o for o in bpy.data.objects if o.type == "MESH"]
        pts = []
        for ob in mesh:
            for c in ob.bound_box:
                pts.append(ob.matrix_world @ mathutils.Vector(c))
        if not pts:
            print("   !! %s: нет геометрии" % os.path.basename(path))
            continue
        xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
        tris = sum(len(o.data.polygons) for o in mesh)
        tiny = 0
        for ob in mesh:
            mn = mathutils.Vector((1e9, 1e9, 1e9)); mx = -mn.copy()
            for v in ob.data.vertices:
                w = ob.matrix_world @ v.co
                for i in range(3):
                    mn[i] = min(mn[i], w[i]); mx[i] = max(mx[i], w[i])
            if max((mx - mn).x, (mx - mn).y, (mx - mn).z) < 0.2:
                tiny += 1
        mats = {m.name for o in mesh for m in o.data.materials if m}
        print("   %-10s %3d объектов, %5d полигонов, габарит %.1f×%.1f×%.1f м, материалов: %d, схлопнутых деталей: %d"
              % (os.path.basename(path), len(mesh), tris,
                 max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs), len(mats), tiny))


def build_tanks():
    reset()
    tank_lib.set_texdir(TEXDIR)
    setup_world()
    roster = []
    slots = [(-6.3, 4.4), (6.3, 4.4), (-6.3, -6.6), (6.3, -6.6)]   # 2×2 для рендера
    for i, (key, spec) in enumerate(tank_lib.SPECS.items()):
        r = tank_lib.build_tank(spec)
        face_unity(r)                                     # ориентация — при корне в нуле
        roster.append(r)
        fbx = os.path.join(GAME_MODELS, "Tanks", spec.key + ".fbx")
        kb = export_fbx(r, fbx) / 1024
        export_glb(r, os.path.join(GEN_MODELS, spec.key + ".glb"))
        r.location = (slots[i][0], slots[i][1], 0.0)       # смещение/поворот — только для превью-рендера
        r.rotation_euler.z = math.radians(24)
        parts = len([o for o in children_recursive(r) if o.type == "MESH"])
        print("   %-14s деталей: %3d, FBX: %6.0f КБ" % (spec.title, parts, kb))
    allmesh = [o for r in roster for o in children_recursive(r) if o.type == "MESH"]
    new_camera(allmesh, direction=(0.60, -1.55, 0.30), margin=0.97, lens=62)
    render(os.path.join(GEN, "lineup.png"), res=(1600, 900), samples=48)
    for i, r in enumerate(roster):                        # крупный план СТ: убрать остальных
        if i != 1:
            r.location.y -= 400.0
    mtmesh = [o for o in children_recursive(roster[1]) if o.type == "MESH"]
    new_camera(mtmesh, direction=(1.35, -1.10, 0.42), margin=0.92, lens=70)
    render(os.path.join(GEN, "tank_detail.png"), res=(1600, 900), samples=42)

    for i, r in enumerate(roster):                        # плотный 2×2: все четыре крупно
        r.location = ((i % 2) * 9.6 - 4.8, 3.9 if i < 2 else -3.9, 0.0)
    new_camera(allmesh, direction=(0.62, -1.50, 0.34), margin=0.99, lens=86)
    render(os.path.join(GEN, "tank_roster.png"), res=(1600, 900), samples=42)
    verify([os.path.join(GAME_MODELS, "Tanks", s.key + ".fbx") for s in tank_lib.SPECS.values()])


def build_props():
    reset()
    tank_lib.set_texdir(TEXDIR)
    M = props_lib.mats()
    made = []
    for name, builder in props_lib.BUILDERS.items():
        r = builder(M)
        face_unity(r)
        select_only(children_recursive(r))
        fbx = os.path.join(GAME_MODELS, "Props", name + ".fbx")
        kb = export_fbx(r, fbx) / 1024
        export_glb(r, os.path.join(GEN_MODELS, "props", name + ".glb"))
        parts = len([o for o in children_recursive(r) if o.type == "MESH"])
        print("   %-14s деталей: %3d, FBX: %6.0f КБ" % (name, parts, kb))
        # в сцену для рендера кладём копию смещённой, оригинал убираем
        for ob in children_recursive(r):
            ob.location.y -= 1000.0
        made.append(r)
    setup_world(bg=1.2)
    shown = []
    for i, r in enumerate(made):
        gx, gy = (i % 4) * 24.0 - 36.0, -(i // 4) * 26.0
        for ob in children_recursive(r):
            ob.location.y += 1000.0 + gy
            ob.location.x += gx
            shown.append(ob)
    new_camera([o for o in shown if o.type == "MESH"],
               direction=(-0.9, 1.1, 0.55), margin=1.02, lens=40)
    render(os.path.join(GEN, "props.png"), res=(1600, 900), samples=48)
    verify([os.path.join(GAME_MODELS, "Props", n + ".fbx") for n in props_lib.BUILDERS])


def main():
    what = sys.argv[-1] if sys.argv[-1] in ("tanks", "props", "all") else "all"
    t0 = time.time()
    if what in ("tanks", "all"):
        print("== Техника ==")
        build_tanks()
    if what in ("props", "all"):
        print("== Объекты окружения ==")
        build_props()
    print("Готово за %.0f сек" % (time.time() - t0))


if __name__ == "__main__":
    main()
