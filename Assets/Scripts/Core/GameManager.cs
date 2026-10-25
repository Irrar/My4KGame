using System;
using UnityEngine;

namespace My4KGame
{
    /// <summary>游戏所处的阶段。</summary>
    public enum GameState
    {
        Menu,       // 选曲
        Countdown,  // 3-2-1 倒计时（歌曲时间是负数）
        Playing,    // 游玩中
        Paused,     // 暂停
        Result,     // 结算
    }

    /// <summary>
    /// 游戏管理器（手册 4.2 的完整版）：音频时钟、状态机、判定窗口、计分、输入分发。
    ///
    /// —— 手册 4.2 的核心思路，一个字都没改 ——
    /// 用 AudioSettings.dspTime 当主时钟，不用 Time.time。
    /// dspTime 是声卡自己的采样时钟：单调、稳定、和渲染帧率无关。
    /// 再用 PlayScheduled 提前安排起播，于是"从调用到真正出声"的延迟交给音频系统处理，
    /// 我们拿到的就是一个精确已知的起播时刻 dspSongStart。
    ///
    /// —— 本工程在此基础上补完的四件事 ——
    /// 1. 起播前的等待期：歌曲时间是负数，倒计时、前奏、谱面前 2 秒统一用一条时间轴。
    /// 2. 判定用原始 dsp 时间（准），画面用平滑后的 SongTime（稳），两者解耦。
    /// 3. 音画校准：抵消音频输出缓冲区那 10~50ms 的延迟。
    /// 4. 暂停：暂停时 dspTime 还在走，所以恢复时要把起播时刻整体后移相同的时长，
    ///    否则歌曲时间会跳一下——这是暂停功能最常见的坑。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    [AddComponentMenu("My4KGame/Game Manager")]
    public sealed class GameManager : MonoBehaviour
    {
        // ================================================================ 单例
        public static GameManager Instance { get; private set; }

        // ================================================================ Inspector
        [Header("音频（留空会自动补一个 AudioSource）")]
        public AudioSource musicSource;

        [Header("谱面")]
        [Tooltip("启动后默认停在选曲界面；这个字段是脚本直接开谱时的默认谱面名")]
        public string defaultChart = "NeonPulse_Normal";

        [Header("节奏")]
        [Tooltip("起播前的倒计时长度（秒）。这段歌曲时间是负数。")]
        public double leadInSeconds = PlayfieldLayout.DefaultLeadIn;

        [Header("判定窗口（秒，取绝对值比较）")]
        [Tooltip("PERFECT ±40ms")]
        public float perfectWindow = 0.040f;
        [Tooltip("GREAT ±80ms")]
        public float greatWindow = 0.080f;
        [Tooltip("GOOD ±120ms，超出这个范围按键 = 空打")]
        public float goodWindow = 0.120f;
        [Tooltip("音符过了判定线这么久还没被打中 = MISS")]
        public float missWindow = 0.140f;

        [Header("音画校准（秒）")]
        [Tooltip("正值 = 判定更愿意接受偏晚的按键。建议范围 -0.15 ~ +0.15")]
        public double calibrationOffset = 0.0;

        [Header("演示 / 调试")]
        [Tooltip("勾上后自动完美命中所有音符，用来演示或录屏")]
        public bool autoPlay = false;

        [Header("流速（纯视觉参数，不参与任何判定）")]
        [Tooltip("允许在游戏里用 ↑ / ↓ 实时调速")]
        public bool allowLiveSpeedChange = true;

        [Header("键位（必须是 4 个，对应轨道 0~3）")]
        public KeyCode[] keys = { KeyCode.D, KeyCode.F, KeyCode.J, KeyCode.K };

        [Header("系统引用（WorldBuilder 会自动连好，一般不用手改）")]
        public NoteSpawner spawner;

        // ================================================================ 事件
        /// <summary>游戏阶段变化。</summary>
        public event Action<GameState> StateChanged;
        /// <summary>一次判定：轨道 / 等级 / 时间差(毫秒) / 当前连击。</summary>
        public event Action<int, Judgement, double, int> Judged;
        /// <summary>任意一次按键（含空打），给轨道高亮和打击特效用。</summary>
        public event Action<int, Judgement> LanePressed;
        /// <summary>开谱成功。</summary>
        public event Action<ChartData> ChartStarted;
        /// <summary>一局结束（结算面板用）。</summary>
        public event Action<ScoreSystem> SongFinished;
        /// <summary>校准值变化（滑条用）。</summary>
        public event Action<double> CalibrationChanged;
        /// <summary>流速变化（视觉参数；UI 用）。</summary>
        public event Action<float> ScrollSpeedChanged;
        /// <summary>某轨道开始长按（Hold 头被打中的那一刻）。表现层据此点亮"正在按住"的持续反馈。</summary>
        public event Action<int> HoldStarted;
        /// <summary>某轨道结束长按（松手 / 到尾 / 中断）。</summary>
        public event Action<int> HoldEnded;

