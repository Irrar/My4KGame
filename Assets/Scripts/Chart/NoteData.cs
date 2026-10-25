using System;
using System.Collections.Generic;
using UnityEngine;

namespace My4KGame
{
    /// <summary>
    /// 单个音符的数据（手册 4.1 的扩展版）。
    ///
    /// 关键概念 —— <see cref="JudgeTimeMs"/>：音符*应该被击中的音乐时间戳*（毫秒）。
    /// 它在谱面加载时由 BPM / 拍号 / offset 一次性算出来，之后*永不改变*：
    ///    · 调流速不改它；
    ///    · 暂停/恢复不改它；
    ///    · 视觉位置怎么算都不回写它。
    /// 判定只用「当前歌曲时间 − JudgeTimeMs」这个差值，所以流速再快再慢，
    /// 同一个音符的命中判定永远落在同一毫秒上。
    ///
    /// Hold（长按）：duration / durationBeat 大于 0 时，这个音符从 JudgeTimeMs 开始
    /// 一直持续到 EndJudgeTimeMs；玩家需要在头按下、按住到尾。
    /// </summary>
    [Serializable]
    public class NoteData
    {
        [Tooltip("音符到达判定线的时间（秒，从歌曲第 0 秒算起）")]
        public float time;

        [Tooltip("轨道号 0..3")]
        public int lane;

        [Tooltip("可选：用拍号定位（第 0 拍 = 歌曲第 0 秒）。填了 -1 以外的值就以 beat 为准，time 会被算出来")]
        public float beat = -1f;

        [Tooltip("长按持续秒数。0 = 普通音符；>0 = Hold")]
        public float duration = 0f;

        [Tooltip("可选：长按持续拍数。填了且 beat 有效时，优先按拍数算持续时长")]
        public float durationBeat = -1f;

        // ---------------------------------------------------------------- 判定时间戳（只读）
        // 不加 [SerializeField]：它只存在于运行时，由 ChartLibrary 烘一次，永远不再变。
        private double _judgeTimeMs = double.NaN;
        private double _endJudgeTimeMs = double.NaN;
        private bool _isHold;

        /// <summary>
        /// 判定时间戳（毫秒）。谱面加载时算好，之后不可变。
        /// 若在加载前就取用，会退化成 time * 1000（兼容手写测试代码）。
        /// </summary>
        public double JudgeTimeMs => double.IsNaN(_judgeTimeMs) ? time * 1000.0 : _judgeTimeMs;

        /// <summary>判定时间戳的秒数形式（视觉映射用；判定内部用毫秒，避免浮点误差）。</summary>
        public double JudgeTimeSeconds => JudgeTimeMs / 1000.0;

        /// <summary>是否已经"烘"过判定时间戳。</summary>
        public bool HasJudgeTime => !double.IsNaN(_judgeTimeMs);

        /// <summary>长按结束时间戳（毫秒）。普通音符等于 JudgeTimeMs。</summary>
        public double EndJudgeTimeMs => double.IsNaN(_endJudgeTimeMs) ? JudgeTimeMs : _endJudgeTimeMs;

        /// <summary>是不是长按（Hold）。</summary>
        public bool IsHold => _isHold;

        /// <summary>长按持续毫秒数（普通音符为 0）。</summary>
        public double HoldDurationMs => IsHold ? EndJudgeTimeMs - JudgeTimeMs : 0.0;

        /// <summary>
        /// 由谱面加载流程调用一次，写入判定时间戳。
        /// 全工程只有 ChartLibrary 调它 —— 其它任何地方调都是 bug（会让判定时间漂移）。
        /// </summary>
        public void BakeJudgeTimeMs(double judgeTimeMs)
        {
            _judgeTimeMs = judgeTimeMs;
            time = (float)(judgeTimeMs / 1000.0);
        }

        /// <summary>由谱面加载流程调用一次，写入长按结束时间戳和是否 Hold。</summary>
        public void BakeHold(double endJudgeTimeMs, bool isHold)
        {
            _endJudgeTimeMs = endJudgeTimeMs;
            _isHold = isHold;
        }

