using UnityEngine;
using UnityEngine.SceneManagement;

namespace Samsar
{
    public enum MatchMode { Battle, TestRange }

    /// <summary>Точка входа: меню -> ангар -> бой (или полигон), настройки матча.</summary>
    public static class GameManager
    {
        public static MatchMode Mode = MatchMode.Battle;
        public static int NextSeed;

        public static void StartBattle(int tankIndex, int participants, int difficulty, bool squad)
        {
            Mode = MatchMode.Battle;
            NextSeed = Random.Range(1, 999999);
            GameConfig.SelectedTank = tankIndex;
            GameConfig.BotCount = participants;
            GameConfig.Difficulty = difficulty;
            GameConfig.Squad = squad;
            LoadingScreen.NextSceneName = "Battle";
            LoadingScreen.LoadingText = "Подготовка боя";
            SceneManager.LoadScene("Loading");
        }

        public static void StartTestRange(int tankIndex)
        {
            Mode = MatchMode.TestRange;
            NextSeed = Random.Range(1, 999999);
            GameConfig.SelectedTank = tankIndex;
            LoadingScreen.NextSceneName = "Battle";
            LoadingScreen.LoadingText = "Полигон";
            SceneManager.LoadScene("Loading");
        }

        public static void ToMenu()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene("MainMenu");
        }

        public static void ToHangar()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene("Hangar");
        }
    }
}
