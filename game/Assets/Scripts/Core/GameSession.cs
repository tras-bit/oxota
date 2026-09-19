// Настройки забега и общие константы игры «Samsar».
using UnityEngine;

namespace Samsar
{
    /// <summary>Сложность ботов (в меню: 3 уровня).</summary>
    public enum BotDifficulty { Easy = 0, Normal = 1, Hard = 2 }

    /// <summary>Что выбрал игрок в ангаре; передаётся в боевую сцену между загрузками.</summary>
    public static class GameSession
    {
        public static string TankId = "mt";
        public static BotDifficulty Difficulty = BotDifficulty.Normal;
        public static int SquadSize = 1;          // 1 = соло, 2 = взвод с ИИ-напарником
        public static bool SquadMateIsBot = true;
        public static int BotsInBattle = 12;      // 5..10 чистых мародёров + бойцы остальных классов
        public static string PlayerName = "Охотник";

        // Результаты боя (заполняется в бою, показывается после)
        public static int LastPlace;
        public static int LastKills;
        public static float LastDamage;
        public static float LastSurvived;
        public static int LastLoot;
        public static bool LastVictory;

        public static void Reset()
        {
            LastPlace = 0; LastKills = 0; LastDamage = 0f;
            LastSurvived = 0f; LastLoot = 0; LastVictory = false;
        }
    }

    /// <summary>Правила режима «Стальной охотник» (по ответам анкеты).</summary>
    public static class Rules
    {
        public const float BattleTime = 20 * 60f;      // 20+ минут
        public const int MaxCombatants = 32;           // 30+ участников
        public const int LevelsMax = 7;                // прокачка I–VII прямо в бою
        public const int AbilitiesPerTank = 2;         // по 2 умения, первое — авиаудар
        public const bool AllowRespawnFromLoot = true;// «Возрождение» только из лута
        public const float AutoDetectRange = 50f;      // ближе 50 м враг виден всегда
        public const float ZoneDamagePerSecond = 22f;  // урон в красной зоне
        public const float AirDropInterval = 240f;     // воздушный груз по расписанию
        public const float SquadRespawnTime = 25f;     // время возрождения напарника-бота

        // Фазы сужения зоны: радиус красной зоны и пауза до следующего сужения (жёлтая — предупреждение)
        public static readonly float[] ZoneRadius = { 1450f, 1150f, 880f, 620f, 380f, 180f };
        public static readonly float[] ZonePhaseTime = { 150f, 150f, 135f, 120f, 105f, 999f };

        /// <summary>Опыт за действия в бою.</summary>
        public static float XpDamage => 0.35f;      // за единицу урона
        public static float XpKill => 120f;         // за уничтоженную машину
        public static float XpLoot => 45f;          // за подобранную добычу
        public static float XpAirDrop => 160f;      // за воздушный груз
    }
}
