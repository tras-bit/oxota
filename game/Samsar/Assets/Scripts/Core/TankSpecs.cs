using UnityEngine;

namespace Samsar
{
    public enum TankClass { Light, Medium, Heavy, Td }

    public enum AbilityId { None, Airstrike, Artillery, ReconDrone, SmokeScreen, Boost, Repair, Shield, IncendiaryRing }

    public enum ModuleEffect
    {
        None, Damage, Reload, Penetration, Accuracy, AimTime, Health, Armor, Speed, Turn, Vision, AbilityCooldown, AmmoCap, Repair
    }

    [System.Serializable]
    public class ModuleOption
    {
        public string Name;
        public string Desc;
        public ModuleEffect Effect;
        public float Value;

        public ModuleOption(string name, string desc, ModuleEffect effect, float value)
        {
            Name = name; Desc = desc; Effect = effect; Value = value;
        }
    }

    /// <summary>Характеристики машины. 8 специальных машин, открытых сразу (прогрессии между боями нет).</summary>
    public class TankSpec
    {
        public string Id;
        public string Name;
        public string Tagline;
        public TankClass Class;

        public float MaxHealth = 1800f;
        public float ArmorFront = 120f;
        public float ArmorSide = 70f;
        public float ArmorRear = 45f;
        public float SlopeFront = 62f;
        public float SlopeSide = 20f;
        public float SlopeRear = 25f;

        public float EnginePower = 780f;
        public float Mass = 42f;
        public float MaxSpeedForward = 46f;
        public float MaxSpeedReverse = 18f;
        public float TurnRate = 40f;
        public float ReverseRatio = 0.4f;      // доля скорости назад от скорости вперёд

        public float BarrelLength = 4.5f;
        public float Reload = 6.4f;
        public float AimTime = 2.1f;
        public float Dispersion = 0.34f;      // метров на 100 м
        public float Damage = 320f;
        public float Penetration = 175f;
        public int AmmoStart = 24;
        public int AmmoMax = 45;

        public float ViewRange = 420f;
        public float DetectRadius = 260f;

        public AbilityId Ability1 = AbilityId.None;
        public AbilityId Ability2 = AbilityId.None;
        public float Ability1Cooldown = 70f;
        public float Ability2Cooldown = 110f;

        public Color BodyColor = new Color(0.30f, 0.33f, 0.25f);
        public Color AccentColor = new Color(0.75f, 0.72f, 0.68f);

        // Характеристики машины I уровня (база) и множители по уровням II..VII.
        public float[] HealthMul = { 1f, 0.78f, 0.9f, 1.04f, 1.2f, 1.38f, 1.58f };
        public float[] DamageMul = { 1f, 0.82f, 0.92f, 1.03f, 1.15f, 1.28f, 1.42f };
        public float[] ReloadMul = { 1f, 1.08f, 1.0f, 0.93f, 0.86f, 0.79f, 0.73f };
        public float[] PenMul = { 1f, 0.9f, 0.96f, 1.03f, 1.1f, 1.17f, 1.25f };
        public float[] SpeedMul = { 1f, 0.94f, 0.98f, 1.02f, 1.06f, 1.11f, 1.17f };
        public float[] ArmorMul = { 1f, 0.95f, 0.99f, 1.03f, 1.08f, 1.14f, 1.21f };

        public float HealthAt(int level) { return MaxHealth * HealthMul[Lv(level)]; }
        public float DamageAt(int level) { return Damage * DamageMul[Lv(level)]; }
        public float ReloadAt(int level) { return Reload * ReloadMul[Lv(level)]; }
        public float PenAt(int level) { return Penetration * PenMul[Lv(level)]; }
        public float SpeedAt(int level) { return MaxSpeedForward * SpeedMul[Lv(level)]; }
        public float ArmorAt(int level) { return ArmorFront * ArmorMul[Lv(level)]; }

        static int Lv(int level) { return Mathf.Clamp(level - 1, 0, 6); }

        /// <summary>Варианты модулей на уровень (по 2 на каждый уровень II..VII).</summary>
        public ModuleOption[] ModulesForLevel(int level)
        {
            return ModulePool.Get(Id, level);
        }
    }

