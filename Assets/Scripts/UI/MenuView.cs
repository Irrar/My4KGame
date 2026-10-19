using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace My4KGame
{
    /// <summary>
    /// 选曲界面：列谱面、看最佳成绩、调音画校准、开自动演示。
    ///
    /// 谱面列表是"扫出来的"，不是写死的：
    /// ChartLibrary 把 Assets/Resources/Charts 下所有 json 都读出来，
    /// 所以往那个目录丢一个新 json，界面里就自动多一个难度，一行代码都不用改。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuView : MonoBehaviour
    {
        [Header("根节点")]
        public RectTransform root;
        public Canvas canvas;
        public CanvasGroup group;

        [Header("界面元素")]
        public Text title;
        public Text subtitle;
        public RectTransform chartList;
        public Text footer;
        public Text calibrationLabel;
        public Text calibrationValue;
        public Text speedValue;
        public Text speedHint;
        public SpeedPreviewView preview;
        public Button autoPlayButton;
        public Text autoPlayLabel;
        public Button rescanButton;

        private GameManager _game;
        private Camera _camera;
        private bool _bound;
        private readonly List<GameObject> _chartButtons = new List<GameObject>();

        // ================================================================ 构建
        public void EnsureBuilt()
        {
            if (root == null) root = (RectTransform)transform;
            if (canvas == null) canvas = GetComponentInParent<Canvas>();
            if (group == null)
            {
                group = GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            }

            // ---- 背景：一层深色底 + 一条左侧强调色竖线
            if (findChild("Backdrop") == null)
            {
                Image backdrop = UiFactory.CreateImage(root, "Backdrop", new Color(0.02f, 0.03f, 0.05f, 0.96f));
                UiFactory.Stretch(backdrop.rectTransform);

                Image stripe = UiFactory.CreateImage(root, "AccentStripe", new Color(0.30f, 0.78f, 0.96f, 0.55f));
                UiFactory.Place(stripe.rectTransform, new Vector2(0f, 0.5f), new Vector2(64f, 0f),
                    new Vector2(6f, 640f));
            }

            // ---- 标题区
            if (title == null)
            {
                title = UiFactory.CreateOutlinedText(root, "Title", "My4KGame", 110,
                    Theme.TextPrimary, TextAnchor.UpperLeft);
                UiFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(110f, -110f),
                    new Vector2(1100f, 130f), new Vector2(0f, 1f));
            }
            if (subtitle == null)
            {
                subtitle = UiFactory.CreateText(root, "Subtitle",
                    "4K 下落式音游 · 用 D F J K 打击四条轨道 · 音符落到判定线时按下", 30,
                    Theme.TextMuted, TextAnchor.UpperLeft);
                UiFactory.Place(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(112f, -252f),
                    new Vector2(1500f, 44f), new Vector2(0f, 1f));
            }

            // ---- 谱面按钮容器
            if (chartList == null)
            {
                chartList = UiFactory.CreateRect(root, "ChartList");
                UiFactory.Place(chartList, new Vector2(0f, 0.5f), new Vector2(112f, -46f),
                    new Vector2(880f, 520f), new Vector2(0f, 0.5f));
            }

            // ---- 右侧：校准与自动演示
            // ---- 旧版本留下的滑条：清掉，避免和新按钮叠在一起
            RemoveLegacySlider("CalibrationSlider");
            RemoveLegacySlider("SpeedSlider");

            if (calibrationLabel == null)
            {
                calibrationLabel = UiFactory.CreateText(root, "CalibrationLabel",
                    "音画校准", 28, Theme.TextPrimary, TextAnchor.UpperLeft);
                UiFactory.Place(calibrationLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(-120f, 214f),
                    new Vector2(700f, 40f), new Vector2(1f, 0.5f));
            }
            if (calibrationValue == null)
            {
                UiFactory.StepperRow row = UiFactory.CreateStepperRow(root, "CalibrationStepper",
                    new Vector2(-120f, 162f), new Vector2(480f, 56f), 28, "0 ms",
                    () => NudgeCalibration(-CalibrationStepMs),
                    () => NudgeCalibration(CalibrationStepMs));
                calibrationValue = row.value;
            }

            Text calibrationHint = findChildText("CalibrationHint");
            if (calibrationHint == null)
            {
                calibrationHint = UiFactory.CreateText(root, "CalibrationHint",
                    "每次 5ms，范围 ±150ms（判偏早就调大）", 21,
                    Theme.TextMuted, TextAnchor.UpperLeft);
                UiFactory.Place(calibrationHint.rectTransform, new Vector2(1f, 0.5f), new Vector2(-120f, 120f),
                    new Vector2(700f, 28f), new Vector2(1f, 0.5f));
            }

            // ---- 音符流速：卡片式 UI（大字号居中数值 + 左右箭头 + 实时下落预览）
            EnsureSpeedCard();

            if (autoPlayButton == null)
            {
                autoPlayButton = UiFactory.CreateButton(root, "自动演示：关", new Vector2(280f, 58f), 26,
                    Theme.PanelLight, Theme.TextPrimary, ToggleAutoPlay);
                UiFactory.Place((RectTransform)autoPlayButton.transform, new Vector2(1f, 0.5f),
                    new Vector2(-260f, -390f), new Vector2(280f, 58f), new Vector2(1f, 0.5f));
                autoPlayLabel = autoPlayButton.GetComponentInChildren<Text>();
            }

            if (rescanButton == null)
            {
                rescanButton = UiFactory.CreateButton(root, "重新扫描谱面", new Vector2(280f, 58f), 26,
                    Theme.PanelLight, Theme.TextPrimary, RescanCharts);
                UiFactory.Place((RectTransform)rescanButton.transform, new Vector2(1f, 0.5f),
                    new Vector2(-260f, -456f), new Vector2(280f, 58f), new Vector2(1f, 0.5f));
            }

            if (footer == null)
            {
                footer = UiFactory.CreateText(root, "Footer",
                    "提示：把新的谱面 json 放到 Assets/Resources/Charts 里，再点「重新扫描谱面」就会出现在列表里",
                    22, new Color(1f, 1f, 1f, 0.30f), TextAnchor.LowerLeft);
                UiFactory.Place(footer.rectTransform, new Vector2(0f, 0f), new Vector2(112f, 40f),
                    new Vector2(1700f, 40f), new Vector2(0f, 0f));
            }

            RebuildChartButtons();
        }

        private Text findChildText(string name)
        {
            Transform child = root != null ? root.Find(name) : null;
            return child != null ? child.GetComponent<Text>() : null;
        }

        private Transform findChild(string name) => root != null ? root.Find(name) : null;

        // ================================================================ 谱面按钮
        /// <summary>按 ChartLibrary 的内容重建谱面按钮列表。可以重复调用。</summary>
        public void RebuildChartButtons()
        {
            if (chartList == null) return;

            // 先把旧按钮清干净（编辑模式下要用 DestroyImmediate，否则 Unity 会报错）
            for (int i = chartList.childCount - 1; i >= 0; i--)
            {
                DestroyObject(chartList.GetChild(i).gameObject);
            }
            _chartButtons.Clear();

            var entries = ChartLibrary.Entries;
            float y = 0f;
            const float height = 92f;
            const float gap = 14f;

            foreach (ChartEntry entry in entries)
            {
                if (entry == null) continue;
                CreateChartButton(entry, y, height);
                y -= height + gap;
            }

            if (entries.Count == 0)
            {
                Text empty = UiFactory.CreateText(chartList, "EmptyHint",
                    "一个谱面都没找到：请把 json 放到 Assets/Resources/Charts 下", 28,
                    Theme.Miss, TextAnchor.MiddleLeft);
                UiFactory.Place(empty.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -40f),
                    new Vector2(880f, 60f), new Vector2(0f, 1f));
            }
        }

        private static void DestroyObject(GameObject go)
        {
            if (go == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(go);
                return;
            }
#endif
            UnityEngine.Object.Destroy(go);
        }

        private void CreateChartButton(ChartEntry entry, float y, float height)
        {
            bool valid = entry.IsValid;
            string name = entry.ResourceName;
            ChartEntry captured = entry;

            Color background = valid ? Theme.Panel : new Color(0.22f, 0.10f, 0.12f, 0.95f);

            var rect = UiFactory.CreateRect(chartList, "Chart_" + name);
            UiFactory.Place(rect, new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(880f, height),
                new Vector2(0f, 1f));

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SpriteFactory.White;
            image.color = background;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            if (valid) button.onClick.AddListener(() => StartChart(captured));

            // 左侧难度色条
            Color accent = valid ? DifficultyColor(entry.Chart.difficulty) : Theme.Miss;
            Image stripe = UiFactory.CreateImage(rect, "Stripe", accent);
            UiFactory.Place(stripe.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f),
                new Vector2(6f, height));

            string best = SaveData.BestScore(name) > 0
                ? $"最佳 {SaveData.BestScore(name):N0}  {SaveData.BestRank(name)}"
                : "尚未游玩";

            string line1 = valid
                ? $"{DisplayNameOf(entry)}   Lv.{entry.Chart.level}"
                : $"{name}   （谱面有问题）";

            Text titleText = UiFactory.CreateText(rect, "Name", line1, 36, Theme.TextPrimary,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.Place(titleText.rectTransform, new Vector2(0f, 0.5f), new Vector2(36f, 14f),
                new Vector2(560f, 44f), new Vector2(0f, 0.5f));

            string line2 = valid
                ? $"{entry.Chart.NoteCount} 音符   BPM {entry.Chart.bpm:0.##}   <color=#8FA3BF>{best}</color>"
                : $"<color=#FF7A85>{entry.Error}</color>";
            Text infoText = UiFactory.CreateText(rect, "Info", line2, 24, Theme.TextMuted,
                TextAnchor.MiddleLeft);
            UiFactory.Place(infoText.rectTransform, new Vector2(0f, 0.5f), new Vector2(38f, -22f),
                new Vector2(700f, 34f), new Vector2(0f, 0.5f));

            Text play = UiFactory.CreateText(rect, "Play", "▶  开始", 28, accent,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UiFactory.Place(play.rectTransform, new Vector2(1f, 0.5f), new Vector2(-36f, 0f),
                new Vector2(200f, 40f), new Vector2(1f, 0.5f));

            _chartButtons.Add(rect.gameObject);
        }

        /// <summary>
        /// 按钮上显示的难度名。谱面没写 difficulty（比如手册第 6 章那种纯数组）时，
        /// 退回去显示文件名，总比显示一个 "Custom" 让人摸不着头脑好。
        /// </summary>
        private static string DisplayNameOf(ChartEntry entry)
        {
            if (entry == null) return "?";
            if (entry.Chart == null) return entry.ResourceName;
            string difficulty = entry.Chart.difficulty;
            if (string.IsNullOrEmpty(difficulty) || difficulty == "Custom") return entry.ResourceName;
            return difficulty;
        }

        private static Color DifficultyColor(string difficulty)        {
            switch (difficulty)
            {
                case "Easy": return new Color(0.42f, 0.86f, 0.58f);
                case "Normal": return new Color(0.98f, 0.82f, 0.36f);
                case "Hard": return new Color(0.96f, 0.42f, 0.48f);
                case "Calibration": return new Color(0.55f, 0.70f, 0.95f);
                default: return new Color(0.72f, 0.72f, 0.78f);
            }
        }

        // ================================================================ 交互
        private void StartChart(ChartEntry entry)
        {
            GameManager game = GameManager.Instance;
            if (game == null || entry == null) return;
            game.StartSong(entry.ResourceName);
        }

        private void ToggleAutoPlay()
        {
            GameManager game = GameManager.Instance;
            if (game == null) return;
            game.autoPlay = !game.autoPlay;
            RefreshAutoPlayButton();
        }

        private void RefreshAutoPlayButton()
        {
            if (autoPlayLabel == null && autoPlayButton != null)
                autoPlayLabel = autoPlayButton.GetComponentInChildren<Text>();
            if (autoPlayLabel == null) return;

            bool on = GameManager.Instance != null && GameManager.Instance.autoPlay;
            autoPlayLabel.text = on ? "自动演示：开" : "自动演示：关";
            autoPlayLabel.color = on ? Theme.AccentWarm : Theme.TextPrimary;
        }

        private void RescanCharts()
        {
            ChartLibrary.Reload();
            RebuildChartButtons();
        }

        // ================================================================ 加减按钮
        private const float CalibrationStepMs = 5f;

        /// <summary>音画校准 ±5ms。只改判定时间的校准偏移，和流速无关。</summary>
        private void NudgeCalibration(float deltaMs)
        {
            GameManager game = GameManager.Instance;
            if (game == null) return;

            double ms = game.calibrationOffset * 1000.0 + deltaMs;
            ms = System.Math.Round(System.Math.Max(-150.0, System.Math.Min(150.0, ms)));
            game.SetCalibration(ms / 1000.0);
            RefreshCalibrationValue();
        }

        /// <summary>
        /// 流速 ±0.25：直接改全局数据源 <see cref="ScrollSpeedState"/>，
        /// 游戏内的悬浮窗订阅了同一个事件，所以两边永远同步。
        /// </summary>
        private void NudgeSpeed(float delta)
        {
            ScrollSpeedState.Adjust(delta);
        }

        private void RefreshCalibrationValue()
        {
            if (calibrationValue == null) return;
            GameManager game = GameManager.Instance;
            double ms = game != null ? game.calibrationOffset * 1000.0 : 0.0;
            calibrationValue.text = $"{ms:+0;-0;0} ms";
        }

        private void RefreshSpeedValue()
        {
            if (speedValue == null) return;
            speedValue.text = ScrollSpeedState.Value.ToString("0.00",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private void OnSpeedStateChanged(float value) => RefreshSpeedValue();

        // ================================================================ 流速卡片
        /// <summary>
        /// 卡片式流速控件：大字号居中数值 + 左右箭头 + 提示 + 实时下落预览。
        /// 幂等：已有卡片就只刷新数值；旧版控件会先被清掉。
        /// </summary>
        private void EnsureSpeedCard()
        {
            if (root == null) return;

            Transform existing = root.Find("SpeedCard");
            if (existing != null)
            {
                if (speedValue == null) speedValue = findChildText("SpeedCardValue");
                if (speedHint == null) speedHint = findChildText("SpeedCardHint");
                if (preview == null) preview = existing.GetComponentInChildren<SpeedPreviewView>(true);
                RefreshSpeedValue();
                return;
            }

            // 旧版本留下的控件先清理，避免两套 UI 叠在一起
            DestroyChild("SpeedLabel");
            DestroyChild("SpeedStepper");
            DestroyChild("SpeedHint");
            DestroyChild("SpeedPreview");

            // ---- 卡片底板
            Image card = UiFactory.CreateImage(root, "SpeedCard", new Color(0.075f, 0.095f, 0.13f, 0.96f));
            UiFactory.Place(card.rectTransform, new Vector2(1f, 0.5f), new Vector2(-120f, -56f),
                new Vector2(480f, 230f), new Vector2(1f, 0.5f));

            Image accent = UiFactory.CreateImage(card.rectTransform, "Accent", Theme.Accent);
            UiFactory.Place(accent.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 0f),
                new Vector2(480f, 4f), new Vector2(0.5f, 1f));

            // ---- 标题
            Text title = UiFactory.CreateText(card.rectTransform, "Title", "音符流速", 24,
                Theme.TextMuted, TextAnchor.MiddleCenter);
            UiFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -20f),
                new Vector2(420f, 28f), new Vector2(0.5f, 1f));

            // ---- 大字号居中数值
            speedValue = UiFactory.CreateText(card.rectTransform, "SpeedCardValue", "5.50", 74,
                Theme.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.Place(speedValue.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 2f),
                new Vector2(240f, 92f));

            // ---- 左右箭头
            CreateArrow(card.rectTransform, "◀", new Vector2(-176f, 2f),
                () => NudgeSpeed(-ScrollSpeedTrack.Step));
            CreateArrow(card.rectTransform, "▶", new Vector2(176f, 2f),
                () => NudgeSpeed(ScrollSpeedTrack.Step));

            // ---- 提示
            speedHint = UiFactory.CreateText(card.rectTransform, "SpeedCardHint",
                "每次调整0.25，只改视觉，不影响判定", 19, Theme.TextMuted, TextAnchor.MiddleCenter);
            UiFactory.Place(speedHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 14f),
                new Vector2(450f, 24f), new Vector2(0.5f, 0f));

            // ---- 实时预览（放在卡片下方）
            RectTransform previewRect = UiFactory.CreateRect(root, "SpeedPreview");
            UiFactory.Place(previewRect, new Vector2(1f, 0.5f), new Vector2(-120f, -252f),
                new Vector2(360f, 120f), new Vector2(1f, 0.5f));
            preview = previewRect.gameObject.AddComponent<SpeedPreviewView>();
            preview.root = previewRect;
            preview.EnsureBuilt();

            RefreshSpeedValue();
        }

        private void CreateArrow(Transform parent, string label, Vector2 position, System.Action onClick)
        {
            UiFactory.CreateTapButton(parent, label, new Vector2(0.5f, 0.5f), position,
                new Vector2(58f, 58f), new Color(0.13f, 0.16f, 0.21f), Theme.TextPrimary, 30, onClick);
        }

        private void DestroyChild(string name)
        {
            Transform child = root != null ? root.Find(name) : null;
            if (child != null) DestroyObject(child.gameObject);
        }

        /// <summary>清掉旧版本用滑条时留下的物体（升级工程时避免两套控件叠在一起）。</summary>
        private void RemoveLegacySlider(string objectName)
        {
            Transform legacy = root != null ? root.Find(objectName) : null;
            if (legacy != null) DestroyObject(legacy.gameObject);
        }

        // ================================================================ 生命周期
        private void Awake()
        {
            EnsureBuilt();
        }

        private void Update()
        {
            if (!_bound) Bind();
            if (_game == null) return;
            if (_camera == null) _camera = Camera.main;
        }

        private void OnEnable()
        {
            // 全局流速数据源：不管在游戏内还是主界面改的，这里都能收到
            ScrollSpeedState.Changed += OnSpeedStateChanged;
            RefreshSpeedValue();
        }

        private void OnDisable()
        {
            ScrollSpeedState.Changed -= OnSpeedStateChanged;

            if (_game == null || !_bound) return;
            _game.StateChanged -= OnStateChanged;
            _bound = false;
        }

        private void Bind()
        {
            _game = GameManager.Instance;
            if (_game == null) return;

            _game.StateChanged += OnStateChanged;
            _bound = true;

            RefreshCalibrationValue();
            RefreshSpeedValue();
            RefreshAutoPlayButton();
            Show(_game.State == GameState.Menu);
        }

        private void OnStateChanged(GameState state)
        {
            if (state == GameState.Menu)
            {
                RebuildChartButtons();   // 回到菜单时刷新一下"最佳成绩"

                // 游戏里用 ↑↓ 调过流速的话，回菜单时把数值同步过来
                RefreshSpeedValue();
            }
            Show(state == GameState.Menu);
        }

        public void Show(bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
            group.interactable = visible;
        }
    }
}
