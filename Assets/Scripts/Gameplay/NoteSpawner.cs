using System;
using System.Collections.Generic;
using UnityEngine;

namespace My4KGame
{
    /// <summary>
    /// 音符生成器 + 判定扫描器（手册 4.4 的完整版）。
    ///
    /// 手册的版本是"每个音符自己读按键"，能跑通，但有两个实际问题：
    ///   · 同轨道连打时一次按键会被多个音符同时吃掉（多算分、多算连击）；
    ///   · 每帧每个音符都调一次 Input.GetKeyDown，音符一多就不划算。
    /// 所以这里把按键集中到 <see cref="ResolvePress"/>：
    /// 一次按下，只挑"同轨道里离这次按键最近的、还在判定窗口内的那一个音符"。
    ///
    /// 生成时机：歌曲时间 >= 音符时间 - 下落时长。手册里就是这么算的，没变。
    /// 但生成用对象池，一首 200 音符的歌全程只有十几个 GameObject 在跑，
    /// 不会因为频繁 Instantiate/Destroy 产生 GC 卡顿 —— 而卡顿在音游里等于判定漂移。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("My4KGame/Note Spawner")]
    public sealed class NoteSpawner : MonoBehaviour
    {
        [Header("预制体（留空则运行时用代码造）")]
        public NoteController notePrefab;

        [Header("音符挂在哪个节点下（留空 = 自己）")]
        public Transform noteRoot;

        [Header("对象池")]
        [Tooltip("预创建多少个音符备用")]
        public int prewarmCount = 24;

        private readonly List<NoteController> _active = new List<NoteController>();
        private readonly List<NoteController> _pool = new List<NoteController>();

        private ChartData _chart;
        private int _nextIndex;
        private int _judged;
        private bool _running;

        /// <summary>当前谱面。</summary>
        public ChartData Chart => _chart;

        /// <summary>本局还没判定的音符总数（含还没生成的）。</summary>
        public int Remaining => _chart == null ? 0 : Mathf.Max(0, _chart.NoteCount - _judged);

        /// <summary>场上正在下落的音符数。</summary>
        public int ActiveCount => _active.Count;

        /// <summary>全部音符都判完了。</summary>
        public bool AllResolved => _chart != null && _judged >= _chart.NoteCount;

        /// <summary>场上正在下落的音符（只读，给测试/调试用）。</summary>
        public IReadOnlyList<NoteController> ActiveNotes => _active;

        // ================================================================ 装载 / 清空
        /// <summary>装载一张谱面，重置所有状态。</summary>
        public void LoadChart(ChartData chart)
        {
            Clear();
            _chart = chart;
            _nextIndex = 0;
            _judged = 0;
            _running = chart != null && chart.NoteCount > 0;
            if (_running) Prewarm();
        }

