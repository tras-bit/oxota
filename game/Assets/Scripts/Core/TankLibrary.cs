// Ссылки на модели техники (заполняются автоматически при сборке проекта в редакторе).
using UnityEngine;

namespace Samsar
{
    public class TankLibrary : MonoBehaviour
    {
        [System.Serializable]
        public class Entry
        {
            public string id;
            public GameObject model;
        }

        public Entry[] tanks;

        public GameObject Get(string id)
        {
            if (tanks != null)
                foreach (var e in tanks)
                    if (e.id == id && e.model != null) return e.model;
            return null;
        }
    }
}
