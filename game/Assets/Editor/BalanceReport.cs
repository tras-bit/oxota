// Отчёт по балансу: 50 дуэлей на каждую пару машин на настоящей математике игры
// (DamageSystem.Resolve + правила брони из TankArmor). Сцена не нужна — считает в редакторе.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Samsar.EditorTools
{
    public static class BalanceReport
    {
        const int DuelsPerPair = 50;
        const float Distance = 300f;          // типичная дистанция боя на 3×3 км

        [MenuItem("Samsar/4. Отчёт по балансу (50 дуэлей на пару)", false, 40)]
        public static void Run()
        {
            var roster = TankSpec.Roster;
            var sb = new StringBuilder();
            sb.AppendLine("# Отчёт по балансу (сгенерирован в редакторе Unity)");
            sb.AppendLine();
            sb.AppendLine("Дата: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ". ");
            sb.AppendLine("Дистанция " + Distance.ToString("0") + " м, " + DuelsPerPair +
                          " дуэлей на каждую пару, попадания случайные (корпус/башня/ходовая/МТО), " +
                          "углы случайные. Числа — то, что реально посчитает игра.");
            sb.AppendLine();

            sb.AppendLine("## Сколько выстрелов нужно, чтобы уничтожить противника в лоб");
            sb.AppendLine();
            sb.Append("| атакующий → цель |");
            foreach (var t in roster) sb.Append(" " + t.id.ToUpper() + " |");
            sb.AppendLine();
            sb.Append("|---|");
            foreach (var t in roster) sb.Append("---|");
            sb.AppendLine();

            var warnings = new List<string>();
            foreach (var attacker in roster)
            {
                sb.Append("| **" + attacker.id.ToUpper() + "** |");
                foreach (var defender in roster)
                {
                    if (attacker == defender) { sb.Append(" — |"); continue; }
                    var stat = Simulate(attacker, defender, frontal: true);
                    sb.Append(" " + Fmt(stat) + " |");
                    if (stat.penetrations == 0)
                        warnings.Add(attacker.id + "→" + defender.id + " не пробивает лоб вообще");
                    else if (stat.shotsToKill <= 1f)
                        warnings.Add(attacker.id + "→" + defender.id + " убивает с одного выстрела");
                    else if (stat.shotsToKill > 12f)
                        warnings.Add(attacker.id + "→" + defender.id + " затягивается: " +
                                     stat.shotsToKill.ToString("0") + " выстрелов");
                }
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("## Что выяснилось в дуэлях");
            sb.AppendLine();
            sb.AppendLine("- рикошеты: " + Pct(Overall.ricochets, Overall.shots));
            sb.AppendLine("- критические повреждения: " + Pct(Overall.crits, Overall.penetrations));
            sb.AppendLine("- средний урон за пробитие: " + (Overall.penetrations > 0
                ? (Overall.damage / Overall.penetrations).ToString("0") + " ед." : "—"));
            sb.AppendLine();

            if (warnings.Count == 0)
            {
                sb.AppendLine("**Замечаний нет**: каждая машина пробивает каждую, мгновенных убийств нет.");
            }
            else
            {
                sb.AppendLine("## Что поправить");
                sb.AppendLine();
                foreach (var w in warnings) sb.AppendLine("- " + w);
            }

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../docs/balance-report.md"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();

            Debug.Log("Отчёт по балансу готов: " + path +
                      (warnings.Count == 0 ? "\nЗамечаний нет." : "\nЗамечаний: " + warnings.Count));
            EditorUtility.RevealInFinder(path);
        }

        struct Stat
        {
            public float shotsToKill;   // среднее число выстрелов до уничтожения
            public float seconds;       // грубо: выстрелы × перезарядка
            public int penetrations, ricochets, crits, shots;
            public float damage;
        }

        static void Add(Stat a, ref Stat b)
        {
            b.penetrations += a.penetrations; b.ricochets += a.ricochets; b.crits += a.crits;
            b.shots += a.shots; b.damage += a.damage;
        }

        static Stat Overall;

        static string Fmt(Stat s)
        {
            if (s.penetrations == 0) return "не пробивает";
            if (float.IsInfinity(s.shotsToKill)) return "не убивает";
            return s.shotsToKill.ToString("0.0") + " выстр / " + s.seconds.ToString("0") + " с";
        }

        static string Pct(int part, int total)
        {
            if (total <= 0) return "—";
            return part + " из " + total + " (" + (100f * part / total).ToString("0") + "%)";
        }

        /// <summary>Прогон дуэлей: стрелок и цель стоят на месте и стреляют до уничтожения.</summary>
        static Stat Simulate(TankSpec attacker, TankSpec defender, bool frontal)
        {
            var stat = new Stat();
            int totalShots = 0;
            float totalSeconds = 0f;
            int kills = 0;

            var rnd = new System.Random(attacker.id.GetHashCode() * 397 ^ defender.id.GetHashCode());

            for (int duel = 0; duel < DuelsPerPair; duel++)
            {
                float hp = defender.hp;
                int shots = 0;
                var armor = new TankArmorLite(defender);

                while (hp > 0f && shots < 60)
                {
                    ModuleType zone = (ModuleType)rnd.Next(0, 6);
                    if (zone == ModuleType.Gun || zone == ModuleType.Ammo) zone = ModuleType.Hull;

                    float rel = frontal ? 0f : (float)(rnd.NextDouble() * Math.PI);   // доворот цели
                    float impactDeg = 5f + (float)rnd.NextDouble() * 65f;             // угол от нормали
                    Vector3 dir = Quaternion.Euler(0f, rel * Mathf.Rad2Deg, 0f) *
                                  Quaternion.Euler(0f, impactDeg, 0f) * Vector3.forward;
                    Vector3 normal = Quaternion.Euler(0f, impactDeg, 0f) * Vector3.back;

                    float thickness = armor.Thickness(zone, frontal, out float mult);
                    var res = DamageSystem.Resolve(thickness, dir, normal, attacker.penetration,
                                                   attacker.damage, zone, mult,
                                                   Distance, (float)rnd.NextDouble());

                    shots++;
                    stat.shots++;
                    if (res.ricochet) stat.ricochets++;
                    if (res.penetrated)
                    {
                        stat.penetrations++;
                        if (res.critical) stat.crits++;
                        if (res.damage > 0f) { stat.damage += res.damage; hp -= res.damage; }
                    }
                }

                totalShots += shots;
                totalSeconds += shots * attacker.reload;
                kills++;
            }

            Add(stat, ref Overall);
            if (kills > 0)
            {
                stat.shotsToKill = (float)totalShots / kills;
                stat.seconds = totalSeconds / kills;
            }
            else stat.shotsToKill = float.PositiveInfinity;
            return stat;
        }

        /// <summary>Копия правил брони из TankArmor: лоб / борт / корма считаются по-разному.</summary>
        struct TankArmorLite
        {
            readonly TankSpec spec;
            public TankArmorLite(TankSpec s) { spec = s; }

            public float Thickness(ModuleType zone, bool frontal, out float mult)
            {
                mult = 1f;
                switch (zone)
                {
                    case ModuleType.Turret: return spec.armorTurret;
                    case ModuleType.Tracks: return 30f;
                    case ModuleType.Engine: return spec.armorRear * 0.8f;
                    case ModuleType.Gun: return 40f;
                    case ModuleType.Ammo: return spec.armorHull * 0.9f;
                }
                if (frontal) return spec.armorHull;
                // цель повёрнута боком: половина попаданий в борт, половина в корму
                return UnityEngine.Random.value < 0.6f ? spec.armorHull * 0.7f : spec.armorRear;
            }
        }
    }
}
