using UnityEngine;

namespace My4KGame
{
    /// <summary>
    /// 单个音符（手册 4.3 的完整版）：负责自己的下落位置，以及"太晚了就判 Miss"。
    ///
    /// 和手册的写法有两处刻意的改动，都是为了修掉原型阶段的真实 bug：
    ///
    /// 1) 判定不在这里做。
    ///    手册 4.3 里每个音符自己在 Update 里读 Input.GetKeyDown，
    ///    如果同一轨道同时有两个音符（连打），一次按键会被两个音符都吃掉，判定两次、加两次分。
    ///    本工程改成 NoteSpawner 统一扫描：一次按键只吃掉"离按键最近的那一个"音符。
    ///
    /// 2) 动画和回收分离。
    ///    命中后音符要先放大淡出再回收（不然会"啪"地消失，视觉上很硬）。
    ///    所以 IsAlive 立刻变 false（不再参与判定），但 GameObject 还活着播动画，
    ///    播完由 NoteSpawner 收进对象池。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("My4KGame/Note Controller")]
    public sealed class NoteController : MonoBehaviour
    {
        [Header("外观（留空会自动生成子物体）")]
        [Tooltip("音符本体，默认 2.0 × 0.5 世界单位")]
        public SpriteRenderer body;
        [Tooltip("音符背后的柔光")]
        public SpriteRenderer glow;

        [Header("命中动画")]
        [Tooltip("命中后放大淡出的时长（秒）")]
        public float hitPopSeconds = 0.14f;
        [Tooltip("失误后淡出的时长（秒）")]
        public float missFadeSeconds = 0.22f;

        /// <summary>这个音符的谱面数据。</summary>
        public NoteData Data { get; private set; }

        /// <summary>还在场上、还没被判定（false 表示已经打过或已经漏掉）。</summary>
        public bool IsAlive { get; private set; }

        public int Lane => Data == null ? 0 : Data.lane;

        /// <summary>
        /// 音符背后柔光的基准缩放。
        /// Glow 贴图本身是 256px（PPU 100 = 2.56 世界单位），所以这里的 1.0/0.62
        /// 约等于 2.56 × 1.59 世界单位，比音符本体（2.0 × 0.5）大一圈就够了。
        /// 调大一点就会把整条轨道糊成一片 —— 这里踩过坑。
        /// </summary>
        private static readonly Vector2 GlowBaseScale = new Vector2(1.00f, 0.62f);

        /// <summary>柔光的基准透明度。</summary>
        private const float GlowBaseAlpha = 0.20f;

        private NoteSpawner _owner;
        private GameManager _game;
        private ScrollSpeedTrack _scroll;
        private Vector3 _bodyBaseScale = Vector3.one;

        private float _animTimer;
        private float _animDuration;
        private Judgement _animJudgement;

        // ================================================================ 初始化
        /// <summary>由 NoteSpawner 在生成/复用时调用。</summary>
        public void Initialize(NoteSpawner owner, NoteData data)
        {
            _owner = owner;
            _game = GameManager.Instance;
            _scroll = _game != null ? _game.Scroll : null;
            Data = data;

            IsAlive = true;
            _animTimer = 0f;
            _animJudgement = Judgement.None;
            transform.localRotation = Quaternion.identity;

            EnsureVisuals();
            ApplyLaneColor();
            ResetVisualState();

            // 立刻摆到"还差 travel 秒"的位置，避免第一帧从 (0,0) 闪过来
            float songTime = _game != null ? (float)_game.SongTime : (float)data.JudgeTimeSeconds - PlayfieldLayout.TravelTime;
            UpdatePosition(songTime);
        }

        /// <summary>缺子物体就直接用代码补一个，保证预制体即使被改坏也能跑。</summary>
        private void EnsureVisuals()
        {
            if (body == null)
            {
                Transform existing = transform.Find("Body");
                if (existing != null) body = existing.GetComponent<SpriteRenderer>();
                if (body == null)
                {
                    var go = new GameObject("Body");
                    go.transform.SetParent(transform, false);
                    body = go.AddComponent<SpriteRenderer>();
                }
                if (body.sprite == null) body.sprite = SpriteFactory.Note;
            }
            if (body.sprite == null) body.sprite = SpriteFactory.Note;

            if (glow == null)
            {
                Transform existing = transform.Find("Glow");
                if (existing != null) glow = existing.GetComponent<SpriteRenderer>();
                if (glow == null)
                {
                    var go = new GameObject("Glow");
                    go.transform.SetParent(transform, false);
                    glow = go.AddComponent<SpriteRenderer>();
                    glow.transform.localScale = new Vector3(GlowBaseScale.x, GlowBaseScale.y, 1f);
                }
                if (glow.sprite == null) glow.sprite = SpriteFactory.Glow;
            }
            if (glow.sprite == null) glow.sprite = SpriteFactory.Glow;

            glow.sortingOrder = -2;
            body.sortingOrder = 0;
            _bodyBaseScale = body.transform.localScale;
            if (_bodyBaseScale.sqrMagnitude < 0.0001f) _bodyBaseScale = Vector3.one;
        }

