#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace My4KGame.EditorTools
{
    /// <summary>
    /// 播放模式冒烟测试：真的把游戏跑一局，检查"按下 → 判定 → 计分 → 结算"整条链有没有断。
    ///
    /// 为什么需要它：编译通过、场景建好，并不代表游戏能玩。
    /// 空引用、事件没订阅上、暂停后时间跳变，这些问题只有真跑一局才会暴露。
    /// 所以这里像玩家一样去按 D/F/J/K，然后把结果写成 Docs/smoke_result.txt。
    ///
    /// 技术上有个坑：进入播放模式会触发域重载（domain reload），编辑器脚本里的静态状态会被清空，
    /// 所以不能用"编辑器里挂个 update 回调"的写法，必须靠 EditorPrefs 记下"待办"，等
    /// playModeStateChanged=EnteredPlayMode 之后再往场景里塞一个驱动器组件（这里用协程写线性代码）。
    /// </summary>
    public static class PlayModeSmokeTest
    {
        private const string PendingKey = "My4KGame.SmokePending";
        private static bool _hooked;

        [MenuItem("My4KGame/9. 播放模式冒烟测试（真的跑一局）", false, 9)]
        public static void RunFromMenu()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("My4KGame", "先停止当前的播放状态。", "好");
                return;
            }
            RunFromCommandLine();
        }

        /// <summary>开始测试。真正的驱动由 <see cref="SmokeDriver"/> 在播放模式里执行。</summary>
        public static void RunFromCommandLine()
        {
            EditorPrefs.SetBool(PendingKey, true);
            TryHook();

            if (!EditorApplication.isPlaying)
            {
                if (File.Exists(ProjectBuilder.ScenePath))
                {
                    EditorSceneManager.OpenScene(ProjectBuilder.ScenePath, OpenSceneMode.Single);
                }
                EditorApplication.isPlaying = true;
            }
        }

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            if (EditorPrefs.GetBool(PendingKey, false)) TryHook();
        }

        private static void TryHook()
        {
            if (_hooked) return;
            _hooked = true;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            if (!EditorPrefs.GetBool(PendingKey, false)) return;

            EditorPrefs.DeleteKey(PendingKey);
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            _hooked = false;

            var go = new GameObject("My4KGameSmokeDriver");
            go.AddComponent<SmokeDriver>();
        }
    }

    /// <summary>播放在播放模式里跑的那段测试脚本。用协程写，读起来就是一步步的操作。</summary>
    public sealed class SmokeDriver : MonoBehaviour
    {
        private const string ResultPath = "Docs/smoke_result.txt";
        private readonly List<string> _lines = new List<string>();
        private bool _failed;

        private void Start()
        {
            StartCoroutine(Run());
        }

        private void Check(bool condition, string message)
        {
            if (condition)
            {
                _lines.Add("[OK]   " + message);
            }
            else
            {
                _failed = true;
                _lines.Add("[失败] " + message);
            }
        }

        private IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 120f;

            // ---------------------------------------------------------- 1. 等游戏起来
            GameManager gm = null;
            while (gm == null && Time.realtimeSinceStartup < deadline)
            {
                gm = GameManager.Instance;
                yield return null;
            }
            yield return null;   // 让各个 View 的 Awake/Start 都跑完

            if (gm == null)
            {
                Check(false, "GameManager.Instance 没有创建出来（WorldBuilder 失败？）");
                Finish();
                yield break;
            }
            Check(true, "GameManager 已就绪，Unity " + Application.unityVersion);

            // 每次跑测试先复位"会存进 PlayerPrefs 的东西"，否则上一次（甚至玩家本人）
            // 留下的校准值会让所有按键整体偏移 → 全空打，测试结果不可复现。
            gm.SetCalibration(0.0);
            gm.SetScrollValue(ScrollSpeedTrack.CenterValue, save: false);
            _lines.Add("[信息] 已复位音画校准 = 0ms、流速 = 5.50（保证每次跑都是同样的起点）");

            Check(gm.State == GameState.Menu, $"启动后应停在选曲界面（实际 {gm.State}）");
            Check(Nz(Object.FindObjectOfType<HudView>()), "HudView 存在");
            Check(Nz(Object.FindObjectOfType<MenuView>()), "MenuView 存在");
            Check(Nz(Object.FindObjectOfType<ResultView>()), "ResultView 存在");
            Check(Nz(Object.FindObjectOfType<NoteSpawner>()), "NoteSpawner 存在");
            Check(CountLanes() == PlayfieldLayout.LaneCount, $"{PlayfieldLayout.LaneCount} 条轨道都在");

            // ---------------------------------------------------------- 2. 开谱
            gm.autoPlay = false;
            Check(gm.StartSong("ManualTest"), "StartSong(ManualTest) 成功");
            Check(gm.State == GameState.Countdown, $"开谱后应进入倒计时（实际 {gm.State}）");
            Check(gm.Score.TotalNotes == 4, $"分数系统拿到 4 个音符（实际 {gm.Score.TotalNotes}）");
            Check(gm.Score.Score == 0, "开局分数是 0");

            // ---------------------------------------------------------- 3. 等倒计时结束
            while (gm.State == GameState.Countdown && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Check(gm.State == GameState.Playing, $"倒计时结束后进入 Playing（实际 {gm.State}）");

            // 没有音符的地方按键 = 空打，不该加分
            long scoreBefore = gm.Score.Score;
            Judgement emptyTap = gm.PressLane(3);
            Check(emptyTap == Judgement.None, "附近没有音符时按键判空打（Judgement.None）");
            Check(gm.Score.Score == scoreBefore, "空打不加分");

            // ---------------------------------------------------------- 4. 像玩家一样把 4 个键按掉
            float[] noteTimes = { 2.0f, 3.0f, 4.0f, 5.0f };
            int[] noteLanes = { 0, 1, 2, 3 };
            int pressIndex = 0;
            bool pauseTested = false;
            int emptyResults = 0;

            while (gm.State != GameState.Result && Time.realtimeSinceStartup < deadline)
            {
                if (gm.State == GameState.Playing)
                {
                    double songTime = gm.SongTime;

                    if (pressIndex < noteTimes.Length && songTime >= noteTimes[pressIndex] - 0.01)
                    {
                        Judgement result = gm.PressLane(noteLanes[pressIndex]);
                        if (result == Judgement.None) emptyResults++;
                        pressIndex++;
                    }

                    // 按掉两个音符之后，测一次暂停 / 恢复
                    if (pressIndex >= 2 && !pauseTested)
                    {
                        pauseTested = true;

                        gm.Pause();
                        Check(gm.State == GameState.Paused, "Pause() 之后状态是 Paused");

                        yield return new WaitForSecondsRealtime(0.30f);
                        double frozen = gm.SongTime;
                        yield return new WaitForSecondsRealtime(0.20f);
                        Check(Math.Abs(gm.SongTime - frozen) < 0.002, "暂停期间歌曲时间冻结");

                        gm.Resume();
                        Check(gm.State == GameState.Playing, "Resume() 之后状态回到 Playing");

                        // 恢复后歌曲时间应该继续往前走，不能跳
                        double afterResume = gm.SongTime;
                        yield return null;
                        yield return null;
                        Check(gm.SongTime >= afterResume - 0.001, "恢复后歌曲时间继续前进");
                    }
                }

                yield return null;
            }

            // ---------------------------------------------------------- 5. 结算检查
            Check(gm.State == GameState.Result, $"歌曲播完进入结算（实际 {gm.State}）");
            Check(emptyResults == 0, $"4 次按键全部命中音符（空打 {emptyResults} 次）");
            Check(gm.Score.JudgedNotes == 4, $"4 个音符都被判定（实际 {gm.Score.JudgedNotes}）");
            Check(gm.Score.Count(Judgement.Miss) == 0, "没有 MISS（按键时机正确）");
            Check(gm.Score.Count(Judgement.Perfect) + gm.Score.Count(Judgement.Great) >= 3,
                $"大多数判定在 PERFECT/GREAT（PERFECT {gm.Score.Count(Judgement.Perfect)} / GREAT {gm.Score.Count(Judgement.Great)}）");
            Check(gm.Score.Score > 0, $"结算分数大于 0（{gm.Score.Score:N0}）");
            Check(!string.IsNullOrEmpty(gm.Score.Rank) && gm.Score.Rank != "-", $"评级有效（{gm.Score.Rank}）");
            Check(!gm.IsSongRunning, "结算后歌曲时钟已停");
            _lines.Add("[信息] " + gm.Score.Summary());
            _lines.Add($"[信息] 新纪录标记：{gm.IsNewBest}");

            // ---------------------------------------------------------- 6. 返回选曲
            gm.EnterMenu();
            yield return null;
            Check(gm.State == GameState.Menu, "EnterMenu() 回到选曲界面");
            Check(gm.Score.Score == 0 || gm.Chart == null, "回到菜单后清空当前谱面");

            // ---------------------------------------------------------- 7. 自动演示跑一小段
            gm.autoPlay = true;
            Check(gm.StartSong("NeonPulse_Easy"), "自动演示：StartSong(NeonPulse_Easy) 成功");
            while (gm.State == GameState.Countdown && Time.realtimeSinceStartup < deadline) yield return null;

            float demoUntil = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < demoUntil && gm.State == GameState.Playing)
            {
                yield return null;
            }
            Check(gm.Score.JudgedNotes > 0, $"自动演示打中了 {gm.Score.JudgedNotes} 个音符");
            Check(gm.Score.Count(Judgement.Miss) == 0, "自动演示没有 MISS");

            // ---------------------------------------------------------- 8. 流速实时调整 / 判定不漂移
            yield return StartCoroutine(ScrollSpeedLiveChecks(gm));

            // ---------------------------------------------------------- 9. 主界面按钮点击链路探针
            yield return StartCoroutine(MenuButtonChecks(gm));

            // ---------------------------------------------------------- 10. Hold 长按交互
            yield return StartCoroutine(HoldInteractionChecks(gm));

            // ---------------------------------------------------------- 11. Master 超密谱面
            yield return StartCoroutine(MasterChartChecks(gm));

            Finish();
        }

        private static bool Nz(UnityEngine.Object o) => o != null;

        /// <summary>
        /// Master（大师）难度冒烟：超密谱面（1245 个音符 / 7.98 音符每秒）能不能正常加载、
        /// 自动演示会不会漏谱或卡死。自动演示 = 每个音符都恰好按在判定时间上，
        /// 所以只要没有 MISS，就说明这张谱"每个音符都打得出来"。
        /// </summary>
        private IEnumerator MasterChartChecks(GameManager gm)
        {
            _lines.Add("[信息] ---- Master 难度（超密谱面）----");

            if (gm == null || gm.spawner == null)
            {
                Check(false, "GameManager / NoteSpawner 为空，无法验证 Master");
                yield break;
            }

            gm.autoPlay = true;
            if (!gm.StartSong("WorldsEndBLACKBOX_Master"))
            {
                Check(false, "StartSong(WorldsEndBLACKBOX_Master) 失败");
                gm.autoPlay = false;
                yield break;
            }

            Check(gm.Score.TotalNotes > 1000, $"Master 谱面加载了 {gm.Score.TotalNotes} 个音符（应 >1000）");

            float deadline = Time.realtimeSinceStartup + 14f;
            while (Time.realtimeSinceStartup < deadline && gm.Score.JudgedNotes < 60) yield return null;

            Check(gm.Score.JudgedNotes >= 60,
                $"自动演示 {gm.Score.JudgedNotes} 个音符（前 14 秒）");
            Check(gm.Score.Count(Judgement.Miss) == 0,
                $"Master 自动演示无 MISS（MISS {gm.Score.Count(Judgement.Miss)} 个）");

            float nps = 0f;
            if (gm.spawner.Chart != null && gm.spawner.Chart.NoteCount > 0)
            {
                double span = gm.spawner.Chart.LastNoteTime - gm.spawner.Chart.FirstNoteTime;
                if (span > 1.0) nps = (float)(gm.spawner.Chart.NoteCount / span);
            }
            Check(nps >= 5.0f, $"Master 平均密度 {nps:0.00} 音符/秒（应 ≥5）");

            // ---- 跳过前奏静音：音乐与时钟必须仍然对齐，否则整套判定都会漂
            if (gm.musicSource != null && gm.musicSource.clip != null && gm.Chart != null)
            {
                double startOffset = AudioTimeline.StartOffset(gm.Chart.audio);
                double expected = startOffset + gm.CurrentAudioTime;
                double drift = gm.musicSource.time - expected;
                Check(Math.Abs(drift) < 0.10,
                    $"音频位置与歌曲时钟对齐：musicSource {gm.musicSource.time:F2}s / 期望 {expected:F2}s" +
                    $"（偏移 {startOffset:F2}s，偏差 {drift * 1000:F0}ms）");
            }

            if (gm.Chart != null)
            {
                double firstNote = gm.Chart.FirstNoteTime;
                Check(firstNote >= 0.0 && firstNote < 0.35,
                    $"谱面第一个音符已被对齐到 0 秒附近（实际 {firstNote:F2}s）");
            }

            gm.autoPlay = false;
            gm.EnterMenu();
            yield return null;
        }

        /// <summary>
        /// Hold 长按交互：用真实谱面（WorldsEnd Expert 第 2 秒就有一根 Hold）跑一小段，
        /// 验证「开始事件 → 轨道常亮 → 结束事件」这条链有没有断。
        ///
        /// 之所以专门测它：之前 Hold 只有"头停住"这一个视觉线索，玩家分不清是按住还是卡住，
        /// 而这套反馈完全依赖 HoldStarted / HoldEnded 两个事件，事件一断就退化成"没反馈"。
        /// </summary>
        private IEnumerator HoldInteractionChecks(GameManager gm)
        {
            _lines.Add("[信息] ---- Hold 长按交互 ----");

            if (gm == null || gm.spawner == null)
            {
                Check(false, "GameManager / NoteSpawner 为空，无法验证 Hold");
                yield break;
            }

            int started = 0, ended = 0;
            Action<int> onStart = lane => started++;
            Action<int> onEnd = lane => ended++;
            gm.HoldStarted += onStart;
            gm.HoldEnded += onEnd;

            PlayfieldView view = Object.FindObjectOfType<PlayfieldView>();
            bool laneLit = false;
            bool sampled = false;

            gm.autoPlay = true;
            if (!gm.StartSong("WorldsEndBLACKBOX_Expert"))
            {
                Check(false, "StartSong(WorldsEndBLACKBOX_Expert) 失败");
                gm.HoldStarted -= onStart;
                gm.HoldEnded -= onEnd;
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + 25f;
            while (started == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Check(started > 0, $"Hold 开始事件触发了（{started} 次）");

            // 按住期间采样一次：轨道应处于 held 状态（常亮 + 呼吸）
            for (int i = 0; i < 240 && !sampled; i++)
            {
                if (gm.spawner != null)
                {
                    foreach (NoteController n in gm.spawner.ActiveNotes)
                    {
                        if (n == null || !n.IsHeld) continue;
                        sampled = true;
                        if (view != null && view.lanes != null &&
                            n.Lane < view.lanes.Length && view.lanes[n.Lane] != null)
                        {
                            laneLit = view.lanes[n.Lane].IsHeld;
                        }
                        break;
                    }
                }
                if (!sampled) yield return null;
            }

            if (sampled)
            {
                Check(laneLit, "按住期间对应轨道被点亮（LaneView.IsHeld = true）");
            }
            else
            {
                _lines.Add("[信息] 未采样到「按住中」的瞬间（Hold 很短），跳过轨道常亮断言");
            }

            deadline = Time.realtimeSinceStartup + 25f;
            while (ended == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Check(ended > 0, $"Hold 结束事件触发了（{ended} 次）");
            Check(started >= ended, $"开始/结束事件数量合理（开始 {started} / 结束 {ended}）");

            gm.autoPlay = false;
            gm.EnterMenu();
            yield return null;
            Check(ended == started, $"清场后事件配对平衡（开始 {started} / 结束 {ended}）");

            gm.HoldStarted -= onStart;
            gm.HoldEnded -= onEnd;
        }

        /// <summary>
        /// 主界面按钮探针：把「点击」这条链拆成 4 段分别验证
        ///   EventSystem 在不在 → 按钮能不能被命中 → 回调有没有被调用 → 数值有没有变。
        /// 这样就能分清"点不动"是输入没送到、射线被挡、还是回调没绑上。
        /// </summary>
        private IEnumerator MenuButtonChecks(GameManager gm)
        {
            _lines.Add("[信息] ---- 主界面按钮点击链路 ----");

            gm.EnterMenu();
            yield return null;
            yield return null;

            // ---- 1) EventSystem / 输入模块
            var es = UnityEngine.EventSystems.EventSystem.current;
            Check(es != null, "场景里有 EventSystem");
            if (es != null)
            {
                Check(es.enabled && es.gameObject.activeInHierarchy, "EventSystem 处于启用状态");
                var module = es.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                Check(module != null && module.enabled, "StandaloneInputModule 存在且启用");
            }

            // ---- 2) 按钮结构与遮挡（注意：Menu 和 HUD 里各有一个 Tap▶，必须按父节点取）
            MenuView menu = Object.FindObjectOfType<MenuView>();
            HudView hud = Object.FindObjectOfType<HudView>();
            GameObject plus = menu != null ? FindChild(menu.transform, "SpeedCard/Tap▶") : null;
            GameObject calibPlus = menu != null ? FindChild(menu.transform, "CalibrationStepper/Tap+") : null;
            GameObject hudPlus = hud != null ? FindChild(hud.transform, "SpeedPanel/Tap▶") : null;

            Check(plus != null, "主界面流速卡片的 ▶ 按钮存在");
            Check(calibPlus != null, "主界面音画校准的 + 按钮存在");
            Check(hudPlus != null, "游戏内悬浮窗的 ▶ 按钮存在");

            var probeTargets = new List<GameObject>();
            if (plus != null) probeTargets.Add(plus);
            if (calibPlus != null) probeTargets.Add(calibPlus);
            if (hudPlus != null) probeTargets.Add(hudPlus);

            foreach (GameObject go in probeTargets)
            {
                if (go == null) continue;

                var img = go.GetComponent<UnityEngine.UI.Image>();
                var tap = go.GetComponent<TapRepeatButton>();
                Check(img != null && img.raycastTarget, $"{go.name}：有 Image 且 raycastTarget=true");
                Check(tap != null, $"{go.name}：挂了 TapRepeatButton");
                if (tap != null) Check(tap.background != null, $"{go.name}：background 已绑定");

                // 只有"当前应该可见"的面板才检查 CanvasGroup；
                // HUD 在菜单里本来就是 alpha=0 / 不接收射线（那是正确行为，不是 bug）
                bool inHud = hud != null && go.transform.IsChildOf(hud.transform);
                foreach (var cg in go.GetComponentsInParent<CanvasGroup>(true))
                {
                    if (inHud)
                    {
                        _lines.Add($"[信息] {go.name}：祖先 CanvasGroup「{cg.name}」alpha={cg.alpha:0.00} " +
                                   $"blocksRaycasts={cg.blocksRaycasts}（菜单态下 HUD 隐藏属正常）");
                    }
                    else
                    {
                        Check(cg.blocksRaycasts, $"{go.name}：祖先 CanvasGroup「{cg.name}」blocksRaycasts=true");
                        Check(cg.alpha > 0.9f, $"{go.name}：祖先 CanvasGroup「{cg.name}」alpha={cg.alpha:0.00}");
                    }
                }

                var raycaster = go.GetComponentInParent<UnityEngine.UI.GraphicRaycaster>();
                Check(raycaster != null && raycaster.enabled, $"{go.name}：Canvas 有启用的 GraphicRaycaster");
            }

            // ---- 3) 绕过射线，直接调回调：验证绑定逻辑本身
            if (plus != null && plus.GetComponent<TapRepeatButton>() is TapRepeatButton plusTap)
            {
                float before = ScrollSpeedState.Value;
                plusTap.OnPointerDown(new UnityEngine.EventSystems.PointerEventData(es));
                yield return null;
                Check(!Mathf.Approximately(ScrollSpeedState.Value, before),
                    $"直接触发主界面 ▶：流速 {before:0.00} → {ScrollSpeedState.Value:0.00}");
            }

            if (hudPlus != null && hudPlus.GetComponent<TapRepeatButton>() is TapRepeatButton hudTap)
            {
                float before = ScrollSpeedState.Value;
                hudTap.OnPointerDown(new UnityEngine.EventSystems.PointerEventData(es));
                yield return null;
                Check(!Mathf.Approximately(ScrollSpeedState.Value, before),
                    $"直接触发悬浮窗 ▶：流速 {before:0.00} → {ScrollSpeedState.Value:0.00}");
            }

            if (calibPlus != null && calibPlus.GetComponent<TapRepeatButton>() is TapRepeatButton calTap && gm != null)
            {
                double before = gm.calibrationOffset;
                calTap.OnPointerDown(new UnityEngine.EventSystems.PointerEventData(es));
                yield return null;
                Check(Math.Abs(gm.calibrationOffset - before) > 1e-6,
                    $"直接触发校准 +：{before * 1000:+0;-0;0}ms → {gm.calibrationOffset * 1000:+0;-0;0}ms");
            }

            // ---- 4) 真实射线命中（批量模式 Screen 为 0 时跳过）
            if (plus != null && es != null && Screen.width > 1 && Screen.height > 1)
            {
                var rt = (RectTransform)plus.transform;
                Vector2 screenPoint = UnityEngine.RectTransformUtility.WorldToScreenPoint(
                    null, rt.TransformPoint(rt.rect.center));
                var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = screenPoint };
                var hits = new List<UnityEngine.EventSystems.RaycastResult>();
                es.RaycastAll(ped, hits);
                string top = hits.Count > 0 ? hits[0].gameObject.name : "（无命中）";
                _lines.Add($"[信息] Screen {Screen.width}x{Screen.height}，射线首位命中：{top}");
                Check(hits.Count > 0, "按钮位置能被 UI 射线命中");
                if (hits.Count > 0)
                {
                    Check(hits[0].gameObject == plus || hits[0].gameObject.transform.IsChildOf(plus.transform),
                        $"射线首位命中就是 ▶ 按钮（实际 {top}）");
                }
            }
            else
            {
                _lines.Add("[信息] 批量模式 Screen 尺寸无效，跳过真实射线测试");
            }

            gm.SetScrollValue(ScrollSpeedTrack.CenterValue, save: false);
            gm.EnterMenu();
            yield return null;
        }

        private static GameObject FindChild(Transform root, string path)
        {
            if (root == null) return null;
            Transform t = root.Find(path);
            return t != null ? t.gameObject : null;
        }

        private static bool Nearly(double a, double b) => Math.Abs(a - b) < 0.001;

        /// <summary>
        /// 流速专项：在真实游玩状态里验证需求文档 §5 的验收项 1/2/3/4/5/6。
        ///
        /// 做法：以 0.5x / 1x / 2x 各开一局 ManualTest，等第 1 个音符（lane0 @ 2000ms）出现后
        ///   · 运行中切换流速 → 断言该音符的 judgeTimeMs 一个毫秒都没变；
        ///   · 把音符的 transform 挪到 y=40（荒谬位置）再用"判定时间戳 +20ms"去按 →
        ///     断言仍然是 PERFECT（证明判定完全不看屏幕坐标）；
        ///   · 三种流速下的判定结果必须完全一致。
        /// </summary>
        private IEnumerator ScrollSpeedLiveChecks(GameManager gm)
        {
            _lines.Add("[信息] ---- 流速实时调整 / 判定不漂移（播放模式）----");

            if (gm.spawner == null)
            {
                Check(false, "GameManager.spawner 为空，无法做流速验证");
                yield break;
            }

            float[] speeds = { 0.5f, 1f, 2f };
            var verdicts = new List<Judgement>();
            double windowBefore = gm.PerfectWindowMs;
            bool windowStable = true;

            foreach (float speed in speeds)
            {
                gm.SetScrollSpeed(speed, immediate: true, save: false);
                Check(Mathf.Abs(gm.ScrollSpeed - speed) < 0.001f, $"流速设为 ×{speed:0.0}");

                gm.autoPlay = false;
                if (!gm.StartSong("ManualTest"))
                {
                    Check(false, $"×{speed:0.0}：StartSong(ManualTest) 失败");
                    continue;
                }

                float deadline = Time.realtimeSinceStartup + 10f;
                while (gm.State == GameState.Countdown && Time.realtimeSinceStartup < deadline) yield return null;

                // 找到 lane0 的第一个音符（它可能就是被上一轮判掉的那个，所以每轮重新开谱）
                NoteController target = null;
                deadline = Time.realtimeSinceStartup + 5f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    foreach (NoteController note in gm.spawner.ActiveNotes)
                    {
                        if (note != null && note.IsAlive && note.Data != null && note.Data.lane == 0)
                        {
                            target = note;
                            break;
                        }
                    }
                    if (target != null) break;
                    yield return null;
                }

                if (target == null)
                {
                    Check(false, $"×{speed:0.0}：等不到 lane0 的音符");
                    gm.EnterMenu();
                    continue;
                }

                double judgeTimeMs = target.Data.JudgeTimeMs;

                // ① 运行中改流速：已生成音符的判定时间戳必须纹丝不动
                float other = Mathf.Approximately(speed, 1f) ? 2f : 1f;
                gm.SetScrollSpeed(other, immediate: true, save: false);
                yield return null;
                Check(Nearly(target.Data.JudgeTimeMs, judgeTimeMs),
                    $"×{speed:0.0}→×{other:0.0} 运行中调速：该音符 judgeTimeMs 仍是 {judgeTimeMs:F1}ms");

                windowStable &= Nearly(gm.PerfectWindowMs, windowBefore);

                // 切回本轮的流速，让这一轮有一个确定的视觉速度
                gm.SetScrollSpeed(speed, immediate: true, save: false);
                yield return null;

                // ② 判定不得读取屏幕坐标：把音符挪到荒谬位置再判
                target.transform.position = new Vector3(99f, 40f, 0f);
                Judgement verdict = gm.spawner.ResolvePress(0, target.Data.JudgeTimeSeconds + 0.020);
                verdicts.Add(verdict);
                _lines.Add($"[信息] ×{speed:0.0}：+20ms 击打（音符被挪到 y=40）→ {JudgementInfo.ToText(verdict)}");
                Check(verdict == Judgement.Perfect,
                    $"×{speed:0.0}：+20ms 应判 PERFECT（实际 {JudgementInfo.ToText(verdict)}）");

                gm.EnterMenu();
                yield return null;
            }

            // ③ 三种流速的判定结果必须一致
            bool same = verdicts.Count == speeds.Length && verdicts.TrueForAll(v => v == verdicts[0]);
            Check(same, "×0.5 / ×1 / ×2 三档流速下相同击打偏移 → 判定一致：" +
                        string.Join(" / ", verdicts.ConvertAll(JudgementInfo.ToText)));
            Check(windowStable, $"调速全程 PERFECT 窗口始终是 ±{windowBefore:F0}ms");

            // ④ 视觉映射确实随流速变化（用纯映射验证，避免被出生点/判定线夹住）
            ScrollSpeedTrack track = gm.Scroll;
            track.SetCoefficient(1f, immediate: true);
            float y1 = track.SecondsAheadToY(0.5f);
            track.SetCoefficient(2f, immediate: true);
            float y2 = track.SecondsAheadToY(0.5f);
            Check(y2 > y1 + 0.5f, $"同一时间差 0.5s：1x → y={y1:F2}，2x → y={y2:F2}（视觉距离变大了）");

            // ⑤ 加减按钮步进：按一次 +0.25 立刻生效，判定窗口一个字节都不动
            gm.SetScrollValue(ScrollSpeedTrack.CenterValue, save: false);
            float levelBefore = gm.ScrollValue;
            gm.AdjustScrollValue(ScrollSpeedTrack.Step);
            Check(Mathf.Abs(gm.ScrollValue - (levelBefore + ScrollSpeedTrack.Step)) < 0.001f,
                $"按钮 +0.25：{levelBefore:0.00} → {gm.ScrollValue:0.00}（×{gm.ScrollSpeed:0.00}）");
            Check(Nearly(gm.PerfectWindowMs, windowBefore), $"调档后 PERFECT 窗口仍是 ±{windowBefore:F0}ms");
            gm.AdjustScrollValue(-ScrollSpeedTrack.Step);

            // ⑥ 端到端：不同档位下，真实音符在屏幕上的位置必须按当前档位映射
            //    （这才是"流速到底有没有真的改变音符速度"的证据，而不是只测公式）
            float[] levels = { ScrollSpeedTrack.MinValue, ScrollSpeedTrack.CenterValue, ScrollSpeedTrack.MaxValue };
            foreach (float level in levels)
            {
                gm.SetScrollValue(level, save: false);
                if (!gm.StartSong("ManualTest"))
                {
                    Check(false, $"档位 {level:0.0}：StartSong 失败");
                    continue;
                }

                float waitUntil = Time.realtimeSinceStartup + 12f;
                while (gm.State == GameState.Countdown && Time.realtimeSinceStartup < waitUntil) yield return null;

                NoteController sample = null;
                waitUntil = Time.realtimeSinceStartup + 6f;
                while (Time.realtimeSinceStartup < waitUntil)
                {
                    foreach (NoteController note in gm.spawner.ActiveNotes)
                    {
                        if (note != null && note.IsAlive && note.Data != null) { sample = note; break; }
                    }
                    if (sample != null) break;
                    yield return null;
                }

                if (sample == null)
                {
                    Check(false, $"档位 {level:0.0}：等不到音符");
                    gm.EnterMenu();
                    continue;
                }

                float ahead = ScrollSpeedTrack.SecondsAheadOf(sample.Data.JudgeTimeMs, gm.SongTime);
                float expectedY = gm.Scroll.SecondsAheadToY(ahead);
                float actualY = sample.transform.position.y;
                float travel = gm.Scroll.TravelSeconds;

                _lines.Add($"[信息] 档位 {level:0.0}：系数 ×{gm.ScrollSpeed:0.00}，一屏 {travel:0.00}s，" +
                           $"ahead={ahead:0.00}s → 音符 Y={actualY:0.00}");
                Check(ahead > 0f && Mathf.Abs(actualY - expectedY) < 0.25f,
                    $"档位 {level:0.0}：音符实际 Y={actualY:0.00}，按当前档位映射应为 {expectedY:0.00}（一屏 {travel:0.00}s）");

                gm.EnterMenu();
                yield return null;
            }

            // ⑦ 主界面 ⇄ 游戏内 同步 + 持久化
            ScrollSpeedState.Set(6.25f);
            Check(Mathf.Abs(ScrollSpeedState.Value - 6.25f) < 0.001f, "主界面把流速设成 6.25");

            gm.StartSong("ManualTest");
            yield return null;
            Check(Mathf.Abs(gm.ScrollValue - 6.25f) < 0.001f &&
                  Mathf.Abs(gm.ScrollSpeed - ScrollSpeedTrack.ValueToCoefficient(6.25f)) < 0.01f,
                $"进歌后游戏内读到 {gm.ScrollValue:0.00}（应为 6.25，系数 ×{gm.ScrollSpeed:0.00}）");

            gm.AdjustScrollValue(1.25f);   // 6.25 → 7.50
            Check(Mathf.Abs(ScrollSpeedState.Value - 7.50f) < 0.001f,
                $"游戏内调到 {ScrollSpeedState.Value:0.00}");

            gm.EnterMenu();
            yield return null;
            Check(Mathf.Abs(ScrollSpeedState.Value - 7.50f) < 0.001f,
                $"退出歌曲回主界面后仍是 {ScrollSpeedState.Value:0.00}（两边订阅同一个数据源）");

            string savedRaw = PlayerPrefs.GetString(ScrollSpeedState.PrefKey, "");
            Check(float.TryParse(savedRaw, System.Globalization.NumberStyles.Float,
                      System.Globalization.CultureInfo.InvariantCulture, out float savedValue) &&
                  Mathf.Abs(savedValue - 7.50f) < 0.001f,
                $"流速已持久化：PlayerPrefs[\"{ScrollSpeedState.PrefKey}\"] = {savedRaw}");

            gm.SetScrollValue(ScrollSpeedTrack.CenterValue, save: true);
            gm.EnterMenu();
            yield return null;
        }

        private static int CountLanes()
        {
            int count = 0;
            foreach (LaneView lane in Object.FindObjectsOfType<LaneView>(true))
            {
                if (lane != null) count++;
            }
            return count;
        }

        private void Finish()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=========== My4KGame 冒烟测试 ===========");
            sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (string line in _lines) sb.AppendLine(line);
            sb.AppendLine(_failed ? "结论：失败" : "结论：通过");
            sb.AppendLine("=========================================");

            try
            {
                Directory.CreateDirectory("Docs");
                File.WriteAllText(ResultPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Debug.LogError("[My4KGame] 写测试结果失败：" + ex.Message);
            }

            Debug.Log(sb.ToString());
            Debug.Log(_failed ? "[My4KGame] SMOKE_RESULT=FAIL" : "[My4KGame] SMOKE_RESULT=PASS");

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(_failed ? 1 : 0);
            }
            else
            {
                EditorUtility.DisplayDialog("My4KGame 冒烟测试",
                    (_failed ? "发现问题，详情见 Docs/smoke_result.txt" : "全部通过！详情见 Docs/smoke_result.txt"),
                    "好");
                EditorApplication.isPlaying = false;
            }
        }
    }
}
#endif
