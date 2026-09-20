#!/usr/bin/env python3
"""Проверка FBX-моделей без Blender и без Unity: оси, единицы, масштаб, схлопнутые детали.

Зачем: FBX можно испортить молча. Экспортёр держит оси и масштаб единиц в трансформах узлов,
и одна ошибка (перевёрнутая ось, «вмороженный» поворот, масштаб 0.01) проявляется только
при открытии Unity — машина стоит на хвосте, едет боком или выглядит стометровой. Скрипт
читает бинарный FBX напрямую (никаких зависимостей) и проверяет:

  • GlobalSettings: UpAxis = Y (+1), FrontAxis = Z (+1), UnitScaleFactor = 1 — конвенция Unity;
  • габариты каждой детали в мировых координатах (учитываются матрицы узлов!) — деталь,
    схлопнутая в точку, и «гигантская» деталь видны сразу;
  • габарит всей модели и число объектов;
  • имя и материал каждой детали (в Unity материалы сопоставляются по имени).

Запуск:  python3 tools/checks/check_fbx.py                 # все модели игры
         python3 tools/checks/check_fbx.py path/to/x.fbx   # конкретный файл
"""
import glob
import os
import struct
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT = os.path.join(ROOT, "game", "Assets", "Models")


# ---------------------------------------------------------------- чтение FBX
def read_node(f):
    hdr = f.read(13)
    if len(hdr) < 13:
        return None
    end, nprops, _plen, nlen = struct.unpack("<IIIB", hdr)
    if end == 0:
        return None
    name = f.read(nlen).decode("utf-8", "replace")
    props = []
    for _ in range(nprops):
        t = f.read(1)
        if t == b"Y":
            props.append(struct.unpack("<h", f.read(2))[0])
        elif t == b"C":
            props.append(f.read(1)[0])
        elif t == b"I":
            props.append(struct.unpack("<i", f.read(4))[0])
        elif t == b"F":
            props.append(struct.unpack("<f", f.read(4))[0])
        elif t == b"D":
            props.append(struct.unpack("<d", f.read(8))[0])
        elif t == b"L":
            props.append(struct.unpack("<q", f.read(8))[0])
        elif t == b"S":
            ln = struct.unpack("<I", f.read(4))[0]
            props.append(f.read(ln).decode("utf-8", "replace"))
        elif t == b"R":
            ln = struct.unpack("<I", f.read(4))[0]
            props.append(f.read(ln))
        elif t in (b"f", b"d", b"l", b"i", b"b"):
            alen, enc, clen = struct.unpack("<III", f.read(12))
            raw = f.read(clen)
            if enc == 1:
                raw = zlib.decompress(raw)
            fmt = {b"d": "d", b"f": "f", b"i": "i", b"l": "q", b"b": "b"}[t]
            props.append(struct.unpack("<%d%s" % (alen, fmt), raw))
        else:
            raise ValueError("неизвестный тип свойства %r" % t)
    children = []
    if end > 0:
        while f.tell() < end - 13:
            ch = read_node(f)
            if ch is None:
                break
            children.append(ch)
        f.seek(end)
    return (name, props, children)


def walk(node, depth=0, out=None):
    if out is None:
        out = []
    if node is None:
        return out
    out.append((depth, node))
    for c in node[2] or []:
        walk(c, depth + 1, out)
    return out


def parse(path):
    nodes = []
    with open(path, "rb") as f:
        f.read(21)
        f.read(2)
        ver = struct.unpack("<I", f.read(4))[0]
        f.read(4)
        f.seek(27)
        while True:
            n = read_node(f)
            if n is None:
                break
            walk(n, 0, nodes)
    return ver, nodes


# ---------------------------------------------------------------- матрицы
def euler_matrix(rx, ry, rz):
    """XYZ-порядок, как в FBX (градусы)."""
    import math
    cx, sx = math.cos(math.radians(rx)), math.sin(math.radians(rx))
    cy, sy = math.cos(math.radians(ry)), math.sin(math.radians(ry))
    cz, sz = math.cos(math.radians(rz)), math.sin(math.radians(rz))
    return [
        [cy * cz, -cy * sz, sy],
        [cx * sz + sx * sy * cz, cx * cz - sx * sy * sz, -sx * cy],
        [sx * sz - cx * sy * cz, sx * cz + cx * sy * sz, cx * cy],
    ]


