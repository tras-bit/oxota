using UnityEngine;

namespace Samsar
{
    /// <summary>Движение машины: разгон, поворот, поведение на грунте, поломки модулей.</summary>
    public class TankController : MonoBehaviour
    {
        public Vehicle Vehicle;
        public Vector2 MoveInput;          // x — поворот, y — вперёд/назад (в танковых единицах)
        public bool Brake;
        public bool UseHandbrake;

        Rigidbody rb;
        float groundRoll;
        float airborneTime;

        public float CurrentSpeed { get { return rb != null ? Vector3.Dot(rb.velocity, transform.forward) : 0f; } }
        public float SpeedKmh { get { return rb != null ? rb.velocity.magnitude * 3.6f : 0f; } }

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (Vehicle == null) Vehicle = GetComponent<Vehicle>();
        }

        void FixedUpdate()
        {
            if (Vehicle == null || !Vehicle.Alive || rb == null) return;
            float dt = Time.fixedDeltaTime;
            var stats = Vehicle.Stats;

            float speedForward = stats.SpeedForward / 3.6f;   // км/ч -> м/с
            float speedReverse = stats.SpeedReverse / 3.6f;
            bool trackBroken = Vehicle.TrackBroken;
            float mobility = (Vehicle.EngineBroken ? 0.5f : 1f) * (trackBroken ? Vehicle.TrackSlowFactor : 1f) * (Vehicle.BoostUntil > Time.time ? 1.45f : 1f);
            if (Vehicle.RingUntil > Time.time) mobility *= 0.95f;

            // ==== ориентация по грунту ====
            RaycastHit hit = default(RaycastHit);
            bool grounded = false;
            var groundHits = Physics.RaycastAll(transform.position + Vector3.up * 3f, Vector3.down, 14f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(groundHits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < groundHits.Length; i++)
            {
                var own = groundHits[i].collider.GetComponentInParent<Vehicle>();
                if (own == Vehicle) continue;
                hit = groundHits[i];
                grounded = true;
                break;
            }
            if (grounded)
            {
                airborneTime = 0f;
                Vector3 forwardOnSlope = Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized;
                if (forwardOnSlope.sqrMagnitude > 0.01f)
                {
                    var targetRot = Quaternion.LookRotation(forwardOnSlope, hit.normal);
                    rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, dt * 6f));
                }
                // держим машину у поверхности (мягко, чтобы не проваливаться на склонах)
                float ride = Vehicle.Model != null ? Vehicle.Model.RideHeight : 0.85f;
                float desiredY = hit.point.y + ride;
                if (transform.position.y < desiredY)
                {
                    Vector3 p = rb.position;
                    p.y = Mathf.MoveTowards(p.y, desiredY, 8f * dt);
                    rb.MovePosition(p);
                }
            }
            else
            {
                airborneTime += dt;
                rb.AddForce(Physics.gravity * 1.6f, ForceMode.Acceleration);
            }

            // ==== поворот корпуса ====
            float turnRate = stats.TurnRate * mobility;
            if (trackBroken) turnRate *= 0.7f;
            float yawInput = MoveInput.x;
            if (Brake) yawInput *= 0.4f;
            // на месте поворачиваемся быстрее, чем на скорости
            float speedFactor = Mathf.Lerp(1.35f, 0.65f, Mathf.Clamp01(Mathf.Abs(CurrentSpeed) / speedForward));
            float yawRate = yawInput * turnRate * speedFactor * Mathf.Deg2Rad;
            var angVel = rb.angularVelocity;
            angVel.y = yawRate;
            rb.angularVelocity = angVel;

            // ==== разгон / торможение ====
            float throttle = Brake ? 0f : MoveInput.y;
            float targetSpeed = throttle > 0f ? throttle * speedForward : throttle * speedReverse;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 desiredVel = forward * targetSpeed;

            Vector3 planarVel = Vector3.ProjectOnPlane(rb.velocity, Vector3.up);
            Vector3 velDiff = desiredVel - planarVel;
            float accel = (stats.SpeedForward / 3.6f) * 1.4f * mobility;   // разгон ~1.4 скорости в секунду
            float brakeAccel = accel * 2.2f;
            float force = throttle == 0f || Mathf.Sign(targetSpeed) != Mathf.Sign(Vector3.Dot(planarVel, forward))
                ? brakeAccel
                : accel;
            Vector3 delta = Vector3.ClampMagnitude(velDiff, force * dt);
            Vector3 newPlanar = planarVel + delta;
            Vector3 vertical = new Vector3(0f, rb.velocity.y, 0f);

            // сопротивление и потери на бездорожье
            rb.velocity = newPlanar + vertical;

            // трение о грунт: убираем боковое скольжение (танк не дрифтует)
            Vector3 lateral = Vector3.Dot(rb.velocity, transform.right) * transform.right;
            rb.velocity -= lateral * Mathf.Clamp01(dt * (grounded ? 6f : 0.5f));

            Vehicle.DistanceTravelled += planarVel.magnitude * dt;

            if (Vehicle.Model != null) Vehicle.Model.SpinWheels(Mathf.Abs(CurrentSpeed) / Mathf.Max(1f, speedForward));
            if (grounded && Mathf.Abs(CurrentSpeed) > speedForward * 0.35f && Random.value < dt * 3f)
                Vfx.Dust(rb.position + Vector3.up * 0.2f - forward * 1.5f, 0.6f);
        }
    }
}
