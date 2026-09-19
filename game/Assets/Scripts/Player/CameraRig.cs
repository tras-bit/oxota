// Камера: обзор от третьего лица + снайперский прицел ×8, защита от заезда в геометрию.
using UnityEngine;

namespace Samsar
{
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig Instance;

        public TankController target;
        public Camera cam;
        public float distance = 16f;
        public float minDistance = 3f;
        public float height = 4.2f;
        public float sensitivity = 3.2f;

        float yaw, pitch = 14f;
        float sniperBlend;

        void Awake()
        {
            Instance = this;
            cam = GetComponent<Camera>();
            if (cam == null) cam = gameObject.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.15f;
            cam.farClipPlane = 5000f;
        }

        public void Follow(TankController t)
        {
            target = t;
            yaw = t.transform.eulerAngles.y;
        }

        void LateUpdate()
        {
            if (target == null) return;

            bool sniper = PlayerInput.Instance != null && PlayerInput.Instance.SniperMode && !target.Dead;
            sniperBlend = Mathf.MoveTowards(sniperBlend, sniper ? 1f : 0f, Time.deltaTime * 4f);

            if (!sniper)
            {
                yaw += Input.GetAxis("Mouse X") * sensitivity;
                pitch -= Input.GetAxis("Mouse Y") * sensitivity;
                pitch = Mathf.Clamp(pitch, -8f, 55f);
                distance = Mathf.Clamp(distance - Input.GetAxis("Mouse ScrollWheel") * 8f, 8f, 30f);
            }

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pivot = target.transform.position + Vector3.up * (1.6f + height * 0.15f);
            Vector3 desired = pivot + rot * new Vector3(0f, 0f, -distance);

            // не проваливаться сквозь объекты
            RaycastHit hit;
            Vector3 dir = desired - pivot;
            if (Physics.SphereCast(pivot, 0.4f, dir.normalized, out hit, dir.magnitude, ~0, QueryTriggerInteraction.Ignore))
                desired = pivot + dir.normalized * Mathf.Max(minDistance, hit.distance - 0.3f);

            transform.position = Vector3.Lerp(transform.position, desired, Time.deltaTime * 9f);
            Quaternion look = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * 9f);

            // снайперский режим: смотрим вдоль орудия
            if (sniperBlend > 0.01f && target.rig != null && target.rig.GunPivot != null)
            {
                Vector3 gunPos = target.rig.GunTip.position - target.rig.GunPivot.forward * 1.2f;
                transform.position = Vector3.Lerp(transform.position, gunPos, sniperBlend);
                Quaternion gunLook = Quaternion.LookRotation(target.rig.GunPivot.forward, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, gunLook, sniperBlend);
                cam.fieldOfView = Mathf.Lerp(60f, 7.5f, sniperBlend);
                HUD.SetSniperMode(true);
            }
            else
            {
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 60f, Time.deltaTime * 6f);
                HUD.SetSniperMode(false);
            }
        }
    }
}