        // ================================================================ 长按反馈通知
        // 只做转发：判定状态仍然全部由 NoteSpawner 负责，这里不参与任何判定计算。
        /// <summary>由 NoteSpawner 调用：lane 轨道的 Hold 开始被按住。</summary>
        public void NotifyHoldStarted(int lane) { HoldStarted?.Invoke(lane); }

        /// <summary>由 NoteSpawner 调用：lane 轨道的 Hold 结束（松手/到尾/中断）。</summary>
        public void NotifyHoldEnded(int lane) { HoldEnded?.Invoke(lane); }

        // ================================================================ 运行时状态
        private const string CalibrationKey = "My4KGame.Calibration";

        private double _dspSongStart = -1.0;
        private double _smoothSongTime;
        private double _pausedAtDsp;
        private double _songEndTime = double.MaxValue;
        private bool _songRunning;

        public GameState State { get; private set; } = GameState.Menu;
        public ChartData Chart { get; private set; }
        public string ChartName { get; private set; } = "";
        public ScoreSystem Score { get; private set; } = new ScoreSystem();
        public bool IsNewBest { get; private set; }
        public bool IsSongRunning => _songRunning;
        public bool AutoPlay => autoPlay;

        /// <summary>
        /// 当前歌曲时间（秒）——手册 4.2 的 <c>CurrentAudioTime</c>。
        /// 负数表示还在倒计时。这是"判定用"的时间，最准。
        /// </summary>
        public double CurrentAudioTime => _songRunning ? AudioSettings.dspTime - _dspSongStart : -leadInSeconds;

        /// <summary>平滑后的歌曲时间（秒），给画面用。音符移动靠它，所以不会一帧一抖。</summary>
        public double SongTime => _smoothSongTime;

        /// <summary>扣掉音画校准后的判定时间。</summary>
        public double JudgeTime => CurrentAudioTime - calibrationOffset;

        // ---------------------------------------------------------------- 判定窗口（毫秒）
        // 判定一律用毫秒比较：整数级的时间量，浮点误差小，也好和文档验收表对齐。
        // 这四个值来自 Inspector 里的秒数，*不包含任何流速因子* —— 这是硬约束。
        public double PerfectWindowMs => perfectWindow * 1000.0;
        public double GreatWindowMs => greatWindow * 1000.0;
        public double GoodWindowMs => goodWindow * 1000.0;
        public double MissWindowMs => missWindow * 1000.0;

        // ---------------------------------------------------------------- 视觉流速（与判定无关）
        /// <summary>
        /// 视觉流速轨道。只被"位置映射"和"生成提前量"使用。
        /// 判定链路（Judge / JudgeMs / ResolvePress）完全不读它。
        /// </summary>
        public ScrollSpeedTrack Scroll { get; private set; }

        /// <summary>当前目标流速（×0.25 ~ ×4.00，由 ScrollSpeedState 的数值换算而来）。</summary>
        public float ScrollSpeed => Scroll != null ? Scroll.TargetCoefficient : ScrollSpeedState.Coefficient;

        /// <summary>当前流速数值（1.00 ~ 10.00，全局唯一数据源）。</summary>
        public float ScrollValue => ScrollSpeedState.Value;

        /// <summary>音频总长度（秒）。没有音频时返回 0。</summary>
        public double SongLength =>
            musicSource != null && musicSource.clip != null ? musicSource.clip.length : 0.0;

        /// <summary>本局进度 0~1。</summary>
        public double SongProgress
        {
            get
            {
                if (_songEndTime <= 0.0 || double.IsInfinity(_songEndTime)) return 0.0;
                return Math.Max(0.0, Math.Min(1.0, SongTime / _songEndTime));
            }
        }

        /// <summary>倒计时还剩几秒（负区间时为正数，倒计时结束后为 0）。</summary>
        public double CountdownRemaining => Math.Max(0.0, -CurrentAudioTime);

        // ================================================================ 生命周期
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[My4KGame] 场景里有多个 GameManager，多余的已被禁用。", this);
                enabled = false;
                return;
            }
            Instance = this;
            EnsureAudio();

            Scroll = new ScrollSpeedTrack(PlayfieldLayout.TravelTime);

