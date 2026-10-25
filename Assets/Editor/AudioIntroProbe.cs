#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace My4KGame.EditorTools
{
    /// <summary>
    /// 测量每首歌开头有多少静音（菜单 `My4KGame → 12. 测量音频前奏`）。
    ///
    /// 结果填进 <see cref="AudioTimeline"/> 的 LeadingSilence 表，游戏就会跳过这段静音，
    /// 玩家按下 ▶ 后不再干等。新加歌时跑一次即可。
    /// </summary>
    public static class AudioIntroProbe
    {
        [MenuItem("My4KGame/12. 测量音频前奏（跳过开头静音用）", false, 12)]
        public static void ProbeAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources/Audio" });
            if (guids.Length == 0)
            {
                Debug.LogWarning("[My4KGame] Assets/Resources/Audio 下没有音频。");
                return;
            }

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;
                float onset = FirstAudible(clip, out string envelope);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                Debug.Log($"[前奏] {name}：时长 {clip.length:F2}s，第一个有声位置 {onset:F2}s " +
                          $"（填进 AudioTimeline: \"{name}\", {onset:F2}f）");
                Debug.Log($"[前奏] {name} 前 3s 每 100ms 峰值：{envelope}");
            }
        }

        /// <summary>返回第一个峰值超过阈值的秒数；envelope 是前 3 秒的包络文本。</summary>
        public static float FirstAudible(AudioClip clip, out string envelope)
        {
            envelope = "";
            int channels = clip.channels;
            int rate = clip.frequency;
            int total = clip.samples;

            // Unity 的 GetData 要求数组正好等于 clip.samples * channels，所以整段读进来。
            // 一次几十 MB，只在编辑器里手动跑，没问题。
            int limit = total;
            var data = new float[limit * channels];
            if (!clip.GetData(data, 0)) return 0f;

            int win = Mathf.Max(1, rate / 100);   // 10ms
            int frames = limit / win;
            var sb = new StringBuilder();
            float result = 0f;
            bool found = false;

            for (int i = 0; i < frames; i++)
            {
                float peak = 0f;
                for (int j = 0; j < win; j++)
                {
                    int idx = (i * win + j) * channels;
                    for (int c = 0; c < channels; c++)
                    {
                        float v = Mathf.Abs(data[idx + c]);
                        if (v > peak) peak = v;
                    }
                }

                if (i < 30) sb.Append(peak.ToString("F2")).Append(' ');   // 前 3 秒
                if (!found && peak > 0.01f)
                {
                    result = i * 0.01f;
                    found = true;
                }
            }

            envelope = sb.ToString();
            return result;
        }
    }
}
#endif
