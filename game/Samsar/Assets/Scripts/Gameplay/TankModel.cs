using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    /// <summary>Модель машины для прототипа: собирается из примитивов, детали зависит от класса и машины.</summary>
    public class TankModel
    {
        public GameObject Root;
        public Transform Turret;
        public Transform Barrel;
        public Transform BarrelTip;
        public BoxCollider HullCollider;
        public BoxCollider TurretCollider;
        public BoxCollider BarrelCollider;
        public List<Transform> Wheels = new List<Transform>();
        public float HullLength = 6f;
        public float HullWidth = 3f;
        public float TurretYawLimit = 360f;   // у ПТ-САУ рубка поворачивается ограниченно
        public float RideHeight = 0.75f;

        // материалы кузова — общие для всех деталей машины (для затемнения подбитой техники)
        public Material BodyMaterial, DarkMaterial, TrackMaterial, GlassMaterial;
        readonly Color[] originalColors = new Color[4];

        public void StoreColors()
        {
            if (BodyMaterial != null) originalColors[0] = BodyMaterial.color;
            if (DarkMaterial != null) originalColors[1] = DarkMaterial.color;
            if (TrackMaterial != null) originalColors[2] = TrackMaterial.color;
            if (GlassMaterial != null) originalColors[3] = GlassMaterial.color;
        }

        /// <summary>Подбитая машина темнеет, после возрождения цвет возвращается.</summary>
        public void SetBurnt(bool burnt)
        {
            SetMat(BodyMaterial, 0, burnt);
            SetMat(DarkMaterial, 1, burnt);
            SetMat(TrackMaterial, 2, burnt);
            SetMat(GlassMaterial, 3, burnt);
        }

        void SetMat(Material m, int index, bool burnt)
        {
            if (m == null) return;
            Color baseColor = originalColors[index];
            m.color = burnt ? Color.Lerp(baseColor, new Color(0.07f, 0.065f, 0.06f), 0.88f) : baseColor;
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", burnt ? 0.2f : (index == 0 ? 0.32f : 0.2f));
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", burnt ? 0.05f : (index == 3 ? 0.85f : 0.3f));
        }

        static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }

        static GameObject Primitive(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Quaternion localRot, Material mat, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            var rend = go.GetComponent<Renderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        /// <summary>Собрать машину по спецификации.</summary>
        public static TankModel Build(TankSpec spec, Material bodyMat, Material darkMat, Material trackMat, Material glassMat)
        {
            var model = new TankModel();
            var root = new GameObject("Tank_" + spec.Id);
            model.Root = root;
            model.BodyMaterial = bodyMat;
            model.DarkMaterial = darkMat;
            model.TrackMaterial = trackMat;
            model.GlassMaterial = glassMat;
            model.StoreColors();

            bool td = spec.Class == TankClass.Td;
            bool heavy = spec.Class == TankClass.Heavy;
            bool light = spec.Class == TankClass.Light;

            float width = light ? 2.7f : heavy ? 3.6f : td ? 3.1f : 3.2f;
            float length = light ? 4.9f : heavy ? 7.2f : td ? 6.6f : 6.2f;
            float hullHeight = light ? 0.85f : heavy ? 1.25f : td ? 0.8f : 1.05f;
            model.HullWidth = width;
            model.HullLength = length;
            model.RideHeight = light ? 0.7f : heavy ? 0.95f : 0.82f;

            var hull = new GameObject("Hull");
            hull.transform.SetParent(root.transform, false);
            model.BodyRoot = hull.transform;

            // корпус и наклонные листы
            Primitive(PrimitiveType.Cube, hull.transform, new Vector3(0f, hullHeight * 0.5f, 0f),
                new Vector3(width * 0.94f, hullHeight, length * 0.86f), Quaternion.identity, bodyMat, "hull_body");
            Primitive(PrimitiveType.Cube, hull.transform, new Vector3(0f, hullHeight * 0.85f, length * 0.36f),
                new Vector3(width * 0.92f, hullHeight * 0.8f, length * 0.34f),
                Quaternion.Euler(-32f, 0f, 0f), bodyMat, "glacis");
            Primitive(PrimitiveType.Cube, hull.transform, new Vector3(0f, hullHeight * 0.8f, -length * 0.4f),
                new Vector3(width * 0.9f, hullHeight * 0.7f, length * 0.2f),
                Quaternion.Euler(18f, 0f, 0f), bodyMat, "rear_plate");
            Primitive(PrimitiveType.Cube, hull.transform, new Vector3(0f, hullHeight * 1.28f, -length * 0.18f),
                new Vector3(width * 0.86f, hullHeight * 0.5f, length * 0.4f), Quaternion.identity, bodyMat, "engine_deck");

            // надстройки/полки
            Primitive(PrimitiveType.Cube, hull.transform, new Vector3(-width * 0.5f, hullHeight * 1.35f, -length * 0.1f),
                new Vector3(width * 0.22f, 0.22f, length * 0.5f), Quaternion.identity, darkMat, "box_left");
            Primitive(PrimitiveType.Cube, hull.transform, new Vector3(width * 0.5f, hullHeight * 1.35f, -length * 0.1f),
                new Vector3(width * 0.22f, 0.22f, length * 0.5f), Quaternion.identity, darkMat, "box_right");

            // гусеницы и катки
            float trackWidth = width * 0.28f;
            float trackHeight = light ? 0.75f : heavy ? 1.0f : 0.85f;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (width * 0.5f - trackWidth * 0.5f);
                Primitive(PrimitiveType.Cube, hull.transform, new Vector3(x, trackHeight * 0.5f, 0f),
                    new Vector3(trackWidth, trackHeight, length * 1.02f), Quaternion.identity, trackMat, "track");
                int wheelCount = light ? 6 : heavy ? 7 : 6;
                for (int w = 0; w < wheelCount; w++)
                {
                    float t = wheelCount > 1 ? w / (float)(wheelCount - 1) : 0.5f;
                    float z = Mathf.Lerp(-length * 0.44f, length * 0.44f, t);
                    var wheel = Primitive(PrimitiveType.Cylinder, hull.transform,
                        new Vector3(x, trackHeight * 0.46f, z),
                        new Vector3(trackHeight * 0.72f, trackWidth * 0.34f, trackHeight * 0.72f),
                        Quaternion.Euler(0f, 0f, 90f), darkMat, "wheel");
                    model.Wheels.Add(wheel.transform);
                }
                // ведущее колесо и ленивец
                Primitive(PrimitiveType.Cylinder, hull.transform, new Vector3(x, trackHeight * 0.72f, length * 0.48f),
                    new Vector3(trackHeight * 0.6f, trackWidth * 0.3f, trackHeight * 0.6f), Quaternion.Euler(0f, 0f, 90f), darkMat, "sprocket");
                Primitive(PrimitiveType.Cylinder, hull.transform, new Vector3(x, trackHeight * 0.6f, -length * 0.48f),
                    new Vector3(trackHeight * 0.6f, trackWidth * 0.3f, trackHeight * 0.6f), Quaternion.Euler(0f, 0f, 90f), darkMat, "idler");
            }

            // крылья
            for (int side = -1; side <= 1; side += 2)
            {
                Primitive(PrimitiveType.Cube, hull.transform,
                    new Vector3(side * (width * 0.5f - trackWidth * 0.5f), trackHeight + 0.06f, 0f),
                    new Vector3(trackWidth * 1.15f, 0.09f, length * 1.0f), Quaternion.identity, bodyMat, "fender");
            }

            // башня / рубка
            var turretRoot = new GameObject("TurretPivot");
            turretRoot.transform.SetParent(hull.transform, false);
            float turretY = hullHeight * 1.32f;
            turretRoot.transform.localPosition = new Vector3(0f, turretY, td ? -length * 0.12f : length * 0.02f);
            model.Turret = turretRoot.transform;

            float turretLen = heavy ? 3.6f : light ? 2.8f : 3.2f;
            float turretWid = heavy ? 3.0f : light ? 2.1f : 2.6f;
            float turretHeight = td ? 0.7f : heavy ? 0.95f : 0.8f;

            if (td)
            {
                Primitive(PrimitiveType.Cube, turretRoot.transform, new Vector3(0f, turretHeight * 0.5f, 0f),
                    new Vector3(turretWid, turretHeight, turretLen * 0.8f), Quaternion.identity, bodyMat, "casemate");
                Primitive(PrimitiveType.Cube, turretRoot.transform, new Vector3(0f, turretHeight * 0.95f, turretLen * 0.3f),
                    new Vector3(turretWid * 0.95f, turretHeight * 0.5f, turretLen * 0.35f),
                    Quaternion.Euler(-22f, 0f, 0f), bodyMat, "casemate_glacis");
                model.TurretYawLimit = 16f;
            }
            else if (heavy)
            {
                Primitive(PrimitiveType.Cylinder, turretRoot.transform, new Vector3(0f, turretHeight * 0.5f, 0f),
                    new Vector3(turretWid * 0.5f, turretHeight * 0.5f, turretWid * 0.5f), Quaternion.identity, bodyMat, "turret");
                Primitive(PrimitiveType.Cube, turretRoot.transform, new Vector3(0f, turretHeight * 1.05f, -turretLen * 0.28f),
                    new Vector3(turretWid * 0.62f, 0.34f, turretLen * 0.34f), Quaternion.identity, darkMat, "cupola");
            }
            else
            {
                Primitive(PrimitiveType.Cylinder, turretRoot.transform, new Vector3(0f, turretHeight * 0.5f, 0f),
                    new Vector3(turretWid * 0.5f, turretHeight * 0.5f, turretWid * 0.5f), Quaternion.identity, bodyMat, "turret");
                Primitive(PrimitiveType.Cube, turretRoot.transform, new Vector3(0f, turretHeight * 1.0f, -turretLen * 0.2f),
                    new Vector3(turretWid * 0.5f, 0.3f, turretLen * 0.28f), Quaternion.identity, darkMat, "cupola");
            }

            // маска орудия
            Primitive(PrimitiveType.Cube, turretRoot.transform, new Vector3(0f, turretHeight * 0.55f, turretLen * 0.4f),
                new Vector3(turretWid * 0.5f, turretHeight * 0.7f, turretLen * 0.22f), Quaternion.identity, bodyMat, "mantlet");

            // оптика и пулемёт
            Primitive(PrimitiveType.Cube, turretRoot.transform, new Vector3(-turretWid * 0.3f, turretHeight * 1.0f, turretLen * 0.28f),
                new Vector3(0.42f, 0.3f, 0.3f), Quaternion.identity, glassMat, "optic");
            Primitive(PrimitiveType.Cylinder, turretRoot.transform, new Vector3(turretWid * 0.42f, turretHeight * 0.95f, turretLen * 0.3f),
                new Vector3(0.09f, 0.5f, 0.09f), Quaternion.Euler(90f, 0f, 0f), darkMat, "mg");

            // антенна
            Primitive(PrimitiveType.Cylinder, turretRoot.transform, new Vector3(turretWid * 0.36f, turretHeight + 0.9f, -turretLen * 0.3f),
                new Vector3(0.035f, 0.9f, 0.035f), Quaternion.Euler(6f, 0f, 0f), darkMat, "antenna");

            // орудие
            var barrelPivot = new GameObject("BarrelPivot");
            barrelPivot.transform.SetParent(turretRoot.transform, false);
            barrelPivot.transform.localPosition = new Vector3(0f, turretHeight * 0.55f, turretLen * 0.35f);
            model.Barrel = barrelPivot.transform;

            float barrelLength = td ? 5.6f : heavy ? 4.6f : light ? 3.8f : 4.4f;
            float barrelRadius = spec.Damage > 450f ? 0.16f : 0.125f;
            Primitive(PrimitiveType.Cylinder, barrelPivot.transform, new Vector3(0f, 0f, barrelLength * 0.5f),
                new Vector3(barrelRadius * 2f, barrelLength * 0.5f, barrelRadius * 2f),
                Quaternion.Euler(90f, 0f, 0f), bodyMat, "barrel");
            Primitive(PrimitiveType.Cylinder, barrelPivot.transform, new Vector3(0f, 0f, barrelLength * 0.95f),
                new Vector3(barrelRadius * 2.6f, barrelLength * 0.09f, barrelRadius * 2.6f),
                Quaternion.Euler(90f, 0f, 0f), darkMat, "muzzle_brake");

            var tip = new GameObject("BarrelTip");
            tip.transform.SetParent(barrelPivot.transform, false);
            tip.transform.localPosition = new Vector3(0f, 0f, barrelLength * 1.06f);
            model.BarrelTip = tip.transform;

            // ==== коллайдеры ====
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = spec.Mass;
            rb.drag = 0.05f;
            rb.angularDrag = 4f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.centerOfMass = new Vector3(0f, -0.4f, 0f);

            var hullCol = root.AddComponent<BoxCollider>();
            hullCol.size = new Vector3(width, hullHeight * 1.9f, length * 1.02f);
            hullCol.center = new Vector3(0f, hullHeight * 1.0f, 0f);
            hullCol.material = new PhysicMaterial("metal") { dynamicFriction = 0.55f, staticFriction = 0.6f, bounciness = 0.05f };
            model.HullCollider = hullCol;

            var turretColGo = new GameObject("TurretCollider");
            turretColGo.transform.SetParent(turretRoot.transform, false);
            turretColGo.transform.localPosition = new Vector3(0f, turretHeight * 0.5f, 0f);
            var tCol = turretColGo.AddComponent<BoxCollider>();
            tCol.size = new Vector3(turretWid, turretHeight, turretLen * 0.9f);
            model.TurretCollider = tCol;

            // если есть модель из Blender — подменяем визуальную часть
            TankImportedVisual.TryAttach(model, spec);

            var barrelColGo = new GameObject("BarrelCollider");
            barrelColGo.transform.SetParent(barrelPivot.transform, false);
            barrelColGo.transform.localPosition = new Vector3(0f, 0f, barrelLength * 0.5f);
            var bCol = barrelColGo.AddComponent<BoxCollider>();
            bCol.size = new Vector3(barrelRadius * 3f, barrelRadius * 3f, barrelLength);
            model.BarrelCollider = bCol;

            return model;
        }

        /// <summary>Обновить вращение катков (визуал).</summary>
        public void SpinWheels(float speed)
        {
            float rate = speed * 40f * Time.deltaTime;
            for (int i = 0; i < Wheels.Count; i++)
                Wheels[i].Rotate(Vector3.right, rate, Space.Self);
        }
    }
}