            // 流速的全局数据源：读回上次设置，之后主界面 / 游戏内都只改它。
            ScrollSpeedState.Load();
            ScrollSpeedState.Changed += OnScrollSpeedStateChanged;
            ApplyScrollSpeed();
        }

        private void OnDestroy()
        {
            ScrollSpeedState.Changed -= OnScrollSpeedStateChanged;
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            LoadCalibration();
            EnterMenu();
        }

        private void Update()
        {
            if (_songRunning && State != GameState.Paused) AdvanceSmoothClock();

            // 视觉流速的平滑：只影响 AppliedCoefficient（一个浮点数），不碰任何判定数据
            if (Scroll != null && State != GameState.Paused) Scroll.Tick(Time.unscaledDeltaTime);

            switch (State)
            {
                case GameState.Countdown:
                    if (CurrentAudioTime >= 0.0) SetState(GameState.Playing);
                    break;

                case GameState.Playing:
                    HandleLaneInput();
                    HandleLaneRelease();
                    if (SongTime >= _songEndTime) FinishSong();
                    break;
            }

            HandleSpeedKeys();
            HandlePausedInput();
            HandleGlobalKeys();
        }

        // ================================================================ 时钟
        private void EnsureAudio()
        {
            if (musicSource == null) musicSource = GetComponent<AudioSource>();
            if (musicSource == null) musicSource = gameObject.AddComponent<AudioSource>();

            musicSource.playOnAwake = false;
            musicSource.loop = false;
            musicSource.spatialBlend = 0f;   // 2D 音频，不加空间衰减
            musicSource.volume = 1f;
            musicSource.priority = 0;        // 最高优先级，避免被其它音效抢占
        }

        /// <summary>
        /// 画面时间向音频时钟收敛：每帧吃掉 30% 的偏差。
        /// 偏差超过 30ms 就直接吸附（说明已经音画不同步了，宁可跳一下）。
        /// </summary>
        private void AdvanceSmoothClock()
        {
            double target = CurrentAudioTime;

            if (State == GameState.Countdown)
            {
                _smoothSongTime = target;   // 倒计时阶段还没出声，直接跟随即可
                return;
            }

            _smoothSongTime += Time.unscaledDeltaTime;
            double error = target - _smoothSongTime;
            if (Math.Abs(error) > 0.030) _smoothSongTime = target;
            else _smoothSongTime += error * 0.30;
        }

        // ================================================================ 开谱 / 结算
        /// <summary>开始一局。chartName 是 Resources/Charts 下的文件名（不带后缀）。</summary>
        public bool StartSong(string chartName)
        {
            if (string.IsNullOrEmpty(chartName)) chartName = defaultChart;

            if (!ChartLibrary.TryLoad(chartName, out ChartData chart, out string error))
            {
                Debug.LogError("[My4KGame] 谱面加载失败：" + error);
                return false;
            }
            if (!string.IsNullOrEmpty(error))
                Debug.LogWarning("[My4KGame] 谱面 " + chartName + " 有小问题：" + error);

            StopSongInternal();

            Chart = chart;
            ChartName = chartName;
            Score.Begin(chart.NoteCount);

            AudioClip clip = LoadChartAudio(chart);
            float audioStart = AudioTimeline.StartOffset(chart.audio);
            if (musicSource != null)
            {
                musicSource.Stop();
                musicSource.clip = clip;
                // 跳过开头静音：直接定位到第一个有声的位置再起播
                musicSource.time = audioStart;
            }
            if (clip == null)
            {
                Debug.LogWarning($"[My4KGame] 找不到音频 Resources/Audio/{chart.audio}，" +
                                 "本局会静音进行（用来调谱面几何位置很方便）。");
            }

            _dspSongStart = AudioSettings.dspTime + leadInSeconds;
            _smoothSongTime = -leadInSeconds;
            _songRunning = true;
            _songEndTime = ComputeSongEnd(chart, clip);

            // 提前安排起播：调用到真正出声之间的延迟由音频系统兜着
            if (clip != null && musicSource != null) musicSource.PlayScheduled(_dspSongStart);

            if (spawner != null) spawner.LoadChart(chart);

            SetState(GameState.Countdown);
            ChartStarted?.Invoke(chart);
            Debug.Log($"[My4KGame] 开谱：{chart.songName} / {chart.difficulty} Lv.{chart.level} " +
                      $"/ {chart.NoteCount} 音符 / BPM {chart.bpm:0.##}");
            return true;
        }

        private AudioClip LoadChartAudio(ChartData chart)
        {
            AudioClip clip = Resources.Load<AudioClip>("Audio/" + chart.audio);
            if (clip == null) clip = Resources.Load<AudioClip>("Audio/NeonPulse");
            return clip;
        }

        private double ComputeSongEnd(ChartData chart, AudioClip clip)
        {
            double noteEnd = chart.LastNoteTime + 2.0;
            double musicEnd = clip != null && clip.length > 1.0
                ? AudioTimeline.EffectiveLength(chart.audio, clip.length)
                : double.MaxValue;
            double end = Math.Min(noteEnd, musicEnd);
            return end <= 0.5 ? 1.0 : end;
        }

        private void FinishSong()
        {
            if (State != GameState.Playing) return;

            _songRunning = false;
            if (musicSource != null) musicSource.Stop();

            IsNewBest = SaveData.Submit(ChartName, Score.Score, Score.Rank, Score.Accuracy);
            SetState(GameState.Result);
            Debug.Log("[My4KGame] 本局成绩 ——" + Score.Summary() + (IsNewBest ? "  ★新纪录！" : ""));
            SongFinished?.Invoke(Score);
        }

        /// <summary>回到选曲界面。</summary>
        public void EnterMenu()
        {
            StopSongInternal();
            Chart = null;
            ChartName = "";
            Score.Begin(1);
            SetState(GameState.Menu);
        }

        /// <summary>重开当前谱面。</summary>
        public void Restart()
        {
            if (!string.IsNullOrEmpty(ChartName)) StartSong(ChartName);
            else EnterMenu();
        }

        private void StopSongInternal()
        {
            _songRunning = false;
            _dspSongStart = -1.0;
            _smoothSongTime = -leadInSeconds;
            _songEndTime = double.MaxValue;
            if (musicSource != null) musicSource.Stop();
            if (spawner != null) spawner.Clear();
        }

        // ================================================================ 暂停
        public void Pause()
        {
            if (State != GameState.Playing) return;
            _pausedAtDsp = AudioSettings.dspTime;
            if (musicSource != null) musicSource.Pause();
            SetState(GameState.Paused);
        }

        public void Resume()
        {
            if (State != GameState.Paused) return;
            // 暂停期间 dspTime 照常在走，所以把起播时刻整体往后推同样长的时间
            double pausedDuration = AudioSettings.dspTime - _pausedAtDsp;
            _dspSongStart += pausedDuration;
            if (musicSource != null) musicSource.UnPause();
            SetState(GameState.Playing);
        }

        public void TogglePause()
        {
            if (State == GameState.Playing) Pause();
            else if (State == GameState.Paused) Resume();
        }

        // ================================================================ 判定
        /// <summary>
        /// 【判定唯一入口】按**毫秒**误差给出判定等级。
        /// deltaMs = 按键时间 − 音符判定时间戳，负数表示按早了。
        ///
        /// 注意这里只看 deltaMs 和四个固定窗口：不读流速、不读屏幕坐标、
        /// 不读音符当前的 transform.position —— 这正是"判定归时间"的落点。
        /// </summary>
        public Judgement JudgeMs(double deltaMs)
        {
            double abs = Math.Abs(deltaMs);
            if (abs <= PerfectWindowMs) return Judgement.Perfect;
            if (abs <= GreatWindowMs) return Judgement.Great;
            if (abs <= GoodWindowMs) return Judgement.Good;
            return Judgement.None;
        }

        /// <summary>
        /// 手册 4.2 的 <c>Judge</c>（参数是秒）。保留它只是为了兼容手册的写法，
        /// 内部换算成毫秒后交给 <see cref="JudgeMs"/>，窗口和流速仍然无关。
        /// </summary>
        public Judgement Judge(float timeDifference) => JudgeMs(timeDifference * 1000.0);

        /// <summary>
        /// 记录一次判定：更新分数、连击，并广播事件。
        /// 由 NoteSpawner 在"确实打到了某个音符"时调用；空打不会走到这里。
        /// </summary>
        public void RegisterJudgement(int lane, Judgement judgement, double deltaMs)
        {
            if (judgement == Judgement.None) return;
            Score.Register(judgement);
            Judged?.Invoke(lane, judgement, deltaMs, Score.Combo);
        }

        // ================================================================ 输入
        private void HandleLaneInput()
        {
            if (keys == null || keys.Length < PlayfieldLayout.LaneCount) return;
            for (int lane = 0; lane < PlayfieldLayout.LaneCount; lane++)
            {
                if (Input.GetKeyDown(keys[lane])) PressLane(lane);
            }
        }

        private void HandleLaneRelease()
        {
            if (keys == null || keys.Length < PlayfieldLayout.LaneCount) return;
            for (int lane = 0; lane < PlayfieldLayout.LaneCount; lane++)
            {
                if (Input.GetKeyUp(keys[lane])) ReleaseLane(lane);
            }
        }

        /// <summary>手动触发一次轨道按键（也方便外部脚本调用 / 自动演示）。</summary>
        public Judgement PressLane(int lane)
        {
            if (State != GameState.Playing || spawner == null) return Judgement.None;
            Judgement result = spawner.ResolvePress(lane, JudgeTime);
            LanePressed?.Invoke(lane, result);
            return result;
        }

        /// <summary>手动触发一次轨道松开（长按 Hold 用）。</summary>
        public Judgement ReleaseLane(int lane)
        {
            if (State != GameState.Playing || spawner == null) return Judgement.None;
            return spawner.ResolveRelease(lane, JudgeTime);
        }

        /// <summary>
        /// 暂停界面：按任意 D/F/J/K 继续游戏。
        /// 这里只负责恢复，同一帧不再把它当打击按键处理，避免"继续"顺手吃掉一个音符。
        /// </summary>
        private void HandlePausedInput()
        {
            if (State != GameState.Paused) return;
            if (keys == null || keys.Length < PlayfieldLayout.LaneCount) return;

            for (int lane = 0; lane < PlayfieldLayout.LaneCount; lane++)
            {
                if (Input.GetKeyDown(keys[lane]))
                {
                    Resume();
                    return;
                }
            }
        }

        private void HandleGlobalKeys()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                switch (State)
                {
                    case GameState.Playing:
                        Pause();
                        break;
                    case GameState.Paused:
                        EnterMenu();
                        break;
                    case GameState.Result:
                        EnterMenu();
                        break;
                }
            }

            if (State == GameState.Result && Input.GetKeyDown(KeyCode.R))
            {
                Restart();
            }
        }

        // ================================================================ 校准
        private void LoadCalibration()
        {
            string raw = PlayerPrefs.GetString(CalibrationKey, "");
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double saved))
            {
                calibrationOffset = Math.Max(-0.3, Math.Min(0.3, saved));
            }
        }

        public void SetCalibration(double offset)
        {
            calibrationOffset = Math.Max(-0.3, Math.Min(0.3, offset));
            PlayerPrefs.SetString(CalibrationKey, calibrationOffset.ToString("F4",
                System.Globalization.CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
            CalibrationChanged?.Invoke(calibrationOffset);
        }

        // ================================================================ 流速（纯视觉）
        /// <summary>
        /// 设置流速数值（1.00~10.00）。这是**唯一**的调速入口：
        /// 改动只会写进全局的 <see cref="ScrollSpeedState"/>，再同步到视觉轨道；
        /// 不触碰 chart.notes、不改 JudgeTimeMs、不改任何判定窗口。
        /// </summary>
        public void SetScrollValue(float value, bool save = true)
        {
            ScrollSpeedState.Set(value, save);
        }

        /// <summary>在当前数值上加减（按钮 / 方向键，一次 0.25）。</summary>
        public void AdjustScrollValue(float delta)
        {
            ScrollSpeedState.Adjust(delta);
        }

        /// <summary>兼容旧接口：按"系数"设置（内部换算成数值）。</summary>
        public void SetScrollSpeed(float coefficient, bool immediate = true, bool save = true)
        {
            SetScrollValue(ScrollSpeedTrack.CoefficientToValue(coefficient), save);
        }

        /// <summary>全局数值变化 → 同步视觉轨道 + 通知 UI。</summary>
        private void OnScrollSpeedStateChanged(float value)
        {
            ApplyScrollSpeed();
            ScrollSpeedChanged?.Invoke(value);
        }

        private void ApplyScrollSpeed()
        {
            if (Scroll == null) return;
            Scroll.SetCoefficient(ScrollSpeedState.Coefficient, immediate: true);
        }

        /// <summary>
        /// ↑ / ↓ 逐格微调（0.25），PageUp / PageDown 一次一拍（1.00）。
        /// 只在游玩相关状态下响应，避免在菜单里误触。
        /// </summary>
        private void HandleSpeedKeys()
        {
            if (!allowLiveSpeedChange) return;
            if (State != GameState.Playing && State != GameState.Paused && State != GameState.Countdown) return;

            if (Input.GetKeyDown(KeyCode.UpArrow)) AdjustScrollValue(ScrollSpeedTrack.Step);
            else if (Input.GetKeyDown(KeyCode.DownArrow)) AdjustScrollValue(-ScrollSpeedTrack.Step);
            else if (Input.GetKeyDown(KeyCode.PageUp)) AdjustScrollValue(1f);
            else if (Input.GetKeyDown(KeyCode.PageDown)) AdjustScrollValue(-1f);
        }

        // ================================================================ 内部
        private void SetState(GameState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
