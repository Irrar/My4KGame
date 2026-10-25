using UnityEngine;
using UnityEngine.UI;

namespace My4KGame
{
    /// <summary>
    /// 结算界面：评级、分数、准确率、四档判定数量、最大连击、是否新纪录。
    ///
    /// 这里的信息层级是刻意排的：
    ///   评级最大（一眼知道这局怎么样）→ 分数 → 准确率 → 详细判定分布。
    /// 把 MISS 数量放在最下面一行，是因为它是负面信息，
    /// 需要玩家主动去看，而不是每次结算都怼到脸上。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResultView : MonoBehaviour
    {
        [Header("根节点")]
        public RectTransform root;
        public Canvas canvas;
        public CanvasGroup group;

        [Header("卡片元素")]
        public Image card;
        public Text rankText;
        public Text songText;
        public Text scoreText;
        public Text accuracyText;
        public Text comboText;
        public Text detailText;
        public Text newRecordText;
        public Button retryButton;
        public Button menuButton;

        private GameManager _game;
        private bool _bound;

        public void EnsureBuilt()
        {
            if (root == null) root = (RectTransform)transform;
            if (canvas == null) canvas = GetComponentInParent<Canvas>();
            if (group == null)
            {
                group = GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            }

            Transform existing = root.Find("Backdrop");
            if (existing == null)
            {
                Image backdrop = UiFactory.CreateImage(root, "Backdrop", new Color(0.01f, 0.02f, 0.03f, 0.78f));
                UiFactory.Stretch(backdrop.rectTransform);
            }

            if (card == null)
            {
                card = UiFactory.CreateImage(root, "Card", new Color(0.07f, 0.09f, 0.12f, 0.98f));
                UiFactory.Place(card.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(900f, 640f));
            }

            if (rankText == null)
            {
                rankText = UiFactory.CreateOutlinedText(card.transform, "Rank", "S", 170, Theme.Accent,
                    TextAnchor.MiddleCenter);
                UiFactory.Place(rankText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -150f),
                    new Vector2(600f, 200f), new Vector2(0.5f, 1f));
            }
            if (songText == null)
            {
                songText = UiFactory.CreateText(card.transform, "Song", "", 30, Theme.TextMuted,
                    TextAnchor.MiddleCenter);
                UiFactory.Place(songText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -270f),
                    new Vector2(820f, 40f), new Vector2(0.5f, 1f));
            }
            if (newRecordText == null)
            {
                newRecordText = UiFactory.CreateText(card.transform, "NewRecord", "★ 新纪录！", 32,
                    Theme.AccentWarm, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiFactory.Place(newRecordText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -312f),
                    new Vector2(500f, 40f), new Vector2(0.5f, 1f));
                newRecordText.gameObject.SetActive(false);
            }

