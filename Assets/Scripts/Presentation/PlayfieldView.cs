using UnityEngine;

namespace My4KGame
{
    /// <summary>
    /// 舞台表现层：判定线、四条轨道、打击特效的调度。
    ///
    /// 它就是"数据 → 视觉"的那一层：
    /// GameManager 只管算，这里只管把"打中了 / 打空了 / 打漏了"翻译成
    /// 轨道闪光、判定线脉冲和一圈扩散的圆环。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayfieldView : MonoBehaviour
    {
        [Header("引用（WorldBuilder 自动连）")]
        public SpriteRenderer judgeLine;
        public LaneView[] lanes;
        public HitEffectPool effects;

        [Header("判定线脉冲")]
        [Tooltip("每次判定时判定线亮一下的强度")]
        public float judgePulseStrength = 0.55f;
        [Tooltip("脉冲衰减速度（1/秒）")]
        public float judgePulseDecay = 6f;

        [Header("长按（Hold）持续反馈")]
        [Tooltip("按住时判定区外圈光晕的基础透明度")]
        public float holdGlowAlpha = 0.42f;
        [Tooltip("按住时每隔多久补一次打击光环（秒）")]
        public float holdPulseInterval = 0.18f;
        [Tooltip("光晕的缩放（x 宽 / y 高）；Glow 贴图 256px/PPU100 ≈ 2.56 世界单位，轨道宽 2.4")]
        public Vector2 holdGlowScale = new Vector2(0.95f, 0.45f);

        private GameManager _game;
        private bool _bound;
        private float _judgePulse;
        private Color _judgePulseColor = Color.white;
        private Color _judgeBaseColor = new Color(1f, 0.85f, 0.35f);

        // 每条轨道一个"按住光晕"：只在 Long Hold 期间显示，松手立刻熄灭
        private readonly SpriteRenderer[] _holdGlow = new SpriteRenderer[PlayfieldLayout.LaneCount];
        private readonly float[] _holdPulseTimer = new float[PlayfieldLayout.LaneCount];

        private void Update()
        {
            if (!_bound) Bind();

            if (_judgePulse > 0f)
            {
                _judgePulse = Mathf.Max(0f, _judgePulse - Time.deltaTime * judgePulseDecay);
                ApplyJudgeLineColor();
            }

            TickHoldFeedback();
        }

        private void OnDisable()
        {
            if (_game == null || !_bound) return;
            _game.LanePressed -= OnLanePressed;
            _game.Judged -= OnJudged;
            _game.StateChanged -= OnStateChanged;
            _game.HoldStarted -= OnHoldStarted;
            _game.HoldEnded -= OnHoldEnded;
            _bound = false;
        }

        private void Bind()
        {
            _game = GameManager.Instance;
            if (_game == null) return;

            _game.LanePressed += OnLanePressed;
            _game.Judged += OnJudged;
            _game.StateChanged += OnStateChanged;
            _game.HoldStarted += OnHoldStarted;
            _game.HoldEnded += OnHoldEnded;
            _bound = true;

            ApplyJudgeLineColor();
            SetLaneVisibility(_game.State != GameState.Menu);
        }

        private void OnStateChanged(GameState state)
        {
            bool showPlayfield = state == GameState.Countdown || state == GameState.Playing ||
                                 state == GameState.Paused || state == GameState.Result;
            SetLaneVisibility(showPlayfield);
        }

        private void SetLaneVisibility(bool visible)
        {
            if (lanes == null) return;
            foreach (LaneView lane in lanes)
            {
                if (lane != null) lane.gameObject.SetActive(visible);
            }
            if (judgeLine != null) judgeLine.enabled = true;   // 判定线一直留着当视觉基准
        }

        private void OnLanePressed(int lane, Judgement judgement)
        {
            if (lanes == null || lane < 0 || lane >= lanes.Length || lanes[lane] == null) return;

            if (judgement == Judgement.None) lanes[lane].FlashEmpty();
            else lanes[lane].Flash(judgement);
        }

        private void OnJudged(int lane, Judgement judgement, double delta, int combo)
        {
            // 判定线上对应位置爆一圈
            if (effects != null)
            {
                Vector3 position = new Vector3(
                    PlayfieldLayout.LaneToX(lane),
                    PlayfieldLayout.JudgeLineY,
                    0f);
                effects.Play(position, JudgementInfo.ToColor(judgement));
            }

            // 判定线脉冲：命中是暖色，失误是红色
            _judgePulse = judgePulseStrength;
            _judgePulseColor = judgement == Judgement.Miss ? Theme.Miss : JudgementInfo.ToColor(judgement);
            ApplyJudgeLineColor();
        }

        // ================================================================ 长按（Hold）持续反馈
        // 设计目标（来自实际试玩反馈）：
        //   · 只把音符"停"在判定线上，玩家分不清是"按住了"还是"卡住了"；
        //   · 所以按下瞬间给一次和 tap 完全一样的扩散光环，按住期间每 holdPulseInterval
        //     再补一次，同时判定区常亮 + 判定线上有一团呼吸的光晕；
        //   · 松手/到尾立刻熄灭，避免"已经松手了还在亮"的错觉。
        private void OnHoldStarted(int lane)
        {
            if (lane < 0 || lane >= PlayfieldLayout.LaneCount) return;

            if (lanes != null && lane < lanes.Length && lanes[lane] != null) lanes[lane].SetHeld(true);

            if (_holdGlow[lane] == null) _holdGlow[lane] = CreateHoldGlow(lane);
            if (_holdGlow[lane] != null) _holdGlow[lane].gameObject.SetActive(true);

            _holdPulseTimer[lane] = holdPulseInterval;
            PlayHoldPulse(lane);
        }

        private void OnHoldEnded(int lane)
        {
            if (lane < 0 || lane >= PlayfieldLayout.LaneCount) return;

            if (lanes != null && lane < lanes.Length && lanes[lane] != null) lanes[lane].SetHeld(false);
            if (_holdGlow[lane] != null) _holdGlow[lane].gameObject.SetActive(false);
            _holdPulseTimer[lane] = 0f;
        }

        private void TickHoldFeedback()
        {
            for (int lane = 0; lane < PlayfieldLayout.LaneCount; lane++)
            {
                SpriteRenderer glow = _holdGlow[lane];
                if (glow == null || !glow.gameObject.activeSelf) continue;

                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6.5f);
                Color c = JudgementInfo.ToColor(Judgement.Perfect);
                c.a = holdGlowAlpha * (0.70f + 0.55f * pulse);
                glow.color = c;
                glow.transform.localScale = new Vector3(
                    holdGlowScale.x * (1f + 0.06f * pulse),
                    holdGlowScale.y * (1f + 0.14f * pulse), 1f);

                _holdPulseTimer[lane] -= Time.deltaTime;
                if (_holdPulseTimer[lane] <= 0f)
                {
                    _holdPulseTimer[lane] = Mathf.Max(0.05f, holdPulseInterval);
                    PlayHoldPulse(lane);
                }
            }
        }

        private void PlayHoldPulse(int lane)
        {
            if (effects == null) return;
            effects.Play(
                new Vector3(PlayfieldLayout.LaneToX(lane), PlayfieldLayout.JudgeLineY, 0f),
                JudgementInfo.ToColor(Judgement.Perfect));
        }

        private SpriteRenderer CreateHoldGlow(int lane)
        {
            var go = new GameObject("HoldGlow" + lane);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(PlayfieldLayout.LaneToX(lane), PlayfieldLayout.JudgeLineY, 0f);
            go.transform.localScale = new Vector3(holdGlowScale.x, holdGlowScale.y, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Glow;
            sr.color = JudgementInfo.ToColor(Judgement.Perfect);
            sr.sortingOrder = -6;      // 盖住判定区(-9)和轨道底(-10)，但不挡音符
            go.SetActive(false);
            return sr;
        }

        private void ApplyJudgeLineColor()
        {
            if (judgeLine == null) return;
            Color c = Color.Lerp(_judgeBaseColor, _judgePulseColor, _judgePulse);
            c.a = 0.85f + 0.15f * _judgePulse;
            judgeLine.color = c;
        }
    }
}
