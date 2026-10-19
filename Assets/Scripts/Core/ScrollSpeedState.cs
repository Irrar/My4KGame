using System;
using UnityEngine;

namespace My4KGame
{
    /// <summary>
    /// 流速状态的全局唯一数据源（单例式静态类）。
    ///
    /// 存在的理由：流速在"主界面"和"游戏内"两处都能调，如果各自存一份，
    /// 一定会出现"主界面调了 6.25，进游戏还是 5.50"这种不同步。
    /// 所以这里做成一个不带场景依赖的全局状态：
    ///   · 谁要读 → <see cref="Value"/>(或 <see cref="Coefficient"/>)
    ///   · 谁要改 → <see cref="Set"/>(或 <see cref="Adjust"/>)
    ///   · 谁要跟着变 → 订阅 <see cref="Changed"/>
    /// 主界面卡片、游戏内悬浮窗、视觉轨道都只认这一个值。
    ///
    /// 它**只管一个 1.00~10.00 的数值**，不存任何音符数据，也不碰判定窗口 ——
    /// 判定时间轴在谱面加载时就固定了，跟这里完全无关。
    /// </summary>
    public static class ScrollSpeedState
    {
        /// <summary>取值下限（最慢）。</summary>
        public const float MinValue = ScrollSpeedTrack.MinValue;

        /// <summary>取值上限（最快）。</summary>
        public const float MaxValue = ScrollSpeedTrack.MaxValue;

        /// <summary>每次调整的步长。</summary>
        public const float Step = ScrollSpeedTrack.Step;

        /// <summary>默认值：正好是中位，等价于 ×1.00。</summary>
        public const float DefaultValue = ScrollSpeedTrack.CenterValue;

        private const string PrefKeyInternal = "My4KGame.ScrollSpeedValue";

        /// <summary>存档键（测试/诊断用）。</summary>
        public const string PrefKey = PrefKeyInternal;

        /// <summary>数值变化时触发，参数是新的数值（1.00~10.00）。</summary>
        public static event Action<float> Changed;

        /// <summary>当前数值（1.00~10.00，已对齐 0.25）。</summary>
        public static float Value { get; private set; } = DefaultValue;

        /// <summary>当前数值对应的视觉系数（×0.25 ~ ×4.00）。</summary>
        public static float Coefficient => ScrollSpeedTrack.ValueToCoefficient(Value);

        /// <summary>当前数值下"音符走完一屏"需要的时间（秒）。只用于显示。</summary>
        public static float TravelSeconds => PlayfieldLayout.TravelTime / Mathf.Max(0.01f, Coefficient);

        private static bool _loaded;

        /// <summary>从本地存档读回上次的设置。多次调用只有第一次生效。</summary>
        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            float value = DefaultValue;

            string raw = PlayerPrefs.GetString(PrefKey, "");
            if (float.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float saved))
            {
                value = saved;
            }
            else
            {
                // 兼容上一版存在 My4KGame.ScrollLevel 里的档位
                string oldRaw = PlayerPrefs.GetString("My4KGame.ScrollLevel", "");
                if (float.TryParse(oldRaw, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float oldLevel))
                {
                    value = oldLevel;
                }
            }

            Value = ScrollSpeedTrack.SnapValue(value);
        }

        /// <summary>设置数值。会自动夹取到 1.00~10.00 并对齐 0.25。</summary>
        public static void Set(float value, bool save = true)
        {
            float snapped = ScrollSpeedTrack.SnapValue(value);
            if (Mathf.Approximately(snapped, Value)) return;

            Value = snapped;

            if (save)
            {
                PlayerPrefs.SetString(PrefKey, Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                PlayerPrefs.Save();
            }

            Changed?.Invoke(Value);
        }

        /// <summary>在当前数值上加减（一次 0.25）。</summary>
        public static void Adjust(float delta)
        {
            Set(Value + delta);
        }

        /// <summary>重置回默认值（清空成绩时用）。</summary>
        public static void ResetToDefault()
        {
            Value = DefaultValue;
            PlayerPrefs.SetString(PrefKey, Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
            Changed?.Invoke(Value);
        }
    }
}
