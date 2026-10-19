using System;
using System.Collections.Generic;
using UnityEngine;

namespace My4KGame
{
    /// <summary>
    /// 本地成绩记录。用 PlayerPrefs 就够了（Windows 下存在注册表里），
    /// 不需要账号系统，也不需要自己写文件。
    /// </summary>
    public static class SaveData
    {
        private const string Prefix = "My4KGame.Best.";

        public static long BestScore(string chartName)
        {
            if (string.IsNullOrEmpty(chartName)) return 0;
            return long.TryParse(PlayerPrefs.GetString(Prefix + chartName + ".Score", "0"), out long v) ? v : 0;
        }

        public static string BestRank(string chartName)
        {
            if (string.IsNullOrEmpty(chartName)) return "";
            return PlayerPrefs.GetString(Prefix + chartName + ".Rank", "");
        }

        public static double BestAccuracy(string chartName)
        {
            if (string.IsNullOrEmpty(chartName)) return 0.0;
            string raw = PlayerPrefs.GetString(Prefix + chartName + ".Acc", "0");
            return double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0.0;
        }

        /// <summary>提交一次成绩。返回 true 表示刷新了个人纪录。</summary>
        public static bool Submit(string chartName, long score, string rank, double accuracy)
        {
            if (string.IsNullOrEmpty(chartName)) return false;
            bool isNewBest = score > BestScore(chartName);
            if (isNewBest)
            {
                PlayerPrefs.SetString(Prefix + chartName + ".Score", score.ToString());
                PlayerPrefs.SetString(Prefix + chartName + ".Rank", rank ?? "");
                PlayerPrefs.SetString(Prefix + chartName + ".Acc",
                    accuracy.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                PlayerPrefs.Save();
            }
            return isNewBest;
        }

        /// <summary>清空所有纪录（菜单里的"重置成绩"用）。</summary>
        public static void ClearAll()
        {
            foreach (var e in ChartLibrary.Entries)
            {
                if (e == null || string.IsNullOrEmpty(e.ResourceName)) continue;
                PlayerPrefs.DeleteKey(Prefix + e.ResourceName + ".Score");
                PlayerPrefs.DeleteKey(Prefix + e.ResourceName + ".Rank");
                PlayerPrefs.DeleteKey(Prefix + e.ResourceName + ".Acc");
            }
            PlayerPrefs.SetString("My4KGame.Calibration", "0");
            PlayerPrefs.SetString("My4KGame.ScrollSpeed", "1");
            PlayerPrefs.DeleteKey("My4KGame.ScrollLevel");
            PlayerPrefs.Save();

            // 流速走全局状态（它会自己写回默认值）
            ScrollSpeedState.ResetToDefault();
        }
    }
}