    /// <summary>Пул модулей: на каждом уровне два варианта, детерминированно зависят от машины и уровня.</summary>
    public static class ModulePool
    {
        static readonly ModuleOption[] Pool =
        {
            new ModuleOption("Усиленное орудие", "+12% урона", ModuleEffect.Damage, 0.12f),
            new ModuleOption("Досылатель", "-12% времени перезарядки", ModuleEffect.Reload, 0.12f),
            new ModuleOption("Подкалиберные", "+10% бронепробития", ModuleEffect.Penetration, 0.10f),
            new ModuleOption("Стабилизатор", "-18% разброса", ModuleEffect.Accuracy, 0.18f),
            new ModuleOption("Приводы наведения", "-20% времени сведения", ModuleEffect.AimTime, 0.20f),
            new ModuleOption("Усиленный корпус", "+15% прочности", ModuleEffect.Health, 0.15f),
            new ModuleOption("Экраны брони", "+12% брони", ModuleEffect.Armor, 0.12f),
            new ModuleOption("Форсированный двигатель", "+10% скорости", ModuleEffect.Speed, 0.10f),
            new ModuleOption("Новая трансмиссия", "-15% времени ремонта модулей", ModuleEffect.Repair, 0.15f),
            new ModuleOption("Большой боекомплект", "+30% вместимости снарядов", ModuleEffect.AmmoCap, 0.30f),
        };

        static readonly string[,] Matrix = new string[8, 7];

        public static ModuleOption[] Get(string tankId, int level)
        {
            level = Mathf.Clamp(level, 1, 7);
            int seed = tankId.GetHashCode() * 31 + level * 7919;
            var rnd = new System.Random(seed);
            var a = Pool[rnd.Next(Pool.Length)];
            var b = Pool[rnd.Next(Pool.Length)];
            int guard = 0;
            while (b == a && guard++ < 20) b = Pool[rnd.Next(Pool.Length)];
            return new[] { a, b };
        }
    }

    public static class TankSpecs
    {
        static TankSpec[] all;

        public static TankSpec[] All { get { if (all == null) Build(); return all; } }
        public static int Count { get { return All.Length; } }
        public static TankSpec Get(int index) { return All[Mathf.Clamp(index, 0, All.Length - 1)]; }
        public static TankSpec ById(string id)
        {
            foreach (var t in All) if (t.Id == id) return t;
            return All[0];
        }

        public static string ClassName(TankClass c)
        {
            switch (c)
            {
                case TankClass.Light: return "ЛТ";
                case TankClass.Medium: return "СТ";
                case TankClass.Td: return "ПТ-САУ";
                default: return "ТТ";
            }
        }

