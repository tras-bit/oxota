// Обнаружение целей: круговая зона обзора 360°, линия видимости, скрытие невидимых машин.
using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    public class VisionSystem : MonoBehaviour
    {
        public TankController tank;
        public TankSpec spec;
        public float rangeMult = 1f;

        public bool VisibleToPlayer;      // видит ли эту машину игрок (считает BattleManager)
        public bool CanSeePlayer;         // видит ли эта машина игрока
        public float DetectionRange => spec.viewRange * rangeMult;

        readonly List<TankController> targets = new List<TankController>();
        Renderer[] renderers;
        bool renderersHidden;

        public void Init(TankController t, TankSpec s)
        {
            tank = t;
            spec = s;
        }

        /// <summary>Проверка прямой видимости между машинами (без учёта дальности).</summary>
        public bool HasLineOfSight(TankController other)
        {
            Vector3 from = transform.position + Vector3.up * 1.7f;
            Vector3 to = other.transform.position + Vector3.up * 1.4f;
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            RaycastHit hit;
            if (Physics.Raycast(from, dir.normalized, out hit, dist - 0.3f, ~0, QueryTriggerInteraction.Ignore))
                return hit.collider.GetComponentInParent<TankController>() == other;
            return true;
        }

        public bool CanSee(TankController other)
        {
            if (other == null || other.Dead || other == tank) return false;
            float dist = Vector3.Distance(transform.position, other.transform.position);
            if (dist > DetectionRange) return false;
            if (dist <= Rules.AutoDetectRange) return true;      // вплотную видно всегда
            return HasLineOfSight(other);
        }

        public List<TankController> VisibleTargets()
        {
            targets.Clear();
            foreach (var t in TankRegistry.All)
            {
                if (t == tank || t.Dead) continue;
                if (t.IsAlly && tank.IsAlly) continue;
                if (t.IsPlayer == tank.IsPlayer && tank.IsPlayer) continue;
                if (t.IsAlly && tank.IsPlayer) continue;        // союзник по взводу — не цель
                if (CanSee(t)) targets.Add(t);
            }
            return targets;
        }

        /// <summary>Показать/скрыть модель (противники видны только при обнаружении).</summary>
        public void SetRenderersVisible(bool visible)
        {
            if (renderersHidden == !visible) return;
            renderersHidden = !visible;
            if (renderers == null || renderers.Length == 0)
                renderers = tank.rig != null ? tank.rig.Renderers().ToArray() : GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
                if (r != null) r.enabled = visible;
        }

        /// <summary>Дальность обзора с учётом сектора (конус 100° — если включено в настройках).</summary>
        public bool InSector(Vector3 point)
        {
            if (!Settings.UseVisionCone) return true;
            Vector3 dir = point - transform.position;
            dir.y = 0f;
            return Vector3.Angle(tank.rig.TurretPivot.forward, dir) <= Settings.VisionConeAngle * 0.5f;
        }
    }

    /// <summary>Пользовательские настройки боя (анкета: конус/круг, сложность, число ботов).</summary>
    public static class Settings
    {
        public static bool UseVisionCone = false;     // по умолчанию круговая зона 360°
        public static float VisionConeAngle = 100f;
        public static bool ShowDamageNumbers = true;
        public static bool ColorBlindFriendly = false;
    }
}
