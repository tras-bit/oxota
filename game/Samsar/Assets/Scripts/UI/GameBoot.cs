using UnityEngine;

namespace Samsar
{
    /// <summary>Инициализация при старте: мышь, качество, звук. В сцене с меню — точка входа игры.</summary>
    public class GameBoot : MonoBehaviour
    {
        public bool StartInHangar;

        void Awake()
        {
            QualitySettings.vSyncCount = 1;
            GameConfig.ApplyQuality();
            AudioBus.Ensure();
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            Application.targetFrameRate = 120;
            if (StartInHangar) GameManager.ToHangar();
        }
    }
}