        static void Build()
        {
            all = new[]
            {
                new TankSpec
                {
                    Id = "sarych", Name = "Сарыч", Tagline = "Лёгкий разведчик: скорость вместо брони",
                    Class = TankClass.Light,
                    MaxHealth = 1350f, ArmorFront = 70f, ArmorSide = 40f, ArmorRear = 30f,
                    SlopeFront = 70f, SlopeSide = 22f, SlopeRear = 25f,
                    EnginePower = 640f, Mass = 22f, MaxSpeedForward = 72f, MaxSpeedReverse = 26f, TurnRate = 56f,
                    BarrelLength = 3.9, Reload = 5.2f, AimTime = 1.9f, Dispersion = 0.38f, Damage = 220f, Penetration = 150f,
                    AmmoStart = 30, AmmoMax = 54,
                    ViewRange = 520f, DetectRadius = 330f,
                    Ability1 = AbilityId.ReconDrone, Ability2 = AbilityId.Boost,
                    Ability1Cooldown = 55f, Ability2Cooldown = 35f,
                    BodyColor = new Color(0.34f, 0.36f, 0.27f), AccentColor = new Color(0.85f, 0.82f, 0.6f),
                },
                new TankSpec
                {
                    Id = "boar", Name = "Вепрь", Tagline = "Средний танк: золотая середина",
                    Class = TankClass.Medium,
                    MaxHealth = 1800f, ArmorFront = 120f, ArmorSide = 70f, ArmorRear = 45f,
                    EnginePower = 780f, Mass = 38f, MaxSpeedForward = 54f, MaxSpeedReverse = 20f, TurnRate = 46f,
                    BarrelLength = 4.5, Reload = 6.2f, AimTime = 2.1f, Dispersion = 0.33f, Damage = 320f, Penetration = 195f,
                    AmmoStart = 24, AmmoMax = 45,
                    ViewRange = 420f, DetectRadius = 270f,
                    Ability1 = AbilityId.SmokeScreen, Ability2 = AbilityId.Repair,
                    Ability1Cooldown = 60f, Ability2Cooldown = 90f,
                    BodyColor = new Color(0.30f, 0.34f, 0.26f), AccentColor = new Color(0.8f, 0.78f, 0.72f),
                },
                new TankSpec
                {
                    Id = "tur", Name = "Тур", Tagline = "Тяжёлый: медленный, злой, в лоб не берётся",
                    Class = TankClass.Heavy,
                    MaxHealth = 2700f, ArmorFront = 190f, ArmorSide = 100f, ArmorRear = 60f,
                    SlopeFront = 55f, SlopeSide = 25f, SlopeRear = 30f,
                    EnginePower = 900f, Mass = 62f, MaxSpeedForward = 38f, MaxSpeedReverse = 14f, TurnRate = 32f,
                    BarrelLength = 4.8, Reload = 9.4f, AimTime = 2.9f, Dispersion = 0.36f, Damage = 480f, Penetration = 225f,
                    AmmoStart = 18, AmmoMax = 36,
                    ViewRange = 380f, DetectRadius = 240f,
                    Ability1 = AbilityId.Artillery, Ability2 = AbilityId.Shield,
                    Ability1Cooldown = 120f, Ability2Cooldown = 100f,
                    BodyColor = new Color(0.26f, 0.29f, 0.24f), AccentColor = new Color(0.7f, 0.68f, 0.62f),
                },
                new TankSpec
                {
                    Id = "korshun", Name = "Коршун", Tagline = "ПТ-САУ: бьёт из засады, брони почти нет",
                    Class = TankClass.Td,
                    MaxHealth = 1500f, ArmorFront = 90f, ArmorSide = 45f, ArmorRear = 30f,
                    SlopeFront = 75f, SlopeSide = 15f, SlopeRear = 20f,
                    EnginePower = 660f, Mass = 32f, MaxSpeedForward = 48f, MaxSpeedReverse = 16f, TurnRate = 38f,
                    BarrelLength = 5.8, Reload = 11.5f, AimTime = 2.3f, Dispersion = 0.22f, Damage = 620f, Penetration = 290f,
                    AmmoStart = 16, AmmoMax = 32,
                    ViewRange = 460f, DetectRadius = 290f,
                    Ability1 = AbilityId.SmokeScreen, Ability2 = AbilityId.Shield,
                    Ability1Cooldown = 55f, Ability2Cooldown = 95f,
                    BodyColor = new Color(0.28f, 0.3f, 0.22f), AccentColor = new Color(0.75f, 0.7f, 0.55f),
                },
                new TankSpec
                {
                    Id = "viy", Name = "Вий", Tagline = "Тяжёлый штурмовик с огненным кольцом",
                    Class = TankClass.Heavy,
                    MaxHealth = 2500f, ArmorFront = 175f, ArmorSide = 95f, ArmorRear = 55f,
                    EnginePower = 860f, Mass = 58f, MaxSpeedForward = 41f, MaxSpeedReverse = 15f, TurnRate = 34f,
                    BarrelLength = 4.7, Reload = 8.6f, AimTime = 2.7f, Dispersion = 0.35f, Damage = 450f, Penetration = 215f,
                    AmmoStart = 20, AmmoMax = 38,
                    ViewRange = 390f, DetectRadius = 250f,
                    Ability1 = AbilityId.IncendiaryRing, Ability2 = AbilityId.Repair,
                    Ability1Cooldown = 95f, Ability2Cooldown = 85f,
                    BodyColor = new Color(0.3f, 0.24f, 0.2f), AccentColor = new Color(0.85f, 0.5f, 0.25f),
                },
                new TankSpec
                {
                    Id = "grom", Name = "Гром", Tagline = "Артиллерийская поддержка: авиаудар и залп",
                    Class = TankClass.Medium,
                    MaxHealth = 1650f, ArmorFront = 105f, ArmorSide = 60f, ArmorRear = 40f,
                    EnginePower = 720f, Mass = 36f, MaxSpeedForward = 50f, MaxSpeedReverse = 18f, TurnRate = 42f,
                    BarrelLength = 5.0, Reload = 7.6f, AimTime = 2.5f, Dispersion = 0.31f, Damage = 380f, Penetration = 185f,
                    AmmoStart = 22, AmmoMax = 40,
                    ViewRange = 430f, DetectRadius = 275f,
                    Ability1 = AbilityId.Airstrike, Ability2 = AbilityId.Artillery,
                    Ability1Cooldown = 100f, Ability2Cooldown = 130f,
                    BodyColor = new Color(0.31f, 0.32f, 0.28f), AccentColor = new Color(0.78f, 0.75f, 0.66f),
                },
                new TankSpec
                {
                    Id = "rys", Name = "Рысь", Tagline = "Быстрая поддержка: дрон, дым, ремонт союзнику",
                    Class = TankClass.Light,
                    MaxHealth = 1450f, ArmorFront = 75f, ArmorSide = 45f, ArmorRear = 32f,
                    EnginePower = 700f, Mass = 25f, MaxSpeedForward = 68f, MaxSpeedReverse = 24f, TurnRate = 52f,
                    BarrelLength = 4.1, Reload = 6.8f, AimTime = 2.0f, Dispersion = 0.34f, Damage = 260f, Penetration = 170f,
                    AmmoStart = 26, AmmoMax = 48,
                    ViewRange = 500f, DetectRadius = 320f,
                    Ability1 = AbilityId.ReconDrone, Ability2 = AbilityId.SmokeScreen,
                    Ability1Cooldown = 50f, Ability2Cooldown = 45f,
                    BodyColor = new Color(0.33f, 0.33f, 0.24f), AccentColor = new Color(0.8f, 0.8f, 0.7f),
                },
                new TankSpec
                {
                    Id = "medved", Name = "Медведь", Tagline = "Тяжёлый прорыв: щит, ремонт, давление",
                    Class = TankClass.Heavy,
                    MaxHealth = 2800f, ArmorFront = 200f, ArmorSide = 110f, ArmorRear = 62f,
                    SlopeFront = 58f, SlopeSide = 26f, SlopeRear = 30f,
                    EnginePower = 950f, Mass = 65f, MaxSpeedForward = 36f, MaxSpeedReverse = 14f, TurnRate = 30f,
                    BarrelLength = 4.9, Reload = 10.2f, AimTime = 3.0f, Dispersion = 0.37f, Damage = 520f, Penetration = 235f,
                    AmmoStart = 16, AmmoMax = 34,
                    ViewRange = 370f, DetectRadius = 235f,
                    Ability1 = AbilityId.Shield, Ability2 = AbilityId.Repair,
                    Ability1Cooldown = 90f, Ability2Cooldown = 95f,
                    BodyColor = new Color(0.27f, 0.28f, 0.23f), AccentColor = new Color(0.72f, 0.7f, 0.64f),
                },
            };
        }