        /// <summary>清场：回收所有音符。</summary>
        public void Clear()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                NoteController note = _active[i];
                if (note != null)
                {
                    note.RecycleNow();
                    note.gameObject.SetActive(false);
                    if (!_pool.Contains(note)) _pool.Add(note);
                }
            }
            _active.Clear();
            _chart = null;
            _nextIndex = 0;
            _judged = 0;
            _running = false;
        }

        private void Prewarm()
        {
            for (int i = 0; i < prewarmCount; i++)
            {
                NoteController note = CreateNote();
                if (note == null) break;
                note.gameObject.SetActive(false);
                _pool.Add(note);
            }
        }

        // ================================================================ 每帧
        private void Update()
        {
            if (!_running || _chart == null) return;

            GameManager game = GameManager.Instance;
            if (game == null) return;
            if (game.State != GameState.Playing && game.State != GameState.Countdown) return;

            double songTime = game.SongTime;
            SpawnDueNotes(songTime);

            if (game.AutoPlay) AutoPlay(game, songTime);
        }

        private void SpawnDueNotes(double songTime)
        {
            // 出场提前量来自视觉轨道：流速越快，提前量越小（音符只在屏幕上方一小段路）。
            // 它只决定"什么时候创建这个物体"，不参与任何判定计算。
            GameManager game = GameManager.Instance;
            double travelSeconds = game != null && game.Scroll != null
                ? game.Scroll.TravelSeconds
                : PlayfieldLayout.TravelTime;

            var notes = _chart.notes;
            while (_nextIndex < notes.Count)
            {
                NoteData data = notes[_nextIndex];

                // 还差多少秒到判定线（用判定时间戳算，不用屏幕位置）
                double secondsAhead = data.JudgeTimeSeconds - songTime;
                if (secondsAhead > travelSeconds) break;   // 还没到该出场的时候

                NoteController note = Rent();
                if (note == null) break;
                note.Initialize(this, data);
                _active.Add(note);
                _nextIndex++;
            }
        }

        /// <summary>自动演示：音符一到判定线就是 PERFECT。</summary>
        private void AutoPlay(GameManager game, double songTime)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                NoteController note = _active[i];
                if (note == null || !note.IsAlive) continue;
                if (songTime * 1000.0 < note.Data.JudgeTimeMs) continue;

                Judgement judgement = game.Judge(0f);
                Retire(note, judgement, 0.0);
            }
        }

        // ================================================================ 判定
        /// <summary>
        /// 【判定唯一入口】一次轨道按键。
        /// 返回判定结果（Judgement.None = 空打：附近没有可打的音符）。
        ///
        /// pressTimeSeconds 用 GameManager.JudgeTime（音频时钟，已扣掉音画校准）。
        /// 内部立刻换成毫秒，和 note.JudgeTimeMs 做差 —— 只比较时间，不读坐标、不乘流速。
        /// </summary>
        public Judgement ResolvePress(int lane, double pressTimeSeconds)
        {
            if (_chart == null) return Judgement.None;

            GameManager game = GameManager.Instance;
            if (game == null) return Judgement.None;

            double pressTimeMs = pressTimeSeconds * 1000.0;

            NoteController best = null;
            double bestAbs = double.MaxValue;
            double bestDeltaMs = 0.0;

            for (int i = 0; i < _active.Count; i++)
            {
                NoteController note = _active[i];
                if (note == null || !note.IsAlive || note.Data == null) continue;
                if (note.Data.lane != lane) continue;

                double deltaMs = pressTimeMs - note.Data.JudgeTimeMs;
                double absMs = Math.Abs(deltaMs);
                if (absMs > game.GoodWindowMs) continue;      // 不在判定窗口内（窗口固定，与流速无关）
                if (absMs < bestAbs)
                {
                    bestAbs = absMs;
                    bestDeltaMs = deltaMs;
                    best = note;
                }
            }

            if (best == null) return Judgement.None;      // 空打，不扣分

            Judgement judgement = game.JudgeMs(bestDeltaMs);
            Retire(best, judgement, bestDeltaMs);
            return judgement;
        }

        /// <summary>音符自己发现"已经太晚了"时调这里。</summary>
        public void MissNote(NoteController note)
        {
            if (note == null || !note.IsAlive) return;
            GameManager game = GameManager.Instance;
            double lateMs = game != null ? game.MissWindowMs : 140.0;
            Retire(note, Judgement.Miss, lateMs);
        }

        /// <summary>记账：把音符移出判定队列、通知 GameManager 算分、播放动画。deltaMs = 毫秒误差。</summary>
        private void Retire(NoteController note, Judgement judgement, double deltaMs)
        {
            if (note == null || !note.IsAlive) return;

            _active.Remove(note);
            _judged++;

            GameManager game = GameManager.Instance;
            if (game != null && note.Data != null)
                game.RegisterJudgement(note.Data.lane, judgement, deltaMs);

            if (judgement == Judgement.Miss) note.PlayMiss();
            else note.PlayHit(judgement);
        }

        // ================================================================ 对象池
        private NoteController Rent()
        {
            while (_pool.Count > 0)
            {
                int last = _pool.Count - 1;
                NoteController pooled = _pool[last];
                _pool.RemoveAt(last);
                if (pooled != null)
                {
                    pooled.gameObject.SetActive(true);
                    return pooled;
                }
            }

            NoteController created = CreateNote();
            if (created != null) created.gameObject.SetActive(true);
            return created;
        }

        /// <summary>动画播完，由 NoteController 回调，回收进池子。</summary>
        public void Recycle(NoteController note)
        {
            if (note == null) return;
            note.RecycleNow();
            note.gameObject.SetActive(false);
            if (!_pool.Contains(note)) _pool.Add(note);
        }

        private NoteController CreateNote()
        {
            NoteController note;
            if (notePrefab != null)
            {
                note = Instantiate(notePrefab);
                note.name = "Note";
            }
            else
            {
                // 没有预制体也能跑：代码造一个
                var go = new GameObject("Note");
                note = go.AddComponent<NoteController>();
            }

            Transform parent = noteRoot != null ? noteRoot : transform;
            note.transform.SetParent(parent, false);
            note.transform.localScale = Vector3.one;
            return note;
        }
    }
}