def apply(m, v, t):
    return (
        m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2] + t[0],
        m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2] + t[1],
        m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2] + t[2],
    )


def mat_mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def props_of(node):
    """Словарь Properties70 для узла (Lcl Translation и т.п.)."""
    out = {}
    for _d, (name, p, ch) in walk(node):
        if name != "Properties70":
            continue
        for _d2, (pname, pprops, _pch) in walk((name, p, ch)):
            if pname != "P" or not pprops or not isinstance(pprops[0], str):
                continue
            if len(pprops) == 4:                      # скаляр: [имя, тип, 'Integer', значение]
                out[pprops[0]] = pprops[3]
            elif len(pprops) >= 7:                    # вектор: [имя, имя, '', 'A+', x, y, z]
                out[pprops[0]] = pprops[4:7]
            else:
                out[pprops[0]] = pprops[1:]
    return out


# ---------------------------------------------------------------- проверка
def check(path, verbose=True):
    ver, nodes = parse(path)
    problems = []

    # 1. оси и единицы
    axes = {}
    for _d, (name, props, ch) in nodes:
        if name != "GlobalSettings":
            continue
        for _d2, (pname, pprops, _pch) in walk((name, props, ch)):
            if pname == "P" and pprops and isinstance(pprops[0], str):
                axes[pprops[0]] = pprops[3] if len(pprops) == 4 else pprops[1:]
    for _d, (name, props, ch) in [(d, n) for d, n in nodes if n[0] == "GlobalSettings"]:
        for _d2, (pname, pprops, _pch) in walk((name, props, ch)):
            if pname == "P" and len(pprops) >= 5 and isinstance(pprops[0], str):
                axes[pprops[0]] = pprops[4]
    # FBX хранит единицы в сантиметрах: UnitScaleFactor = 100 означает метры, 1 — сантиметры.
    # Blender и Unity сами приводят к метрам, поэтому и мы делим на 100.
    unit_scale = float(axes.get("UnitScaleFactor", 1.0) or 1.0)
    to_meters = unit_scale / 100.0
    if abs(unit_scale - 100.0) < 1e-6:
        to_meters = 1.0                                # файл уже в метрах
    if axes:
        checks = [("UpAxis", 1), ("UpAxisSign", 1), ("FrontAxis", 2), ("FrontAxisSign", 1),
                  ("CoordAxis", 0), ("CoordAxisSign", 1)]
        for key, want in checks:
            got = axes.get(key)
            if got is None:
                continue
            if isinstance(got, (list, tuple)):
                got = got[0] if got else None
            if got is not None and int(got) != want:
                problems.append("ось %s = %s, ожидалось %d (конвенция Unity: Y вверх, +Z вперёд)"
                                % (key, got, want))

    # 2. модели и геометрия
    models = {}
    geo = {}
    for _d, (name, props, ch) in nodes:
        if name == "Model" and len(props) > 1 and isinstance(props[1], str):
            key = props[1].split("\x00")[0]
            models[props[0]] = {"name": key, "props": props_of((name, props, ch))}
        elif name == "Geometry" and len(props) > 1:
            for _d2, (gname, gprops, _gch) in walk((name, props, ch)):
                if gname != "Vertices":
                    continue
                # массив вершин лежит одним свойством-кортежем (иногда — самим списком)
                arr = gprops[0] if len(gprops) == 1 and isinstance(gprops[0], tuple) else gprops
                geo[props[0]] = arr

    # связи: геометрия -> модель (OO), модель -> родитель (OO/OP)
    geo_owner = {}
    model_parent = {}
    for _d, (name, props, ch) in nodes:
        if name != "Connections":
            continue
        for _d2, (ctype, cprops, _cch) in walk((name, props, ch)):
            if ctype != "C" or len(cprops) < 3:
                continue
            kind, a, b = cprops[0], cprops[1], cprops[2]
            if kind == "OO" and a in geo and b in models:
                geo_owner[a] = b
            if kind == "OO" and a in models and b in models:
                model_parent[a] = b
            elif kind == "OP":
                model_parent[a] = None      # ребёнок сцены

    # мировые трансформы моделей: поднимаемся к корню по цепочке родителей
    def world_of(mid, depth=0):
        m = models[mid]["props"]
        t = tuple(float(x) for x in m.get("Lcl Translation", (0.0, 0.0, 0.0)))
        r = tuple(float(x) for x in m.get("Lcl Rotation", (0.0, 0.0, 0.0)))
        ls = tuple(float(x) for x in m.get("Lcl Scaling", (1.0, 1.0, 1.0)))
        M = euler_matrix(*r)
        for i in range(3):
            for j in range(3):
                M[i][j] *= ls[j] if j < len(ls) else 1.0
        parent = model_parent.get(mid) if depth < 12 else None
        if parent in models:
            pm, pt = world_of(parent, depth + 1)
            M = mat_mul(pm, M)
            t = apply(pm, t, pt)
        return M, t

    if not models:
        problems.append("в файле нет объектов Model — модель пустая?")
        return problems, {}

    total_min = [1e9] * 3
    total_max = [-1e9] * 3
    collapsed = []
    big = []
    for mid, info in models.items():
        verts = None
        for gid, owner in geo_owner.items():
            if owner == mid:
                verts = geo.get(gid)
                break
        if not verts:
            continue
        M, t = world_of(mid)
        xs, ys, zs = [], [], []
        for i in range(0, len(verts), 3):
            p = apply(M, (verts[i], verts[i + 1], verts[i + 2]), t)
            xs.append(p[0] * to_meters); ys.append(p[1] * to_meters); zs.append(p[2] * to_meters)
        size = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
        for i, arr in enumerate((xs, ys, zs)):
            total_min[i] = min(total_min[i], min(arr))
            total_max[i] = max(total_max[i], max(arr))
        if max(size) < 0.03:
            collapsed.append((info["name"], size))
        # предел «одной детали»: у техники деталь не может быть длиннее 12 м,
        # у построек (ангар, станция, рельсы) бывают длинные цельные части — до 45 м
        limit = 12.0 if os.sep + "Tanks" + os.sep in path else 45.0
        if max(size) > limit:
            big.append((info["name"], size))

    bbox = tuple(round(total_max[i] - total_min[i], 2) for i in range(3))
    for name, size in collapsed:
        problems.append("деталь %s схлопнута в точку (габарит %.3f×%.3f×%.3f м)"
                        % (name, *size))
    for name, size in big:
        problems.append("деталь %s неправдоподобно большая (%.1f×%.1f×%.1f м)"
                        % (name, *size))
    if max(bbox) > 60.0:
        problems.append("габарит модели %.1f×%.1f×%.1f м — похоже, потерян масштаб"
                        % bbox)

    if verbose:
        print("  %-14s FBX %d, объектов %3d, деталей с геометрией %3d, габарит %.1f×%.1f×%.1f м"
              % (os.path.basename(path), ver, len(models), len(geo_owner), *bbox))
        if axes:
            print("      оси: вверх %s, вперёд %s, единицы %s (в метры ×%g)"
                  % ({0: "X", 1: "Y", 2: "Z"}.get(int(axes.get("UpAxis", 1)), "?"),
                     {0: "X", 1: "Y", 2: "Z"}.get(int(axes.get("FrontAxis", 2)), "?"),
                     axes.get("UnitScaleFactor"), to_meters))
    return problems, bbox


def main():
    args = sys.argv[1:]
    if args:
        files = args
    else:
        files = sorted(glob.glob(os.path.join(DEFAULT, "Tanks", "*.fbx"))) + \
                sorted(glob.glob(os.path.join(DEFAULT, "Props", "*.fbx")))
    if not files:
        sys.exit("не нашёл FBX — проверь путь")
    bad = 0
    for path in files:
        problems, _ = check(path)
        if problems:
            bad += 1
            for p in problems:
                print("   ! " + p)
    print("Проверено файлов: %d, с замечаниями: %d" % (len(files), bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
