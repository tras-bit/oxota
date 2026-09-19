#!/usr/bin/env bash
# Установка Blender (headless, Python-модуль bpy) для работы с моделями техники.
#
# В песочнице нет GUI и нет системных libGL/libX11, поэтому:
#   1) bpy ставится в отдельный venv (~/.local/bpy-venv) — вне репозитория;
#   2) отсутствующие X11/GL-библиотеки замещаются shim'ом (tools/make_gl_stubs.py);
#   3) проверяется импорт Blender, рендер Cycles на CPU и экспорт в GLB.
#
# После установки запускать скрипты так:  tools/blender.sh tools/tank_demo.py
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VENV="${HOME}/.local/bpy-venv"
BPY_VERSION="${BPY_VERSION:-4.5.14}"   # Blender 4.5 LTS

echo "== 1/3. Виртуальное окружение и bpy ${BPY_VERSION}"
if [ ! -x "${VENV}/bin/python" ]; then
  python3 -m venv "$VENV"
fi
"${VENV}/bin/pip" install --quiet --upgrade pip wheel
"${VENV}/bin/pip" install --no-cache-dir "bpy==${BPY_VERSION}"

echo "== 2/3. Заглушки для отсутствующих системных библиотек"
python3 "${ROOT}/tools/make_gl_stubs.py"

echo "== 3/3. Проверка: импорт, рендер Cycles (CPU), экспорт GLB"
export LD_LIBRARY_PATH="${HOME}/.local/blender-stubs:${VENV}/lib/python3.11/site-packages/bpy/lib:${LD_LIBRARY_PATH:-}"
"${VENV}/bin/python" - <<'PY'
import bpy, time, os
print("Blender:", bpy.app.version_string)
os.makedirs("/tmp/blender_check", exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.mesh.primitive_cube_add(size=1)
sc = bpy.context.scene
sc.render.engine = "CYCLES"
sc.cycles.device = "CPU"
sc.cycles.samples = 8
sc.render.resolution_x, sc.render.resolution_y = 160, 90
sc.render.filepath = "/tmp/blender_check/check.png"
t = time.time()
bpy.ops.render.render(write_still=True)
bpy.ops.export_scene.gltf(filepath="/tmp/blender_check/check.glb", export_format="GLB")
print("Рендер + экспорт GLB: OK (%.1f сек)" % (time.time() - t))
PY

echo
echo "Готово. Blender установлен: ${VENV}"
echo "Запуск скриптов:  tools/blender.sh tools/tank_demo.py"
