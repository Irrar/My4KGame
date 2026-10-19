using System;
using UnityEngine;

namespace My4KGame
{
    /// <summary>
    /// 计分系统 —— 一个纯 C# 类（不是 MonoBehaviour），因此可以脱离 Unity 单独测试。
    ///
    /// 计分规则（写进作业报告也好用）：
    ///   总分 1,000,000 = Σ(单音符基础分 × 判定系数)
    ///   基础分 = 1,000,000 / 谱面音符数
    ///   判定系数：PERFECT 1.0 / GREAT 0.7 / GOOD 0.4 / MISS 0
    ///   准确率 = Σ判定系数 / 已判定音符数 × 100%
    ///   评级看准确率：SSS ≥ 98% / SS ≥ 95% / S ≥ 90% / A ≥ 80% / B ≥ 70% / C ≥ 60% / 其余 D
    ///
    /// 为什么分数和准确率要分开：分数会被谱面音符数影响（音符越多，每个越不值钱），
    /// 准确率则只跟手感有关。两个一起看才能判断"是打得好"还是"歌简单"。
    /// </summary>
    public sealed class ScoreSystem
    {
        public const int MaxScore = 1_000_000;

        private readonly int[] _counts = new int[5];
        private int _totalNotes = 1;
        private int _judged;
        private double _weightSum;
        private int _combo;
        private int _maxCombo;

        /// <summary>本谱面音符总数。</summary>
        public int TotalNotes => _totalNotes;

        /// <summary>已经判定过的音符数。</summary>
        public int JudgedNotes => _judged;

        /// <summary>剩余未判定的音符数。</summary>
        public int Remaining => Mathf.Max(0, _totalNotes - _judged);

        /// <summary>当前连击。</summary>
        public int Combo => _combo;

        /// <summary>本局最高连击。</summary>
        public int MaxCombo => _maxCombo;

        /// <summary>所有音符都判完了。</summary>
        public bool IsFinished => _judged >= _totalNotes;

        /// <summary>当前分数（0 ~ 1,000,000）。</summary>
        public long Score => (long)Math.Round(MaxScore * (_weightSum / Mathf.Max(1, _totalNotes)));

        /// <summary>准确率（0 ~ 100）。</summary>
        public double Accuracy => _judged == 0 ? 0.0 : _weightSum / _judged * 100.0;

        /// <summary>评级字母。</summary>
        public string Rank
        {
            get
            {
                if (_judged == 0) return "-";
                double acc = Accuracy;
                if (acc >= 98.0) return "SSS";
                if (acc >= 95.0) return "SS";
                if (acc >= 90.0) return "S";
                if (acc >= 80.0) return "A";
                if (acc >= 70.0) return "B";
                if (acc >= 60.0) return "C";
                return "D";
            }
        }

        /// <summary>能否算"全连"。</summary>
        public bool IsFullCombo => _maxCombo >= _totalNotes && _totalNotes > 0 && _maxCombo > 0;

        /// <summary>开局：按谱面音符数重置。</summary>
        public void Begin(int totalNotes)
        {
            _totalNotes = Mathf.Max(1, totalNotes);
            _judged = 0;
            _weightSum = 0.0;
            _combo = 0;
            _maxCombo = 0;
            Array.Clear(_counts, 0, _counts.Length);
        }

        /// <summary>记录一次判定。</summary>
        public void Register(Judgement judgement)
        {
            if (judgement == Judgement.None) return;

            _judged++;
            int idx = (int)judgement;
            if (idx >= 0 && idx < _counts.Length) _counts[idx]++;

            double weight = JudgementInfo.Weight(judgement);
            _weightSum += weight;

            if (JudgementInfo.IsHit(judgement))
            {
                _combo++;
                if (_combo > _maxCombo) _maxCombo = _combo;
            }
            else
            {
                _combo = 0;
            }
        }

        /// <summary>某档判定出现了多少次。</summary>
        public int Count(Judgement judgement)
        {
            int idx = (int)judgement;
            return idx >= 0 && idx < _counts.Length ? _counts[idx] : 0;
        }

        /// <summary>把成绩整理成一句话，用于 Console 打印 / 结算面板。</summary>
        public string Summary()
        {
            return $"分数 {Score:N0} / {MaxScore:N0}   准确率 {Accuracy:F2}%   评级 {Rank}   " +
                   $"最大连击 {_maxCombo}   " +
                   $"PERFECT {Count(Judgement.Perfect)} / GREAT {Count(Judgement.Great)} / " +
                   $"GOOD {Count(Judgement.Good)} / MISS {Count(Judgement.Miss)}";
        }
    }
}
