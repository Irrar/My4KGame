#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace My4KGame.EditorTools
{
    /// <summary>
    /// 「流速可调、判定不变」的无头验收测试（不需要进播放模式）。
    ///
    /// 对应需求文档 §5 的验收项 1/2/4/6/7，以及 §6 的实现约束：
    ///   · 判定时间戳固定（同一谱面任意流速 → judgeTimeMs 完全一致）
    ///   · 判定窗口不变（0.5x~4x 下毫秒值不变，且边界判定一致）
    ///   · 视觉映射独立（时间差相同，流速翻倍 → 视觉距离翻倍）
    ///   · 多流速一致性（同一击打偏移，不同流速 → 同一判定）
    ///   · BPM / 变速谱面（beat → 毫秒换算正确，且与流速无关）
    /// </summary>
    public static class ScrollSpeedVerification
    {
        private static readonly float[] Speeds = { 0.5f, 1.0f, 2.0f, 4.0f };

        public static bool RunHeadless(StringBuilder sb)
        {
            bool pass = true;
            sb.AppendLine("---- 流速 / 判定解耦验收 ----");

            pass &= VerifyJudgeTimeFixed(sb);
            pass &= VerifyWindowsFixed(sb);
            pass &= VerifyVisualMapping(sb);
            pass &= VerifyLevelMapping(sb);
            pass &= VerifyMultiSpeedConsistency(sb);
            pass &= VerifyBpmConversion(sb);

            return pass;
        }

        // ================================================================ 验收 1：判定时间戳固定
        private static bool VerifyJudgeTimeFixed(StringBuilder sb)
        {
            if (!ChartLibrary.TryLoad("NeonPulse_Normal", out ChartData chart, out string error) && chart == null)
            {
                sb.AppendLine("[失败] 验收1：加载 NeonPulse_Normal 失败 " + error);
                return false;
            }

            double[] baseline = Snapshot(chart);
            var track = new ScrollSpeedTrack(PlayfieldLayout.TravelTime);
            bool ok = true;

            foreach (float speed in Speeds)
            {
                track.SetCoefficient(speed, immediate: true);

                // 模拟一整屏的视觉计算：如果视觉代码偷偷回写了判定数据，这里就会露馅
                for (float ahead = 0f; ahead <= track.TravelSeconds + 0.5f; ahead += 0.01f)
                {
                    track.SecondsAheadToY(ahead);
                }

                double[] now = Snapshot(chart);
                if (!SameArray(baseline, now)) ok = false;
            }

            // 再加载一次同一张谱面：确保"重新开谱"也不会得到不同的时间戳
            ChartLibrary.TryLoad("NeonPulse_Normal", out ChartData reloaded, out _);
            if (reloaded != null && !SameArray(baseline, Snapshot(reloaded))) ok = false;

            sb.AppendLine(ok
                ? $"[OK]   验收1 判定时间戳固定：{chart.NoteCount} 个音符在 0.5x/1x/2x/4x 下 judgeTimeMs 完全一致" +
                  $"（首音 {baseline[0]:F1}ms，末音 {baseline[baseline.Length - 1]:F1}ms）"
                : "[失败] 验收1 判定时间戳会随流速变化");
            return ok;
        }

        // ================================================================ 验收 2/6：窗口不变 + 多流速一致性
        private static bool VerifyWindowsFixed(StringBuilder sb)
        {
            GameManager gm = Object.FindObjectOfType<GameManager>();
            if (gm == null)
            {
                sb.AppendLine("[失败] 验收2：场景里找不到 GameManager");
                return false;
            }

            double perfect = gm.PerfectWindowMs;
            double great = gm.GreatWindowMs;
            double good = gm.GoodWindowMs;
            double miss = gm.MissWindowMs;

            // 临界点探测值：0 / 刚进档 / 刚出档
            double[] probes = { 0.0, 39.9, 40.1, 79.9, 80.1, 119.9, 120.1, 500.0 };
            string basline = Verdicts(gm, probes);

            var track = new ScrollSpeedTrack(PlayfieldLayout.TravelTime);
            bool windowsSame = true;
            bool verdictsSame = true;

            foreach (float speed in Speeds)
            {
                track.SetCoefficient(speed, immediate: true);
                if (!NearlyEqual(gm.PerfectWindowMs, perfect) ||
                    !NearlyEqual(gm.GreatWindowMs, great) ||
                    !NearlyEqual(gm.GoodWindowMs, good) ||
                    !NearlyEqual(gm.MissWindowMs, miss))
                {
                    windowsSame = false;
                }
                if (Verdicts(gm, probes) != basline) verdictsSame = false;
            }

            bool ok = windowsSame && verdictsSame;
            sb.AppendLine(ok
                ? $"[OK]   验收2 判定窗口不变：PERFECT ±{perfect:F0}ms / GREAT ±{great:F0}ms / " +
                  $"GOOD ±{good:F0}ms / MISS >{miss:F0}ms —— 0.5x~4x 全程一致\n" +
                  $"       边界探测 {string.Join(" ", Array.ConvertAll(probes, p => p.ToString("F1")))}ms → {basline}"
                : "[失败] 验收2 判定窗口或边界判定随流速变化");
            return ok;
        }

        // ================================================================ 验收 4：视觉映射独立
        private static bool VerifyVisualMapping(StringBuilder sb)
        {
            var track = new ScrollSpeedTrack(PlayfieldLayout.TravelTime);
            const float ahead = 0.5f;

            float y05, y1, y2, y4;
            track.SetCoefficient(0.5f, immediate: true); y05 = track.SecondsAheadToY(ahead);
            track.SetCoefficient(1.0f, immediate: true); y1 = track.SecondsAheadToY(ahead);
            track.SetCoefficient(2.0f, immediate: true); y2 = track.SecondsAheadToY(ahead);
            track.SetCoefficient(4.0f, immediate: true); y4 = track.SecondsAheadToY(ahead);

            float judgeY = PlayfieldLayout.JudgeLineY;
            float d05 = y05 - judgeY, d1 = y1 - judgeY, d2 = y2 - judgeY, d4 = y4 - judgeY;

            bool monotonic = d05 < d1 && d1 < d2 && d2 < d4;
            bool doubled = Mathf.Abs(d2 - d1 * 2f) < 0.001f;
            bool halved = Mathf.Abs(d05 - d1 * 0.5f) < 0.001f;

            bool ok = monotonic && doubled && halved;
            sb.AppendLine(ok
                ? $"[OK]   验收4 视觉映射独立：同一时间差 {ahead:F2}s 下，视觉距离 0.5x={d05:F2} / 1x={d1:F2} / " +
                  $"2x={d2:F2} / 4x={d4:F2}（2x 正好是 1x 的两倍，判定数据不变）"
                : $"[失败] 验收4 视觉映射不符合预期：d05={d05:F3} d1={d1:F3} d2={d2:F3} d4={d4:F3}");
            return ok;
        }

        /// <summary>档位（1~10，0.25 一档）↔ 视觉系数的映射是否单调、端点正确、可往返、每格可感知。</summary>
        private static bool VerifyLevelMapping(StringBuilder sb)
        {
            const float baseTravel = PlayfieldLayout.TravelTime;

            bool endpoints = Nearly(ScrollSpeedTrack.ValueToCoefficient(ScrollSpeedTrack.MinValue),
                                 ScrollSpeedTrack.MinCoefficient) &&
                             Nearly(ScrollSpeedTrack.ValueToCoefficient(ScrollSpeedTrack.CenterValue), 1.0) &&
                             Nearly(ScrollSpeedTrack.ValueToCoefficient(ScrollSpeedTrack.MaxValue),
                                 ScrollSpeedTrack.MaxCoefficient);

            bool monotonic = true;
            bool roundTrip = true;
            float previous = 0f;
            var rows = new List<string>();

            for (float level = ScrollSpeedTrack.MinValue; level <= ScrollSpeedTrack.MaxValue + 0.001f;
                 level += ScrollSpeedTrack.Step)
            {
                float coefficient = ScrollSpeedTrack.ValueToCoefficient(level);
                if (coefficient <= previous) monotonic = false;
                previous = coefficient;

                if (Mathf.Abs(ScrollSpeedTrack.CoefficientToValue(coefficient) - level) > 0.01f) roundTrip = false;

                if (Nearly(level, 1.0) || Nearly(level, 5.5) || Nearly(level, 10.0))
                {
                    rows.Add($"{level:0.0}档 ×{coefficient:0.00}（一屏 {baseTravel / coefficient:0.00}s）");
                }
            }

            bool snapping = Nearly(ScrollSpeedTrack.SnapValue(5.6f), 5.5) &&
                            Nearly(ScrollSpeedTrack.SnapValue(0.2f), ScrollSpeedTrack.MinValue) &&
                            Nearly(ScrollSpeedTrack.SnapValue(11f), ScrollSpeedTrack.MaxValue);

            // 按一下（0.25 档）的变化量必须肉眼可见：至少 6%
            float atCenter = ScrollSpeedTrack.ValueToCoefficient(ScrollSpeedTrack.CenterValue);
            float oneStep = ScrollSpeedTrack.ValueToCoefficient(
                ScrollSpeedTrack.CenterValue + ScrollSpeedTrack.Step);
            float oneLevel = ScrollSpeedTrack.ValueToCoefficient(
                ScrollSpeedTrack.CenterValue + 1f);
            float stepPercent = (oneStep / atCenter - 1f) * 100f;
            float levelPercent = (oneLevel / atCenter - 1f) * 100f;
            bool perceivable = stepPercent >= 6f;

            bool ok = endpoints && monotonic && roundTrip && snapping && perceivable;
            sb.AppendLine(ok
                ? "[OK]   档位映射：1~10 档单调递增，端点为 ×0.25 / ×4.00，中位 5.5 档 = ×1.00，0.25 对齐 —— " +
                  string.Join("，", rows) +
                  $"\n       按下一次 +0.25 档：{atCenter:0.000} → {oneStep:0.000}（+{stepPercent:F1}%）；" +
                  $"一次 +1 档：{oneLevel:0.000}（+{levelPercent:F1}%）"
                : $"[失败] 档位映射不正确：endpoints={endpoints} monotonic={monotonic} roundTrip={roundTrip} " +
                  $"snap={snapping} 每格变化={stepPercent:F1}%");
            return ok;
        }

        private static bool VerifyMultiSpeedConsistency(StringBuilder sb)        {
            GameManager gm = Object.FindObjectOfType<GameManager>();
            if (gm == null) return false;

            // 同一批击打偏移，在不同流速下必须得到完全相同的判定序列
            double[] deltas = { -130, -110, -90, -60, -30, 0, 30, 60, 90, 110, 130 };
            string baseline = Verdicts(gm, deltas);

            var track = new ScrollSpeedTrack(PlayfieldLayout.TravelTime);
            bool ok = true;
            var rows = new List<string>();

            foreach (float speed in Speeds)
            {
                track.SetCoefficient(speed, immediate: true);
                string verdicts = Verdicts(gm, deltas);
                rows.Add($"{speed:0.0}x → {verdicts}");
                if (verdicts != baseline) ok = false;
            }

            sb.AppendLine(ok
                ? "[OK]   验收6 多流速一致性：同样一组击打偏移在 0.5x/1x/2x/4x 下判定完全相同\n       " +
                  string.Join("\n       ", rows)
                : "[失败] 验收6 不同流速下判定结果不一致");
            return ok;
        }

        // ================================================================ 验收 7：BPM / 变速
        private static bool VerifyBpmConversion(StringBuilder sb)
        {
            // 120 BPM，每拍 500ms：第 0/4/8 拍 = 0 / 2000 / 4000 ms
            ChartData chartA = ChartLibrary.Parse(
                "{\"bpm\":120,\"notes\":[" +
                "{\"beat\":0,\"lane\":0},{\"beat\":4,\"lane\":1},{\"beat\":8,\"lane\":2}]}", out _);
            double[] a = Snapshot(chartA);

            // 第 4 拍起改成 60 BPM（每拍 1000ms）：0 / 2000 / 6000 ms
            ChartData chartB = ChartLibrary.Parse(
                "{\"bpm\":120,\"bpmChanges\":[{\"beat\":4,\"bpm\":60}],\"notes\":[" +
                "{\"beat\":0,\"lane\":0},{\"beat\":4,\"lane\":1},{\"beat\":8,\"lane\":2}]}", out _);
            double[] b = Snapshot(chartB);

            // 带 offset 0.25s：整体 +250ms
            ChartData chartC = ChartLibrary.Parse(
                "{\"bpm\":120,\"offset\":0.25,\"notes\":[" +
                "{\"beat\":0,\"lane\":0},{\"beat\":4,\"lane\":1}]}", out _);
            double[] c = Snapshot(chartC);

            bool okA = a != null && a.Length == 3 && NearlyEqual(a[0], 0) && NearlyEqual(a[1], 2000) && NearlyEqual(a[2], 4000);
            bool okB = b != null && b.Length == 3 && NearlyEqual(b[0], 0) && NearlyEqual(b[1], 2000) && NearlyEqual(b[2], 6000);
            bool okC = c != null && c.Length == 2 && NearlyEqual(c[0], 250) && NearlyEqual(c[1], 2250);

            // 变速谱面的时间戳同样不受流速影响
            var track = new ScrollSpeedTrack(PlayfieldLayout.TravelTime);
            foreach (float speed in Speeds) track.SetCoefficient(speed, immediate: true);
            bool stable = SameArray(b, Snapshot(chartB));

            bool ok = okA && okB && okC && stable;
            sb.AppendLine(ok
                ? "[OK]   验收7 BPM/变速：120BPM 第0/4/8拍 → " + Join(a) + "ms；第4拍变速到60BPM → " + Join(b) +
                  "ms；offset 0.25s → " + Join(c) + "ms（均与流速无关）"
                : $"[失败] 验收7 BPM 换算不正确：a={Join(a)} b={Join(b)} c={Join(c)} stable={stable}");
            return ok;
        }

        // ================================================================ 工具
        private static double[] Snapshot(ChartData chart)
        {
            if (chart == null || chart.notes == null) return null;
            var values = new double[chart.notes.Count];
            for (int i = 0; i < chart.notes.Count; i++) values[i] = chart.notes[i].JudgeTimeMs;
            return values;
        }

        private static string Verdicts(GameManager gm, double[] deltas)
        {
            var sb = new StringBuilder();
            foreach (double delta in deltas)
            {
                sb.Append(Code(gm.JudgeMs(delta))).Append(' ');
            }
            return sb.ToString().Trim();
        }

        /// <summary>判定等级的短码。Great / Good 都以 G 开头，所以必须区分成两位，否则读不出区别。</summary>
        private static string Code(Judgement judgement)
        {
            switch (judgement)
            {
                case Judgement.Perfect: return "P";
                case Judgement.Great: return "GR";
                case Judgement.Good: return "GD";
                case Judgement.Miss: return "M";
                default: return "空打";
            }
        }

        private static string Join(double[] values)
        {
            if (values == null) return "null";
            return "[" + string.Join(", ", Array.ConvertAll(values, v => v.ToString("F1"))) + "]";
        }

        private static bool SameArray(double[] a, double[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!NearlyEqual(a[i], b[i])) return false;
            }
            return true;
        }

        private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) < 0.001;

        private static bool Nearly(double a, double b) => NearlyEqual(a, b);

        /// <summary>命令行入口：只跑流速相关的无头验收，然后退出。</summary>
        public static void RunFromCommandLine()
        {
            var sb = new StringBuilder();
            bool pass = false;
            try
            {
                pass = RunHeadless(sb);
            }
            catch (Exception ex)
            {
                sb.AppendLine("[异常] " + ex);
            }

            Debug.Log(sb.ToString());
            Debug.Log(pass ? "[My4KGame] SCROLL_RESULT=PASS" : "[My4KGame] SCROLL_RESULT=FAIL");
            if (Application.isBatchMode) EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
#endif