            if (scoreText == null)
            {
                scoreText = UiFactory.CreateOutlinedText(card.transform, "Score", "0", 76,
                    Theme.TextPrimary);
                UiFactory.Place(scoreText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f),
                    new Vector2(820f, 100f));
            }
            if (accuracyText == null)
            {
                accuracyText = UiFactory.CreateText(card.transform, "Accuracy", "准确率 0.00%", 34,
                    Theme.TextMuted);
                UiFactory.Place(accuracyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -34f),
                    new Vector2(820f, 46f));
            }
            if (comboText == null)
            {
                comboText = UiFactory.CreateText(card.transform, "Combo", "最大连击 0", 30,
                    Theme.TextMuted);
                UiFactory.Place(comboText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -86f),
                    new Vector2(820f, 40f));
            }
            if (detailText == null)
            {
                detailText = UiFactory.CreateText(card.transform, "Detail", "", 26, Theme.TextMuted);
                UiFactory.Place(detailText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -150f),
                    new Vector2(840f, 60f));
            }

            if (retryButton == null)
            {
                retryButton = UiFactory.CreateButton(card.transform, "重试 (R)", new Vector2(300f, 66f), 30,
                    Theme.Accent, new Color(0.02f, 0.06f, 0.09f), OnRetry);
                UiFactory.Place((RectTransform)retryButton.transform, new Vector2(0.5f, 0f),
                    new Vector2(-170f, 56f), new Vector2(300f, 66f), new Vector2(0.5f, 0f));
            }
            if (menuButton == null)
            {
                menuButton = UiFactory.CreateButton(card.transform, "选曲 (Esc)", new Vector2(300f, 66f), 30,
                    Theme.PanelLight, Theme.TextPrimary, OnMenu);
                UiFactory.Place((RectTransform)menuButton.transform, new Vector2(0.5f, 0f),
                    new Vector2(170f, 56f), new Vector2(300f, 66f), new Vector2(0.5f, 0f));
            }

            SetVisible(false);
        }

        // ================================================================ 生命周期
        private void Awake()
        {
            EnsureBuilt();
        }

        private void Update()
        {
            if (!_bound) Bind();
        }

        private void OnDisable()
        {
            if (_game == null || !_bound) return;
            _game.SongFinished -= OnSongFinished;
            _game.StateChanged -= OnStateChanged;
            _bound = false;
        }

        private void Bind()
        {
            _game = GameManager.Instance;
            if (_game == null) return;

            _game.SongFinished += OnSongFinished;
            _game.StateChanged += OnStateChanged;
            _bound = true;

            // 场景里保存下来的按钮，其 onClick（代码里挂的委托）不会跟着保存，
            // 所以每次进运行都要重新绑一遍，否则鼠标点了没反应。
            BindButtons();

            SetVisible(_game.State == GameState.Result);
        }

        /// <summary>重新绑定按钮点击。先清空再加，保证重复调用也不会叠加。</summary>
        private void BindButtons()
        {
            if (retryButton != null)
            {
                retryButton.onClick.RemoveAllListeners();
                retryButton.onClick.AddListener(OnRetry);
            }
            if (menuButton != null)
            {
                menuButton.onClick.RemoveAllListeners();
                menuButton.onClick.AddListener(OnMenu);
            }
        }

        private void OnStateChanged(GameState state)
        {
            SetVisible(state == GameState.Result);
        }

        private void OnSongFinished(ScoreSystem score)
        {
            if (score == null) return;
            Refresh(score);
            SetVisible(true);
        }

        // ================================================================ 数据填充
        private void Refresh(ScoreSystem score)
        {
            if (score == null) return;

            if (rankText != null)
            {
                rankText.text = score.Rank;
                rankText.color = RankColor(score.Rank);
            }
            if (songText != null && _game != null && _game.Chart != null)
            {
                songText.text = $"{_game.Chart.songName}    ·   {_game.Chart.difficulty} Lv.{_game.Chart.level}";
            }
            if (scoreText != null) scoreText.text = score.Score.ToString("N0");
            if (accuracyText != null) accuracyText.text = $"准确率 {score.Accuracy:F2}%";
            if (comboText != null)
            {
                comboText.text = score.IsFullCombo
                    ? $"最大连击 {score.MaxCombo}   （全连！）"
                    : $"最大连击 {score.MaxCombo}";
            }
            if (detailText != null)
            {
                detailText.text =
                    $"<color=#5CE1F0>PERFECT {score.Count(Judgement.Perfect)}</color>    " +
                    $"<color=#7BE080>GREAT {score.Count(Judgement.Great)}</color>    " +
                    $"<color=#FAD15C>GOOD {score.Count(Judgement.Good)}</color>    " +
                    $"<color=#F55C6B>MISS {score.Count(Judgement.Miss)}</color>";
            }
            if (newRecordText != null)
            {
                newRecordText.gameObject.SetActive(_game != null && _game.IsNewBest);
            }
        }

        private static Color RankColor(string rank)
        {
            switch (rank)
            {
                case "SSS": return new Color(1f, 0.86f, 0.42f);
                case "SS": return new Color(0.98f, 0.90f, 0.55f);
                case "S": return new Color(0.42f, 0.90f, 0.96f);
                case "A": return new Color(0.48f, 0.88f, 0.55f);
                case "B": return new Color(0.55f, 0.70f, 0.95f);
                case "C": return new Color(0.75f, 0.75f, 0.80f);
                default: return new Color(0.95f, 0.42f, 0.45f);
            }
        }

        // ================================================================ 按钮
        private void OnRetry()
        {
            if (_game != null) _game.Restart();
        }

        private void OnMenu()
        {
            if (_game != null) _game.EnterMenu();
        }

        public void SetVisible(bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
            group.interactable = visible;
        }
    }
}
