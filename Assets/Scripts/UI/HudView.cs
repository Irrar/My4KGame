using UnityEngine;
using UnityEngine.UI;

namespace My4KGame
{
    /// <summary>
    /// 游戏中的抬头显示：分数、准确率、连击、判定文字、倒计时、进度条、键位提示。
    ///
    /// 两个设计细节，都是"手感"层面的：
    ///   1. 判定文字贴在世界坐标上（判定线上方 1 米），窗口怎么缩放都不会跑位；
    ///   2. 连击数变化时会弹一下（放大再回落），让玩家用余光也知道自己连上了，
    ///      不用盯着数字看 —— 音游里眼睛应该留给下落的音符。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudView : MonoBehaviour
    {
        [Header("根节点（留空 = 自己）")]
        public RectTransform root;
        public Canvas canvas;
        public CanvasGroup group;

        [Header("顶部信息")]
        public Text songTitle;
        public Text difficulty;
        public Text score;
        public Text accuracy;
        public Image progressBackground;
        public Image progressFill;

        [Header("判定区（会自动贴到世界坐标上）")]
        public Text comboText;
        public Text comboCaption;
        public Text judgementText;
        public Text countdownText;
        public Text[] keyLabels = new Text[PlayfieldLayout.LaneCount];

        [Header("提示")]
        public Text hintText;
        public Text autoPlayHint;
        public RectTransform speedPanel;
        public Text speedValueText;

        private GameManager _game;
        private Camera _camera;
        private bool _bound;

        private float _judgementTimer;
        private const float JudgementLifetime = 0.55f;
        private float _comboPop;
        private Vector3 _comboBaseScale = Vector3.one;
        private double _lastProgress = -1.0;

        // ================================================================ 构建
        /// <summary>补齐所有缺失的界面元素。可以重复调用（重复调用只会补空缺）。</summary>
        public void EnsureBuilt()
        {
            if (root == null) root = (RectTransform)transform;
            if (canvas == null) canvas = GetComponentInParent<Canvas>();
            if (group == null)
            {
                group = GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            }
            if (keyLabels == null || keyLabels.Length < PlayfieldLayout.LaneCount)
                keyLabels = new Text[PlayfieldLayout.LaneCount];

            // ---- 左上：曲名与难度
            if (songTitle == null)
            {
                songTitle = UiFactory.CreateText(root, "SongTitle", "Neon Pulse", 42,
                    Theme.TextPrimary, TextAnchor.UpperLeft, FontStyle.Bold);
                UiFactory.Place(songTitle.rectTransform, new Vector2(0f, 1f), new Vector2(48f, -40f),
                    new Vector2(900f, 56f), new Vector2(0f, 1f));
            }
            if (difficulty == null)
            {
                difficulty = UiFactory.CreateText(root, "Difficulty", "", 28,
                    Theme.TextMuted, TextAnchor.UpperLeft);
                UiFactory.Place(difficulty.rectTransform, new Vector2(0f, 1f), new Vector2(50f, -98f),
                    new Vector2(900f, 40f), new Vector2(0f, 1f));
            }

            // ---- 右上：分数与准确率
            if (score == null)
            {
                score = UiFactory.CreateOutlinedText(root, "Score", "0", 68,
                    Theme.TextPrimary, TextAnchor.UpperRight);
                UiFactory.Place(score.rectTransform, new Vector2(1f, 1f), new Vector2(-48f, -34f),
                    new Vector2(800f, 84f), new Vector2(1f, 1f));
            }
            if (accuracy == null)
            {
                accuracy = UiFactory.CreateText(root, "Accuracy", "准确率 0.00%", 30,
                    Theme.TextMuted, TextAnchor.UpperRight);
                UiFactory.Place(accuracy.rectTransform, new Vector2(1f, 1f), new Vector2(-50f, -122f),
                    new Vector2(800f, 40f), new Vector2(1f, 1f));
            }

            // ---- 顶部：进度条
            if (progressBackground == null)
            {
                progressBackground = UiFactory.CreateImage(root, "ProgressBar", new Color(1f, 1f, 1f, 0.08f));
                UiFactory.Place(progressBackground.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -182f),
                    new Vector2(900f, 8f));
            }
            if (progressFill == null)
            {
                progressFill = UiFactory.CreateImage(progressBackground.transform, "ProgressFill", Theme.Accent);
                UiFactory.Stretch(progressFill.rectTransform);
                progressFill.type = Image.Type.Filled;
                progressFill.fillMethod = Image.FillMethod.Horizontal;
                progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
                progressFill.fillAmount = 0f;
            }

            // ---- 中间：连击
            if (comboText == null)
            {
                comboText = UiFactory.CreateOutlinedText(root, "Combo", "", 96, Color.white);
                comboText.rectTransform.sizeDelta = new Vector2(600f, 120f);
                comboText.gameObject.SetActive(false);
            }
            if (comboCaption == null)
            {
                comboCaption = UiFactory.CreateText(root, "ComboCaption", "COMBO", 26,
                    new Color(1f, 1f, 1f, 0.55f));
                comboCaption.rectTransform.sizeDelta = new Vector2(400f, 40f);
                comboCaption.gameObject.SetActive(false);
            }

            // ---- 判定文字
            if (judgementText == null)
            {
                judgementText = UiFactory.CreateOutlinedText(root, "Judgement", "", 54, Color.white);
                judgementText.rectTransform.sizeDelta = new Vector2(800f, 90f);
                judgementText.gameObject.SetActive(false);
            }

            // ---- 倒计时
            if (countdownText == null)
            {
                countdownText = UiFactory.CreateOutlinedText(root, "Countdown", "", 150, Color.white);
                UiFactory.Place(countdownText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f),
                    new Vector2(800f, 200f));
                countdownText.gameObject.SetActive(false);
            }

