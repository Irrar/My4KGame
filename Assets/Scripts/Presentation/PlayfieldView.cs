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

        private GameManager _game;
        private bool _bound;
        private float _judgePulse;
        private Color _judgePulseColor = Color.white;
        private Color _judgeBaseColor = new Color(1f, 0.85f, 0.35f);

        private void Update()
        {
            if (!_bound) Bind();

            if (_judgePulse > 0f)
            {
                _judgePulse = Mathf.Max(0f, _judgePulse - Time.deltaTime * judgePulseDecay);
                ApplyJudgeLineColor();
            }
        }

        private void OnDisable()
        {
            if (_game == null || !_bound) return;
            _game.LanePressed -= OnLanePressed;
            _game.Judged -= OnJudged;
            _game.StateChanged -= OnStateChanged;
            _bound = false;
        }

        private void Bind()
        {
            _game = GameManager.Instance;
            if (_game == null) return;

            _game.LanePressed += OnLanePressed;
            _game.Judged += OnJudged;
            _game.StateChanged += OnStateChanged;
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

        private void ApplyJudgeLineColor()
        {
            if (judgeLine == null) return;
            Color c = Color.Lerp(_judgeBaseColor, _judgePulseColor, _judgePulse);
            c.a = 0.85f + 0.15f * _judgePulse;
            judgeLine.color = c;
        }
    }
}
