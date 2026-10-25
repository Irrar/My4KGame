using UnityEngine;

namespace My4KGame
{
    /// <summary>
    /// 舞台尺寸与配色常量 —— 全工程唯一的"布局真相"。
    ///
    /// 数值直接沿用《Unity 音游制作手册》第 2 章：
    ///   判定线 Position Y = -4，Scale = (10, 0.1)
    ///   四条轨道 Scale = (2.4, 12)，Position X = -3.75 / -1.25 / 1.25 / 3.75
    /// 手册里的这些数字散落在 Inspector 里，容易改一处忘一处；
    /// 这里集中成常量，代码和场景都由它推导，不可能对不上。
    /// </summary>
    public static class PlayfieldLayout
    {
        /// <summary>即 4K 的 4：四条轨道。</summary>
        public const int LaneCount = 4;

        /// <summary>单条轨道的宽度（世界单位）。</summary>
        public const float LaneWidth = 2.4f;

        /// <summary>轨道面板高度。</summary>
        public const float LaneHeight = 12f;

        /// <summary>判定线的 Y 坐标（手册 4.3 里的 targetY）。</summary>
        public const float JudgeLineY = -4f;

        /// <summary>音符出生点的 Y 坐标（手册 4.3 里的 startY）。</summary>
        public const float SpawnY = 6f;

        /// <summary>
        /// 1x 流速下，音符从出生点落到判定线所需时间（手册里的 approachTime）。
        /// 注意：这是「视觉基准」，不是判定参数 —— 判定窗口在 GameManager 里，和它无关。
        /// 玩家看到的速度数值与系数的换算在 ScrollSpeedTrack / ScrollSpeedState 里，别在这里再加一套。
        /// </summary>
        public const float TravelTime = 2.0f;

        /// <summary>判定线宽度。</summary>
        public const float JudgeLineWidth = 10.4f;

        /// <summary>起播前的准备时间：这段时间歌曲时间是负数，用来跑 3-2-1 倒计时。</summary>
        public const double DefaultLeadIn = 2.0;

        /// <summary>四条轨道的中心 X 坐标。</summary>
        public static readonly float[] LaneX = { -3.75f, -1.25f, 1.25f, 3.75f };

        /// <summary>四条轨道的主题色（音符、轨道高亮、按键提示统一使用）。</summary>
        public static readonly Color[] LaneColors =
        {
            new Color(0.30f, 0.78f, 0.96f),   // Lane0 / D  天蓝
            new Color(0.98f, 0.82f, 0.36f),   // Lane1 / F  金黄
            new Color(0.42f, 0.86f, 0.58f),   // Lane2 / J  青绿
            new Color(0.78f, 0.50f, 0.92f),   // Lane3 / K  紫
        };

        /// <summary>默认键位（手册 4.3 用的 D F J K）。</summary>
        public static readonly KeyCode[] DefaultKeys = { KeyCode.D, KeyCode.F, KeyCode.J, KeyCode.K };

        /// <summary>键位显示名。</summary>
        public static readonly string[] KeyNames = { "D", "F", "J", "K" };

        public static Color LaneColor(int lane) => LaneColors[Mathf.Clamp(lane, 0, LaneCount - 1)];

        /// <summary>把轨道号换成世界坐标 X。</summary>
        public static float LaneToX(int lane) => LaneX[Mathf.Clamp(lane, 0, LaneCount - 1)];

        /// <summary>给定 Y 坐标，反推"距离到达判定线还有多少秒"（用于画辅助线等）。</summary>
        public static float YToTimeAhead(float y)
        {
            float progress = Mathf.InverseLerp(SpawnY, JudgeLineY, y);
            return (1f - progress) * TravelTime;
        }
    }

    /// <summary>全局配色（和 PlayfieldLayout 分开，改主题色只动这里）。</summary>
    public static class Theme
    {
        public static readonly Color Background = new Color(0.043f, 0.055f, 0.078f);
        public static readonly Color Panel = new Color(0.078f, 0.098f, 0.137f);
        public static readonly Color PanelLight = new Color(0.118f, 0.145f, 0.196f);
        public static readonly Color LaneBase = new Color(0.075f, 0.090f, 0.125f, 0.92f);
        public static readonly Color TextPrimary = new Color(0.94f, 0.96f, 0.99f);
        public static readonly Color TextMuted = new Color(0.62f, 0.67f, 0.75f);
        public static readonly Color Accent = new Color(0.30f, 0.78f, 0.96f);
        public static readonly Color AccentWarm = new Color(0.98f, 0.82f, 0.36f);

        public static readonly Color Perfect = new Color(0.36f, 0.90f, 0.98f);
        public static readonly Color Great = new Color(0.48f, 0.88f, 0.50f);
        public static readonly Color Good = new Color(0.98f, 0.82f, 0.36f);
        public static readonly Color Miss = new Color(0.96f, 0.36f, 0.42f);
    }
}