        public static string AbilityName(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.Airstrike: return "Авиаудар";
                case AbilityId.Artillery: return "Артзалп";
                case AbilityId.ReconDrone: return "Разведдрон";
                case AbilityId.SmokeScreen: return "Дымовая завеса";
                case AbilityId.Boost: return "Форсаж";
                case AbilityId.Repair: return "Полевой ремонт";
                case AbilityId.Shield: return "Экран защиты";
                case AbilityId.IncendiaryRing: return "Огненное кольцо";
                default: return "—";
            }
        }

        public static string AbilityDesc(AbilityId id)
        {
            switch (id)
            {
                case AbilityId.Airstrike: return "Пометить точку: через 3 сек штурмовик наносит мощный удар по площади";
                case AbilityId.Artillery: return "Залп по площади, накрывает несколько целей";
                case AbilityId.ReconDrone: return "Дрон подсвечивает противников вокруг точки в радиусе 150 м";
                case AbilityId.SmokeScreen: return "Дымовая завеса: противники теряют вас в 60 м";
                case AbilityId.Boost: return "Кратковременный прирост мощности двигателя";
                case AbilityId.Repair: return "Ремонт модулей и восстановление прочности";
                case AbilityId.Shield: return "Экран: сильное снижение получаемого урона на 8 сек";
                case AbilityId.IncendiaryRing: return "Кольцо огня вокруг машины: урон и замедление врагов рядом";
                default: return "";
            }
        }
    }
}
