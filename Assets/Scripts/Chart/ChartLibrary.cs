using System;
using System.Collections.Generic;
using UnityEngine;

namespace My4KGame
{
    /// <summary>谱面条目：选曲界面用它列出所有可用谱面。</summary>
    public sealed class ChartEntry
    {
        /// <summary>Resources 下的名字，例如 "NeonPulse_Normal"。</summary>
        public string ResourceName;

        /// <summary>解析好的谱面（解析失败时为 null）。</summary>
        public ChartData Chart;

        /// <summary>解析失败的原因（成功时为 null）。</summary>
        public string Error;

        /// <summary>能不能玩。注意：Error 里可能只是"去掉了几个坏音符"这类警告，不影响开谱。</summary>
        public bool IsValid => Chart != null;

        public string DisplayName => Chart == null ? ResourceName : Chart.difficulty;
        public int Level => Chart == null ? 0 : Chart.level;
        public int NoteCount => Chart == null ? 0 : Chart.NoteCount;
    }

    /// <summary>
    /// 谱面仓库：把 Assets/Resources/Charts 下所有 json 读成 <see cref="ChartData"/>。
    ///
    /// 两种格式都支持（这是故意做的兼容）：
    ///   1. 手册第 6 章那种"纯数组" —— [ {"time":2.0,"lane":0}, ... ]
    ///   2. 本工程生成的"带元数据对象" —— { "songName":..., "notes":[ ... ] }
    ///
    /// 用 Resources 而不是 StreamingAssets，是因为 Resources.Load 在编辑器和
    /// 打包后的 exe 里行为完全一致，不用处理各平台的路径差异。
    /// </summary>
    public static class ChartLibrary
    {
        public const string ResourceFolder = "Charts";

        private static List<ChartEntry> _entries;

        /// <summary>所有谱面（按难度排序）。第一次访问时自动加载。</summary>
        public static IReadOnlyList<ChartEntry> Entries => _entries ?? (_entries = LoadAll());

        /// <summary>强制重新扫描一遍 Resources/Charts（编辑器里改完 json 可以调它）。</summary>
        public static IReadOnlyList<ChartEntry> Reload()
        {
            _entries = LoadAll();
            return _entries;
        }

        private static List<ChartEntry> LoadAll()
        {
            var result = new List<ChartEntry>();
            var assets = Resources.LoadAll<TextAsset>(ResourceFolder);
            foreach (var asset in assets)
            {
                if (asset == null) continue;
                var entry = new ChartEntry { ResourceName = asset.name };
                try
                {
                    entry.Chart = Parse(asset.text, out string error);
                    entry.Error = error;
                }
                catch (Exception ex)
                {
                    entry.Error = "解析异常：" + ex.Message;
                }
                result.Add(entry);
            }

            result.Sort(CompareEntries);
            return result;
        }

        private static int CompareEntries(ChartEntry a, ChartEntry b)
        {
            int Rank(ChartEntry e)
            {
                if (!e.IsValid) return 99;
                switch (e.Chart.difficulty)
                {
                    case "Easy": return 0;
                    case "Normal": return 1;
                    case "Hard": return 2;
                    case "Calibration": return 3;
                    default: return 4;
                }
            }

            int ra = Rank(a), rb = Rank(b);
            if (ra != rb) return ra.CompareTo(rb);
            int la = a.Level, lb = b.Level;
            if (la != lb) return la.CompareTo(lb);
            return string.Compare(a.ResourceName, b.ResourceName, StringComparison.Ordinal);
        }

