// Модель бронирования и расчёт пробития: углы, рикошеты, критические повреждения модулей.
using UnityEngine;

namespace Samsar
{
    /// <summary>Модуль/зона попадания.</summary>
    public enum ModuleType { Hull, Turret, Tracks, Engine, Gun, Ammo }

    public struct PenResult
    {
        public bool penetrated;     // пробитие
        public bool ricochet;       // рикошет
        public bool critical;       // критическое повреждение модуля
        public float damage;        // нанесённый урон (после расчёта)
        public float effectiveArmor;// приведённая броня в точке попадания
        public ModuleType zone;
        public string message;      // текст для HUD: «Пробитие!», «Рикошет», «Гусеница сбита»
    }

    public static class DamageSystem
    {
        /// <summary>Приведённая броня с учётом угла: толщина / cos(угла между нормалью и снарядом).</summary>
        public static float EffectiveArmor(float thickness, Vector3 shellDir, Vector3 normal)
        {
            float cos = Mathf.Abs(Vector3.Dot(shellDir.normalized, normal.normalized));
            cos = Mathf.Max(cos, 0.12f);          // ограничение на «экстремальные» углы
            return thickness / cos;
        }

        public static PenResult Resolve(float thickness, Vector3 shellDir, Vector3 normal,
                                        float penetration, float damage, ModuleType zone,
                                        float armorMult, float distance, float rnd)
        {
            var r = new PenResult { zone = zone };
            float eff = EffectiveArmor(thickness * armorMult, shellDir, normal);
            r.effectiveArmor = eff;

            // падение пробития с дистанцией: −5% на каждые 100 м (снаряд теряет скорость плавно)
            float pen = penetration * (1f - Mathf.Min(0.20f, distance / 100f * 0.05f));
            // нормализация бронебойного снаряда: до −8° при большом калибре (упрощённо)
            float impactAngle = Vector3.Angle(shellDir, -normal);
            if (pen >= 120f && impactAngle > 60f) eff *= 0.92f;

            float roll = 0.85f + rnd * 0.3f;      // ±15% случайности

            if (impactAngle > 72f && pen < eff * 1.6f)
            {
                r.ricochet = true;
                r.penetrated = false;
                r.damage = 0f;
                r.message = "Рикошет";
                return r;
            }

            if (pen * roll >= eff)
            {
                r.penetrated = true;
                r.damage = damage * (0.9f + rnd * 0.2f);
                // критические повреждения модулей
                switch (zone)
                {
                    case ModuleType.Tracks:
                        r.damage = damage * 0.35f;
                        r.critical = rnd > 0.25f;
                        r.message = r.critical ? "Гусеница сбита!" : "Попадание в ходовую";
                        break;
                    case ModuleType.Engine:
                        r.damage = damage * 0.6f;
                        r.critical = rnd > 0.3f;
                        r.message = r.critical ? "Двигатель повреждён!" : "Попадание в МТО";
                        break;
                    case ModuleType.Gun:
                        r.damage = damage * 0.25f;
                        r.critical = rnd > 0.3f;
                        r.message = r.critical ? "Орудие повреждено!" : "Попадание в орудие";
                        break;
                    case ModuleType.Ammo:
                        r.damage = damage * 1.25f;
                        r.critical = rnd > 0.6f;
                        r.message = r.critical ? "Детонация БК!" : "Пробитие!";
                        break;
                    default:
                        r.message = "Пробитие!";
                        break;
                }
            }
            else
            {
                r.penetrated = false;
                r.damage = damage * 0.12f;         // «непробитие» всё равно царапает
                r.message = "Не пробил";
            }
            return r;
        }
    }
}