        /// <summary>用轨道颜色给音符上色，四条轨道一眼能分清。</summary>
        private void ApplyLaneColor()
        {
            if (Data == null) return;
            Color lane = PlayfieldLayout.LaneColor(Data.lane);

            if (body != null)
            {
                body.color = Color.Lerp(lane, Color.white, 0.25f);
            }
            if (glow != null)
            {
                Color g = lane;
                g.a = GlowBaseAlpha;
                glow.color = g;
            }
        }

        private void ResetVisualState()
        {
            if (body != null)
            {
                body.transform.localScale = _bodyBaseScale;
                Color c = body.color;
                c.a = 1f;
                body.color = c;
            }
            if (glow != null)
            {
                glow.transform.localScale = new Vector3(GlowBaseScale.x, GlowBaseScale.y, 1f);
                Color g = glow.color;
                g.a = GlowBaseAlpha;
                glow.color = g;
            }
        }

        // ================================================================ 每帧
        private void Update()
        {
            if (!IsAlive)
            {
                if (_animTimer > 0f) PlayAnimation();
                return;
            }

            GameManager game = _game != null ? _game : GameManager.Instance;
            if (game == null || Data == null) return;

            float songTime;
            if (game.State == GameState.Playing || game.State == GameState.Countdown ||
                game.State == GameState.Paused)
            {
                songTime = (float)game.SongTime;
            }
            else
            {
                return;     // 结算/菜单界面把音符冻在原地
            }

            UpdatePosition(songTime);

            // 手册 4.3 第 2 步：过了判定线还没被打中 = MISS
            // 注意用毫秒比较「当前时钟 − 判定时间戳」，和视觉位置、流速毫无关系
            if (songTime * 1000.0 - Data.JudgeTimeMs > game.MissWindowMs)
            {
                _owner?.MissNote(this);
            }
        }

        /// <summary>
        /// 【视觉轨道】把「还差多少秒到判定线」映射成屏幕 Y。
        /// 流速系数只在这里起作用：它改变的是 TravelSeconds，也就是"同样的时间差画多远"，
        /// 而 secondsAhead 本身由「判定时间戳 − 歌曲时钟」决定，与流速无关。
        /// </summary>
        private void UpdatePosition(float songTime)
        {
            if (Data == null) return;

            float secondsAhead = ScrollSpeedTrack.SecondsAheadOf(Data.JudgeTimeMs, songTime);

            float y;
            ScrollSpeedTrack scroll = _scroll != null ? _scroll : (_game != null ? _game.Scroll : null);
            if (scroll != null)
            {
                y = scroll.SecondsAheadToY(secondsAhead);
            }
            else
            {
                // 极端兜底（没有 GameManager 时）：等价于固定 1x 流速
                float progress = 1f - secondsAhead / PlayfieldLayout.TravelTime;
                y = Mathf.Lerp(PlayfieldLayout.SpawnY, PlayfieldLayout.JudgeLineY, progress);
            }

            transform.position = new Vector3(PlayfieldLayout.LaneToX(Data.lane), y, 0f);
        }

        // ================================================================ 判定结果
        /// <summary>命中：立刻退出判定队列，然后播放放大淡出动画。</summary>
        public void PlayHit(Judgement judgement)
        {
            if (!IsAlive) return;
            IsAlive = false;
            _animJudgement = judgement;
            _animDuration = judgement == Judgement.Miss ? missFadeSeconds : hitPopSeconds;
            _animTimer = _animDuration;
        }

        /// <summary>漏掉：同样走动画，只是表现成"继续往下掉并淡出"。</summary>
        public void PlayMiss()
        {
            PlayHit(Judgement.Miss);
        }

        private void PlayAnimation()
        {
            _animTimer -= Time.deltaTime;
            float k = 1f - Mathf.Clamp01(_animTimer / Mathf.Max(0.0001f, _animDuration)); // 0 → 1

            if (_animJudgement == Judgement.Miss)
            {
                // 失误：继续往下掉一点点，同时淡出
                Vector3 p = transform.position;
                p.y -= Time.deltaTime * 1.2f;
                transform.position = p;

                SetAlpha(1f - k);
            }
            else
            {
                // 命中：放大 + 淡出，柔光同时扩散
                if (body != null)
                {
                    body.transform.localScale = _bodyBaseScale * (1f + 0.45f * k);
                    Color c = body.color;
                    c.a = 1f - k;
                    body.color = c;
                }
                if (glow != null)
                {
                    glow.transform.localScale = new Vector3(GlowBaseScale.x + 1.2f * k, GlowBaseScale.y + 1.4f * k, 1f);
                    Color g = glow.color;
                    g.a = GlowBaseAlpha * (1f - k);
                    glow.color = g;
                }
            }

            if (_animTimer <= 0f) _owner?.Recycle(this);
        }

        private void SetAlpha(float alpha)
        {
            if (body != null)
            {
                Color c = body.color;
                c.a = Mathf.Clamp01(alpha);
                body.color = c;
            }
            if (glow != null)
            {
                Color g = glow.color;
                g.a = Mathf.Clamp01(alpha * GlowBaseAlpha);
                glow.color = g;
            }
        }

        /// <summary>给对象池复用前调用。</summary>
        public void RecycleNow()
        {
            IsAlive = false;
            _animTimer = 0f;
            ResetVisualState();
        }
    }
}
