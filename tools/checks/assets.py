#!/usr/bin/env python3
"""Проверка комплектности ассетов проекта: что Unity увидит при сборке сцен.

Частая причина «игра не запускается» — не хватает одного файла: модели техники, текстуры
или сцены. Editor-скрипты в этом случае молча создают пустой префаб, и в бою оказываются
невидимые танки. Скрипт проверяет, что на месте всё, что эти скрипты загружают по именам:

  • 4 модели техники в game/Assets/Models/Tanks;
  • 13 объектов окружения в game/Assets/Models/Props (имена совпадают со списком
    TankAssetBuilder.LoadPropPrefabs);
  • наборы текстур, которые просит TankAssetBuilder (по каждому: albedo, normal, mask);
  • сцены Main и Battle;
  • точки входа: скрипты, которые Unity обязана скомпилировать (перечислены ниже).

Запуск:  python3 tools/checks/assets.py
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
GAME = os.path.join(ROOT, "game")
TANKS = os.path.join(GAME, "Assets", "Models", "Tanks")
PROPS = os.path.join(GAME, "Assets", "Models", "Props")
TEX = os.path.join(GAME, "Assets", "Textures")
SCENES = os.path.join(GAME, "Assets", "Scenes")
SCRIPTS = os.path.join(GAME, "Assets", "Scripts")

TANK_KEYS = ["lt", "mt", "ht", "td"]
PROP_KEYS = ["house", "house_small", "warehouse", "station", "tower", "tree_spruce",
             "tree_birch", "fence", "container", "block", "rubble", "rail_segment", "bale"]
# листва в игре использует набор травы (см. MAT_Leaf в TankAssetBuilder) — своего набора нет
TEX_SETS = ["steel_olive", "steel_sand", "steel_grey", "steel_green", "metal_rusty",
            "metal_dark", "rubber", "concrete", "wood", "optics", "ground_grass",
            "ground_dirt", "ground_rock", "ground_asphalt"]
REQUIRED_FILES = [
    os.path.join(SCRIPTS, "Core", "TankSpec.cs"),
    os.path.join(SCRIPTS, "Core", "GameSession.cs"),
    os.path.join(SCRIPTS, "Core", "TankLibrary.cs"),
    os.path.join(SCRIPTS, "Battle", "BattleManager.cs"),
    os.path.join(SCRIPTS, "Battle", "TankRig.cs"),
    os.path.join(SCRIPTS, "UI", "AngarUI.cs"),
    os.path.join(SCRIPTS, "UI", "HUD.cs"),
    os.path.join(SCRIPTS, "Progression", "Profile.cs"),
    os.path.join(GAME, "Assets", "Editor", "SamsarSetup.cs"),
    os.path.join(GAME, "Assets", "Editor", "TankAssetBuilder.cs"),
    os.path.join(GAME, "Assets", "Editor", "TerrainBuilder.cs"),
    os.path.join(GAME, "Assets", "Editor", "BattleSceneBuilder.cs"),
    os.path.join(GAME, "Assets", "Editor", "MainSceneBuilder.cs"),
    os.path.join(GAME, "ProjectSettings", "ProjectVersion.txt"),
    os.path.join(GAME, "Packages", "manifest.json"),
]


def main():
    problems = []
    print("Проверка ассетов проекта (%s)" % os.path.relpath(GAME, ROOT))

    for key in TANK_KEYS:
        f = os.path.join(TANKS, key + ".fbx")
        if not os.path.isfile(f):
            problems.append("нет модели техники: Assets/Models/Tanks/%s.fbx" % key)
    for key in PROP_KEYS:
        f = os.path.join(PROPS, key + ".fbx")
        if not os.path.isfile(f):
            problems.append("нет объекта окружения: Assets/Models/Props/%s.fbx "
                            "(имя должно совпадать со списком в TankAssetBuilder.LoadPropPrefabs)"
                            % key)

    for s in TEX_SETS:
        for suffix, ext in (("albedo", ".jpg"), ("normal", ".png"), ("mask", ".png")):
            f = os.path.join(TEX, "%s_%s%s" % (s, suffix, ext))
            if not os.path.isfile(f):
                problems.append("нет текстуры: Assets/Textures/%s_%s%s" % (s, suffix, ext))

    hints = []
    for scene in ("Main", "Battle"):
        f = os.path.join(SCENES, scene + ".unity")
        if not os.path.isfile(f):
            hints.append("сцена Assets/Scenes/%s.unity появится после «Samsar → 0. СОБРАТЬ ВСЁ» в Unity"
                         % scene)

    for f in REQUIRED_FILES:
        if not os.path.isfile(f):
            problems.append("нет файла: %s" % os.path.relpath(f, ROOT))

    # ProjectVersion: версия движка должна совпадать с той, что просил игрок (2022.3.62f2)
    pv = os.path.join(GAME, "ProjectSettings", "ProjectVersion.txt")
    if os.path.isfile(pv):
        txt = open(pv, encoding="utf-8").read()
        m = re.search(r"m_EditorVersion:\s*(\S+)", txt)
        if m and not m.group(1).startswith("2022.3"):
            problems.append("версия движка в проекте %s, а нужна 2022.3.x" % m.group(1))

    counts = {
        "модели техники": len([f for f in os.listdir(TANKS)]) if os.path.isdir(TANKS) else 0,
        "объекты окружения": len([f for f in os.listdir(PROPS)]) if os.path.isdir(PROPS) else 0,
        "текстуры": len([f for f in os.listdir(TEX)]) if os.path.isdir(TEX) else 0,
        "C#-файлы": 0,
    }
    for d in (SCRIPTS, os.path.join(GAME, "Assets", "Editor")):
        if os.path.isdir(d):
            for root_dir, _dirs, files_ in os.walk(d):
                counts["C#-файлы"] += sum(1 for f in files_ if f.endswith(".cs"))
    print("   файлов: " + ", ".join("%s %d" % (k, v) for k, v in counts.items()))
    for h in hints:
        print("   · " + h)

    if problems:
        print("Замечания (%d):" % len(problems))
        for p in problems:
            print("   ! " + p)
        return 1
    print("Все нужные ассеты на месте.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