        public NoteData() { }

        public NoteData(float time, int lane)
        {
            this.time = time;
            this.lane = lane;
        }

        public override string ToString() =>
            IsHold
                ? $"lane{lane} hold @ {JudgeTimeMs:F1}ms ~ {EndJudgeTimeMs:F1}ms"
                : $"lane{lane} @ {JudgeTimeMs:F1}ms";
    }

    /// <summary>变速谱面用：从某一拍开始换 BPM。</summary>
    [Serializable]
    public class BpmChange
    {
        [Tooltip("从第几拍开始换（第 0 拍 = 歌曲第 0 秒）")]
        public float beat;

        [Tooltip("新的 BPM")]
        public float bpm;
    }

    /// <summary>
    /// 一整张谱面。比手册多了几项元数据（歌名、BPM、难度、偏移、变速表），
    /// 这样选曲界面不用写死任何东西，加一首歌只要丢一个 json 进 Charts 目录。
    /// </summary>
    [Serializable]
    public class ChartData
    {
        [Tooltip("曲名，显示在界面左上角")]
        public string songName = "";

        [Tooltip("音频文件名（Resources/Audio 下不带后缀的名字）")]
        public string audio = "";

        [Tooltip("每分钟拍数，用于把 beat 换算成毫秒")]
        public float bpm = 120f;

        [Tooltip("难度名：Easy / Normal / Hard ...")]
        public string difficulty = "";

        [Tooltip("难度等级数字，1~10")]
        public int level = 1;

        [Tooltip("谱面整体偏移（秒）。正值 = 所有音符往后挪")]
        public float offset = 0f;

        [Tooltip("可选：变速表。留空 = 全程用上面的 bpm")]
        public List<BpmChange> bpmChanges = new List<BpmChange>();

        [Tooltip("音符列表，按时间升序")]
        public List<NoteData> notes = new List<NoteData>();

        /// <summary>音符总数。</summary>
        public int NoteCount => notes == null ? 0 : notes.Count;

        /// <summary>最后一个音符的时间。</summary>
        public float LastNoteTime => notes == null || notes.Count == 0 ? 0f : notes[notes.Count - 1].time;

        /// <summary>第一个音符的时间。</summary>
        public float FirstNoteTime => notes == null || notes.Count == 0 ? 0f : notes[0].time;

        /// <summary>整张谱面的时长估计（最后一个音符 + 2 秒收尾）。</summary>
        public float Duration => LastNoteTime + 2f;

        /// <summary>某条轨道上的音符数量（用来做"轨道密度"统计）。</summary>
        public int[] LaneCounts()
        {
            var counts = new int[PlayfieldLayout.LaneCount];
            if (notes == null) return counts;
            foreach (var n in notes)
            {
                if (n.lane >= 0 && n.lane < counts.Length) counts[n.lane]++;
            }
            return counts;
        }

        /// <summary>
        /// 把「第几拍」换算成毫秒，支持变速表。
        /// 这是唯一做拍↔时间换算的地方，加载时调用一次，结果烘进每个音符的 JudgeTimeMs。
        /// </summary>
        public double BeatToMs(float beat)
        {
            if (beat <= 0f) return 0.0;

            var changes = bpmChanges;
            double ms = 0.0;
            double previousBeat = 0.0;
            double currentBpm = bpm > 0.1f ? bpm : 120.0;

            if (changes != null && changes.Count > 0)
            {
                var sorted = new List<BpmChange>(changes);
                sorted.Sort((a, b) => a.beat.CompareTo(b.beat));

                foreach (BpmChange change in sorted)
                {
                    if (change == null || change.bpm <= 0.1f) continue;
                    if (change.beat >= beat) break;
                    if (change.beat > previousBeat)
                    {
                        ms += (change.beat - previousBeat) * 60000.0 / currentBpm;
                        previousBeat = change.beat;
                    }
                    currentBpm = change.bpm;
                }
            }

            ms += (beat - previousBeat) * 60000.0 / currentBpm;
            return ms;
        }
    }
}
