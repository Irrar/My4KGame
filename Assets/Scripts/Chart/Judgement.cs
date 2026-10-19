using UnityEngine;

namespace My4KGame
{
    /// <summary>
    /// 一次按键的判定等级。分档而不是"命中/未命中"二元判断，
    /// 是新手的挫败感来源里最容易修的一环：
    /// 哪怕每次都晚 100ms，玩家也能拿到 GOOD 和连击，练习动作能维持下去。
    /// </summary>
    public enum Judgement
    {
        None = 0,
        Perfect = 1,
        Great = 2,
        Good = 3,
        Miss = 4,
    }

    /// <summary>判定等级的展示信息与计分权重。</summary>
    public static class JudgementInfo
    {
        /// <summary>判定系数的英文名（也是手册里 Console 打印的名字）。</summary>
        public static string ToText(Judgement judgement)
        {
            switch (judgement)
            {
                case Judgement.Perfect: return "PERFECT";
                case Judgement.Great: return "GREAT";
                case Judgement.Good: return "GOOD";
                case Judgement.Miss: return "MISS";
                default: return "NONE";
            }
        }

        /// <summary>中文名，给结算面板用。</summary>
        public static string ToChinese(Judgement judgement)
        {
            switch (judgement)
            {
                case Judgement.Perfect: return "完美";
                case Judgement.Great: return "优秀";
                case Judgement.Good: return "良好";
                case Judgement.Miss: return "失误";
                default: return "空打";
            }
        }

        /// <summary>判定颜色：青色完美、绿色优秀、黄色良好、红色失误。</summary>
        public static Color ToColor(Judgement judgement)
        {
            switch (judgement)
            {
                case Judgement.Perfect: return Theme.Perfect;
                case Judgement.Great: return Theme.Great;
                case Judgement.Good: return Theme.Good;
                case Judgement.Miss: return Theme.Miss;
                default: return Theme.TextMuted;
            }
        }

        /// <summary>计分权重：Perfect 满分，Great 七成，Good 四成，Miss 零分。</summary>
        public static double Weight(Judgement judgement)
        {
            switch (judgement)
            {
                case Judgement.Perfect: return 1.0;
                case Judgement.Great: return 0.7;
                case Judgement.Good: return 0.4;
                default: return 0.0;
            }
        }

        /// <summary>是不是"打中了"（用于连击）。</summary>
        public static bool IsHit(Judgement judgement) =>
            judgement == Judgement.Perfect || judgement == Judgement.Great || judgement == Judgement.Good;
    }
}
