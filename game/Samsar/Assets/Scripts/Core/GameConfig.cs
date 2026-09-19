using UnityEngine;

namespace Samsar
{
    /// <summary>Все настройки игры в одном месте. Меняются в меню и сохраняются в PlayerPrefs.</summary>
    public static class GameConfig
    {
        // ==== бой ====
        public const int ParticipantsDefault = 30;      // 30+ участников
        public const float BattleDuration = 1260f;      // 21 минута
        public const float CountdownTime = 20f;         // выбор точки старта

        // ==== размер карты ====
        public const float MapSize = 3000f;             // 3 x 3 км
        public const int TerrainRes = 200;              // сегментов на сторону

        // ==== зона ====
        public const float ZoneStartRadius = 1450f;
        public static readonly float[] ZoneRadii = { 1450f, 1100f, 820f, 600f, 420f, 260f, 120f, 60f };
        public static readonly float[] ZoneTimes = { 210f, 180f, 165f, 150f, 135f, 120f, 105f, 90f }; // длительность жёлтой фазы
        public const float ZoneRedWarning = 30f;        // за сколько секунд зона становится красной
        public const float ZoneDamagePerSecond = 22f;   // урон в красной зоне

        // ==== обнаружение ====
        public const float AutoDetectRange = 50f;       // автообнаружение в упор
        public const float VisionUpdateInterval = 0.35f;
        public const float SpotFadeTime = 3f;           // сколько секунды держится засветка после ухода

        // ==== физика танка ====
        public const float Gravity = 22f;
        public const float GroundSnapHeight = 0.6f;
        public const float TrackBreakTime = 12f;        // сколько стоит сломанная гусеница (сек) при авторемонте
        public const float EngineRepairTime = 10f;
        public const float GunRepairTime = 8f;

        // ==== лут ====
        public const int LootCratesOnMap = 70;
        public const float LootRespawnTime = 55f;
        public const float AirDropInterval = 150f;
        public const int TrophiesPerKill = 1;

        // ==== боёвка ====
        public const float RicochetAngle = 72f;         // угол от нормали, при котором рикошет
        public const float MaxPenetrationDistance = 700f;
        public const float ShellGravity = 3.5f;         // слабое навесное падение снаряда

        // ==== прогрессия в бою ====
        public const int MaxLevel = 7;                  // уровни I..VII
        public static readonly float[] LevelXp = { 0f, 220f, 520f, 880f, 1300f, 1800f, 2400f };
        public const float XpPerDamage = 1f;
        public const float XpPerKill = 450f;
        public const float XpPerLoot = 90f;
        public const float XpPassivePerSecond = 1.2f;

        // ==== респавн ====
        public const float RespawnWindow = 720f;        // в течение 12 минут боя работает «возрождение» из лута
        public const float RespawnInvulnTime = 5f;

        // ==== настройки игрока (сохраняются) ====
        public static int BotCount
        {
            get { return PlayerPrefs.GetInt("samsar_bots", ParticipantsDefault); }
            set { PlayerPrefs.SetInt("samsar_bots", Mathf.Clamp(value, 4, 40)); PlayerPrefs.Save(); }
        }

        public static int Difficulty
        {
            get { return PlayerPrefs.GetInt("samsar_difficulty", 1); }  // 0 легко, 1 средне, 2 умные
            set { PlayerPrefs.SetInt("samsar_difficulty", Mathf.Clamp(value, 0, 2)); PlayerPrefs.Save(); }
        }

        public static string DifficultyName
        {
            get
            {
                switch (Difficulty)
                {
                    case 0: return "Лёгкая";
                    case 2: return "Сложная";
                    default: return "Средняя";
                }
            }
        }

        public static float MouseSensitivity
        {
            get { return PlayerPrefs.GetFloat("samsar_sens", 1f); }
            set { PlayerPrefs.SetFloat("samsar_sens", value); PlayerPrefs.Save(); }
        }

        public static bool InvertY
        {
            get { return PlayerPrefs.GetInt("samsar_invy", 0) == 1; }
            set { PlayerPrefs.SetInt("samsar_invy", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Использовать модели из Blender (Assets/Resources/Models). Если файла нет — рисуется примитив.</summary>
        public static bool UseImportedModels
        {
            get { return PlayerPrefs.GetInt("samsar_imported_models", 1) == 1; }
            set { PlayerPrefs.SetInt("samsar_imported_models", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static int Quality
        {
            get { return PlayerPrefs.GetInt("samsar_quality", 2); }     // 0 низкая, 1 средняя, 2 высокая
            set { PlayerPrefs.SetInt("samsar_quality", value); PlayerPrefs.Save(); }
        }

        public static int SelectedTank
        {
            get { return PlayerPrefs.GetInt("samsar_tank", 0); }
            set { PlayerPrefs.SetInt("samsar_tank", value); PlayerPrefs.Save(); }
        }

        public static bool Squad
        {
            get { return PlayerPrefs.GetInt("samsar_squad", 0) == 1; }  // игра во взводе (соло + взводы по 2)
            set { PlayerPrefs.SetInt("samsar_squad", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static void ApplyQuality()
        {
            switch (Quality)
            {
                case 0:
                    QualitySettings.SetQualityLevel(0, true);
                    QualitySettings.shadowDistance = 60f;
                    QualitySettings.shadows = ShadowQuality.Disable;
                    break;
                case 1:
                    QualitySettings.SetQualityLevel(2, true);
                    QualitySettings.shadowDistance = 140f;
                    QualitySettings.shadows = ShadowQuality.HardOnly;
                    break;
                default:
                    QualitySettings.SetQualityLevel(Mathf.Max(0, QualitySettings.names.Length - 1), true);
                    QualitySettings.shadowDistance = 300f;
                    QualitySettings.shadows = ShadowQuality.All;
                    break;
            }
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 120;
        }
    }
}
