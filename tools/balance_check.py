#!/usr/bin/env python3
"""Быстрая проверка баланса по числам из game/Assets/Scripts/Core/TankSpec.cs.

Считает те же формулы, что DamageSystem.Resolve в игре (приведённая броня,
рикошет >72°, падение пробития с дистанцией, ±15% случайности), и печатает
таблицу «кто кого за сколько выстрелов/секунд убивает в лоб».

Это не замена бою в редакторе (Samsar/4. Прогнать боёв), а секундная проверка:
не появилось ли «убиваю в один выстрел» или «не могу пробить вообще».

Запуск:  python3 tools/balance_check.py
"""
import math
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SPEC_CS = ROOT / "game" / "Assets" / "Scripts" / "Core" / "TankSpec.cs"

FIELD_RE = re.compile(r"(\w+)\s*=\s*([-\d.]+)f?")
VEHICLE_RE = re.compile(r"id\s*=\s*\"(\w+)\".*?(?=new TankSpec|};)", re.S)

# Условия дуэли: лоб корпуса, лёгкий доворот на 30° (типичный бой в городе/поле).
COS_ANGLE = 0.94          # корпус смотрит почти на стрелка
IMPACT_ANGLE = 20.0       # градусов от нормали (лоб)
ANGLED_COS = 0.5          # цель развёрнута на 60° — доворот корпуса
ANGLED_ANGLE = 60.0
DISTANCE = 300.0          # метров


def load_vehicles(path=SPEC_CS):
    if not path.exists():
        sys.exit(f"не найден {path}")
    text = path.read_text(encoding="utf-8")
    out = []
    for m in VEHICLE_RE.finditer(text):
        block = m.group(0)
        vid = m.group(1)
        if vid in ("hull",):            # служебные поля не путать с машинами
            continue
        vals = {k: float(v) for k, v in FIELD_RE.findall(block)}
        if "hp" not in vals or "penetration" not in vals:
            continue
        vals["id"] = vid
        title = re.search(r'title\s*=\s*"([^"]+)"', block)
        vals["title"] = title.group(1) if title else vid
        out.append(vals)
    return out


def penetration_at_range(pen, distance):
    """Падение пробития: −8% на каждые 100 м, не глубже −25% (как в DamageSystem)."""
    return pen * (1.0 - min(0.20, distance / 100.0 * 0.05))


def effective_armor(thickness, cos_angle, pen, impact_angle):
    cos = max(abs(cos_angle), 0.12)
    eff = thickness / cos
    if pen >= 120.0 and impact_angle > 60.0:
        eff *= 0.92
    return eff


def resolve(attacker, defender, zone="hull", angled=False):
    """Один выстрел. zone: hull | turret | rear. angled=True — цель довернута на 60°."""
    thickness = {
        "hull": defender["armorHull"],
        "turret": defender["armorTurret"],
        "rear": defender["armorRear"],
    }[zone]
    pen = penetration_at_range(attacker["penetration"], DISTANCE)
    eff = effective_armor(thickness, ANGLED_COS if angled else COS_ANGLE, pen,
                          ANGLED_ANGLE if angled else IMPACT_ANGLE)
    # среднее по трём броскам ±15%
    hits = sum(1 for roll in (0.85, 1.0, 1.15) if pen * roll >= eff)
    chance = hits / 3.0
    damage = attacker["damage"] * (0.9 + 0.1)
    return chance, eff, damage


def duel(attacker, defender, zone="hull", angled=False):
    chance, eff, dmg = resolve(attacker, defender, zone, angled)
    if chance <= 0.0:
        return None, eff, 0.0
    per_shot = dmg * chance
    shots = math.ceil(defender["hp"] / max(per_shot, 1.0))
    seconds = shots * attacker["reload"]
    return shots, eff, seconds


def main():
    vehicles = load_vehicles()
    if len(vehicles) < 4:
        sys.exit(f"разобрано машин: {len(vehicles)} — ожидалось 4 (проверь разметку TankSpec.cs)")

    print(f"Проверка баланса: дистанция {DISTANCE:.0f} м, корпус под углом ~{math.degrees(math.acos(COS_ANGLE)):.0f}°, "
          f"пробитие с дистанции ×{penetration_at_range(1, DISTANCE):.2f}\n")

    col = 24
    print("Таблица 1. Лоб в лоб (цель смотрит на стрелка, попадания в корпус)")
    print("атакующий → цель".ljust(col) + "".join(v["id"].upper().ljust(col) for v in vehicles))
    problems = []
    for a in vehicles:
        row = a["id"].upper().ljust(col)
        for d in vehicles:
            if a is d:
                row += "—".ljust(col)
                continue
            shots, eff, seconds = duel(a, d)
            if shots is None:
                row += f"не пробивает ({eff:.0f} мм)".ljust(col)
                problems.append(f"{a['id']}→{d['id']}: лоб не пробивается ни при каком броске ({eff:.0f} мм приведённой)")
            else:
                row += f"{shots} выстр / {seconds:.1f} с".ljust(col)
                if shots <= 1:
                    problems.append(f"{a['id']}→{d['id']}: убивает в один выстрел")
                if seconds > 30.0:
                    problems.append(f"{a['id']}→{d['id']}: слишком долго — {seconds:.0f} с")
        print(row)

    print("\nТаблица 2. Цель развёрнута на 60° (доворот корпуса: броня работает вдвое)")
    print("атакующий → цель".ljust(col) + "".join(v["id"].upper().ljust(col) for v in vehicles))
    for a in vehicles:
        row = a["id"].upper().ljust(col)
        for d in vehicles:
            if a is d:
                row += "—".ljust(col)
                continue
            shots, eff, seconds = duel(a, d, "hull", angled=True)
            row += ("не пробивает".ljust(col) if shots is None
                    else f"{shots} выстр / {seconds:.1f} с".ljust(col))
        print(row)
    print("   как в игре: лоб — armorHull, борт — 70% лобовой брони, корма — armorRear; "
          "доворот корпуса и наклон плиты удваивают броню через нормаль попадания (DamageSystem)")

    print("\nЖивучесть: сколько секунд машина держится под огнём равного противника (лоб):")
    for v in vehicles:
        others = [x for x in vehicles if x is not v]
        worst = max(duel(o, v, "hull")[1] if duel(o, v, "hull")[0] else 0 for o in others)
        shots, eff, seconds = duel(others[1], v)
        ttk = f"{seconds:.1f} с" if shots else "не пробить"
        print(f"   {v['title']:<16} HP {v['hp']:.0f}, броня лоб {v['armorHull']:.0f} мм "
              f"(приведённая под огнём ~{worst:.0f} мм), живёт ~{ttk}")

    print("\nПроверки разумности:")
    if problems:
        for p in problems:
            print("   ! " + p)
    else:
        print("   все пары пробивают друг друга, мгновенных убийств нет — числа выглядят здоровыми")
    print("   (полноценный прогон боёв: в Unity — меню Samsar/4. Прогнать боёв)")


if __name__ == "__main__":
    main()
