#!/usr/bin/env python3
"""Запустить все проверки проекта без Unity и напечатать итог.

    python3 tools/checks/run_all.py

Проверки:
  1. check_csharp.py — синтаксис C# и связи между типами (опечатки в именах полей, переименования);
  2. check_fbx.py    — модели: оси, единицы, схлопнутые детали, габариты, материалы;
  3. assets.py       — наличие всех нужных ассетов (модели, текстуры, скрипты);
  4. ../balance_check.py — таблицы дуэлей по числам из TankSpec.cs;
  5. ../map_preview.py — схема карты (дороги, город, промзона, лес, точки старта, зоны).
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))

# tree-sitter (разбор C#) стоит в окружении Blender-песочницы — берём его, если доступен
VENV_PY = os.path.expanduser("~/.local/bpy-venv/bin/python")
PY = VENV_PY if os.path.exists(VENV_PY) else sys.executable


def run(name, args, cwd=ROOT):
    print("=" * 72)
    print("== %s" % name)
    print("=" * 72)
    r = subprocess.run([PY] + args, cwd=cwd)
    return r.returncode


def main():
    results = []
    results.append(("C#: синтаксис и связи типов", run("C#", [os.path.join(HERE, "check_csharp.py")])))
    results.append(("Модели FBX", run("FBX", [os.path.join(HERE, "check_fbx.py")])))
    assets = os.path.join(HERE, "assets.py")
    if os.path.exists(assets):
        results.append(("Ассеты проекта", run("Ассеты", [assets])))
    results.append(("Баланс", run("Баланс", [os.path.join(ROOT, "tools", "balance_check.py")])))
    results.append(("Карта (схема и точки старта)",
                    run("Карта", [os.path.join(ROOT, "tools", "map_preview.py")])))

    print("=" * 72)
    print("== ИТОГ")
    bad = 0
    for name, code in results:
        print("   %-28s %s" % (name, "проблем нет" if code == 0 else "ЕСТЬ ЗАМЕЧАНИЯ"))
        bad += 1 if code else 0
    print("Проверок: %d, с замечаниями: %d" % (len(results), bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
