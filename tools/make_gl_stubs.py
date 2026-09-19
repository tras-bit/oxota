#!/usr/bin/env python3
"""Генерация «заглушек» для отсутствующих системных библиотек X11/OpenGL.

Blender (модуль bpy) для Linux собран с зависимостями от libGL, libXrender, libXi и т.п.
В этой песочнице (минимальный Debian без GUI) их нет, и `import bpy` падает.
Скрипт находит все неразрешённые символы в библиотеках Blender, которых нет ни в одной
системной библиотеке, и собирает один маленький shim `libstubs.so`, доступный под именами
недостающих библиотек. Для headless-работы (моделирование, рендер Cycles на CPU, экспорт
GLB/FBX) этого достаточно — к GL-контексту Blender не обращается.

Запуск: python3 tools/make_gl_stubs.py
"""
import glob
import json
import os
import subprocess
import sys
import collections
import re

HOME = os.path.expanduser("~")
VENV = os.path.join(HOME, ".local", "bpy-venv")
OUT = os.path.join(HOME, ".local", "blender-stubs")
BPY = None


def find_bpy():
    global BPY
    hits = glob.glob(os.path.join(VENV, "lib", "python3.*", "site-packages", "bpy"))
    if not hits:
        sys.exit("bpy не найден в venv. Сначала запусти tools/install_blender.sh")
    BPY = hits[0]


def syms(path, defined=True):
    flag = "--defined-only" if defined else "--undefined-only"
    r = subprocess.run(["nm", "-D", flag, path], capture_output=True, text=True)
    out = set()
    for line in r.stdout.splitlines():
        p = line.split()
        if p:
            out.add(p[-1].split("@")[0])
    return out


def dyn_sym_types(paths):
    """Тип (FUNC/OBJECT/NOTYPE) и версия для каждого неопределённого символа."""
    types, vers = {}, {}
    for p in paths:
        r = subprocess.run(["readelf", "--dyn-syms", "--wide", p], capture_output=True, text=True)
        for line in r.stdout.splitlines():
            if " UND " not in line:
                continue
            f = line.split()
            if len(f) < 8:
                continue
            name = f[7]
            base, ver = (name.split("@", 1) + [None])[:2] if "@" in name else (name, None)
            types.setdefault(base, f[3])
            if ver:
                vers.setdefault(base, ver)
    return types, vers


def main():
    find_bpy()
    os.makedirs(OUT, exist_ok=True)
    bpy_so = sorted(glob.glob(os.path.join(BPY, "**", "*.so*"), recursive=True))

    # собрать все символы, которые вообще есть в системе/питоне
    scan_dirs = ["/lib", "/usr/lib", "/usr/local/lib", os.path.join(VENV, "lib")]
    sys_so = []
    for d in scan_dirs:
        for root, _, files in os.walk(d):
            for f in files:
                if ".so" in f and not f.endswith(".py"):
                    sys_so.append(os.path.join(root, f))
    provided = set()
    for p in sys_so:
        provided |= syms(p)
    for p in bpy_so:
        provided |= syms(p)

    needed = set()
    for p in bpy_so:
        needed |= {s for s in syms(p, defined=False) if not s.startswith("Py")} - provided
    if not needed:
        print("Все символы разрешаются — заглушки не нужны.")
        return

    types, vers = dyn_sym_types(bpy_so)
    nodes = collections.defaultdict(lambda: {"f": [], "o": []})
    for s in sorted(needed):
        node = vers.get(s) or "BLENDER_STUB_1.0"
        nodes[node]["o" if types.get(s) == "OBJECT" else "f"].append(s)

    body, vmap, vscript = [], [], []
    for node, g in nodes.items():
        for kind, sym in (("f", g["f"]), ("o", g["o"])):
            for s in sym:
                real = s + "__stub"
                if kind == "f":
                    body.append("void %s(void) {}" % real)
                else:
                    body.append("unsigned char %s[256] = {0};" % real)
                vmap.append('__asm__(".symver %s,%s@@%s");' % (real, s, node))
        all_syms = g["f"] + g["o"]
        if all_syms:
            vscript.append("%s {\n  global: %s;\n};" % (node, "; ".join(all_syms)))

    csrc, cmap = "/tmp/_stub_syms.c", "/tmp/_stub_syms.map"
    open(csrc, "w").write("#include <stddef.h>\n" + "\n".join(body + vmap) + "\n")
    open(cmap, "w").write("\n".join(vscript) + "\n")

    lib = os.path.join(OUT, "libstubs.so")
    r = subprocess.run(["gcc", "-shared", "-fPIC", "-o", lib, csrc,
                        "-Wl,--version-script=" + cmap], capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit("gcc: " + r.stderr)

    # Симлинки под имена недостающих библиотек + самопроверка импортом bpy:
    # если каких-то .so всё ещё нет, линкуем их и повторяем (до 25 итераций).
    guard = {"libc.so.6", "libm.so.6", "libdl.so.2", "libpthread.so.0", "librt.so.1",
             "libgcc_s.so.1", "libstdc++.so.6", "ld-linux-x86-64.so.2", "libz.so.1"}
    linked = []
    for attempt in range(25):
        env = dict(os.environ)
        env["LD_LIBRARY_PATH"] = OUT + ":" + os.path.join(VENV, "lib", "python3.11",
                                                          "site-packages", "bpy", "lib")
        r = subprocess.run([os.path.join(VENV, "bin", "python"), "-c", "import bpy"],
                           capture_output=True, text=True, env=env)
        if r.returncode == 0:
            print("Проверка импорта bpy: OK (итераций: %d)" % attempt)
            break
        err = r.stderr
        name = None
        if "cannot open shared object file" in err:
            m = re.search(r"([\w\.\+\-]+\.so[\w\.]*): cannot open shared object file", err)
            if m:
                name = m.group(1)
        elif "undefined symbol:" in err:
            sym = err.split("undefined symbol:")[1].split()[0]
            print("!! незакрытый символ:", sym)
            break
        if not name:
            print("Импорт падает по другой причине:\n", err[-1500:])
            break
        if name in guard:
            print("!! не хватает системной библиотеки:", name)
            break
        link = os.path.join(OUT, name)
        if os.path.lexists(link):
            os.remove(link)
        os.symlink("libstubs.so", link)
        linked.append(name)
        print("shim:", name, "-> libstubs.so")
    json.dump(sorted(needed), open(os.path.join(OUT, "symbols.json"), "w"), indent=0)
    print("Заглушено символов: %d, библиотек-заглушек: %d" % (len(needed), len(linked)))


if __name__ == "__main__":
    main()
