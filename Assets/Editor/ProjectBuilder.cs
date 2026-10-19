#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace My4KGame.EditorTools
{
    /// <summary>
    /// 编辑器侧的工程工具（菜单栏 My4KGame 下面那一排）。
    ///
    /// 它负责把"代码里的东西"落成"工程里的资产"：
    ///   · 把代码画的贴图写成 Assets/Resources/Art/*.png（这样场景里看得见、能改）
    ///   · 生成音符预制体 Assets/Resources/Prefabs/Note.prefab
    ///   · 生成并保存场景 Assets/Scenes/My4KGame.unity
    ///
    /// 这样，工程的运行时并不依赖这些资产（找不到就现场画），
    /// 但这些资产在的时候，编辑器里打开场景就能看到完整的层级结构，
    /// 方便对着手册第 5 章逐个讲解 JudgeLine / Lane0~3 / Note。
    /// </summary>
    public static class ProjectBuilder
    {
        public const string ScenePath = "Assets/Scenes/My4KGame.unity";
        public const string ArtFolder = "Assets/Resources/Art";
        public const string PrefabFolder = "Assets/Resources/Prefabs";
        public const string NotePrefabPath = PrefabFolder + "/Note.prefab";
        public const string PreviewFolder = "Docs";

        // ================================================================ 菜单
        [MenuItem("My4KGame/1. 一键生成全部（贴图 + 预制体 + 场景）", false, 1)]
        public static void BuildAllMenu()
        {
            bool ok = BuildAll();
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("My4KGame",
                    ok
                        ? "生成完成。\n\n接下来直接按顶部的 ▶ 播放键就能玩。\n\n" +
                          "结构：My4KGame（入口）→ Playfield（轨道/判定线/音符） + Systems（管理器） + UIRoot（界面）"
                        : "生成过程中出现问题，请看 Console 的红色报错。",
                    "好");
            }
        }

        [MenuItem("My4KGame/2. 只生成贴图", false, 2)]
        public static void BuildSpritesMenu()
        {
            BuildSprites();
            EditorUtility.DisplayDialog("My4KGame",
                "贴图已生成到 Assets/Resources/Art。\n可以直接用图片编辑软件替换同名 png。", "好");
        }

        [MenuItem("My4KGame/3. 重新生成音符预制体", false, 3)]
        public static void BuildNotePrefabMenu()
        {
            BuildNotePrefab();
            EditorUtility.DisplayDialog("My4KGame", "音符预制体已生成：" + NotePrefabPath, "好");
        }

        [MenuItem("My4KGame/4. 重新生成游戏场景", false, 4)]
        public static void BuildSceneMenu()
        {
            if (BuildScene())
            {
                EditorUtility.DisplayDialog("My4KGame",
                    "场景已生成：" + ScenePath + "\n\n" +
                    "打开它能看到完整层级：\n" +
                    "  My4KGame（GameBootstrap 入口）\n" +
                    "  ├─ Main Camera\n" +
                    "  ├─ Playfield（Lane0~3 / JudgeLine / NoteRoot / Effects）\n" +
                    "  ├─ Systems（GameManager + AudioSource）、NoteSpawner\n" +
                    "  └─ UIRoot（HUD / Menu / Result）", "好");
            }
        }

        [MenuItem("My4KGame/5. 自检（谱面 / 音频 / 引用 / 计分）", false, 5)]
        public static void SelfCheckMenu()
        {
            bool ok = SelfCheck();
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("My4KGame 自检",
                    ok ? "全部检查通过，详细结果已打印到 Console。" : "自检发现问题，请看 Console 的红色报错。",
                    "好");
            }
        }

        [MenuItem("My4KGame/6. 渲染预览图到 Docs/", false, 6)]
        public static void CapturePreviewMenu()
        {
            bool ok = CapturePreview();
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("My4KGame",
                    ok ? "预览图已保存到 Docs/ 目录。" : "渲染失败，请看 Console。", "好");
            }
        }

        [MenuItem("My4KGame/7. 播放（打开场景并运行）", false, 7)]
        public static void PlayMenu()
        {
            if (!File.Exists(ScenePath)) BuildScene();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("My4KGame/10. 清空成绩纪录（最佳分数/校准值）", false, 10)]
        public static void ClearScoresMenu()
        {
            SaveData.ClearAll();
            Debug.Log("[My4KGame] 已清空所有最佳成绩与音画校准记录。");
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("My4KGame", "最佳成绩与音画校准记录已清空。", "好");
        }

        /// <summary>命令行专用：只清成绩，不做其它事（给自动化测试收尾用）。</summary>
        public static void ClearScoresFromCommandLine()
        {
            SaveData.ClearAll();
            Debug.Log("[My4KGame] CLEAR_SCORES_DONE");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ================================================================ 一键生成
        /// <summary>贴图 → 预制体 → 场景，一条龙。</summary>
        public static bool BuildAll()
        {
            bool ok = true;
            try
            {
                EnsureFolders();
                ok &= BuildSprites();
                ok &= BuildNotePrefab();
                ok &= BuildScene();
                ConfigurePlayerSettings();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(ok
                    ? "[My4KGame] 一键生成完成：贴图 / 预制体 / 场景 都就绪了。"
                    : "[My4KGame] 一键生成结束，但有步骤失败，请看上面的报错。");
            }
            catch (Exception ex)
            {
                Debug.LogError("[My4KGame] 一键生成异常：" + ex);
                ok = false;
            }
            return ok;
        }

        private static void EnsureFolders()
        {
            string[] folders =
            {
                "Assets/Scripts", "Assets/Scenes", "Assets/Resources",
                "Assets/Resources/Art", "Assets/Resources/Prefabs",
                "Assets/Resources/Charts", "Assets/Resources/Audio", PreviewFolder,
            };
            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            }
            AssetDatabase.Refresh();
        }

        // ================================================================ 贴图
        /// <summary>把 SpriteFactory 的程序贴图写成 png 资源。</summary>
        public static bool BuildSprites()
        {
            Directory.CreateDirectory(ArtFolder);

            var kinds = (SpriteKind[])Enum.GetValues(typeof(SpriteKind));
            foreach (SpriteKind kind in kinds)
            {
                Texture2D texture = SpriteFactory.Generate(kind);
                byte[] png = texture.EncodeToPNG();
                Object.DestroyImmediate(texture);

                string path = ArtPath(kind);
                File.WriteAllBytes(path, png);
            }

            AssetDatabase.Refresh();

            foreach (SpriteKind kind in kinds)
            {
                string path = ArtPath(kind);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogError($"[My4KGame] 导入贴图失败：{path}");
                    return false;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = SpriteFactory.PixelsPerUnit;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }

            SpriteFactory.ClearCache();
            AssetDatabase.Refresh();
            Debug.Log($"[My4KGame] 贴图已生成：{kinds.Length} 张（{ArtFolder}）");
            return true;
        }

        private static string ArtPath(SpriteKind kind) => $"{ArtFolder}/{kind}.png";

        private static Sprite LoadSprite(SpriteKind kind)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath(kind));
            if (sprite != null) return sprite;

            // 资源还没导入完的时候兜底：现场画一张
            return SpriteFactory.Get(kind);
        }

        // ================================================================ 音符预制体
        public static bool BuildNotePrefab()
        {
            Directory.CreateDirectory(PrefabFolder);

            var root = new GameObject("Note");
            var controller = root.AddComponent<NoteController>();

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(root.transform, false);
            var body = bodyGo.AddComponent<SpriteRenderer>();
            body.sprite = LoadSprite(SpriteKind.Note);
            body.color = Color.white;
            body.sortingOrder = 0;

            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(root.transform, false);
            var glow = glowGo.AddComponent<SpriteRenderer>();
            glow.sprite = LoadSprite(SpriteKind.Glow);
            glow.sortingOrder = -2;
            glow.transform.localScale = new Vector3(1.35f, 1.5f, 1f);

            controller.body = body;
            controller.glow = glow;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, NotePrefabPath);
            Object.DestroyImmediate(root);

            if (prefab == null)
            {
                Debug.LogError("[My4KGame] 音符预制体保存失败。");
                return false;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[My4KGame] 音符预制体已生成：" + NotePrefabPath);
            return true;
        }

        // ================================================================ 场景
        public static bool BuildScene()
        {
            Directory.CreateDirectory("Assets/Scenes");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var rootGo = new GameObject("My4KGame");
            var bootstrap = rootGo.AddComponent<GameBootstrap>();
            bootstrap.autoBuild = true;
            bootstrap.targetFrameRate = 0;

            // 用运行时同一套代码搭出层级，保证"场景里的样子"和"跑起来的样子"是同一个东西
            WorldBuilder.Build(rootGo.transform);

            // 显式挂上音符预制体（WorldBuilder 也会从 Resources 找，这里是再保险一次）
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NotePrefabPath);
            var spawner = Object.FindObjectOfType<NoteSpawner>();
            if (spawner != null && prefab != null)
            {
                spawner.notePrefab = prefab.GetComponent<NoteController>();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            if (!saved)
            {
                Debug.LogError("[My4KGame] 场景保存失败：" + ScenePath);
                return false;
            }

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int index = scenes.FindIndex(s => s.path == ScenePath);
            if (index < 0) scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            else scenes[index].enabled = true;
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log("[My4KGame] 场景已生成并加入 Build Settings：" + ScenePath);
            return true;
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.productName = "My4KGame";
            PlayerSettings.companyName = "HCI Course";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            QualitySettings.vSyncCount = 1;
            Debug.Log("[My4KGame] Player Settings 已配置：窗口化 1920×1080，允许后台运行。");
        }

        // ================================================================ 自检
        /// <summary>把"能自动检查的东西"全部查一遍。返回 false 表示有硬伤。</summary>
        public static bool SelfCheck()
        {
            var sb = new StringBuilder();
            bool pass = true;
            sb.AppendLine("================ My4KGame 自检 ================");

            // ---- 1. 贴图
            var kinds = (SpriteKind[])Enum.GetValues(typeof(SpriteKind));
            int spriteMissing = 0;
            foreach (SpriteKind kind in kinds)
            {
                if (AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath(kind)) == null) spriteMissing++;
            }
            sb.AppendLine(spriteMissing == 0
                ? $"[OK]   贴图 {kinds.Length} 张齐全（{ArtFolder}）"
                : $"[警告] 缺少 {spriteMissing} 张贴图（游戏仍能跑，会现场用代码画）");
            if (spriteMissing > 0) pass = false;

            // ---- 2. 预制体
            var notePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NotePrefabPath);
            if (notePrefab == null)
            {
                sb.AppendLine("[错误] 音符预制体不存在：" + NotePrefabPath);
                pass = false;
            }
            else
            {
                var nc = notePrefab.GetComponent<NoteController>();
                sb.AppendLine(nc != null && nc.body != null && nc.glow != null
                    ? "[OK]   音符预制体结构正确（NoteController + Body + Glow）"
                    : "[错误] 音符预制体缺少组件或被改坏");
                if (nc == null || nc.body == null) pass = false;
            }

            // ---- 3. 场景
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset == null)
            {
                sb.AppendLine("[错误] 场景不存在：" + ScenePath);
                pass = false;
            }
            else
            {
                sb.AppendLine("[OK]   场景文件存在：" + ScenePath);
                var gm = Object.FindObjectOfType<GameManager>();
                var spawner = Object.FindObjectOfType<NoteSpawner>();
                var hud = Object.FindObjectOfType<HudView>();
                var menu = Object.FindObjectOfType<MenuView>();
                var result = Object.FindObjectOfType<ResultView>();
                var playfield = Object.FindObjectOfType<PlayfieldView>();
                var camera = Camera.main;

                CheckRef(sb, ref pass, "主相机", camera != null);
                CheckRef(sb, ref pass, "GameManager", gm != null);
                CheckRef(sb, ref pass, "NoteSpawner", spawner != null);
                CheckRef(sb, ref pass, "PlayfieldView", playfield != null);
                CheckRef(sb, ref pass, "HudView", hud != null);
                CheckRef(sb, ref pass, "MenuView", menu != null);
                CheckRef(sb, ref pass, "ResultView", result != null);
                if (camera != null)
                    sb.AppendLine($"       相机：正交 size {camera.orthographicSize:F1}，背景 {ColorUtility.ToHtmlStringRGB(camera.backgroundColor)}");

                if (gm != null && spawner != null && gm.spawner != spawner)
                {
                    sb.AppendLine("[警告] GameManager.spawner 没连上 NoteSpawner（运行时 WorldBuilder 会自动补）");
                }
                if (spawner != null && spawner.notePrefab == null)
                {
                    sb.AppendLine("[警告] NoteSpawner.notePrefab 为空（运行时会用代码造音符，只是预制体没接上）");
                }
                if (playfield != null) CheckLanes(sb, ref pass, playfield);
            }

            // ---- 4. 谱面与音频
            var entries = ChartLibrary.Reload();
            sb.AppendLine($"[信息] 谱面 {entries.Count} 份（Assets/Resources/Charts）");
            int validCharts = 0;
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                if (!entry.IsValid)
                {
                    sb.AppendLine($"[错误] 谱面 {entry.ResourceName} 无法解析：{entry.Error}");
                    pass = false;
                    continue;
                }
                validCharts++;
                var chart = entry.Chart;
                var clip = Resources.Load<AudioClip>("Audio/" + chart.audio);
                string audioInfo = clip != null ? $"音频 {clip.length:F1}s" : "音频缺失（会静音跑）";
                if (clip == null) pass = false;
                sb.AppendLine($"[OK]   谱面 {entry.ResourceName}：{chart.songName} {chart.difficulty} Lv.{chart.level}  " +
                              $"{chart.NoteCount} 音符  BPM {chart.bpm:0.##}  首音 {chart.FirstNoteTime:F2}s  " +
                              $"末音 {chart.LastNoteTime:F2}s  {audioInfo}");

                if (clip != null && chart.LastNoteTime > clip.length)
                {
                    sb.AppendLine($"       [警告] 末个音符({chart.LastNoteTime:F2}s)超出了音频长度({clip.length:F1}s)");
                }
                int[] laneCounts = chart.LaneCounts();
                sb.AppendLine($"       轨道分布：{string.Join(" / ", laneCounts)}");
            }
            if (validCharts == 0)
            {
                sb.AppendLine("[错误] 没有可用的谱面，运行时会直接报错。");
                pass = false;
            }

            // ---- 5. 判定窗口单调性
            var manager = Object.FindObjectOfType<GameManager>();
            if (manager != null)
            {
                bool windowsOk = manager.perfectWindow > 0 && manager.goodWindow > manager.greatWindow &&
                                 manager.greatWindow > manager.perfectWindow &&
                                 manager.missWindow > manager.goodWindow;
                sb.AppendLine(windowsOk
                    ? $"[OK]   判定窗口：PERFECT ±{manager.perfectWindow * 1000:F0}ms / " +
                      $"GREAT ±{manager.greatWindow * 1000:F0}ms / GOOD ±{manager.goodWindow * 1000:F0}ms / " +
                      $"MISS > {manager.missWindow * 1000:F0}ms"
                    : "[错误] 判定窗口数值不单调（perfect < great < good < miss）");

                bool judgeOk =
                    manager.Judge(0.010f) == Judgement.Perfect &&
                    manager.Judge(-0.035f) == Judgement.Perfect &&
                    manager.Judge(0.060f) == Judgement.Great &&
                    manager.Judge(-0.100f) == Judgement.Good &&
                    manager.Judge(0.200f) == Judgement.None;
                sb.AppendLine(judgeOk
                    ? "[OK]   判定函数 Judge() 各档结果正确"
                    : "[错误] 判定函数 Judge() 结果不符合预期");
                if (!judgeOk) pass = false;
            }

            // ---- 6. 计分逻辑自测（纯 C# 类，可以脱离 Unity 直接验证）
            pass &= ScoreTests(sb);
            // ---- 7. 谱面解析自测
            pass &= ParseTests(sb);
            // ---- 8. 流速 / 判定解耦验收（对应需求文档 §5）
            pass &= ScrollSpeedVerification.RunHeadless(sb);

            sb.AppendLine(pass ? "======== 自检通过 ========" : "======== 自检未通过 ========");
            if (pass) Debug.Log(sb.ToString());
            else Debug.LogError(sb.ToString());
            return pass;
        }

        private static bool CheckLanes(StringBuilder sb, ref bool pass, PlayfieldView playfield)
        {
            int laneCount = 0;
            for (int i = 0; i < PlayfieldLayout.LaneCount; i++)
            {
                if (playfield.lanes != null && i < playfield.lanes.Length && playfield.lanes[i] != null) laneCount++;
            }
            if (laneCount == PlayfieldLayout.LaneCount)
            {
                sb.AppendLine($"[OK]   轨道 {laneCount} 条就位，判定线 {(playfield.judgeLine != null ? "已连接" : "缺失")}");
                return true;
            }
            sb.AppendLine($"[错误] 场景里只有 {laneCount} 条轨道（应为 {PlayfieldLayout.LaneCount}）");
            pass = false;
            return false;
        }

        private static void CheckRef(StringBuilder sb, ref bool pass, string label, bool ok)
        {
            if (ok) sb.AppendLine($"[OK]   {label}");
            else
            {
                sb.AppendLine($"[错误] {label} 不存在");
                pass = false;
            }
        }

        private static bool ScoreTests(StringBuilder sb)
        {
            var score = new ScoreSystem();
            score.Begin(100);
            for (int i = 0; i < 100; i++) score.Register(Judgement.Perfect);

            bool fullPerfect = score.Score == ScoreSystem.MaxScore && score.Rank == "SSS" &&
                               Math.Abs(score.Accuracy - 100.0) < 0.001 && score.IsFullCombo;

            score.Begin(10);
            score.Register(Judgement.Perfect);
            score.Register(Judgement.Miss);
            bool comboBreak = score.Combo == 0 && score.MaxCombo == 1 && score.Remaining == 8;

            score.Begin(4);
            for (int i = 0; i < 4; i++) score.Register(Judgement.Great);
            bool greatOnly = Math.Abs(score.Score - 700000) < 1 && score.Rank == "B";

            bool ok = fullPerfect && comboBreak && greatOnly;
            sb.AppendLine(ok
                ? "[OK]   计分与评级逻辑正确（全 Perfect = 1,000,000 / 全 Great = 700,000 / 连击会断）"
                : "[错误] 计分逻辑不符合预期：" +
                  $"fullPerfect={fullPerfect} comboBreak={comboBreak} greatOnly={greatOnly}");
            return ok;
        }

        private static bool ParseTests(StringBuilder sb)
        {
            // 手册式的纯数组
            var bare = ChartLibrary.Parse("[{\"time\":2.0,\"lane\":0}]", out string bareError);
            bool bareOk = bare != null && bare.NoteCount == 1 && string.IsNullOrEmpty(bareError);

            // 带元数据的对象 + 坏数据（重复音符 / 越界轨道）
            var full = ChartLibrary.Parse(
                "{\"songName\":\"T\",\"notes\":[" +
                "{\"time\":1.0,\"lane\":0}," +
                "{\"time\":1.0,\"lane\":0}," +
                "{\"time\":2.0,\"lane\":9}," +
                "{\"time\":3.0,\"lane\":3}]}", out string fullError);
            bool fullOk = full != null && full.NoteCount == 2 && !string.IsNullOrEmpty(fullError);

            // 空谱面应该被拒绝
            var empty = ChartLibrary.Parse("[]", out string emptyError);
            bool emptyOk = empty == null && !string.IsNullOrEmpty(emptyError);

            bool ok = bareOk && fullOk && emptyOk;
            sb.AppendLine(ok
                ? "[OK]   谱面解析：支持手册的纯数组写法、能过滤坏音符、空谱面会报错"
                : $"[错误] 谱面解析异常：bare={bareOk} full={fullOk} empty={emptyOk}");
            return ok;
        }

        // ================================================================ 预览图
        /// <summary>
        /// 用离线渲染拍两张图：选曲界面和游玩画面。
        /// 这样不用真的按播放，也能确认"界面没重叠、音符颜色对、判定线位置合理"。
        /// </summary>
        public static bool CapturePreview()
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogError("[My4KGame] 场景不存在，先执行一键生成。");
                return false;
            }

            Directory.CreateDirectory(PreviewFolder);

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var camera = Camera.main;
            var canvas = Object.FindObjectOfType<Canvas>();
            var hud = Object.FindObjectOfType<HudView>();
            var menu = Object.FindObjectOfType<MenuView>();
            var playfield = Object.FindObjectOfType<PlayfieldView>();

            if (camera == null || canvas == null)
            {
                Debug.LogError("[My4KGame] 场景里缺少相机或 Canvas，无法渲染预览。");
                return false;
            }

            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Debug.LogWarning("[My4KGame] 当前是 Null 图形设备（无渲染能力），跳过预览图。");
                return false;
            }

            var scaler = canvas.GetComponent<CanvasScaler>();
            bool scalerWasEnabled = scaler != null && scaler.enabled;
            RenderMode oldMode = canvas.renderMode;
            Camera oldCamera = canvas.worldCamera;

            // 为了把 uGUI 也渲进 RenderTexture，临时改成"摄像机空间"画布
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            canvas.scaleFactor = 1f;
            if (scaler != null) scaler.enabled = false;
            Canvas.ForceUpdateCanvases();

            bool ok = true;

            // ---------- 图 1：选曲界面
            try
            {
                if (hud != null) hud.SetVisible(false);
                if (menu != null) menu.Show(true);
                Canvas.ForceUpdateCanvases();
                ok &= RenderToPng(camera, $"{PreviewFolder}/preview_menu.png", 1920, 1080);
            }
            catch (Exception ex)
            {
                Debug.LogError("[My4KGame] 渲染选曲预览失败：" + ex);
                ok = false;
            }

            // ---------- 图 2：游玩画面（临时摆几个音符 + 填上示例数据）
            var temporary = new List<GameObject>();
            try
            {
                if (hud != null) hud.SetVisible(true);
                if (menu != null) menu.Show(false);
                FillHudForPreview(hud);
                canvas.scaleFactor = 1f;
                if (hud != null) hud.LayoutWorldLabels();

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NotePrefabPath);
                Transform noteRoot = playfield != null ? playfield.transform.Find("NoteRoot") : null;
                if (prefab != null && noteRoot != null)
                {
                    float[] ys = { 5.2f, 4.1f, 2.9f, 1.6f, 0.4f, -0.9f, -2.2f, -3.3f };
                    int[] lanes = { 0, 2, 1, 3, 0, 2, 1, 2 };
                    for (int i = 0; i < ys.Length; i++)
                    {
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, noteRoot);
                        go.transform.localPosition = new Vector3(PlayfieldLayout.LaneToX(lanes[i]), ys[i], 0f);
                        var nc = go.GetComponent<NoteController>();
                        if (nc != null && nc.body != null)
                            nc.body.color = Color.Lerp(PlayfieldLayout.LaneColor(lanes[i]), Color.white, 0.25f);
                        if (nc != null && nc.glow != null)
                        {
                            Color g = PlayfieldLayout.LaneColor(lanes[i]);
                            g.a = 0.30f;
                            nc.glow.color = g;
                        }
                        temporary.Add(go);
                    }
                }

                // 让一条轨道亮起来，展示"按下去有反馈"的样子（另一条保持常态，方便对比）
                if (playfield != null && playfield.lanes != null)
                {
                    for (int i = 0; i < playfield.lanes.Length; i++)
                    {
                        if (playfield.lanes[i] == null) continue;
                        if (i == 2) playfield.lanes[i].Flash(Judgement.Perfect);
                    }
                }

                Canvas.ForceUpdateCanvases();
                ok &= RenderToPng(camera, $"{PreviewFolder}/preview_play.png", 1920, 1080);
            }
            catch (Exception ex)
            {
                Debug.LogError("[My4KGame] 渲染游玩预览失败：" + ex);
                ok = false;
            }
            finally
            {
                foreach (GameObject go in temporary) Object.DestroyImmediate(go);

                canvas.renderMode = oldMode;
                canvas.worldCamera = oldCamera;
                canvas.scaleFactor = 1f;
                if (scaler != null) scaler.enabled = scalerWasEnabled;

                // 预览时改过的界面状态恢复回去，不要污染场景
                if (hud != null) hud.SetVisible(false);
                if (menu != null) menu.Show(true);

                if (ok) Debug.Log($"[My4KGame] 预览图已保存：{PreviewFolder}/preview_menu.png、{PreviewFolder}/preview_play.png");
            }

            return ok;
        }

        private static void FillHudForPreview(HudView hud)
        {
            if (hud == null) return;

            if (hud.songTitle != null) hud.songTitle.text = "Neon Pulse";
            if (hud.difficulty != null)
                hud.difficulty.text = "<color=#8FA3BF>Normal</color>  Lv.6   BPM 120   135 音符";
            if (hud.score != null) hud.score.text = "864,200";
            if (hud.accuracy != null) hud.accuracy.text = "准确率 96.42%";
            if (hud.progressFill != null) hud.progressFill.fillAmount = 0.42f;
            if (hud.comboText != null)
            {
                hud.comboText.gameObject.SetActive(true);
                hud.comboText.text = "57";
            }
            if (hud.comboCaption != null) hud.comboCaption.gameObject.SetActive(true);
            if (hud.judgementText != null)
            {
                hud.judgementText.gameObject.SetActive(true);
                hud.judgementText.text = "PERFECT  <size=30><color=#B9C6D8>+3ms</color></size>";
                hud.judgementText.color = Theme.Perfect;
            }
            if (hud.countdownText != null) hud.countdownText.gameObject.SetActive(false);
        }

        private static bool RenderToPng(Camera camera, string path, int width, int height)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
            };
            target.Create();

            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();

            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();

            byte[] png = texture.EncodeToPNG();
            File.WriteAllBytes(path, png);

            camera.targetTexture = null;
            RenderTexture.active = previous;
            Object.DestroyImmediate(texture);
            target.Release();
            Object.DestroyImmediate(target);

            if (png == null || png.Length == 0)
            {
                Debug.LogError("[My4KGame] 渲染结果为空：" + path);
                return false;
            }
            return true;
        }

        // ================================================================ 打包 Windows 版
        [MenuItem("My4KGame/11. 打包 Windows 版（生成 exe）", false, 11)]
        public static void BuildWindowsMenu()
        {
            string output = BuildWindowsPlayer();
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("My4KGame",
                    string.IsNullOrEmpty(output)
                        ? "打包失败，请看 Console 的红色报错。"
                        : "打包完成：\n" + output + "\n\n把 Build 文件夹整体压缩发给别人，双击 My4KGame.exe 就能玩。",
                    "好");
            }
        }

        /// <summary>
        /// 打出 Windows 64 位可执行文件（Mono 后端，不需要额外装 IL2CPP 模块）。
        /// 输出到工程目录的上一级：&lt;工程父目录&gt;/Build/My4KGame.exe，
        /// 这样 Build 不会落在仓库里被误提交。
        /// </summary>
        public static string BuildWindowsPlayer()
        {
            if (!File.Exists(ScenePath)) BuildScene();

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.FindIndex(s => s.path == ScenePath) < 0)
            {
                scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string buildDir = Path.GetFullPath(Path.Combine(projectRoot, "..", "Build"));
            Directory.CreateDirectory(buildDir);
            string exePath = Path.Combine(buildDir, "My4KGame.exe");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log($"[My4KGame] 打包结果：{summary.result}  " +
                      $"大小 {summary.totalSize / 1048576.0:F1} MB  耗时 {summary.totalTime.TotalSeconds:F0}s  " +
                      $"输出 {exePath}");

            return summary.result == BuildResult.Succeeded ? exePath : null;
        }

        /// <summary>命令行入口：只打包，然后退出。</summary>
        public static void BuildWindowsFromCommandLine()
        {
            string path = null;
            try { path = BuildWindowsPlayer(); }
            catch (Exception ex) { Debug.LogError("[My4KGame] 打包异常：" + ex); }

            Debug.Log(path != null ? "[My4KGame] BUILD_RESULT=OK " + path : "[My4KGame] BUILD_RESULT=FAILED");
            if (Application.isBatchMode) EditorApplication.Exit(path != null ? 0 : 1);
        }

        // ================================================================ 命令行入口
        /// <summary>
        /// 供 batchmode 调用：
        /// Unity.exe -batchmode -quit -projectPath ... -executeMethod My4KGame.EditorTools.ProjectBuilder.BuildFromCommandLine
        /// </summary>
        public static void BuildFromCommandLine()
        {
            bool ok;
            try
            {
                ok = BuildAll();
                ok &= SelfCheck();
                if (ok)
                {
                    try { CapturePreview(); }
                    catch (Exception ex) { Debug.LogWarning("[My4KGame] 预览图渲染跳过：" + ex.Message); }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[My4KGame] 批处理异常：" + ex);
                ok = false;
            }

            if (!ok)
            {
                Debug.Log("[My4KGame] BATCH_RESULT=FAILED");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[My4KGame] 构建 / 自检 / 预览图全部完成，接着跑播放模式冒烟测试…");
            PlayModeSmokeTest.RunFromCommandLine();
        }
    }
}
#endif