            // ---- 键位提示（贴在每条轨道下方）
            for (int i = 0; i < PlayfieldLayout.LaneCount; i++)
            {
                if (keyLabels[i] != null) continue;
                Text label = UiFactory.CreateOutlinedText(root, "Key" + i, PlayfieldLayout.KeyNames[i], 38,
                    new Color(1f, 1f, 1f, 0.42f));
                label.rectTransform.sizeDelta = new Vector2(140f, 60f);
                keyLabels[i] = label;
            }

            // ---- 底部提示
            if (hintText == null)
            {
                hintText = UiFactory.CreateText(root, "Hint", "Esc 暂停 · ↑↓ 调速", 24,
                    new Color(1f, 1f, 1f, 0.32f), TextAnchor.LowerRight);
                UiFactory.Place(hintText.rectTransform, new Vector2(1f, 0f), new Vector2(-48f, 26f),
                    new Vector2(600f, 36f), new Vector2(1f, 0f));
            }
            // 游戏内流速：半透明悬浮窗（左下角），不做预览图，但能随时点箭头调
            EnsureSpeedPanel();

            if (autoPlayHint == null)
            {
                autoPlayHint = UiFactory.CreateText(root, "AutoPlayHint", "自动演示中", 26,
                    Theme.AccentWarm, TextAnchor.UpperCenter);
                UiFactory.Place(autoPlayHint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -214f),
                    new Vector2(600f, 40f), new Vector2(0.5f, 1f));
                autoPlayHint.gameObject.SetActive(false);
            }

            // 一开始是选曲界面，HUD 先藏起来
            SetVisible(false);
        }

        // ================================================================ 游戏内流速悬浮窗
        /// <summary>
        /// 半透明悬浮窗：`◀ 5.50 ▶` + 一行提示。数值来自全局 <see cref="ScrollSpeedState"/>，
        /// 和主界面卡片是同一个数据源，所以两边永远一致。
        /// </summary>
        private void EnsureSpeedPanel()
        {
            if (root == null) return;

            Transform existing = root.Find("SpeedPanel");
            if (existing != null)
            {
                if (speedPanel == null) speedPanel = (RectTransform)existing;
                if (speedValueText == null)
                {
                    Transform value = existing.Find("SpeedPanelValue");
                    if (value != null) speedValueText = value.GetComponent<Text>();
                }
                RefreshSpeedValue();
                return;
            }

            Image panel = UiFactory.CreateImage(root, "SpeedPanel", new Color(0.02f, 0.03f, 0.05f, 0.55f));
            speedPanel = panel.rectTransform;
            UiFactory.Place(speedPanel, new Vector2(0f, 0f), new Vector2(36f, 34f),
                new Vector2(320f, 96f), new Vector2(0f, 0f));

            UiFactory.CreateTapButton(speedPanel, "◀", new Vector2(0f, 0.5f), new Vector2(10f, 12f),
                new Vector2(46f, 46f), new Color(1f, 1f, 1f, 0.10f), Theme.TextPrimary, 26,
                () => ScrollSpeedState.Adjust(-ScrollSpeedTrack.Step));

            UiFactory.CreateTapButton(speedPanel, "▶", new Vector2(1f, 0.5f), new Vector2(-10f, 12f),
                new Vector2(46f, 46f), new Color(1f, 1f, 1f, 0.10f), Theme.TextPrimary, 26,
                () => ScrollSpeedState.Adjust(ScrollSpeedTrack.Step));

            speedValueText = UiFactory.CreateText(speedPanel, "SpeedPanelValue", "5.50", 34,
                Theme.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.Place(speedValueText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 12f),
                new Vector2(180f, 46f));

            Text hint = UiFactory.CreateText(speedPanel, "SpeedPanelHint", "每次 0.25", 17,
                new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleCenter);
            UiFactory.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 8f),
                new Vector2(300f, 22f), new Vector2(0.5f, 0f));

            RefreshSpeedValue();
        }

        private void RefreshSpeedValue()
        {
            if (speedValueText == null) return;
            speedValueText.text = ScrollSpeedState.Value.ToString("0.00",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private void OnSpeedStateChanged(float value) => RefreshSpeedValue();

        // ================================================================ 生命周期
        private void Awake()
        {
            EnsureBuilt();
            _comboBaseScale = comboText != null ? comboText.rectTransform.localScale : Vector3.one;
        }

        private void Update()
        {
            if (!_bound) Bind();
            if (_game == null) return;

            if (_camera == null) _camera = Camera.main;

            UpdateTopInfo();
            UpdateCountdown();
            UpdateJudgementFade();
            UpdateComboPop();
        }

        private void LateUpdate()
        {
            LayoutWorldLabels();
        }

        private void OnEnable()
        {
            // 全局流速数据源：主界面或游戏内改，这里都会立刻刷新
            ScrollSpeedState.Changed += OnSpeedStateChanged;
            RefreshSpeedValue();
        }

        private void OnDisable()
        {
            ScrollSpeedState.Changed -= OnSpeedStateChanged;

            if (_game == null || !_bound) return;
            _game.Judged -= OnJudged;
            _game.StateChanged -= OnStateChanged;
            _game.ChartStarted -= OnChartStarted;
            _bound = false;
        }

        private void Bind()
        {
            _game = GameManager.Instance;
            if (_game == null) return;

            _game.Judged += OnJudged;
            _game.StateChanged += OnStateChanged;
            _game.ChartStarted += OnChartStarted;
            _bound = true;

            OnStateChanged(_game.State);
            if (_game.Chart != null) OnChartStarted(_game.Chart);
            RefreshSpeedValue();
        }

        // ================================================================ 事件
        private void OnChartStarted(ChartData chart)
        {
            if (chart == null) return;
            if (songTitle != null) songTitle.text = chart.songName;
            if (difficulty != null)
            {
                difficulty.text = $"<color=#8FA3BF>{chart.difficulty}</color>  Lv.{chart.level}   " +
                                  $"BPM {chart.bpm:0.##}   {chart.NoteCount} 音符";
            }
            if (autoPlayHint != null)
            {
                autoPlayHint.gameObject.SetActive(_game != null && _game.autoPlay);
            }
        }

        private void OnStateChanged(GameState state)
        {
            bool visible = state == GameState.Countdown || state == GameState.Playing ||
                           state == GameState.Paused || state == GameState.Result;
            SetVisible(visible);
            if (!visible) return;

            if (countdownText != null) countdownText.gameObject.SetActive(state == GameState.Countdown);
            if (hintText != null)
            {
                hintText.text = state == GameState.Paused ? "已暂停 · Esc 继续" : "Esc 暂停 · ↑↓ 调速";
            }
        }

        /// <summary>
        /// 用 CanvasGroup 而不是 SetActive 来隐藏 HUD：
        /// 这样组件本身始终活着，事件订阅不会断，切回来时不用重新绑定。
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
            group.interactable = visible;
        }

        private void OnJudged(int lane, Judgement judgement, double deltaMs, int combo)
        {
            if (judgementText != null)
            {
                judgementText.gameObject.SetActive(true);
                string text = JudgementInfo.ToText(judgement);
                if (judgement != Judgement.Miss && judgement != Judgement.None)
                {
                    int ms = Mathf.RoundToInt((float)deltaMs);
                    string sign = ms >= 0 ? "+" : "";
                    text += $"  <size=30><color=#B9C6D8>{sign}{ms}ms</color></size>";
                }
                judgementText.text = text;
                judgementText.color = JudgementInfo.ToColor(judgement);
                _judgementTimer = JudgementLifetime;
            }

            if (comboText != null)
            {
                bool showCombo = combo >= 2;
                comboText.gameObject.SetActive(showCombo);
                if (comboCaption != null) comboCaption.gameObject.SetActive(showCombo);
                comboText.text = combo.ToString();
                if (showCombo) _comboPop = 1f;
            }
        }

        // ================================================================ 每帧刷新
        private void UpdateTopInfo()
        {
            var scoreSystem = _game.Score;
            if (score == null || scoreSystem == null) return;

            score.text = scoreSystem.Score.ToString("N0");
            accuracy.text = $"准确率 {scoreSystem.Accuracy:F2}%";

            if (_game.State == GameState.Playing || _game.State == GameState.Paused ||
                _game.State == GameState.Result)
            {
                double progress = _game.SongProgress;
                if (Mathf.Abs((float)(progress - _lastProgress)) > 0.0005f)
                {
                    _lastProgress = progress;
                    if (progressFill != null) progressFill.fillAmount = (float)progress;
                }
            }
        }

        private void UpdateCountdown()
        {
            if (countdownText == null) return;
            if (_game.State != GameState.Countdown)
            {
                if (countdownText.gameObject.activeSelf) countdownText.gameObject.SetActive(false);
                return;
            }

            double remain = _game.CountdownRemaining;
            countdownText.gameObject.SetActive(true);
            if (remain > 0.001)
            {
                int number = Mathf.CeilToInt((float)remain);
                countdownText.text = number.ToString();
                countdownText.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.45f, 1f, (float)(remain % 1.0)));
                countdownText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, (float)(remain % 1.0));
            }
            else
            {
                countdownText.text = "GO";
                countdownText.color = Theme.Accent;
            }
        }

        private void UpdateJudgementFade()
        {
            if (judgementText == null || !judgementText.gameObject.activeSelf) return;

            _judgementTimer -= Time.deltaTime;
            if (_judgementTimer <= 0f)
            {
                judgementText.gameObject.SetActive(false);
                return;
            }

            float k = Mathf.Clamp01(_judgementTimer / JudgementLifetime);
            Color c = judgementText.color;
            c.a = k;
            judgementText.color = c;
        }

        private void UpdateComboPop()
        {
            if (comboText == null || !comboText.gameObject.activeSelf) return;
            if (_comboPop > 0f)
            {
                _comboPop = Mathf.Max(0f, _comboPop - Time.deltaTime * 5f);
                float scale = 1f + 0.16f * _comboPop;
                comboText.rectTransform.localScale = _comboBaseScale * scale;
            }
            else
            {
                comboText.rectTransform.localScale = _comboBaseScale;
            }
        }

        /// <summary>
        /// 把连击、判定文字、键位提示贴到世界坐标上。
        /// 公开出来是给编辑器的预览图用的（编辑器里不走 LateUpdate，得手动调一次）。
        /// </summary>
        public void LayoutWorldLabels()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null || canvas == null) return;

            // 连击放在轨道左侧（轨道中间是要看音符的地方，别挡住）
            Vector3 comboWorld = new Vector3(-6.15f, PlayfieldLayout.JudgeLineY + 3.4f, 0f);
            if (comboText != null)
                UiFactory.AnchorToWorld(comboText.rectTransform, canvas, _camera, comboWorld);
            if (comboCaption != null)
                UiFactory.AnchorToWorld(comboCaption.rectTransform, canvas, _camera, comboWorld - new Vector3(0f, 0.72f, 0f));

            Vector3 judgeWorld = new Vector3(0f, PlayfieldLayout.JudgeLineY + 1.35f, 0f);
            if (judgementText != null)
                UiFactory.AnchorToWorld(judgementText.rectTransform, canvas, _camera, judgeWorld);

            if (keyLabels == null) return;
            for (int i = 0; i < keyLabels.Length; i++)
            {
                if (keyLabels[i] == null) continue;
                Vector3 world = new Vector3(PlayfieldLayout.LaneToX(i), PlayfieldLayout.JudgeLineY - 0.95f, 0f);
                UiFactory.AnchorToWorld(keyLabels[i].rectTransform, canvas, _camera, world);
                Color laneColor = PlayfieldLayout.LaneColor(i);
                laneColor.a = 0.45f;
                keyLabels[i].color = laneColor;
            }
        }
    }
}
