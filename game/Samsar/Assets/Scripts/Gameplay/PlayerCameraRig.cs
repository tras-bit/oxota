using UnityEngine;

namespace Samsar
{
    /// <summary>Камера от третьего лица и снайперский режим (×8), привязка к машине игрока.</summary>
    public class PlayerCameraRig : MonoBehaviour
    {
        public Transform Target;
        public Vehicle Vehicle;

        public float Distance = 14f;
        public float HeightOffset = 3.6f;
        public float MinPitch = -28f;
        public float MaxPitch = 32f;
        public float FollowLerp = 8f;

        float yaw;
        float pitch = 12f;
        bool sniper;
        Camera cam;

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam == null) cam = gameObject.AddComponent<Camera>();
            if (cam != null) cam.fieldOfView = 60f;
        }

        public void SetTarget(Transform target, Vehicle vehicle)
        {
            Target = target;
            Vehicle = vehicle;
            yaw = target != null ? target.eulerAngles.y : 0f;
            pitch = 12f;
        }

        public void AddRotation(float deltaYaw, float deltaPitch)
        {
            yaw += deltaYaw;
            pitch = Mathf.Clamp(pitch - deltaPitch, MinPitch, MaxPitch);
        }

        public void SetSniper(bool on)
        {
            sniper = on;
            if (cam != null) cam.fieldOfView = on ? 8f : 60f;
        }

        public bool Sniper { get { return sniper; } }

        void LateUpdate()
        {
            if (Target == null || cam == null) return;

            Vector3 pivot = Target.position + Vector3.up * HeightOffset;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);

            if (sniper)
            {
                // вид из орудия: камера у ствола, узкий угол
                Transform muzzle = Vehicle != null && Vehicle.BarrelTip != null ? Vehicle.BarrelTip : Target;
                Vector3 scopePos = muzzle.position - rot * Vector3.forward * 1.6f + Vector3.up * 0.6f;
                cam.transform.position = Vector3.Lerp(cam.transform.position, scopePos, Time.deltaTime * 22f);
                cam.transform.rotation = rot;
                cam.nearClipPlane = 0.15f;
                return;
            }

            cam.nearClipPlane = 0.3f;
            Vector3 desired = pivot - rot * Vector3.forward * Distance;

            // не проваливаемся сквозь препятствия
            RaycastHit hit;
            Vector3 dir = desired - pivot;
            float dist = dir.magnitude;
            if (Physics.SphereCast(pivot, 1.2f, dir.normalized, out hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                var other = hit.collider.GetComponentInParent<Vehicle>();
                if (other != Vehicle)
                    desired = pivot + dir.normalized * Mathf.Max(4f, hit.distance - 0.6f);
            }
            float groundY = 0f;
            if (Physics.Raycast(desired + Vector3.up * 80f, Vector3.down, out hit, 300f, ~0, QueryTriggerInteraction.Ignore))
                groundY = hit.point.y;
            desired.y = Mathf.Max(desired.y, groundY + 2.2f);

            cam.transform.position = Vector3.Lerp(cam.transform.position, desired, Time.deltaTime * FollowLerp);
            cam.transform.rotation = rot;
        }

        /// <summary>Точка, куда смотрит перекрестие (луч из центра камеры).</summary>
        public Vector3 GetAimPoint()
        {
            if (cam == null) return Vector3.zero;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            RaycastHit hit;
            float maxDist = 1200f;
            if (Physics.Raycast(ray, out hit, maxDist, ~0, QueryTriggerInteraction.Ignore))
            {
                var v = hit.collider.GetComponentInParent<Vehicle>();
                if (v == Vehicle) return ray.origin + ray.direction * maxDist;
                return hit.point;
            }
            return ray.origin + ray.direction * maxDist;
        }
    }

    /// <summary>Свободная камера для наблюдения после гибели.</summary>
    public class CameraFly : MonoBehaviour
    {
        float yaw, pitch;
        float speed = 90f;

        void Start()
        {
            var e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x;
        }

        void Update()
        {
            if (Input.GetMouseButton(0))
            {
                yaw += Input.GetAxis("Mouse X") * 3.5f;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3.5f, -80f, 80f);
            }
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            if (Input.GetKey(KeyCode.LeftShift)) speed = 240f;
            else speed = 90f;
            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += transform.forward;
            if (Input.GetKey(KeyCode.S)) move -= transform.forward;
            if (Input.GetKey(KeyCode.A)) move -= transform.right;
            if (Input.GetKey(KeyCode.D)) move += transform.right;
            if (Input.GetKey(KeyCode.Space)) move += Vector3.up;
            if (Input.GetKey(KeyCode.LeftControl)) move -= Vector3.up;
            transform.position += move * speed * Time.deltaTime;
        }
    }
}
