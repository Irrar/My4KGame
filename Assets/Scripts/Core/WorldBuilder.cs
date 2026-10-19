using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace My4KGame
{
    /// <summary>
    /// 场景搭建器：用代码把整个游戏"长"出来。
    ///
    /// 为什么不用手拖 —— 手册第 5 章那样在 Inspector 里一个个拖引用，
    /// 是新手最容易卡住的地方（拖错了、拖丢了、改个名字引用就空了）。
    /// 这里改成代码创建 + 自动连线，规则是"缺什么补什么"：
    ///   · 编辑器里用菜单生成一次 → 场景里有了真实物体，可以在 Hierarchy 里看结构；
    ///   · 运行时再调一次 → 已有的物体原地复用，只补缺的，不会重复创建。
    /// 这样既保留了"打开场景能看到 JudgeLine / Lane0~3 / Note"的教学价值，
    /// 又不会因为引用丢失而启动报错。
    /// </summary>
    public static class WorldBuilder
    {
        public const float CameraSize = 5.4f;
        public const float ReferenceWidth = 1920f;
        public const float ReferenceHeight = 1080f;

        // ================================================================ 入口
        /// <summary>搭建（或修复）整个游戏世界。parent 通常是场景里的 GameBootstrap 物体。</summary>
        public static void Build(Transform parent)
        {
            if (parent == null)
            {
                Debug.LogError("[My4KGame] WorldBuilder.Build 需要一个父节点。");
                return;
            }

            Camera camera = EnsureCamera(parent);
            PlayfieldView playfield = EnsurePlayfield(parent, out HitEffectPool effects, out Transform noteRoot);
            GameManager game = EnsureSystems(parent, out NoteSpawner spawner, noteRoot);
            EnsureUserInterface(parent, out Canvas canvas, out HudView hud, out MenuView menu, out ResultView result);

            // ---- 连线：把各处引用一次性接好
            game.spawner = spawner;
            game.musicSource = game.musicSource != null ? game.musicSource : game.GetComponent<AudioSource>();

            spawner.noteRoot = noteRoot;

            playfield.effects = effects;

            hud.canvas = canvas;
            menu.canvas = canvas;
            result.canvas = canvas;

            Debug.Log($"[My4KGame] 世界搭建完成：{PlayfieldLayout.LaneCount} 条轨道 · " +
                      $"相机 {(camera != null ? "就绪" : "缺失")} · UI {(canvas != null ? "就绪" : "缺失")}");
        }

        // ================================================================ 相机
        private static Camera EnsureCamera(Transform parent)
        {
            Camera camera = Camera.main;

            if (camera == null)
            {
                GameObject go = GetOrCreate(parent, "Main Camera");
                camera = GetOrAdd<Camera>(go);
            }

            camera.gameObject.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = CameraSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Theme.Background;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;
            camera.transform.localScale = Vector3.one;

            if (camera.GetComponent<AudioListener>() == null)
                camera.gameObject.AddComponent<AudioListener>();

            return camera;
        }

        // ================================================================ 系统
        private static GameManager EnsureSystems(Transform parent, out NoteSpawner spawner, Transform noteRoot)
        {
            GameObject systems = GetOrCreate(parent, "Systems");
            GameManager game = GetOrAdd<GameManager>(systems);

            if (systems.GetComponent<AudioSource>() == null) systems.AddComponent<AudioSource>();
            game.musicSource = systems.GetComponent<AudioSource>();

            GameObject spawnerGo = GetOrCreate(parent, "NoteSpawner");
            spawner = GetOrAdd<NoteSpawner>(spawnerGo);
            spawner.noteRoot = noteRoot;

            // 音符预制体优先用 Resources 里的；找不到就让 NoteSpawner 运行时自己造
            if (spawner.notePrefab == null)
            {
                var prefab = Resources.Load<GameObject>("Prefabs/Note");
                if (prefab != null) spawner.notePrefab = prefab.GetComponent<NoteController>();
            }

            return game;
        }

        // ================================================================ 舞台
        private static PlayfieldView EnsurePlayfield(Transform parent, out HitEffectPool effects, out Transform noteRoot)
        {
            GameObject root = GetOrCreate(parent, "Playfield");
            var view = GetOrAdd<PlayfieldView>(root);

            var lanes = new LaneView[PlayfieldLayout.LaneCount];
            for (int i = 0; i < PlayfieldLayout.LaneCount; i++)
            {
                lanes[i] = EnsureLane(root.transform, i);
            }

            // 轨道之间的分隔线
            for (int i = 0; i < PlayfieldLayout.LaneCount - 1; i++)
            {
                GameObject divider = GetOrCreate(root.transform, "Divider" + i);
                var sr = GetOrAdd<SpriteRenderer>(divider);
                sr.sprite = SpriteFactory.White;
                sr.color = new Color(1f, 1f, 1f, 0.045f);
                sr.sortingOrder = -9;

                float x = (PlayfieldLayout.LaneX[i] + PlayfieldLayout.LaneX[i + 1]) * 0.5f;
                divider.transform.localPosition = new Vector3(x, 0f, 0f);
                divider.transform.localScale = new Vector3(
                    0.4f,
                    PlayfieldLayout.LaneHeight / 0.04f,   // White 贴图 0.04 世界单位
                    1f);
            }

            // 判定线
            GameObject judgeGo = GetOrCreate(root.transform, "JudgeLine");
            var judgeSr = GetOrAdd<SpriteRenderer>(judgeGo);
            judgeSr.sprite = SpriteFactory.JudgeLine;
            judgeSr.color = Color.white;
            judgeSr.sortingOrder = 5;
            judgeGo.transform.localPosition = new Vector3(0f, PlayfieldLayout.JudgeLineY, 0f);
            judgeGo.transform.localScale = Vector3.one;
            view.judgeLine = judgeSr;

            // 音符父节点
            GameObject noteRootGo = GetOrCreate(root.transform, "NoteRoot");
            noteRootGo.transform.localPosition = Vector3.zero;
            noteRoot = noteRootGo.transform;

            // 特效池
            GameObject effectsGo = GetOrCreate(root.transform, "Effects");
            effects = GetOrAdd<HitEffectPool>(effectsGo);

            view.lanes = lanes;
            view.effects = effects;
            return view;
        }

        private static LaneView EnsureLane(Transform parent, int index)
        {
            GameObject laneGo = GetOrCreate(parent, "Lane" + index);
            var panel = GetOrAdd<SpriteRenderer>(laneGo);
            var laneView = GetOrAdd<LaneView>(laneGo);

            GameObject zoneGo = GetOrCreate(laneGo.transform, "Zone");
            var zone = GetOrAdd<SpriteRenderer>(zoneGo);

            laneView.panel = panel;
            laneView.zone = zone;
            laneView.Configure(index);
            return laneView;
        }

        // ================================================================ 界面
        private static void EnsureUserInterface(Transform parent, out Canvas canvas,
            out HudView hud, out MenuView menu, out ResultView result)
        {
            canvas = EnsureCanvas(parent);

            // 三个面板按绘制顺序排列：HUD 最底，Result 最上
            RectTransform hudRect = EnsureStretchedChild(canvas.transform, "HUD");
            hud = GetOrAdd<HudView>(hudRect.gameObject);
            hud.root = hudRect;
            hud.canvas = canvas;
            hud.EnsureBuilt();

            RectTransform menuRect = EnsureStretchedChild(canvas.transform, "Menu");
            menu = GetOrAdd<MenuView>(menuRect.gameObject);
            menu.root = menuRect;
            menu.canvas = canvas;
            menu.EnsureBuilt();

            RectTransform resultRect = EnsureStretchedChild(canvas.transform, "Result");
            result = GetOrAdd<ResultView>(resultRect.gameObject);
            result.root = resultRect;
            result.canvas = canvas;
            result.EnsureBuilt();

            EnsureEventSystem(parent);
        }

        private static Canvas EnsureCanvas(Transform parent)
        {
            Transform existing = parent.Find("UIRoot");
            GameObject go = existing != null ? existing.gameObject : null;

            if (go == null)
            {
                go = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster));
                go.transform.SetParent(parent, false);
            }
            else
            {
                if (go.GetComponent<Canvas>() == null) go.AddComponent<Canvas>();
                if (go.GetComponent<CanvasScaler>() == null) go.AddComponent<CanvasScaler>();
                if (go.GetComponent<GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>();
            }

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            canvas.pixelPerfect = false;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var rect = (RectTransform)go.transform;
            rect.localScale = Vector3.one;
            rect.anchoredPosition3D = Vector3.zero;

            // 统一字体：中文字体是运行时从系统里取的，靠这个组件在场景加载/编辑器里重新绑上
            if (go.GetComponent<UiFontBinder>() == null) go.AddComponent<UiFontBinder>();

            return canvas;
        }

        private static RectTransform EnsureStretchedChild(Transform parent, string name)
        {
            RectTransform rect = GetOrCreateRect(parent, name);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void EnsureEventSystem(Transform parent)
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(parent, false);
            Debug.Log("[My4KGame] 场景里没有 EventSystem，已自动补一个（按钮能点就靠它）。");
        }

        // ================================================================ 小工具
        private static GameObject GetOrCreate(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) return child.gameObject;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static RectTransform GetOrCreateRect(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child is RectTransform existing)
            {
                return existing;
            }

            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }
    }
}
