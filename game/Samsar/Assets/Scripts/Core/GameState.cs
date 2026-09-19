using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    public enum BattleState { Wait, Countdown, Running, Finished }

    /// <summary>Состояние одного боя: участники, места, время, статистика.</summary>
    public class GameState
    {
        public BattleState State = BattleState.Wait;
        public float TimeLeft = GameConfig.BattleDuration;
        public float Countdown = GameConfig.CountdownTime;
        public int SpawnSeed = 12345;
        public int ZoneStage;
        public float ZoneRadius = GameConfig.ZoneStartRadius;
        public Vector2 ZoneCenter = Vector2.zero;
        public Vector2 NextZoneCenter = Vector2.zero;
        public float NextZoneRadius = GameConfig.ZoneStartRadius;
        public float StageTimer;
        public int AliveCount;
        public int PlaceCounter;              // раздаётся места: 30, 29, ...
        public bool ZoneWarningPlaying;

        public readonly List<Vehicle> Participants = new List<Vehicle>();

        public Vehicle Player;
        public PlayerStats Stats = new PlayerStats();

        public bool IsRunning { get { return State == BattleState.Running; } }
    }

    /// <summary>Статистика игрока за бой.</summary>
    public class PlayerStats
    {
        public float DamageDealt;
        public float DamageTaken;
        public int Kills;
        public int BotKills;
        public int ShotsFired;
        public int Hits;
        public int Penetrations;
        public int Ricochets;
        public int LootTaken;
        public float DistanceTravelled;
        public int FinalPlace;
        public bool Won;
        public bool Survived;
        public int MaxLevelReached = 1;
        public List<string> Achievements = new List<string>();

        public float Accuracy { get { return ShotsFired > 0 ? Hits / (float)ShotsFired : 0f; } }
        public float PenRatio { get { return Hits > 0 ? Penetrations / (float)Hits : 0f; } }
    }

    /// <summary>Проверка достижений за бой (ежедневных задач и валюты нет — только достижения).</summary>
    public static class Achievements
    {
        public static List<string> Evaluate(PlayerStats s, GameState g)
        {
            var res = new List<string>();
            if (s.Kills >= 1) res.Add("Первая кровь");
            if (s.Kills >= 5) res.Add("Охотник (5 уничтоженных)");
            if (s.Kills >= 10) res.Add("Стальной хищник (10 уничтоженных)");
            if (s.DamageDealt >= 5000f) res.Add("5000 урона за бой");
            if (s.DamageDealt >= 10000f) res.Add("10000 урона за бой");
            if (s.Ricochets >= 3) res.Add("Рикошетная броня");
            if (s.LootTaken >= 10) res.Add("Мародёр (10 единиц добычи)");
            if (s.MaxLevelReached >= 7) res.Add("Максимальная модернизация (VII)");
            if (s.ShotsFired >= 15 && s.Accuracy >= 0.75f) res.Add("Снайпер: 75% попаданий");
            if (s.Won) res.Add("Победа: последний охотник");
            if (!s.Survived && s.FinalPlace <= 10) res.Add("Топ-10 после гибели");
            if (s.BotKills == 0 && s.Kills > 0) res.Add("Только живые цели");
            return res;
        }
    }

    /// <summary>Одна запись итоговой таблицы боя.</summary>
    public struct ScoreRow
    {
        public string Name;
        public bool IsPlayer;
        public bool IsBot;
        public int Damage;
        public int Kills;
        public int Place;
        public bool Alive;
        public int Level;
        public bool IsSquadMate;
    }
}
