// Профиль игрока: статистика, достижения, сохранение (PlayerPrefs).
using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    [System.Serializable]
    public class ProfileData
    {
        public int battles;
        public int wins;
        public int kills;
        public float damage;
        public int loot;
        public int bestPlace = 99;
        public List<string> achievements = new List<string>();
    }

    public static class Profile
    {
        const string Key = "samsar_profile_v1";
        public static ProfileData Data { get; private set; } = new ProfileData();

        public static void Load()
        {
            if (PlayerPrefs.HasKey(Key))
            {
                try { Data = JsonUtility.FromJson<ProfileData>(PlayerPrefs.GetString(Key)) ?? new ProfileData(); }
                catch { Data = new ProfileData(); }
            }
            if (Data.achievements == null) Data.achievements = new List<string>();
        }

        public static void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
        }

        public static void RecordBattle(int place, int kills, float damage, bool victory, int totalAlive)
        {
            Load();
            Data.battles++;
            if (victory) Data.wins++;
            Data.kills += kills;
            Data.damage += damage;
            Data.loot += GameSession.LastLoot;
            Data.bestPlace = Mathf.Min(Data.bestPlace, place);
            Save();
        }

        public static bool Has(string id) => Data.achievements.Contains(id);

        public static void Unlock(string id, string title)
        {
            if (Data.achievements.Contains(id)) return;
            Data.achievements.Add(id);
            Save();
            HUD.Toast("Достижение: " + title, HUD.ToastKind.Good);
        }
    }

    /// <summary>Достижения за особые действия (анкета: без ежедневных задач).</summary>
    public static class AchievementSystem
    {
        public const string FirstBlood = "first_blood";
        public const string Survivor = "last_hunter";
        public const string LootMaster = "loot_master";
        public const string AirStrikeAce = "air_strike";
        public const string SquadHero = "squad_hero";
        public const string Veteran = "veteran";

        public static void ReportLastBattle()
        {
            if (GameSession.LastKills >= 1) Profile.Unlock(FirstBlood, "Первая кровь");
            if (GameSession.LastVictory) Profile.Unlock(Survivor, "Последний охотник");
            if (GameSession.LastLoot >= 20) Profile.Unlock(LootMaster, "Собиратель добычи");
            if (GameSession.LastDamage >= 5000f) Profile.Unlock(Veteran, "Ветеран: 5000 урона за бой");

            var player = TankRegistry.Player;
            if (player != null)
            {
                if (player.Modules.Count >= 5) Profile.Unlock(AirStrikeAce, "Прокачанная машина: 5 модулей за бой");
                foreach (var t in TankRegistry.All)
                    if (t.IsAlly && !t.Dead && GameSession.LastVictory)
                    { Profile.Unlock(SquadHero, "Взвод до конца"); break; }
            }
            Save();
        }

        static void Save() { Profile.Save(); }

        /// <summary>Список наград для интерфейса ангара.</summary>
        public class Achievement
        {
            public string id, title, desc;
            public Achievement(string id, string title, string desc)
            { this.id = id; this.title = title; this.desc = desc; }
        }

        public static readonly Achievement[] All =
        {
            new Achievement(FirstBlood,   "Первая кровь",       "уничтожить противника"),
            new Achievement(Survivor,     "Последний охотник",  "победить в бою"),
            new Achievement(LootMaster,   "Собиратель добычи",  "20 единиц лута за бой"),
            new Achievement(Veteran,      "Ветеран",            "5000 урона за бой"),
            new Achievement(SquadHero,    "Взвод до конца",     "победить вместе с напарником"),
            new Achievement(AirStrikeAce, "Прокачанная машина", "5 модулей за бой"),
        };
    }
}
