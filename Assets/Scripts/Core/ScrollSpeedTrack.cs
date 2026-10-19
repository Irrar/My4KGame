using System;
using UnityEngine;

namespace My4KGame
{
    /// <summary>
    /// 视觉轨道（ScrollSpeedTrack）—— 「判定归时间、视觉归流速」里的"流速"那一半。
    ///
    /// ── 这个类为什么刻意做得这么小 ──────────────────────────────────
    /// 它只做一件事：把「音符判定时间戳 − 当前歌曲时间」（秒）映射成屏幕上的 Y 坐标。
    /// 它不知道判定窗口、不知道判定结果、不知道连击、也不持有任何 NoteData ——
    /// 换句话说，判定系统即使完全不认识它也能正常工作。
    ///
    /// ── 映射公式 ────────────────────────────────────────────────
    ///   travelSeconds = baseTravelSeconds / coefficient          （流速越快，走完全程用的时间越短）
    ///   progress      = clamp01(secondsAhead / travelSeconds)     （0 = 在判定线上，1 = 在出生点）
    ///   y             = lerp(JudgeLineY, SpawnY, progress)
    ///
    /// 于是：时间差相同的情况下，流速 2x 映射出的视觉距离是 1x 的两倍；
    /// 但 secondsAhead 本身、以及音符的判定时间戳，一个字节都没变。
    ///
    /// ── 关于平滑 ────────────────────────────────────────────────
    /// 运行中调速时，applied 会以 0.12 秒的时程追上 target，
    /// 避免音符位置瞬间跳变（视觉上是"加速度"，不是"瞬移"）。
    /// 平滑只作用在 applied 这一个浮点数上，跟判定完全无关。
    /// </summary>
    public sealed class ScrollSpeedTrack
    {
        /// <summary>最慢 0.25x（数值 1.00）：一屏 8 秒，慢到可以数音符。</summary>
        public const float MinCoefficient = 0.25f;

        /// <summary>最快 4x（数值 10.00）：一屏 0.5 秒。</summary>
        public const float MaxCoefficient = 4.0f;

        // ---------------------------------------------------------------- 玩家看到的数值 ↔ 视觉系数
        // 玩家只看到一个数：1.00 ~ 10.00，步长 0.25（全局统一，主界面和游戏内共用）。
        // 映射是等比的，而且刻意把范围拉宽到 0.25x ~ 4x：
        //     系数 = 2 ^ (2 × (数值 − 5.50) / 4.50)
        //     1.00  → ×0.25（一屏 8.00s）
        //     5.50  → ×1.00（一屏 2.00s）  ← 默认值，正好是中位
        //     10.00 → ×4.00（一屏 0.50s）
        //
        // 为什么范围要这么宽：如果只到 0.5x~2x，37 个 0.25 步每步只差 3.9%，
        // 按一下几乎看不出变化（这就是"调了像没调"的真正原因）。
        // 现在每 0.25 = ×1.08，一格就能看出来；一次调 1.00 = ×1.36，非常明显。
        public const float MinValue = 1.00f;
        public const float MaxValue = 10.00f;
        public const float Step = 0.25f;
        public const float CenterValue = 5.50f;
        public const float HalfSpan = 4.50f;

        /// <summary>数值 → 视觉系数（等比）。</summary>
        public static float ValueToCoefficient(float value)
        {
            float clamped = Mathf.Clamp(value, MinValue, MaxValue);
            return Mathf.Clamp(Mathf.Pow(2f, 2f * (clamped - CenterValue) / HalfSpan),
                MinCoefficient, MaxCoefficient);
        }

        /// <summary>视觉系数 → 数值（ValueToCoefficient 的反函数，用于回显）。</summary>
        public static float CoefficientToValue(float coefficient)
        {
            float safe = Mathf.Max(0.01f, coefficient);
            float value = CenterValue + Mathf.Log(safe, 2f) * HalfSpan * 0.5f;
            return Mathf.Clamp(value, MinValue, MaxValue);
        }

