#!/usr/bin/env bash
# Запуск Python-скрипта внутри Blender (headless).
#   tools/blender.sh tools/tank_demo.py
#   tools/blender.sh -c "import bpy; print(bpy.app.version_string)"
set -euo pipefail
VENV="${HOME}/.local/bpy-venv"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
if [ ! -x "${VENV}/bin/python" ] || [ ! -f "${HOME}/.local/blender-stubs/libstubs.so" ]; then
  echo "[blender.sh] окружение Blender не найдено — ставлю автоматически (один раз)..." >&2
  bash "${ROOT}/tools/install_blender.sh" >&2 || exit 1
fi
export LD_LIBRARY_PATH="${HOME}/.local/blender-stubs:${VENV}/lib/python3.11/site-packages/bpy/lib:${LD_LIBRARY_PATH:-}"
if [ "${1:-}" = "-c" ]; then
  exec "${VENV}/bin/python" -c "$2"
fi
exec "${VENV}/bin/python" "$@"
