using System.Collections.Generic;

namespace My4KGame
{
    /// <summary>
    /// 音频时间轴：处理"曲子开头有一段静音"这件事。
    ///
    /// 实测（用 AudioClip.GetData 逐 10ms 扫峰值得到）：
    ///   · World's end BLACKBOX：前 1.23 秒是**纯静音**，第一个声音在 1.23s；
    ///   · in your blue eyes：前 12.61 秒是静音，第一个声音在 12.61s；
    ///   · Neon Pulse：0 秒就有声。
    ///
    /// 如果不处理，玩家会经历"倒计时结束 → 再干等 1 秒多才听到声音"，
    /// 而这段时间里音符已经在落了 —— 看起来就像"音符比音乐先出来"。
    ///
    /// 做法：播放时直接把 AudioSource 定位到第一个有声处（跳过静音），
    /// 同时把所有音符的判定时间整体前移同样的秒数。两者的相对关系一点没变
    /// （采音不受影响），但按下去立刻就能听到声音。
    ///
    /// 换歌/加歌时，跑一次编辑器菜单 `My4KGame → 12. 测量音频前奏` 就能拿到数值，
    /// 填到下面的表里即可。
    /// </summary>
    public static class AudioTimeline
    {
        /// <summary>开头需要跳过的静音长度（秒），键是 Resources/Audio 下的文件名（不含后缀）。</summary>
        /// <remarks>
        /// 取值比"第一个有声位置"再早一点点（约 10~30ms），保证原本踩在第一声上的那个音符
        /// 不会被算成负数丢掉；提前的这点时间本来就是静音，听不出来。
        /// </remarks>
        private static readonly Dictionary<string, float> LeadingSilence = new Dictionary<string, float>
        {
            { "WorldsEndBLACKBOX", 1.20f },     // 实测首声 1.23s，首个音符 1.207s
            { "in_your_blue_eyes", 12.58f },    // 实测首声 12.61s，首个音符 12.587s
        };

        /// <summary>这首歌要从第几秒开始播（0 = 原样从头播）。</summary>
        public static float StartOffset(string audioName)
        {
            if (string.IsNullOrEmpty(audioName)) return 0f;
            float value;
            return LeadingSilence.TryGetValue(audioName, out value) ? value : 0f;
        }

        /// <summary>这首歌"有效时长"要扣掉跳过的静音。</summary>
        public static double EffectiveLength(string audioName, double clipLength)
        {
            double length = clipLength - StartOffset(audioName);
            return length > 0.5 ? length : clipLength;
        }
    }
}