        /// <summary>把数值对齐到 0.25 的整数倍，并夹在 1.00 ~ 10.00。</summary>
        public static float SnapValue(float value)
        {
            float snapped = Mathf.Round(value / Step) * Step;
            return Mathf.Clamp(snapped, MinValue, MaxValue);
        }

        /// <summary>调速时的视觉平滑时长（秒）。只影响观感。</summary>
        public const float SmoothSeconds = 0.12f;

        /// <summary>1x 时音符从出生点落到判定线所需时间（秒）。这是视觉基准，不是判定参数。</summary>
        public float BaseTravelSeconds { get; }

        /// <summary>玩家设定的目标流速（立刻生效于"目标"，观感上由 Applied 平滑追上来）。</summary>
        public float TargetCoefficient { get; private set; } = 1f;

        /// <summary>当前实际用于映射的流速（平滑后的值）。</summary>
        public float AppliedCoefficient { get; private set; } = 1f;

        /// <summary>目标数值（1.00~10.00），给 UI 显示用。</summary>
        public float TargetValue => CoefficientToValue(TargetCoefficient);

        /// <summary>当前实际数值（平滑后）。</summary>
        public float Value => CoefficientToValue(AppliedCoefficient);

        /// <summary>按当前流速，音符走完全程需要的时间（秒）。</summary>
        public float TravelSeconds => BaseTravelSeconds / Mathf.Max(0.01f, AppliedCoefficient);

        /// <summary>目标流速变化时触发（UI 用）。</summary>
        public event Action<float> TargetChanged;

        public ScrollSpeedTrack(float baseTravelSeconds)
        {
            BaseTravelSeconds = Mathf.Max(0.05f, baseTravelSeconds);
        }

        /// <summary>设置流速（系数形式）。immediate = true 时不做平滑。</summary>
        public void SetCoefficient(float coefficient, bool immediate = false)
        {
            float clamped = Mathf.Clamp(coefficient, MinCoefficient, MaxCoefficient);
            bool changed = !Mathf.Approximately(clamped, TargetCoefficient);
            TargetCoefficient = clamped;

            if (immediate)
            {
                AppliedCoefficient = clamped;
                if (changed) TargetChanged?.Invoke(clamped);
                return;
            }

            if (changed) TargetChanged?.Invoke(clamped);
        }

        /// <summary>每帧调用，让 AppliedCoefficient 追向 TargetCoefficient。</summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (Mathf.Approximately(AppliedCoefficient, TargetCoefficient)) return;

            float k = SmoothSeconds <= 0.0001f
                ? 1f
                : Mathf.Clamp01(unscaledDeltaTime / SmoothSeconds);
            AppliedCoefficient = Mathf.Lerp(AppliedCoefficient, TargetCoefficient, k);
        }

        // ================================================================ 纯映射（唯一入口）
        /// <summary>
        /// 【视觉轨道唯一出口】「还差多少秒到判定线」→ 屏幕 Y。
        /// 判定代码永远不该调用它 —— 这是评审时要检查的那条线。
        /// </summary>
        public float SecondsAheadToY(float secondsAhead)
        {
            float progress = Mathf.Clamp01(secondsAhead / TravelSeconds);
            return Mathf.Lerp(PlayfieldLayout.JudgeLineY, PlayfieldLayout.SpawnY, progress);
        }

        /// <summary>反函数：屏幕 Y → 还差多少秒（调试/可视化用）。</summary>
        public float YToSecondsAhead(float y)
        {
            float progress = Mathf.Clamp01(
                Mathf.InverseLerp(PlayfieldLayout.JudgeLineY, PlayfieldLayout.SpawnY, y));
            return progress * TravelSeconds;
        }

        /// <summary>某个音符此刻在视觉上距离判定线还有多少秒（只读时钟，不读位置）。</summary>
        public static float SecondsAheadOf(double judgeTimeMs, double songTimeSeconds)
        {
            return (float)(judgeTimeMs / 1000.0 - songTimeSeconds);
        }

        public override string ToString() => $"×{TargetCoefficient:0.00}";
    }
}
