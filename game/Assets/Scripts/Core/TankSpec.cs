// Ядро игры «Samsar»: характеристики техники, классы, ростер.
// Отдельный namespace, чтобы код не конфликтовал с другими ассетами.
using UnityEngine;

namespace Samsar
{
    public enum TankClass { LT, MT, HT, TD }

    /// <summary>Боевые характеристики одной машины.</summary>
    [System.Serializable]
    public class TankSpec
    {
        public string id;               // lt / mt / ht / td
        public string title;            // «ЛТ «Ветер»»
        public string modelName;        // имя FBX-модели в Assets/Models/Tanks
        public TankClass cls;

        public float hp = 800f;                 // прочность
        public float maxSpeed = 15f;            // м/с (≈54 км/ч)
        public float reverseSpeed = 6f;
        public float accel = 6f;                // м/с²
        public float turnRate = 55f;            // град/с разворот корпуса
        public float turretTraverse = 35f;      // град/с поворот башни

        public float reload = 5f;               // с
        public float damage = 200f;             // урон за пробитие
        public float penetration = 160f;        // бронепробиваемость, мм
        public float shellSpeed = 220f;         // м/с, снаряд летит время
        public float dispersion = 0.4f;         // м на 100 м
        public float aimTime = 2.2f;            // с сведения

        public float width = 3.2f;              // ширина по гусеницам, м (как в модели)
        public float length = 6.6f;             // длина корпуса, м (как в модели)
        public float hullHeight = 1.05f;        // высота корпуса, м
        public float viewRange = 350f;          // м, обнаружение
        public float armorHull = 70f;           // мм
        public float armorTurret = 90f;
        public float armorRear = 40f;
        public int shells = 24;                 // боекомплект

        public string ability2 = "repair";      // второе умение (первое — авиаудар у всех)
        public string color = "#5a6b45";

        public float SpeedKmh => maxSpeed * 3.6f;

        public static readonly TankSpec[] Roster = new TankSpec[]
        {
            new TankSpec {
                id = "lt", title = "ЛТ «Ветер»", modelName = "lt", cls = TankClass.LT,
                hp = 650f, maxSpeed = 18f, reverseSpeed = 7f, accel = 7.5f, turnRate = 62f,
                turretTraverse = 46f, reload = 2.6f, damage = 170f, penetration = 185f,
                shellSpeed = 240f, dispersion = 0.38f, aimTime = 1.7f, viewRange = 380f,
                armorHull = 40f, armorTurret = 55f, armorRear = 25f, shells = 32,
                width = 2.9f, length = 5.6f, hullHeight = 0.95f,
                ability2 = "smoke", color = "#6d7b3f"
            },
            new TankSpec {
                id = "mt", title = "СТ «Варяг»", modelName = "mt", cls = TankClass.MT,
                hp = 900f, maxSpeed = 14f, reverseSpeed = 6f, accel = 5.8f, turnRate = 50f,
                turretTraverse = 38f, reload = 4.6f, damage = 240f, penetration = 200f,
                shellSpeed = 220f, dispersion = 0.35f, aimTime = 2.2f, viewRange = 350f,
                armorHull = 75f, armorTurret = 105f, armorRear = 40f, shells = 26,
                width = 3.2f, length = 6.6f, hullHeight = 1.05f,
                ability2 = "repair", color = "#8b7c4a"
            },
            new TankSpec {
                id = "ht", title = "ТТ «Гранит»", modelName = "ht", cls = TankClass.HT,
                hp = 1250f, maxSpeed = 10.5f, reverseSpeed = 4.5f, accel = 4.2f, turnRate = 40f,
                turretTraverse = 28f, reload = 5.2f, damage = 420f, penetration = 240f,
                shellSpeed = 190f, dispersion = 0.42f, aimTime = 3.0f, viewRange = 330f,
                armorHull = 100f, armorTurret = 145f, armorRear = 50f, shells = 20,
                width = 3.9f, length = 7.4f, hullHeight = 1.20f,
                ability2 = "fire_ring", color = "#5b6675"
            },
            new TankSpec {
                id = "td", title = "ПТ «Гроза»", modelName = "td", cls = TankClass.TD,
                hp = 800f, maxSpeed = 12f, reverseSpeed = 5f, accel = 4.8f, turnRate = 42f,
                turretTraverse = 16f, reload = 9.5f, damage = 540f, penetration = 290f,
                shellSpeed = 260f, dispersion = 0.28f, aimTime = 3.4f, viewRange = 360f,
                armorHull = 90f, armorTurret = 110f, armorRear = 40f, shells = 16,
                width = 3.3f, length = 7.0f, hullHeight = 1.15f,
                ability2 = "camouflage", color = "#4d5a38"
            },
        };