        /// <summary>按 Resources 名查一张谱面。</summary>
        public static ChartEntry Find(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName)) return null;
            foreach (var e in Entries)
            {
                if (e.ResourceName == resourceName) return e;
            }
            return null;
        }

        /// <summary>加载一张谱面，失败时通过 error 返回原因。</summary>
        public static bool TryLoad(string resourceName, out ChartData chart, out string error)
        {
            chart = null;
            error = null;

            var entry = Find(resourceName);
            if (entry != null)
            {
                chart = entry.Chart;
                error = entry.Error;
                return entry.IsValid;
            }

            // 不在目录里（可能是刚拖进来的、或名字写错了）——再直接尝试按路径读一次
            var asset = Resources.Load<TextAsset>(ResourceFolder + "/" + resourceName);
            if (asset == null)
            {
                error = $"找不到谱面 \"{resourceName}\"。请确认它放在 Assets/Resources/{ResourceFolder}/ 下，" +
                        "并且文件名和 Resource Name 一致。";
                return false;
            }

            chart = Parse(asset.text, out error);
            return chart != null && string.IsNullOrEmpty(error);
        }

        /// <summary>
        /// 把 json 文本解析成谱面。返回 null 表示彻底无法解析；
        /// 部分音符有问题时会把问题写进 error，但能用的音符仍然保留。
        /// </summary>
        public static ChartData Parse(string json, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "谱面文件是空的。";
                return null;
            }

            // 去掉记事本可能留下的 UTF-8 BOM
            json = json.Trim('\uFEFF', '\u200B', ' ', '\r', '\n', '\t');

            ChartData chart;
            if (json.StartsWith("["))
            {
                // 手册第 6 章的纯数组写法：补一个壳再交给 JsonUtility
                chart = JsonUtility.FromJson<ChartData>("{\"notes\":" + json + "}");
            }
            else
            {
                chart = JsonUtility.FromJson<ChartData>(json);
            }

            if (chart == null)
            {
                error = "JSON 格式不对，JsonUtility 解析失败。常见原因：多了个逗号、少了引号、用了中文标点。";
                return null;
            }

            if (chart.notes == null) chart.notes = new List<NoteData>();

            // JsonUtility 在字段缺失时可能保留字段初始值，也可能不保留，这里统一兜底
            if (string.IsNullOrEmpty(chart.songName)) chart.songName = "未命名曲目";
            if (string.IsNullOrEmpty(chart.audio)) chart.audio = "NeonPulse";
            if (string.IsNullOrEmpty(chart.difficulty)) chart.difficulty = "Custom";
            if (chart.level <= 0) chart.level = 1;
            if (chart.bpm <= 0f) chart.bpm = 120f;

            // ============================================================
            // 判定时间戳的"唯一一次"计算（整个工程只在这里做）
            //   毫秒 = (有 beat 就用 BPM 换算，否则直接用 time) + offset
            // 算完之后烘进 NoteData.JudgeTimeMs，之后任何代码都不再改它：
            // 调速不改、暂停不改、视觉位置也不回写。
            // ============================================================
            double offsetMs = chart.offset * 1000.0;

            int badLane = 0, negativeTime = 0, duplicates = 0;
            var seen = new HashSet<long>();
            var cleaned = new List<NoteData>(chart.notes.Count);

            foreach (NoteData note in chart.notes)
            {
                if (note == null) continue;
                if (note.lane < 0 || note.lane >= PlayfieldLayout.LaneCount) { badLane++; continue; }

                double judgeTimeMs = (note.beat >= 0f ? chart.BeatToMs(note.beat) : note.time * 1000.0)
                                     + offsetMs;

                if (judgeTimeMs < 0.0 || double.IsNaN(judgeTimeMs) || double.IsInfinity(judgeTimeMs))
                {
                    negativeTime++;
                    continue;
                }

                long key = ((long)Math.Round(judgeTimeMs) << 3) | (uint)note.lane;
                if (!seen.Add(key)) { duplicates++; continue; }

                note.BakeJudgeTimeMs(judgeTimeMs);   // time 字段同步更新为等价秒数
                cleaned.Add(note);
            }

            chart.notes = cleaned;
            chart.offset = 0f;   // 偏移已经烘进每个音符，之后不再需要

            chart.notes.Sort((a, b) => a.JudgeTimeMs.CompareTo(b.JudgeTimeMs));

            if (cleaned.Count == 0)
            {
                error = "谱面里没有能用的音符。请检查每一项的 lane 是否在 0~3、time 是否 >= 0。";
                return null;
            }

            if (badLane > 0 || negativeTime > 0)
            {
                error = $"已忽略 {badLane} 个轨道号越界的音符、{negativeTime} 个时间为负的音符。";
            }
            if (duplicates > 0)
            {
                error = (error ?? "") + $" 已去掉 {duplicates} 个重复音符。";
            }

            return chart;
        }
    }
}
