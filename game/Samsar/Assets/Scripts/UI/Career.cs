using UnityEngine;

namespace Samsar
{
    /// <summary>Общая статистика игрока (прогрессии машин нет — все доступны сразу, но статистика ведётся).</summary>
    public static class Career
    {
        public static int Battles
        {
            get { return PlayerPrefs.GetInt("samsar_battles", 0); }
            set { PlayerPrefs.SetInt("samsar_battles", value); PlayerPrefs.Save(); }
        }

        public static int Wins
        {
            get { return PlayerPrefs.GetInt("samsar_wins", 0); }
            set { PlayerPrefs.SetInt("samsar_wins", value); PlayerPrefs.Save(); }
        }

        public static int Kills
        {
            get { return PlayerPrefs.GetInt("samsar_kills", 0); }
            set { PlayerPrefs.SetInt("samsar_kills", value); PlayerPrefs.Save(); }
        }

        public static int Damage
        {
            get { return PlayerPrefs.GetInt("samsar_damage", 0); }
            set { PlayerPrefs.SetInt("samsar_damage", value); PlayerPrefs.Save(); }
        }

        public static int BestPlace
        {
            get { return PlayerPrefs.GetInt("samsar_best", 99); }
            set
            {
                if (value < BestPlace)
                {
                    PlayerPrefs.SetInt("samsar_best", value);
                    PlayerPrefs.Save();
                }
            }
        }

        public static void RegisterBattle(PlayerStats stats)
        {
            if (stats == null) return;
            Battles++;
            if (stats.Won) Wins++;
            Kills += stats.Kills;
            Damage += Mathf.RoundToInt(stats.DamageDealt);
            if (stats.FinalPlace > 0) BestPlace = stats.FinalPlace;
        }
    }
}