        public static TankSpec Get(string id)
        {
            foreach (var s in Roster)
                if (s.id == id) return s;
            return Roster[1];
        }

        public static TankSpec Next(string id)
        {
            for (int i = 0; i < Roster.Length; i++)
                if (Roster[i].id == id) return Roster[(i + 1) % Roster.Length];
            return Roster[0];
        }
    }

    /// <summary>Уровни прокачки прямо в бою: I–VII, на каждом — выбор из двух модулей.</summary>
    public static class Progression
    {
        public static readonly float[] XpForLevel = { 0f, 280f, 650f, 1150f, 1750f, 2450f, 3300f };
        public const int MaxLevel = 7;   // уровни I..VII

        public static int LevelForXp(float xp)
        {
            int lvl = 1;
            for (int i = 0; i < XpForLevel.Length; i++)
                if (xp >= XpForLevel[i]) lvl = i + 1;
            return Mathf.Clamp(lvl, 1, MaxLevel);
        }

        public static float XpToNext(float xp)
        {
            int lvl = LevelForXp(xp);
            if (lvl >= MaxLevel) return 0f;
            return XpForLevel[lvl] - xp;   // XpForLevel[1] — порог II уровня
        }

        public static float LevelProgress(float xp)
        {
            int lvl = LevelForXp(xp);
            if (lvl >= MaxLevel) return 1f;
            float lo = XpForLevel[lvl - 1], hi = XpForLevel[lvl];
            return Mathf.Clamp01((xp - lo) / Mathf.Max(1f, hi - lo));
        }
    }

    /// <summary>Модуль улучшения, который выбирается при переходе на новый уровень.</summary>
    public class UpgradeModule
    {
        public string id, title, desc;

        public UpgradeModule(string id, string title, string desc)
        {
            this.id = id; this.title = title; this.desc = desc;
        }

        /// <summary>Пул модулей. Из него на каждом уровне выдаётся случайная пара.</summary>
        public static readonly UpgradeModule[] Pool = new UpgradeModule[]
        {
            new UpgradeModule("gun_damage",   "Усиленный ствол",   "+12% урона орудия"),
            new UpgradeModule("gun_reload",   "Ускоренная перезарядка", "−15% времени перезарядки"),
            new UpgradeModule("gun_accuracy", "Стабилизатор",      "−25% разброса, быстрее сведение"),
            new UpgradeModule("armor_hull",   "Экраны корпуса",    "+18% брони корпуса"),
            new UpgradeModule("armor_turret", "Усиленная башня",   "+18% брони башни"),
            new UpgradeModule("engine",       "Форсированный двигатель", "+15% мощности, +10% скорости"),
            new UpgradeModule("tracks",       "Усиленные гусеницы", "+25% прочности гусениц, меньше замедление"),
            new UpgradeModule("view",         "Ночной прицел",     "+20% дальности обзора"),
            new UpgradeModule("hp",           "Ремкомплект корпуса", "+12% прочности машины"),
            new UpgradeModule("ammo",         "Дополнительный БК",  "+25% боекомплекта, +2 снаряда сразу"),
            new UpgradeModule("cooldown",     "Заряд умений",       "−20% времени восстановления умений"),
            new UpgradeModule("repair",       "Ремонт в бою",       "Мгновенно ремонтирует модули и +20% прочности"),
        };

        public static UpgradeModule Get(string id)
        {
            foreach (var m in Pool) if (m.id == id) return m;
            return Pool[0];
        }

        /// <summary>Случайная пара разных модулей для выбора на уровне.</summary>
        public static UpgradeModule[] RollPair(int level, System.Random rnd)
        {
            var a = Pool[rnd.Next(Pool.Length)];
            UpgradeModule b = null;
            for (int i = 0; i < 40 && (b == null || b.id == a.id); i++)
                b = Pool[rnd.Next(Pool.Length)];
            return new[] { a, b };
        }
    }
}
