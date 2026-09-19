using UnityEngine;

namespace Samsar
{
    /// <summary>
    /// 3D-ангар: вращаем машину, смотрим ТТХ, выбираем технику и настройки боя.
    /// Все 8 машин открыты сразу (прогрессии между боями нет).
    /// </summary>
    public class HangarUI : MonoBehaviour
    {
        public Transform StagePoint;
        public Light KeyLight;

        GameObject currentTank;
        TankModel model;
        int selected;
        float turretYaw;
        float cameraYaw = 35f;
        float cameraPitch = 18f;
        float cameraDistance = 13f;
        bool dragging;
        GUIStyle title, label, small, center, big;
        bool stylesReady;
        Texture2D white;

        void Start()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            selected = GameConfig.SelectedTank;
            BuildStage();
            SpawnTank();
        }

        void Update()
        {
            // вращение камеры мышью
            if (Input.GetMouseButtonDown(0)) dragging = true;
            if (Input.GetMouseButtonUp(0)) dragging = false;
            float mx = Input.GetAxis("Mouse X");
            float my = Input.GetAxis("Mouse Y");
            if (dragging && Mathf.Abs(mx) + Mathf.Abs(my) > 0.01f)
            {
                cameraYaw += mx * 4f;
                cameraPitch = Mathf.Clamp(cameraPitch - my * 3f, 2f, 70f);
            }
            cameraDistance = Mathf.Clamp(cameraDistance - Input.GetAxis("Mouse ScrollWheel") * 12f, 7f, 34f);

            // клавиши выбора
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) Select(selected + 1);
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) Select(selected - 1);
            if (Input.GetKeyDown(KeyCode.Return)) StartBattle();

            if (currentTank != null)
            {
                float rot = cameraYaw * Mathf.Deg2Rad;
                Vector3 pos = currentTank.transform.position +
                    new Vector3(Mathf.Sin(rot) * cameraDistance * Mathf.Cos(cameraPitch * Mathf.Deg2Rad),
                                Mathf.Sin(cameraPitch * Mathf.Deg2Rad) * cameraDistance * 0.75f + 1.6f,
                                Mathf.Cos(rot) * cameraDistance * Mathf.Cos(cameraPitch * Mathf.Deg2Rad));
                var cam = Camera.main;
                if (cam != null)
                {
                    cam.transform.position = Vector3.Lerp(cam.transform.position, pos, Time.deltaTime * 6f);
                    cam.transform.LookAt(currentTank.transform.position + Vector3.up * 1.6f);
                }
                // лёгкое вращение башни
                if (model != null && model.Turret != null)
                {
                    turretYaw += Time.deltaTime * 6f;
                    model.Turret.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.35f) * 22f, 0f);
                }
            }
        }

        void BuildStage()
        {
            // пол и стены ангара
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "HangarFloor";
            floor.transform.localScale = new Vector3(6f, 1f, 6f);
            floor.GetComponent<Renderer>().sharedMaterial = MatLib.Flat(new Color(0.16f, 0.17f, 0.18f), 0.25f);

            var wallMat = MatLib.Flat(new Color(0.11f, 0.12f, 0.13f), 0.1f);
            for (int i = 0; i < 4; i++)
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "HangarWall";
                wall.transform.position = Quaternion.Euler(0f, i * 90f, 0f) * new Vector3(0f, 7f, 30f);
                wall.transform.rotation = Quaternion.Euler(0f, i * 90f + 90f, 0f);
                wall.transform.localScale = new Vector3(60f, 14f, 1f);
                wall.GetComponent<Renderer>().sharedMaterial = wallMat;
            }

            var sun = new GameObject("HangarLight");
            var l = sun.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.0f;
            l.color = new Color(0.95f, 0.95f, 1f);
            sun.transform.rotation = Quaternion.Euler(45f, 35f, 0f);
            KeyLight = l;

            var rim = new GameObject("RimLight");
            var rl = rim.AddComponent<Light>();
            rl.type = LightType.Point;
            rl.range = 30f;
            rl.intensity = 2.2f;
            rl.color = new Color(1f, 0.75f, 0.4f);
            rim.transform.position = new Vector3(-7f, 5f, -6f);

            var fill = new GameObject("FillLight");
            var fl = fill.AddComponent<Light>();
            fl.type = LightType.Point;
            fl.range = 40f;
            fl.intensity = 1.2f;
            fl.color = new Color(0.5f, 0.65f, 0.9f);
            fill.transform.position = new Vector3(8f, 6f, 8f);

            if (Camera.main == null)
            {
                var camGo = new GameObject("MainCamera");
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 45f;
                cam.backgroundColor = new Color(0.05f, 0.06f, 0.07f);
                camGo.AddComponent<AudioListener>();
            }
            Camera.main.farClipPlane = 500f;

            white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
        }

        void SpawnTank()
        {
            if (currentTank != null) Destroy(currentTank);
            var spec = TankSpecs.Get(selected);
            var bodyMat = MatLib.Metal(spec.BodyColor, 0.35f);
            var darkMat = MatLib.Metal(new Color(0.16f, 0.15f, 0.14f), 0.2f);
            var trackMat = MatLib.Flat(new Color(0.07f, 0.07f, 0.07f), 0.1f);
            var glassMat = MatLib.Metal(new Color(0.25f, 0.35f, 0.4f), 0.85f);
            model = TankModel.Build(spec, bodyMat, darkMat, trackMat, glassMat);
            currentTank = model.Root;
            var rb = currentTank.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.constraints = RigidbodyConstraints.FreezeAll;
            }
            currentTank.transform.rotation = Quaternion.Euler(0f, -35f, 0f);
            currentTank.transform.position = new Vector3(0f, 0.2f, 0f);
            GameConfig.SelectedTank = selected;
        }

        void Select(int index)
        {
            selected = (index % TankSpecs.Count + TankSpecs.Count) % TankSpecs.Count;
            SpawnTank();
            AudioSynth.PlayUi("click", 0.5f);
        }

        void StartBattle()
        {
            AudioSynth.PlayUi("click", 0.7f);
            GameManager.StartBattle(selected, GameConfig.BotCount, GameConfig.Difficulty, GameConfig.Squad);
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
            big = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            label = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            center = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            stylesReady = true;
        }

        void Panel(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        void OnGUI()
        {
            if (currentTank == null) return;
            EnsureStyles();
            var spec = TankSpecs.Get(selected);

            // левая панель: список машин
            var listRect = new Rect(16f, 90f, 260f, Screen.height - 130f);
            Panel(listRect, new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(new Rect(listRect.x + 12f, listRect.y + 6f, 240f, 28f), "ТЕХНИКА (8 машин)", big);
            for (int i = 0; i < TankSpecs.Count; i++)
            {
                var s = TankSpecs.Get(i);
                var btn = new Rect(listRect.x + 10f, listRect.y + 40f + i * 42f, 240f, 38f);
                Panel(btn, i == selected ? new Color(0.85f, 0.5f, 0.15f, 0.9f) : new Color(0.12f, 0.14f, 0.16f, 0.9f));
                GUI.Label(new Rect(btn.x + 8f, btn.y + 2f, 224f, 20f), "<b>" + s.Name + "</b>  (" + TankSpecs.ClassName(s.Class) + ")", label);
                GUI.Label(new Rect(btn.x + 8f, btn.y + 19f, 224f, 18f), s.Tagline, small);
                if (GUI.Button(btn, GUIContent.none)) Select(i);
            }

            // правая панель: ТТХ
            var st = new Rect(Screen.width - 400f, 90f, 384f, Screen.height - 130f);
            Panel(st, new Color(0f, 0f, 0f, 0.6f));
            float y = st.y + 8f;
            GUI.Label(new Rect(st.x + 14f, y, 356f, 30f), spec.Name, title);
            y += 38f;
            GUI.Label(new Rect(st.x + 14f, y, 356f, 20f), TankSpecs.ClassName(spec.Class) + " · " + spec.Tagline, small);
            y += 28f;
            Line(st, ref y, "Прочность", Mathf.RoundToInt(spec.MaxHealth) + " ед.");
            Line(st, ref y, "Броня (лоб/борт/корма)", Mathf.RoundToInt(spec.ArmorFront) + " / " + Mathf.RoundToInt(spec.ArmorSide) + " / " + Mathf.RoundToInt(spec.ArmorRear) + " мм");
            Line(st, ref y, "Скорость вперёд", Mathf.RoundToInt(spec.MaxSpeedForward) + " км/ч");
            Line(st, ref y, "Поворот корпуса", Mathf.RoundToInt(spec.TurnRate) + " °/с");
            Line(st, ref y, "Урон орудия", Mathf.RoundToInt(spec.Damage) + " ед.");
            Line(st, ref y, "Бронепробитие", Mathf.RoundToInt(spec.Penetration) + " мм");
            Line(st, ref y, "Перезарядка", spec.Reload.ToString("0.0") + " с");
            Line(st, ref y, "Сведение / разброс", spec.AimTime.ToString("0.0") + " с / " + spec.Dispersion.ToString("0.00"));
            Line(st, ref y, "Боекомплект", spec.AmmoStart + " из " + spec.AmmoMax);
            Line(st, ref y, "Обзор / обнаружение", Mathf.RoundToInt(spec.ViewRange) + " м / " + Mathf.RoundToInt(spec.DetectRadius) + " м");
            y += 8f;

            GUI.Label(new Rect(st.x + 14f, y, 356f, 24f), "<b>Боевые умения</b>", label);
            y += 26f;
            GUI.Label(new Rect(st.x + 14f, y, 356f, 20f), "[Q] " + TankSpecs.AbilityName(spec.Ability1) + " — " + spec.Ability1Cooldown + " с", label);
            GUI.Label(new Rect(st.x + 14f, y + 16f, 356f, 30f), TankSpecs.AbilityDesc(spec.Ability1), small);
            y += 52f;
            GUI.Label(new Rect(st.x + 14f, y, 356f, 20f), "[E] " + TankSpecs.AbilityName(spec.Ability2) + " — " + spec.Ability2Cooldown + " с", label);
            GUI.Label(new Rect(st.x + 14f, y + 16f, 356f, 30f), TankSpecs.AbilityDesc(spec.Ability2), small);
            y += 52f;

            // настройки боя
            GUI.Label(new Rect(st.x + 14f, y, 356f, 24f), "<b>Настройки боя</b>", label);
            y += 26f;
            GUI.Label(new Rect(st.x + 14f, y, 356f, 20f), "Участников: " + GameConfig.BotCount, label);
            GameConfig.BotCount = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(st.x + 14f, y + 20f, 356f, 18f), GameConfig.BotCount, 10f, 40f));
            y += 46f;
            GUI.Label(new Rect(st.x + 14f, y, 356f, 20f), "Сложность ботов: " + GameConfig.DifficultyName, label);
            y += 22f;
            for (int i = 0; i < 3; i++)
            {
                var b = new Rect(st.x + 14f + i * 120f, y, 112f, 32f);
                Panel(b, GameConfig.Difficulty == i ? new Color(0.85f, 0.5f, 0.15f, 0.9f) : new Color(0.14f, 0.16f, 0.18f, 0.9f));
                if (GUI.Button(b, i == 0 ? "Лёгкая" : i == 1 ? "Средняя" : "Сложная"))
                {
                    GameConfig.Difficulty = i;
                    AudioSynth.PlayUi("click", 0.4f);
                }
            }
            y += 40f;
            GameConfig.Squad = GUI.Toggle(new Rect(st.x + 14f, y, 356f, 24f), GameConfig.Squad, " Игра во взводе (союзник-бот)");
            y += 32f;

            if (GUI.Button(new Rect(st.x + 14f, st.y + st.height - 58f, 356f, 46f), "В БОЙ (Enter)")) StartBattle();

            // подсказки сверху
            GUI.Label(new Rect(300f, 18f, Screen.width - 720f, 26f), "АНГАР — выбери машину, зажми ЛКМ для вращения, колесо — приближение", center);
            GUI.Label(new Rect(300f, 44f, Screen.width - 720f, 22f), "Все машины доступны сразу. Прогрессии и доната нет.", center);

            if (GUI.Button(new Rect(16f, 20f, 200f, 40f), "← В главное меню")) GameManager.ToMenu();
            if (GUI.Button(new Rect(226f, 20f, 200f, 40f), "Полигон")) GameManager.StartTestRange(selected);
        }

        void Line(Rect panel, ref float y, string name, string value)
        {
            GUI.Label(new Rect(panel.x + 14f, y, 230f, 20f), name, label);
            var right = new GUIStyle(label) { alignment = TextAnchor.MiddleRight };
            GUI.Label(new Rect(panel.x + 150f, y, 220f, 20f), "<b>" + value + "</b>", right);
            y += 21f;
        }
    }
}
