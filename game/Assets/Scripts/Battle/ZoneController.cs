// Зона боя: жёлтая (предупреждение) → красная (урон). Сужение по фазам с таймером.
using UnityEngine;

namespace Samsar
{
    public class ZoneController : MonoBehaviour
    {
        public static ZoneController Instance;

        public Vector3 Center { get; private set; }
        public float RedRadius { get; private set; }      // дальше — урон
        public float YellowRadius { get; private set; }   // предупреждение о следующей красной
        public float PhaseTimeLeft { get; private set; }
        public int Phase { get; private set; }
        public bool Active { get; private set; }

        float phaseTimer;
        float damageTick;
        float playerWarnTimer;

        void Awake()
        {
            Instance = this;
            Center = new Vector3(0f, 0f, 0f);
            RedRadius = Rules.ZoneRadius[0];
            YellowRadius = Rules.ZoneRadius[0];
        }

        public void Begin()
        {
            Active = true;
            Phase = 0;
            RedRadius = Rules.ZoneRadius[0];
            YellowRadius = Rules.ZoneRadius[0];
            phaseTimer = Rules.ZonePhaseTime[0];
        }

        void Update()
        {
            if (!Active) return;

            phaseTimer -= Time.deltaTime;
            PhaseTimeLeft = Mathf.Max(0f, phaseTimer);

            if (phaseTimer <= 0f && Phase < Rules.ZoneRadius.Length - 1)
            {
                Phase++;
                YellowRadius = Rules.ZoneRadius[Phase];
                if (Phase + 1 < Rules.ZoneRadius.Length - 1)
                    RedRadius = Rules.ZoneRadius[Phase];         // красная подтягивается к жёлтой
                phaseTimer = Rules.ZonePhaseTime[Mathf.Min(Phase, Rules.ZonePhaseTime.Length - 1)];
                HUD.Toast(string.Format("Зона сужается! Радиус {0} м", Mathf.RoundToInt(YellowRadius)),
                          HUD.ToastKind.Bad);
            }
            // жёлтая зона «превращается» в красную: красный радиус догоняет жёлтый
            RedRadius = Mathf.MoveTowards(RedRadius, YellowRadius, 12f * Time.deltaTime);

            ApplyDamage();
        }

        void ApplyDamage()
        {
            damageTick -= Time.deltaTime;
            if (damageTick > 0f) return;
            damageTick = 1f;

            foreach (var t in TankRegistry.Alive())
            {
                float d = Vector3.Distance(new Vector3(t.transform.position.x, 0f, t.transform.position.z), Center);
                bool outside = d > RedRadius;
                if (t.vision != null)
                {
                    bool warn = d > YellowRadius;
                    t.GetComponent<TankOutsideZone>()?.SetState(outside, warn);
                }
                if (outside)
                {
                    t.ApplyRawDamage(Rules.ZoneDamagePerSecond, null);
                    if (t.IsPlayer)
                    {
                        playerWarnTimer = 2f;
                        HUD.Toast("Вы в красной зоне! Немедленно уходите", HUD.ToastKind.Bad);
                        Sfx.ZoneWarning();
                    }
                }
            }
        }

        public bool IsInside(Vector3 pos, bool redOnly = true)
        {
            float d = Vector3.Distance(new Vector3(pos.x, 0f, pos.z), Center);
            return d <= (redOnly ? RedRadius : YellowRadius);
        }

        /// <summary>Случайная точка внутри безопасной зоны (для возрождения).</summary>
        public Vector3 RandomPointInside()
        {
            Vector2 p = Random.insideUnitCircle * (RedRadius * 0.7f);
            Vector3 pos = new Vector3(Center.x + p.x, 0f, Center.z + p.y);
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 200f, Vector3.down, out hit, 500f, ~0, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y + 1.5f;
            else pos.y = 2f;
            return pos;
        }

        public float DistanceFromCenter(Vector3 pos)
        {
            return Vector3.Distance(new Vector3(pos.x, 0f, pos.z), Center);
        }
    }

    /// <summary>Индикация состояния зоны для конкретной машины (для HUD и ИИ).</summary>
    public class TankOutsideZone : MonoBehaviour
    {
        public bool Outside, Warn;
        public void SetState(bool outside, bool warn) { Outside = outside; Warn = warn; }
    }
}
